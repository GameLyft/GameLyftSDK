using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Tenjin MMP integration. Auto-attaches at app launch when GAMELYFT_TENJIN is
    /// defined (Tools → GameLyft → Settings → MMP → Tenjin MMP).
    ///
    /// WHY THIS LIVES IN ITS OWN ASMDEF + USES REFLECTION:
    ///   Tenjin is the one MMP whose Unity SDK ships with NO asmdef, so its BaseTenjin
    ///   type compiles into Assembly-CSharp. An asmdef cannot reference Assembly-CSharp,
    ///   so we can't name BaseTenjin / Tenjin.AttributionInfoDelegate at compile time.
    ///
    ///   An earlier version sidestepped that by placing a LOOSE TenjinMmp.cs (no asmdef)
    ///   under Assets/GameLyftSDK/ so it fell into Assembly-CSharp alongside Tenjin. That
    ///   works ONLY when the SDK is vendored into a project's Assets/. When the SDK is
    ///   imported as a UPM package (Library/PackageCache/...), Unity does NOT add loose
    ///   package scripts to Assembly-CSharp — so that file was never compiled, the
    ///   BeforeSceneLoad bootstrap never ran, and Tenjin attribution silently never fired.
    ///
    ///   The fix: this module lives in its own package sub-assembly (GameLyft.Sdk.Tenjin),
    ///   so the package always compiles it, and it resolves BaseTenjin +
    ///   GetAttributionInfo by REFLECTION at runtime — no compile-time Tenjin reference,
    ///   zero consumer code, and a clean no-op if the Tenjin SDK isn't present.
    ///
    /// FLOW (mirrors SolarEngine / Adjust auto-poll):
    ///   1. Bootstrap at BeforeSceneLoad. Bail if 'mmp_install' already reported, or if the
    ///      BaseTenjin type can't be resolved (Tenjin SDK absent — warn once).
    ///   2. Phase 1: poll the scene for the BaseTenjin singleton the consumer's
    ///      Tenjin.getInstance(apiKey) creates (a DontDestroyOnLoad "Tenjin" GameObject).
    ///   3. Phase 2: invoke GetAttributionInfo(callback) via reflection; the callback
    ///      receives Dictionary&lt;string,string&gt; with the documented Tenjin keys.
    ///   4. Map ad_network / campaign_name / creative_name → standard schema; fire the
    ///      one-shot 'mmp_install' + 'tenjin_attribution' diagnostic.
    ///   5. 180s shared budget; bail on timeout, next session retries until reported.
    /// </summary>
    internal class TenjinMmp : MonoBehaviour
    {
        private const float POLL_INTERVAL_SECONDS = 2f;
        private const float TIMEOUT_SECONDS = 180f;

        // Resolved once at type load. Null when the Tenjin Unity SDK isn't in the project.
        private static readonly Type _baseTenjinType = ResolveBaseTenjinType();

        private bool _callbackFired;
        private Dictionary<string, string> _attribution;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Belt-and-suspenders: defineConstraints (GAMELYFT_TENJIN) on this asmdef is the
            // primary gate; the setting check covers a toggle flipped off mid-recompile.
            var settings = GameLyftSettings.LoadOrNull();
            if (settings == null || !settings.enableTenjinMmp) return;

            // Already reported on this device — don't bother polling.
            if (GameLyftAnalytics.Mmp.IsInstallReported) return;

            if (_baseTenjinType == null)
            {
                GLLog.Warn("Tenjin MMP is enabled (GAMELYFT_TENJIN) but the Tenjin Unity SDK "
                    + "(BaseTenjin) was not found in the project. Import Tenjin, or turn off "
                    + "Tenjin MMP in Tools → GameLyft → Settings.");
                return;
            }

            var go = new GameObject("[GameLyft.TenjinMmp]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<TenjinMmp>();
            GLLog.Trace("TenjinMmp enabled — waiting for Tenjin instance + attribution (mmp_install pending).");
        }

        private void Start()
        {
            StartCoroutine(WaitForAttributionAndReport());
        }

        private IEnumerator WaitForAttributionAndReport()
        {
            float startTime = Time.time;

            // Phase 1: wait for the consumer's BaseTenjin singleton to appear. Their
            // Tenjin.getInstance(apiKey) creates a DontDestroyOnLoad "Tenjin" GameObject
            // with a BaseTenjin subclass; HideAndDontSave does not block FindObjectOfType.
            UnityEngine.Object instance = FindBaseTenjin();
            while (instance == null && Time.time - startTime < TIMEOUT_SECONDS)
            {
                yield return new WaitForSeconds(POLL_INTERVAL_SECONDS);
                instance = FindBaseTenjin();
            }

            if (instance == null)
            {
                GLLog.Trace("TenjinMmp: Tenjin instance not found within " + TIMEOUT_SECONDS
                    + "s — bailing (did you call Tenjin.getInstance(apiKey)?).");
                Destroy(gameObject);
                yield break;
            }

            GLLog.Trace("TenjinMmp: Tenjin instance found — querying GetAttributionInfo.");

            // Phase 2: invoke GetAttributionInfo(delegate) by reflection. The callback
            // (OnAttributionInfo) is dispatched by Tenjin on the main thread.
            if (!InvokeGetAttributionInfo(instance))
            {
                Destroy(gameObject);
                yield break;
            }

            while (!_callbackFired && Time.time - startTime < TIMEOUT_SECONDS)
                yield return new WaitForSeconds(POLL_INTERVAL_SECONDS);

            if (!_callbackFired || _attribution == null || _attribution.Count == 0)
            {
                GLLog.Trace("TenjinMmp: timed out — Tenjin delivered no attribution this session.");
                Destroy(gameObject);
                yield break;
            }

            // DIAGNOSTIC: full raw payload → Firebase so the schema can be confirmed in
            // production via BigQuery. Remove once the mapping below is verified.
            var schemaDump = new Dictionary<string, object>(_attribution.Count);
            foreach (var kvp in _attribution) schemaDump[kvp.Key] = kvp.Value;
            GameLyftAnalytics.Mmp.LogAttributionSchema("tenjin_attribution", schemaDump);

            // Map documented Tenjin keys → standard MMP schema:
            //   ad_network    → source    (Tenjin returns "(not set)" for organic; we
            //                              normalize to null so MmpSurface's "Organic" default applies)
            //   campaign_name → campaign
            //   (no equivalent) → ad_set  (Tenjin's hierarchy has no first-class adgroup)
            //   creative_name → creative
            string source   = Normalize(TryGet(_attribution, "ad_network"));
            string campaign = Normalize(TryGet(_attribution, "campaign_name"));
            string adSet    = null;
            string creative = Normalize(TryGet(_attribution, "creative_name"));

            GameLyftAnalytics.Mmp.LogInstall(source, campaign, adSet, creative);
            Destroy(gameObject);
        }

        // Invoked by Tenjin (via the reflected delegate) on the Unity main thread.
        private void OnAttributionInfo(Dictionary<string, string> data)
        {
            _attribution = data;
            _callbackFired = true;
        }

        // Build a Tenjin.AttributionInfoDelegate from OnAttributionInfo and invoke
        // instance.GetAttributionInfo(thatDelegate). Returns false if the method or its
        // delegate parameter can't be resolved (unexpected Tenjin SDK shape).
        private bool InvokeGetAttributionInfo(UnityEngine.Object instance)
        {
            try
            {
                MethodInfo method = instance.GetType().GetMethod("GetAttributionInfo",
                    BindingFlags.Public | BindingFlags.Instance);
                if (method == null)
                {
                    GLLog.Warn("TenjinMmp: BaseTenjin.GetAttributionInfo(callback) not found via reflection.");
                    return false;
                }

                ParameterInfo[] ps = method.GetParameters();
                if (ps.Length != 1)
                {
                    GLLog.Warn("TenjinMmp: unexpected GetAttributionInfo signature (" + ps.Length + " params).");
                    return false;
                }

                Type delegateType = ps[0].ParameterType;   // Tenjin.AttributionInfoDelegate
                MethodInfo cb = typeof(TenjinMmp).GetMethod(nameof(OnAttributionInfo),
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Delegate del = Delegate.CreateDelegate(delegateType, this, cb);

                method.Invoke(instance, new object[] { del });
                return true;
            }
            catch (Exception e)
            {
                GLLog.Warn("TenjinMmp: GetAttributionInfo reflection call failed: " + e.Message);
                return false;
            }
        }

        private static UnityEngine.Object FindBaseTenjin()
        {
            if (_baseTenjinType == null) return null;
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindAnyObjectByType(_baseTenjinType);
#else
            return UnityEngine.Object.FindObjectOfType(_baseTenjinType);
#endif
        }

        // Resolve the global-namespace BaseTenjin type. The Tenjin SDK has no asmdef, so it
        // compiles into the consumer's Assembly-CSharp; we look there first, then sweep all
        // loaded assemblies as a fallback. Null when Tenjin isn't present.
        private static Type ResolveBaseTenjinType()
        {
            var t = Type.GetType("BaseTenjin, Assembly-CSharp");
            if (t != null) return t;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType("BaseTenjin"); }
                catch { t = null; }
                if (t != null) return t;
            }
            return null;
        }

        private static string TryGet(Dictionary<string, string> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var v) ? v : null;
        }

        // Tenjin returns the literal "(not set)" for organic installs. Coerce to null so
        // MmpSurface.LogInstall's "Organic" default applies instead of surfacing "(not set)".
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s == "(not set)") return null;
            return s;
        }
    }
}
