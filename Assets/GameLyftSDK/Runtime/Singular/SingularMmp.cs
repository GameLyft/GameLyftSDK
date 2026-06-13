using System.Collections.Generic;
using Singular;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Singular MMP integration. Auto-attaches at app launch when GAMELYFT_SINGULAR is
    /// defined (set via Tools → GameLyft → Settings → MMP → Singular MMP). The whole
    /// assembly is excluded by defineConstraints when the toggle is off.
    ///
    /// Why this MonoBehaviour-based pattern (different from Solar Engine / Adjust):
    ///   Singular has no pull API for attribution — it only delivers data via a
    ///   callback registered through SingularSDK.SetSingularDeviceAttributionCallbackHandler.
    ///   So we register ourselves as the handler at BeforeSceneLoad and wait for the
    ///   callback to fire. The setter is just a static field assignment so registration
    ///   order vs. SingularSDKObject's Awake doesn't matter.
    ///
    /// Side effect to be aware of:
    ///   SingularSDK.SetSingularDeviceAttributionCallbackHandler is single-slot — there
    ///   is no list, registering REPLACES any previously-registered handler. If your
    ///   project has its own AttributionCallback script doing something else with the
    ///   payload (Firebase forwarding, deep-link routing, etc.), turning on Singular MMP
    ///   here will steal the callback. Either remove your old handler or extend this
    ///   script with the logic it used to do — the raw attribution dict is right there
    ///   in OnSingularDeviceAttributionCallback.
    ///
    /// Field mapping is BEST-GUESS based on Singular's REST Attribution Details API
    /// schema (which IS documented). The on-device callback schema is not publicly
    /// documented, so we also fire a 'singular_attribution' diagnostic event with the
    /// full raw payload so the real schema can be confirmed via BigQuery after release.
    /// Once the schema is confirmed, update the mapping below and remove the diagnostic
    /// LogAttributionSchema call.
    /// </summary>
    internal class SingularMmp : MonoBehaviour, SingularDeviceAttributionCallbackHandler
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Belt-and-suspenders: defineConstraints is the primary gate, but a flipped
            // toggle mid-recompile could leave a window where this still runs.
            var settings = GameLyftSettings.LoadOrNull();
            if (settings == null || !settings.enableSingularMmp) return;

            var go = new GameObject("[GameLyft.SingularMmp]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            var component = go.AddComponent<SingularMmp>();

            // Register as Singular's attribution handler. The setter just stores a static
            // reference — works whether or not SingularSDK has finished initializing yet.
            // When Singular's native side eventually has attribution to deliver, it'll
            // dispatch to whichever handler is currently registered.
            SingularSDK.SetSingularDeviceAttributionCallbackHandler(component);
            GLLog.Trace("SingularMmp enabled — registered attribution callback (mmp_install pending).");
        }

        // Callback dispatched from SingularSDK after JSON-parsing the native payload.
        // Runs on the Unity main thread.
        public void OnSingularDeviceAttributionCallback(Dictionary<string, object> attributionInfo)
        {
            if (attributionInfo == null || attributionInfo.Count == 0) return;

            GLLog.Trace("SingularMmp: attribution callback fired — mapping to mmp_install.");

            // DIAGNOSTIC: emit the full raw schema as a Firebase event so we can confirm
            // the actual key names in production via BigQuery. Remove once the schema is
            // verified and the mapping below has been hardened against real payloads.
            GameLyftAnalytics.Mmp.LogAttributionSchema("singular_attribution", attributionInfo);

            // BEST-GUESS field mapping. Based on Singular's REST Attribution Details API
            // (which is documented and uses these key names at the install_info level).
            // The on-device callback may use the same flat keys, may nest under "install_info",
            // or may use entirely different names — verify against the singular_attribution
            // diagnostic event before relying on this in production reporting.
            //
            //   network         → source        (Singular's primary attribution channel)
            //   campaign_name   → campaign
            //   (no equivalent) → ad_set        (Singular's hierarchy is shallower than Adjust/AppsFlyer —
            //                                    no first-class adgroup concept on the device side)
            //   creative_name   → creative      (best guess; may not be present in every callback)
            string source   = TryGetString(attributionInfo, "network");
            string campaign = TryGetString(attributionInfo, "campaign_name");
            string adSet    = null;
            string creative = TryGetString(attributionInfo, "creative_name");

            GameLyftAnalytics.Mmp.LogInstall(source, campaign, adSet, creative);
        }

        private static string TryGetString(Dictionary<string, object> dict, string key)
        {
            return dict.TryGetValue(key, out var v) && v != null ? v.ToString() : null;
        }
    }
}
