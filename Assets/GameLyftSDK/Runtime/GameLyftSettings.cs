using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Per-project GameLyft settings (Tools → GameLyft → Settings). Ticking a destination or a
    /// mediation writes its scripting define on Apply, so the matching code compiles in only when
    /// that SDK is used.
    /// </summary>
    public class GameLyftSettings : ScriptableObject
    {
        public const string DEFAULT_ASSET_PATH = "Assets/GameLyftSDK/Resources/GameLyftSettings.asset";
        public const string RESOURCES_LOAD_PATH = "GameLyftSettings";
        public const int MAX_LEVEL_MILESTONES = 10;

        /// <summary>The SDK's own events (the rows of the Adjust token table).</summary>
        public static readonly string[] SdkEvents =
        {
            "gl_engagement", "gl_ad_impression", "gl_ad_fill", "gl_ftue", "gl_level", "gl_purchase",
        };

        [Serializable]
        public class AdjustToken
        {
            public string eventName;
            public string token;
        }

        // ── Destinations: where events are sent ─────────────────────────────────────
        [Tooltip("Send every event to Firebase Analytics. Defines GAMELYFT_FIREBASE. Requires the Firebase Unity SDK (Firebase.App + Firebase.Analytics); your game initializes Firebase as usual.")]
        public bool sendToFirebase = false;

        [Tooltip("Send every event to AppsFlyer. Defines GAMELYFT_APPSFLYER. Requires the AppsFlyer Unity SDK; your game initializes AppsFlyer as usual.")]
        public bool sendToAppsFlyer = false;

        [Tooltip("Send events to Adjust. Defines GAMELYFT_ADJUST. Requires the Adjust Unity SDK. Adjust only accepts events that have a token: fill the token table below for the SDK's events and pass adjustToken to TrackEvent for your own.")]
        public bool sendToAdjust = false;

        [Tooltip("Send every event to Solar Engine. Defines GAMELYFT_SOLAR_ENGINE. Requires the Solar Engine Unity SDK; your game calls initSeSdk() as usual.")]
        public bool sendToSolarEngine = false;

        [Tooltip("Send every event to Singular. Defines GAMELYFT_SINGULAR. Requires the Singular Unity SDK; your game initializes Singular as usual.")]
        public bool sendToSingular = false;

        [Tooltip("Send every event to Airbridge. Defines GAMELYFT_AIRBRIDGE. Requires the Airbridge Unity SDK.")]
        public bool sendToAirbridge = false;

        // ── Ad mediation: enables AdRevenue.Report(...) for gl_ad_impression ────────
        [Tooltip("AdMob mediation. Defines GAMELYFT_ADMOB: enables GameLyftAnalytics.AdRevenue.Report(AdValue, ...) and AdStarted()/AdClosed().")]
        public bool useAdMobMediation = false;

        [Tooltip("AppLovin MAX mediation. Defines GAMELYFT_APPLOVIN: enables GameLyftAnalytics.AdRevenue.Report(MaxSdkBase.AdInfo); full-screen ad time is tracked automatically.")]
        public bool useAppLovinMax = false;

        // ── Level milestones ────────────────────────────────────────────────────────
        [Tooltip("Also fire gl_level_<N>_completed (once per install) when one of these levels is completed — for MMP campaign optimisation.")]
        public bool sendLevelMilestones = false;

        [Tooltip("Up to 10 level numbers.")]
        public List<int> levelMilestones = new List<int>();

        // ── Adjust event tokens for the SDK's own events ───────────────────────────
        public List<AdjustToken> adjustTokens = new List<AdjustToken>();

        // ── Debug ───────────────────────────────────────────────────────────────────
        [Tooltip("Shows SDK warnings on an on-screen panel in addition to the console. Turn OFF before shipping.")]
        public bool testMode = false;

        [Tooltip("Detailed [GameLyft] console logs of every event and delivery. Turn OFF for production.")]
        public bool verboseLogging = false;

        public static GameLyftSettings LoadOrNull()
        {
            return Resources.Load<GameLyftSettings>(RESOURCES_LOAD_PATH);
        }
    }
}
