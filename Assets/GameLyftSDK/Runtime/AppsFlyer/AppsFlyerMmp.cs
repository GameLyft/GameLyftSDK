using System;
using System.Collections;
using System.Collections.Generic;
using AppsFlyerSDK;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// AppsFlyer MMP integration. Auto-starts at app launch when GAMELYFT_APPSFLYER is
    /// defined (set via Tools → GameLyft → Settings → MMP → AppsFlyer MMP). The whole
    /// assembly is excluded by defineConstraints when the toggle is off, so the AppsFlyer
    /// Unity SDK is not required for projects that don't use it.
    ///
    /// Why a PlayerPrefs bridge (unlike Solar Engine / Adjust, which expose pull APIs):
    ///   AppsFlyer delivers conversion data ONLY via UnityPlayer.UnitySendMessage to the
    ///   single GameObject registered through initSDK() — i.e. the consumer's own
    ///   IAppsFlyerConversionData handler (the AppsFlyerObject prefab's script). That
    ///   handler lives inside the AppsFlyer SDK's OWN assembly, which cannot reference this
    ///   SDK (asmdef boundary), so it can't call us directly. And getConversionData(objectName)
    ///   cross-object delivery is unreliable across SDK versions/platforms. So instead the
    ///   consumer's handler stashes the raw payload in PlayerPrefs (pure UnityEngine — works
    ///   from any assembly) and this module polls for it. PlayerPrefs persists, so a callback
    ///   that arrives after our poll window is still picked up on the next launch.
    ///
    /// CONSUMER CONTRACT — add these 3 lines to your onConversionDataSuccess handler
    /// (the AppsFlyerObject prefab's AppsFlyerObjectScript, or your own handler). TIP: the
    /// "Wire AppsFlyer Handler" button in Tools → GameLyft → Settings injects them for you.
    ///
    ///     public void onConversionDataSuccess(string conversionData)
    ///     {
    ///         AppsFlyer.AFLog("didReceiveConversionData", conversionData);
    ///         PlayerPrefs.SetString("AppsflyerGameLyftConversionData", conversionData);
    ///         PlayerPrefs.SetInt("isAppsflyerGameLyftConversionSet", 1);
    ///         PlayerPrefs.Save();
    ///     }
    ///
    /// Flow:
    ///   1. Bootstrap at BeforeSceneLoad. Bail early if 'mmp_install' already fired on this
    ///      device (PlayerPrefs guard) — no point polling if we'll just no-op.
    ///   2. Coroutine polls the conversion-set flag every 2s (waits for AppsFlyer's async
    ///      callback to land). PlayerPrefs persistence means a callback delivered in a prior
    ///      session is picked up immediately on the next launch.
    ///   3. On the flag being set, parse the stored JSON with AppsFlyer.CallbackStringToDictionary,
    ///      extract media_source / campaign / adset / af_ad and forward to
    ///      GameLyftAnalytics.Mmp.LogInstall(), which fires the shared one-shot 'mmp_install'.
    ///   4. Times out after 3 minutes — matches the other MMP modules.
    ///
    /// A 'appsflyer_attribution' diagnostic event with the full raw payload is also emitted
    /// so the real schema can be confirmed via BigQuery; remove once the mapping is verified.
    /// </summary>
    public class AppsFlyerMmp : MonoBehaviour
    {
        // PlayerPrefs bridge keys. These string literals MUST stay in sync with the consumer's
        // onConversionDataSuccess handler (see CONSUMER CONTRACT above) — the handler lives in
        // a different assembly, so it can't reference these consts and must hardcode the same
        // strings.
        public const string CONVERSION_DATA_KEY = "AppsflyerGameLyftConversionData";
        public const string CONVERSION_SET_KEY  = "isAppsflyerGameLyftConversionSet";

        private const float POLL_INTERVAL_SECONDS = 2f;
        private const float TIMEOUT_SECONDS = 180f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Belt-and-suspenders: defineConstraints is the primary gate, but a flipped
            // toggle mid-recompile could leave a window where this still runs.
            var settings = GameLyftSettings.LoadOrNull();
            if (settings == null || !settings.enableAppsFlyerMmp) return;

            // Already reported on this device — don't bother spinning up the polling coroutine.
            if (GameLyftAnalytics.Mmp.IsInstallReported) return;

            var go = new GameObject("[GameLyft.AppsFlyerMmp]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<AppsFlyerMmp>();
        }

        private void Start()
        {
            StartCoroutine(WaitForConversionAndReport());
        }

        private IEnumerator WaitForConversionAndReport()
        {
            float startTime = Time.time;

            // Poll for the consumer's handler to stash conversion data in PlayerPrefs.
            // PlayerPrefs persists across sessions, so a callback that landed in a previous
            // launch satisfies this immediately.
            while (PlayerPrefs.GetInt(CONVERSION_SET_KEY, 0) != 1
                   && Time.time - startTime < TIMEOUT_SECONDS)
            {
                yield return new WaitForSeconds(POLL_INTERVAL_SECONDS);
            }

            if (PlayerPrefs.GetInt(CONVERSION_SET_KEY, 0) != 1)
            {
                // Timed out — AppsFlyer never delivered conversion data this session (or the
                // consumer's handler isn't writing PlayerPrefs). No event fired, guard stays
                // unset so a future session can retry.
                Destroy(gameObject);
                yield break;
            }

            string raw = PlayerPrefs.GetString(CONVERSION_DATA_KEY, "");
            if (string.IsNullOrEmpty(raw))
            {
                Destroy(gameObject);
                yield break;
            }

            HandleConversionData(raw);
            Destroy(gameObject);
        }

        /// <summary>
        /// Manual fallback: forward AppsFlyer's raw conversion-data JSON to GameLyft's MMP
        /// surface. The auto PlayerPrefs poll above is the primary path; this stays public
        /// for consumers who prefer to call it directly from onConversionDataSuccess.
        /// Silently no-ops on null/empty input rather than throwing.
        /// </summary>
        public static void HandleConversionData(string conversionDataJson)
        {
            if (string.IsNullOrEmpty(conversionDataJson)) return;

            Dictionary<string, object> data;
            try
            {
                data = AppsFlyer.CallbackStringToDictionary(conversionDataJson);
            }
            catch (Exception e)
            {
                Debug.LogError("[GameLyft.AppsFlyerMmp] Failed to parse conversion data JSON: " + e.Message);
                return;
            }

            HandleConversionData(data);
        }

        /// <summary>
        /// Same as the string overload but for callers that have already parsed the JSON
        /// (e.g. via AppsFlyer.CallbackStringToDictionary) — no need to re-serialize.
        /// </summary>
        public static void HandleConversionData(Dictionary<string, object> conversionData)
        {
            if (conversionData == null) return;

            // DIAGNOSTIC: emit the full raw payload as a Firebase event so the actual
            // AppsFlyer schema can be verified from production data via BigQuery. Remove
            // once the field mapping below has been confirmed against real conversion data.
            GameLyftAnalytics.Mmp.LogAttributionSchema("appsflyer_attribution", conversionData);

            // AppsFlyer field names per their conversion-data docs:
            //   media_source — acquisition channel ("" or missing for organic)
            //   campaign     — campaign name
            //   adset        — ad set name (note: AppsFlyer uses "adset", not "ad_set")
            //   af_ad        — creative / ad name
            // MmpSurface defaults source to "Organic" when null/empty, so organic installs
            // surface cleanly without us having to inspect af_status.
            string source   = TryGetString(conversionData, "media_source");
            string campaign = TryGetString(conversionData, "campaign");
            string adSet    = TryGetString(conversionData, "adset");
            string creative = TryGetString(conversionData, "af_ad");

            GameLyftAnalytics.Mmp.LogInstall(source, campaign, adSet, creative);
        }

        private static string TryGetString(Dictionary<string, object> dict, string key)
        {
            return dict.TryGetValue(key, out var v) && v != null ? v.ToString() : null;
        }
    }
}
