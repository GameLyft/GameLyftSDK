using System;
using System.Collections.Generic;
using AppsFlyerSDK;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// AppsFlyer MMP integration. Compiles only when GAMELYFT_APPSFLYER is defined
    /// (set via Tools → GameLyft → Settings → MMP → AppsFlyer MMP). The whole assembly
    /// is excluded by defineConstraints when the toggle is off, so the AppsFlyer Unity
    /// SDK is not required for projects that don't use it.
    ///
    /// USAGE — add ONE line to your AppsFlyer conversion-data handler:
    ///
    ///     public void onConversionDataSuccess(string conversionData)
    ///     {
    ///         AppsFlyer.AFLog("didReceiveConversionData", conversionData);
    ///         Dictionary&lt;string, object&gt; conversionDataDictionary =
    ///             AppsFlyer.CallbackStringToDictionary(conversionData);
    ///         // add deferred deeplink logic here
    ///         GameLyft.Sdk.AppsFlyerMmp.HandleConversionData(conversionData);  // ← this one
    ///     }
    ///
    /// Why a manual hookup instead of auto-polling: AppsFlyer's conversion data is delivered
    /// via UnityPlayer.UnitySendMessage to a single GameObject name registered through
    /// initSDK(). The behavior of getConversionData() with a different objectName is not
    /// reliably documented across SDK versions and platforms — having the consumer forward
    /// the raw payload from their existing handler removes that uncertainty entirely.
    ///
    /// The helper extracts the 4 standard attribution fields (media_source, campaign,
    /// adset, af_ad) and forwards them to GameLyftAnalytics.Mmp.LogInstall(), which
    /// applies the shared one-shot guard so 'mmp_install' fires at most once per device
    /// install regardless of how many MMP integrations are wired up.
    /// </summary>
    public static class AppsFlyerMmp
    {
        /// <summary>
        /// Forward AppsFlyer's raw conversion-data JSON callback to GameLyft's MMP surface.
        /// Call from your IAppsFlyerConversionData.onConversionDataSuccess implementation.
        /// Silently no-ops on null/empty input rather than throwing — matches what the
        /// consumer's own handler does in that case.
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
        /// Same as the string overload but for callers that have already parsed the
        /// JSON (e.g. the standard AppsFlyer template parses with
        /// AppsFlyer.CallbackStringToDictionary before adding deeplink logic — no need
        /// to re-serialize and re-parse).
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
            // MmpSurface defaults source to "Organic" when null/empty, so organic
            // installs surface cleanly without us having to inspect af_status.
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
