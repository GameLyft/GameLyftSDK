using System.Collections.Generic;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// GameLyft SDK: one call per event, delivered to every destination ticked in
    /// Tools → GameLyft → Settings (Firebase, AppsFlyer, Adjust, Solar Engine, Singular, Airbridge).
    ///
    /// Every event is written to a durable on-disk queue first and delivered to each destination
    /// once that SDK has started and the device is online, so none is lost. Every event carries
    /// gl_eid (unique id), gl_ts (event time, Unix seconds) and gl_sid (session id).
    ///
    /// Put the GameLyft prefab (GameLyftSDK/Runtime/Prefabs/GameLyft.prefab) in the first scene:
    /// it initializes the SDK and measures engagement (gl_engagement). Calls are safe from any
    /// thread and before initialization — they are queued.
    /// </summary>
    public static class GameLyftAnalytics
    {
        private const int MAX_MILESTONES = 10;
        private const string MILESTONE_SENT_KEY = "GLSdk_ms_";

        private static bool _isInitialized;
        private static GameLyftSettings _settings;

        /// <summary>Ad revenue: AdMob / AppLovin MAX Report() overloads attach here; Log() for any other mediation.</summary>
        public static readonly AdRevenueSurface AdRevenue = new AdRevenueSurface();

        public sealed class AdRevenueSurface
        {
            internal AdRevenueSurface() { }

            /// <summary>Fires 'gl_ad_impression' (one per paid impression).</summary>
            public void Log(string platform, string source, string format, string adUnit, string currency, double revenue)
            {
                Emit("gl_ad_impression", new List<GLParam>
                {
                    S("ad_platform", platform), S("ad_source", source), S("ad_format", format),
                    S("ad_unit_name", adUnit), S("currency", string.IsNullOrEmpty(currency) ? "USD" : currency),
                    D("value", revenue), S("platform", "gameLyft"),
                }, null);
            }
        }

        public static bool IsInitialized => _isInitialized;

        /// <summary>Session of this app launch (gl_sid).</summary>
        public static string SessionId => GLSession.Id;

        /// <summary>How many sessions (launches) this install has had.</summary>
        public static int SessionNumber => GLSession.Number;

        /// <summary>
        /// Tell GameLyft that a platform's SDK has finished initializing; its queued events start
        /// flowing to it. Call once per ticked destination, from your own init code — the SDK never
        /// probes Firebase or the MMP SDKs itself. Safe from any thread (e.g. a ContinueWith callback).
        ///
        ///   Firebase     after CheckAndFixDependenciesAsync() reports DependencyStatus.Available
        ///   AppsFlyer    after AppsFlyer.startSDK()
        ///   Adjust       after Adjust.InitSdk(config)
        ///   SolarEngine  in Solar Engine's init-completed callback (or after initSeSdk())
        ///   Singular     after Singular is initialized
        ///   Airbridge    at app start (Airbridge initializes natively from its settings)
        /// </summary>
        public static void MarkReady(GLDestination destination)
        {
            if (GLReadiness.Mark(destination))
                GLLog.Info(GLReadiness.IdOf(destination) + " marked ready — delivering its events.");
        }

        /// <summary>Called by the GameLyft prefab. Idempotent. Safe to call yourself too.</summary>
        public static void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            var s = Settings;
            GLLog.Configure(s != null && s.verboseLogging, s != null && s.testMode);
            if (GLDestinationRegistry.All.Count == 0)
                GLLog.Warn("No destination is enabled. Tick Firebase or an MMP in Tools → GameLyft → Settings; "
                    + "events are kept on disk until one is.");
            GLLog.Info("Initialized. Session " + GLSession.Number + " (" + GLSession.Id + "), destinations: "
                + string.Join(", ", GLDestinationRegistry.Ids()) + ".");
        }

        /// <summary>
        /// Track a custom event. Values: string / int / long / float / double / bool (others via ToString()).
        /// Pass the Adjust event token to also send it to Adjust (Adjust only accepts events created in
        /// its dashboard); without a token the event goes to every other ticked destination.
        /// </summary>
        public static void TrackEvent(string eventName, Dictionary<string, object> parameters = null, string adjustToken = "")
        {
            if (string.IsNullOrEmpty(eventName)) return;
            var ps = new List<GLParam>();
            if (parameters != null)
                foreach (var kv in parameters)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                    ps.Add(ToParam(kv.Key, kv.Value));
                }
            Emit(eventName, ps, adjustToken);
        }

        /// <summary>Was an ad available when the game asked for one? Fires 'gl_ad_fill'.</summary>
        public static void TrackAdFill(GLAdFormat adFormat, string placement, GLAdResult result)
        {
            Emit("gl_ad_fill", new List<GLParam>
            {
                S("format", adFormat.ToString().ToLowerInvariant()), S("placement", placement),
                S("result", result.ToString()),
                S("connection", GLMain.Online ? "True" : "False"),
            }, null);
        }

        /// <summary>FTUE (onboarding) funnel step. Fires 'gl_ftue'.</summary>
        public static void TrackFTUE(int stepNumber, string stepName, FTUEState state)
        {
            Emit("gl_ftue", new List<GLParam> { L("step", stepNumber), S("name", stepName), S("state", state.ToString()) }, null);
        }

        /// <summary>
        /// Level event. Fires 'gl_level' on EVERY call (starts, fails and retries are all counted).
        /// When the level is completed and listed as a milestone in Settings, also fires
        /// 'gl_level_&lt;N&gt;_completed' once per install.
        /// </summary>
        public static void TrackLevelProgression(int levelNumber, LevelState state, Dictionary<string, object> levelData = null)
        {
            var ps = new List<GLParam> { L("level_number", levelNumber), S("state", state.ToString()) };
            if (levelData != null)
                foreach (var kv in levelData)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null || kv.Key == "level_number" || kv.Key == "state") continue;
                    ps.Add(ToParam(kv.Key, kv.Value));
                }
            Emit("gl_level", ps, null);

            if (state == LevelState.level_complete) GLMain.Run(() => MaybeMilestone(levelNumber));
        }

        /// <summary>Validated in-app purchase. Fires 'gl_purchase'.</summary>
        public static void TrackPurchase(string productId, string currency, double revenue, string productName = null)
        {
            if (string.IsNullOrEmpty(productId)) return;
            var ps = new List<GLParam>
            {
                S("product_id", productId), S("currency", string.IsNullOrEmpty(currency) ? "USD" : currency),
                D("value", revenue), L("success", 1),
            };
            if (!string.IsNullOrEmpty(productName)) ps.Add(S("product_name", productName));
            Emit("gl_purchase", ps, null);
        }

