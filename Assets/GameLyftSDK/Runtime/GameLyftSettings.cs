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

        [Tooltip("When ON, SDK integration warnings appear as an on-screen IMGUI panel in addition to the console. When OFF, warnings go only to the console.")]
        public bool testMode = false;

        [Tooltip("When ON, the SDK polls for Firebase initialization at app start and calls Initialize() automatically once Firebase is ready. No code changes required on your side.")]
        public bool autoInitialize = false;

        public static GameLyftSettings LoadOrNull()
        {
            return Resources.Load<GameLyftSettings>(RESOURCES_LOAD_PATH);
        }
    }
}
