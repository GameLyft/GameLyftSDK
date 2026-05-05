#if GAMELYFT_APPLOVIN
namespace GameLyft.Sdk
{
    /// <summary>
    /// AppLovin MAX-specific ad revenue reporting, exposed as an extension method on
    /// GameLyftAnalytics.AdRevenue. Wrapped in #if GAMELYFT_APPLOVIN so it only
    /// compiles when the AppLovin MAX checkbox is ticked in Tools → GameLyft → Settings.
    ///
    /// Usage:
    ///   MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += (adUnitId, adInfo) =>
    ///   {
    ///       GameLyftAnalytics.AdRevenue.Report(adInfo);
    ///   };
    /// </summary>
    public static class MaxRevenueExtensions
    {
        public static void Report(this GameLyftAnalytics.AdRevenueSurface surface,
            MaxSdkBase.AdInfo adInfo)
        {
            if (adInfo == null)
            {
                GameLyftAnalytics.Warn("AdRevenue.Report(AppLovin MAX) called with null AdInfo. Event dropped.");
                return;
            }

            surface.Log(
                platform: "max",
                source: adInfo.NetworkName ?? "",
                format: adInfo.AdFormat ?? "",
                adUnit: adInfo.AdUnitIdentifier ?? "",
                currency: "USD",
                revenue: adInfo.Revenue);
        }
    }
}
#endif