#if GAMELYFT_ADMOB
        /// <summary>
        /// AdMob: call when a full-screen ad (interstitial, rewarded, app open) opens, e.g. from
        /// OnAdFullScreenContentOpened. While an ad is on screen the app is paused by the OS, and
        /// this keeps that time counted as engagement.
        /// </summary>
        public static void AdStarted() => GLAdState.Started("admob");

        /// <summary>AdMob: call when the full-screen ad closes (OnAdFullScreenContentClosed / ...Failed).</summary>
        public static void AdClosed() => GLAdState.Closed("admob");
#endif

        // ── internals ────────────────────────────────────────────────────────────────────────

        /// <summary>Loaded on the main thread before the first scene (GLMain.Preload); cached after.</summary>
        internal static GameLyftSettings Settings
        {
            get
            {
                if (_settings == null && GLMain.IsMainThread) _settings = GameLyftSettings.LoadOrNull();
                return _settings;
            }
        }

        /// <summary>Build, persist and queue one event. Thread-safe.</summary>
        internal static GLEvent Emit(string name, List<GLParam> ps, string adjustToken)
        {
            var e = new GLEvent
            {
                eid = GLIds.New(10),
                name = name,
                ts = GLIds.NowUnixSeconds(),
                sid = GLSession.Id,
                ps = ps ?? new List<GLParam>(),
                adj = string.IsNullOrEmpty(adjustToken) ? FixedAdjustToken(name) : adjustToken,
                dests = GLDestinationRegistry.DeliveryList(),
            };
            GLStore.Add(e);
            if (GLLog.IsVerbose) GLLog.Trace("Tracked '" + name + "' (gl_eid " + e.eid + ") → " + string.Join(", ", e.dests));
            return e;
        }

        private static void MaybeMilestone(int level)
        {
            var s = Settings;
            if (s == null || !s.sendLevelMilestones || s.levelMilestones == null) return;
            int n = 0;
            foreach (int m in s.levelMilestones)
            {
                if (++n > MAX_MILESTONES) break;
                if (m != level) continue;
                string key = MILESTONE_SENT_KEY + level;
                if (PlayerPrefs.GetInt(key, 0) == 1) return;
                PlayerPrefs.SetInt(key, 1);
                PlayerPrefs.Save();
                Emit("gl_level_" + level + "_completed", new List<GLParam>(), null);
                return;
            }
        }

        /// <summary>Adjust token configured in Settings for one of the SDK's own events.</summary>
        private static string FixedAdjustToken(string eventName)
        {
            var s = Settings;
            if (s == null || s.adjustTokens == null) return "";
            foreach (var t in s.adjustTokens)
                if (t != null && t.eventName == eventName) return t.token ?? "";
            return "";
        }

        private static GLParam ToParam(string key, object v)
        {
            switch (v)
            {
                case string x: return S(key, x);
                case int x: return L(key, x);
                case long x: return L(key, x);
                case short x: return L(key, x);
                case float x: return D(key, x);
                case double x: return D(key, x);
                case decimal x: return D(key, (double)x);
                case bool x: return S(key, x ? "True" : "False");
                default: return S(key, v.ToString());
            }
        }

        internal static GLParam S(string k, string v) => new GLParam(k, v ?? "", "s");
        internal static GLParam L(string k, long v) => new GLParam(k, v.ToString(System.Globalization.CultureInfo.InvariantCulture), "l");
        internal static GLParam D(string k, double v) => new GLParam(k, v.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "d");

        internal static void Warn(string message) => GLLog.Warn(message);
        internal static void Info(string message) => GLLog.Info(message);
    }
}
