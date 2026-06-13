using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Per-project GameLyft SDK settings. Created via Tools → GameLyft → Settings.
    /// Toggling mediation flags writes the corresponding scripting define symbols
    /// (GAMELYFT_ADMOB / GAMELYFT_APPLOVIN) into Player Settings via the Editor UI.
    /// </summary>
    public class GameLyftSettings : ScriptableObject
    {
        public const string DEFAULT_ASSET_PATH = "Assets/GameLyftSDK/Resources/GameLyftSettings.asset";
        public const string RESOURCES_LOAD_PATH = "GameLyftSettings";

        [Tooltip("Enable AdMob mediation. Defines GAMELYFT_ADMOB. Required to call GameLyftAnalytics.AdRevenue.Report(AdValue, ...).")]
        public bool useAdMobMediation = false;

        [Tooltip("Enable AppLovin MAX mediation. Defines GAMELYFT_APPLOVIN. Required to call GameLyftAnalytics.AdRevenue.Report(MaxSdkBase.AdInfo).")]
        public bool useAppLovinMax = false;

        [Tooltip("Enable Solar Engine MMP attribution. Defines GAMELYFT_SOLAR_ENGINE. When ON, the SDK polls Solar Engine for attribution data after init and fires a one-shot 'mmp_install' Firebase event with the mapped fields. Requires Solar Engine Unity SDK in the project.")]
        public bool enableSolarEngineMmp = false;

        [Tooltip("Enable AppsFlyer MMP attribution. Defines GAMELYFT_APPSFLYER. When ON, the SDK auto-polls PlayerPrefs for AppsFlyer's conversion payload and fires a one-shot 'mmp_install' Firebase event mapped from media_source/campaign/adset/af_ad. Your AppsFlyer conversion handler must stash the raw payload in PlayerPrefs first — press the 'Wire AppsFlyer Handler' button below to inject the 3 lines automatically (or add them by hand; see the CONSUMER CONTRACT in AppsFlyerMmp.cs). Also emits an 'appsflyer_attribution' diagnostic event with the full payload. Requires AppsFlyer Unity SDK in the project.")]
        public bool enableAppsFlyerMmp = false;

        [Tooltip("Enable Adjust MMP attribution. Defines GAMELYFT_ADJUST. When ON, the SDK polls Adjust.GetAttribution() after init and fires a one-shot 'mmp_install' Firebase event mapped from Network/Campaign/Adgroup/Creative. Requires Adjust Unity SDK in the project.")]
        public bool enableAdjustMmp = false;

        [Tooltip("Enable Singular MMP attribution. Defines GAMELYFT_SINGULAR. When ON, the SDK auto-registers a SingularDeviceAttributionCallbackHandler and fires a one-shot 'mmp_install' Firebase event mapped with best-guess keys (network/campaign_name/creative_name — Singular's device callback schema isn't publicly documented). Also emits a 'singular_attribution' diagnostic event with the full raw payload so the real schema can be confirmed from BigQuery. Requires Singular Unity SDK in the project.")]
        public bool enableSingularMmp = false;

        [Tooltip("Enable Tenjin MMP attribution. Defines GAMELYFT_TENJIN. When ON, the SDK observes the consumer's existing BaseTenjin instance and polls GetAttributionInfo() to fire a one-shot 'mmp_install' Firebase event mapped from ad_network/campaign_name/creative_name (the documented Tenjin schema). Also emits a 'tenjin_attribution' diagnostic event with the full payload. Requires Tenjin Unity SDK in the project AND that the consumer initializes Tenjin themselves via Tenjin.getInstance(apiKey) within ~3 minutes of app launch.")]
        public bool enableTenjinMmp = false;

        [Tooltip("When ON, SDK integration warnings appear as an on-screen IMGUI panel in addition to the console. When OFF, warnings go only to the console.")]
        public bool testMode = false;

        [Tooltip("Verbose logging: detailed [GameLyft] console logs of ALL SDK activity — every event tracked, queue enqueue + flush to Firebase (with parameters), purchases, ad-impression revenue, and MMP attribution polling. Lifecycle milestones, warnings, and errors always log regardless. Turn OFF for production (high volume).")]
        public bool verboseLogging = false;

        [Tooltip("When ON, the SDK polls for Firebase initialization at app start and calls Initialize() automatically once Firebase is ready. No code changes required on your side.")]
        public bool autoInitialize = false;

        public static GameLyftSettings LoadOrNull()
        {
            return Resources.Load<GameLyftSettings>(RESOURCES_LOAD_PATH);
        }
    }
}
