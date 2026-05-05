#if GAMELYFT_ADMOB
using GoogleMobileAds.Api;

namespace GameLyft.Sdk
{
    /// <summary>
    /// AdMob-specific ad revenue reporting, exposed as an extension method on
    /// GameLyftAnalytics.AdRevenue. Wrapped in #if GAMELYFT_ADMOB so it only
    /// compiles when the AdMob checkbox is ticked in Tools → GameLyft → Settings.
    ///
    /// Usage:
    ///   interstitialAd.OnAdPaid += (adValue) =>
    ///   {
    ///       GameLyftAnalytics.AdRevenue.Report(
    ///           adValue,
    ///           interstitialAd.GetResponseInfo(),
    ///           "interstitial",
    ///           interstitialAd.GetAdUnitID());
    ///   };
    /// </summary>
    public static class AdMobRevenueExtensions
    {
        public static void Report(this GameLyftAnalytics.AdRevenueSurface surface,
            AdValue adValue, ResponseInfo responseInfo, string adFormat, string adUnitId)
        {
            if (adValue == null)
            {
                GameLyftAnalytics.Warn("AdRevenue.Report(AdMob) called with null AdValue. Event dropped.");
                return;
            }

            double revenue = adValue.Value / 1_000_000.0;
            string currencyCode = adValue.CurrencyCode;

            string networkName = "AdMob";
            if (responseInfo != null)
            {
                AdapterResponseInfo loaded = responseInfo.GetLoadedAdapterResponseInfo();
                if (loaded != null)
                    networkName = string.IsNullOrEmpty(loaded.AdSourceName) ? "AdMob" : loaded.AdSourceName;
            }

            surface.Log(
                platform: "admob",
                source: networkName,
                format: adFormat ?? "",
                adUnit: adUnitId ?? "",
                currency: currencyCode ?? "USD",
                revenue: revenue);
        }
    }
}
#endif
