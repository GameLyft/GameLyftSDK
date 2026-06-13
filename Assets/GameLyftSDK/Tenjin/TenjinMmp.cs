#if GAMELYFT_TENJIN
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Tenjin MMP integration. Auto-attaches at app launch when GAMELYFT_TENJIN is
    /// defined (set via Tools → GameLyft → Settings → MMP → Tenjin MMP).
    ///
    /// FILE LOCATION (deliberately unusual):
    ///   This file lives at Assets/GameLyftSDK/Tenjin/, OUTSIDE the GameLyft.Sdk
    ///   asmdef hierarchy (Runtime/ and Editor/ are siblings, not ancestors). Without
    ///   an ancestor asmdef, this file falls into default Assembly-CSharp.
    ///
    ///   That's deliberate: the Tenjin SDK has no asmdef of its own — it lives in
    ///   Assembly-CSharp too. Unity's asmdefs cannot reference Assembly-CSharp, so a
    ///   GameLyft.Sdk.Tenjin sub-asmdef (the pattern we use for Solar Engine, Adjust,
    ///   Singular, AppLovin) is impossible here. Living in Assembly-CSharp alongside
    ///   Tenjin lets us reference Tenjin's BaseTenjin type directly without
    ///   reflection or modifying the third-party SDK.
    ///
    ///   We can still reference GameLyft.Sdk types because GameLyft.Sdk.asmdef has
    ///   autoReferenced: true, which means Assembly-CSharp auto-references it.
    ///
    ///   The whole file body wraps in #if GAMELYFT_TENJIN so the type compiles to
    ///   empty when the toggle is off — even projects without Tenjin SDK still build.
    ///
    /// FLOW (zero consumer code, mirrors Solar Engine / Adjust auto-poll):
    ///   1. Bootstrap at BeforeSceneLoad. Bail if 'mmp_install' is already reported.
    ///   2. Phase 1: poll the scene for a BaseTenjin singleton. The consumer's own
    ///      Tenjin.getInstance(apiKey) call somewhere in their app creates a
    ///      "Tenjin" GameObject with a BaseTenjin MonoBehaviour (subclass per
    ///      platform: AndroidTenjin / IosTenjin / DebugTenjin), DontDestroyOnLoad.
    ///      We watch for it to appear.
    ///   3. Phase 2: once found, call instance.GetAttributionInfo(callback). The
    ///      callback receives Dictionary&lt;string, string&gt; with documented Tenjin keys.
    ///   4. Map ad_network / campaign_name / creative_name to the standard schema.
    ///      Tenjin returns "(not set)" for organic — normalize to null so
    ///      MmpSurface.LogInstall's "Organic" default kicks in cleanly.
    ///   5. 180s shared budget across both phases. If the consumer initializes Tenjin
    ///      late (scene 3+) within budget, we still catch it. If they're past 3 min
    ///      of foreground time, we bail; next session retries from scratch. Once
    ///      'mmp_install' is reported, the guard prevents future polling entirely.
    /// </summary>
    internal class TenjinMmp : MonoBehaviour
    {
        private const float POLL_INTERVAL_SECONDS = 2f;
        private const float TIMEOUT_SECONDS = 180f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Belt-and-suspenders: GAMELYFT_TENJIN is the primary gate (this file
            // body wouldn't compile without it), but the setting check catches a
            // window where the toggle was flipped off and the asset edit happened
            // before recompile finished.
            var settings = GameLyftSettings.LoadOrNull();
            if (settings == null || !settings.enableTenjinMmp) return;

            // Already reported on this device — don't bother polling.
            if (GameLyftAnalytics.Mmp.IsInstallReported) return;

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

            // Phase 1: wait for the BaseTenjin singleton to appear in the scene.
            // The consumer's call to Tenjin.getInstance(apiKey) creates a
            // GameObject("Tenjin") with HideFlags.HideAndDontSave attached to a
            // BaseTenjin subclass. HideAndDontSave does NOT block FindObjectOfType
            // (it only affects the Inspector / save behavior), so this works.
            while (FindBaseTenjin() == null && Time.time - startTime < TIMEOUT_SECONDS)
            {
                yield return new WaitForSeconds(POLL_INTERVAL_SECONDS);
            }

            var instance = FindBaseTenjin();
            if (instance == null)
            {
                // Tenjin was never initialized within the budget. Bail without firing;
                // PlayerPrefs guard stays unset so a future session can retry.
                GLLog.Trace("TenjinMmp: Tenjin instance not found within " + TIMEOUT_SECONDS
                    + "s — bailing (did you call Tenjin.getInstance(apiKey)?).");
                Destroy(gameObject);
                yield break;
            }

            GLLog.Trace("TenjinMmp: Tenjin instance found — querying GetAttributionInfo.");

            // Phase 2: query Tenjin for attribution. Tenjin's GetAttributionInfo
            // dispatches the callback asynchronously (Java/native bridge call on
            // Android, P/Invoke on iOS, immediate on DebugTenjin in editor). We
            // capture the result via closure-write and poll the flag.
            bool callbackFired = false;
            Dictionary<string, string> attribution = null;
            instance.GetAttributionInfo(data =>
            {
                attribution = data;
                callbackFired = true;
            });

            // Wait for the callback or budget exhaustion. Polling the flag (rather
            // than relying on a long single Wait) lets us cleanly bail if the budget
            // runs out mid-callback.
            while (!callbackFired && Time.time - startTime < TIMEOUT_SECONDS)
            {
                yield return new WaitForSeconds(POLL_INTERVAL_SECONDS);
            }

            if (!callbackFired || attribution == null || attribution.Count == 0)
            {
                GLLog.Trace("TenjinMmp: timed out — Tenjin delivered no attribution this session.");
                Destroy(gameObject);
                yield break;
            }

            // DIAGNOSTIC: log the entire raw payload as a Firebase event so the
            // schema can be confirmed in production via BigQuery. Tenjin's documented
            // schema is well-known (ad_network/campaign_name/creative_name + extras
            // like advertising_id/site_id/click_id/campaign_id/remote_campaign_id/
            // referrer/referrer_params), but the diagnostic captures any extras the
            // platform may pass through that aren't in the documented set.
            var schemaDump = new Dictionary<string, object>(attribution.Count);
            foreach (var kvp in attribution)
                schemaDump[kvp.Key] = kvp.Value;
            GameLyftAnalytics.Mmp.LogAttributionSchema("tenjin_attribution", schemaDump);

            // Map Tenjin payload → standard MMP schema. Documented Tenjin keys:
            //   ad_network     → source     (Tenjin returns "(not set)" for organic;
            //                                 we normalize to null so the MmpSurface
            //                                 default of "Organic" kicks in)
            //   campaign_name  → campaign
            //   (no equivalent) → ad_set    (Tenjin's hierarchy is shallower than
            //                                 Adjust/AppsFlyer — no first-class adgroup)
            //   creative_name  → creative
            string source   = Normalize(TryGet(attribution, "ad_network"));
            string campaign = Normalize(TryGet(attribution, "campaign_name"));
            string adSet    = null;
            string creative = Normalize(TryGet(attribution, "creative_name"));

            GameLyftAnalytics.Mmp.LogInstall(source, campaign, adSet, creative);

            Destroy(gameObject);
        }

        private static string TryGet(Dictionary<string, string> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var v) ? v : null;
        }

        // Tenjin returns the literal string "(not set)" for organic installs (rather
        // than empty/null). Coerce it back to null so MmpSurface.LogInstall's source
        // default to "Organic" applies, instead of dashboards seeing the "(not set)"
        // string surface as a legit-looking acquisition channel.
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s == "(not set)") return null;
            return s;
        }

        // Version-gated singleton probe. Same pattern as SolarEngineMmp / AdjustMmp.
        // FindObjectOfType deprecated in Unity 2023.1+ (Unity 6); FindAnyObjectByType
        // is the modern replacement. BaseTenjin is in the global namespace (no
        // namespace declaration in BaseTenjin.cs).
        private static BaseTenjin FindBaseTenjin()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<BaseTenjin>();
#else
            return FindObjectOfType<BaseTenjin>();
#endif
        }
    }
}
#endif
