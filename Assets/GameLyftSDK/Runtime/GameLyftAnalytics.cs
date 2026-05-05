using System.Collections.Generic;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// GameLyft slim analytics SDK. Sends events exclusively to Firebase Analytics
    /// via an internal persistent queue.
    ///
    /// USAGE:
    ///   1. Initialize Firebase yourself (Firebase.FirebaseApp.CheckAndFixDependenciesAsync).
    ///   2. After Firebase is ready, call GameLyftAnalytics.Initialize() ONCE.
    ///   3. Call TrackEvent / TrackFTUE / TrackLevelProgression / ReportAdRevenue freely.
    ///
    /// All calls before Initialize() are queued and drained automatically once
    /// Initialize() is called.
    /// </summary>
    public static class GameLyftAnalytics
    {
        private const string SESSION_KEY = "GLSdk_session";
        private const int GA4_MAX_PARAMS = 25;

        private static bool _isInitialized;
        private static bool _testMode;
        internal static bool _autoInitPolling;

        /// <summary>
        /// Ad revenue sub-surface. Mediation-specific DLLs (GameLyft.Sdk.AdMob, GameLyft.Sdk.Max)
        /// attach strongly-typed Report() methods here via extension methods. You can also call
        /// Log() directly for unsupported mediations.
        ///
        ///   GameLyftAnalytics.AdRevenue.Log("ironsource", "vungle", "rewarded", "unit_x", "USD", 0.014);
        ///   GameLyftAnalytics.AdRevenue.Report(adValue, responseInfo, "interstitial", adUnit);  // AdMob
        ///   GameLyftAnalytics.AdRevenue.Report(adInfo);                                         // AppLovin MAX
        /// </summary>
        public static readonly AdRevenueSurface AdRevenue = new AdRevenueSurface();

        /// <summary>
        /// Receiver type for ad revenue extension methods. Has no instance state — it's a
        /// namespace-like marker that mediation DLLs can attach Report() methods to via
        /// C# extension methods (since partial classes can't span assemblies).
        /// </summary>
        public sealed class AdRevenueSurface
        {
            internal AdRevenueSurface() { }

            /// <summary>
            /// Low-level revenue primitive. Fires a 'gl_ad_impression' Firebase event with the
            /// standard schema (ad_platform / ad_source / ad_format / ad_unit_name / currency /
            /// value / platform / session). Use this for mediations not supported first-class
            /// (ironSource, Unity Ads, TopOn, etc.). For AdMob or AppLovin MAX, prefer the
            /// strongly-typed Report() overloads from the mediation sub-packages.
            /// </summary>
            public void Log(string platform, string source, string format,
                string adUnit, string currency, double revenue)
            {
                EnsureDispatcher();

                // 'session' is injected at flush time by EventDispatcher so events queued
                // pre-Initialize() (and persisted across runs) report the correct, current
                // session number rather than 0 / a stale value.
                var adParams = new List<EventDispatcher.QueuedParameter>
                {
                    EventDispatcher.StringParam("ad_platform", platform ?? ""),
                    EventDispatcher.StringParam("ad_source", source ?? ""),
                    EventDispatcher.StringParam("ad_format", format ?? ""),
                    EventDispatcher.StringParam("ad_unit_name", adUnit ?? ""),
                    EventDispatcher.StringParam("currency", currency ?? "USD"),
                    EventDispatcher.DoubleParam("value", revenue),
                    EventDispatcher.StringParam("platform", "gameLyft"),
                };

                EventDispatcher.Instance.LogEvent("gl_ad_impression", adParams);
            }
        }

        /// <summary>True after Initialize() has been called.</summary>
        public static bool IsInitialized => _isInitialized;

        /// <summary>
        /// Monotonically increasing session counter. Increments once per app launch
        /// on the first Initialize() call. Auto-attached to every tracked event as
        /// the "session" parameter.
        /// </summary>
        public static int SessionCount { get; private set; }

        /// <summary>
        /// Call this AFTER your Firebase init has completed successfully.
        /// Spawns the internal event dispatcher and unblocks the queue.
        /// Idempotent - safe to call multiple times.
        /// </summary>
        public static void Initialize()
        {
            // Silent idempotent return — auto-init + manual Initialize() can both fire safely.
            if (_isInitialized) return;

            // Load settings (test mode flag)
            var settings = GameLyftSettings.LoadOrNull();
            _testMode = settings != null && settings.testMode;

            // Sanity-check: is Firebase actually up? We can't reach out and verify,
            // but FirebaseApp.DefaultInstance will throw or be null if not initialized.
            if (!IsFirebaseAvailable())
                Warn("Initialize() called but Firebase does not appear to be initialized. "
                     + "Call Firebase.FirebaseApp.CheckAndFixDependenciesAsync() and wait for "
                     + "DependencyStatus.Available BEFORE calling GameLyftAnalytics.Initialize().");

            SessionCount = PlayerPrefs.GetInt(SESSION_KEY, 0) + 1;
            PlayerPrefs.SetInt(SESSION_KEY, SessionCount);
            PlayerPrefs.Save();

            EventDispatcher.CreateAndStart();
            _isInitialized = true;
            Info("Initialize() complete. Session " + SessionCount
                + (_testMode ? " (TEST MODE)" : "") + ".");
        }

        /// <summary>
        /// Track an arbitrary event. Parameters can be string / int / long / float / double / bool.
        /// Other types are converted via ToString().
        /// </summary>
        public static void TrackEvent(string eventName, Dictionary<string, object> parameters = null)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            // Suppress this warning while auto-init is actively polling — events queued during
            // the polling window are expected and will drain once Initialize() fires.
            if (!_isInitialized && !_autoInitPolling)
                Warn("TrackEvent('" + eventName + "') called before Initialize(). Event will be "
                     + "queued and drained once Initialize() is called, but 'session' will be 0.");

            EnsureDispatcher();

            // 'session' is injected at flush time by EventDispatcher so events queued
            // pre-Initialize() report the correct session rather than 0.
            var firebaseParams = new List<EventDispatcher.QueuedParameter>
            {
                EventDispatcher.StringParam("event_type", "progression_analytics")
            };

            if (parameters != null)
            {
                // GA4 drops events with more than 25 params server-side. Warn loudly.
                // +2 accounts for the two we auto-inject (event_type at queue time, session at flush time).
                if (parameters.Count + 2 > GA4_MAX_PARAMS)
                    Warn("TrackEvent('" + eventName + "') has " + (parameters.Count + 2)
                         + " parameters; GA4's limit is " + GA4_MAX_PARAMS
                         + ". Extra params will be dropped server-side.");

                foreach (var kvp in parameters)
                {
                    if (kvp.Value == null) continue;

                    if (kvp.Value is string s)
                        firebaseParams.Add(EventDispatcher.StringParam(kvp.Key, s));
                    else if (kvp.Value is int i)
                        firebaseParams.Add(EventDispatcher.LongParam(kvp.Key, i));
                    else if (kvp.Value is long l)
                        firebaseParams.Add(EventDispatcher.LongParam(kvp.Key, l));
                    else if (kvp.Value is float f)
                        firebaseParams.Add(EventDispatcher.DoubleParam(kvp.Key, f));
                    else if (kvp.Value is double d)
                        firebaseParams.Add(EventDispatcher.DoubleParam(kvp.Key, d));
                    else if (kvp.Value is bool b)
                        firebaseParams.Add(EventDispatcher.StringParam(kvp.Key, b.ToString()));
                    else
                        firebaseParams.Add(EventDispatcher.StringParam(kvp.Key, kvp.Value.ToString()));
                }
            }

            EventDispatcher.Instance.LogEvent(eventName, firebaseParams);
        }

        /// <summary>
        /// Track an ad fill event. Records whether an ad was available at a given
        /// placement, along with network connectivity and current session count.
        /// Fires the 'ads_fill' Firebase event.
        /// </summary>
        public static void TrackAdFill(GLAdFormat adFormat, string placement, GLAdResult result)
        {
            TrackEvent("ads_fill", new Dictionary<string, object>
            {
                { "format", adFormat.ToString().ToLowerInvariant() },
                { "placement", placement ?? "" },
                { "result", result.ToString() },
                { "connection", Application.internetReachability != NetworkReachability.NotReachable }
            });
        }

        /// <summary>Track an FTUE (first-time user experience) funnel step.</summary>
        public static void TrackFTUE(int stepNumber, string stepName, FTUEState state)
        {
            TrackEvent("ftue_funnel", new Dictionary<string, object>
            {
                { "step", stepNumber },
                { "name", stepName ?? "" },
                { "state", state.ToString() }
            });
        }

        /// <summary>
        /// Track a level progression event. Auto-dedupes per (level, state) pair via PlayerPrefs
        /// so the same level_complete is never reported twice.
        /// </summary>
        public static void TrackLevelProgression(int levelNumber, LevelState state, Dictionary<string, object> levelData = null)
        {
            string dedupeKey = "GLSdk_lvl_" + levelNumber + "_" + state;
            if (PlayerPrefs.GetString(dedupeKey) == "true") return;

            if (state == LevelState.level_complete)
                TrackEvent("level_" + levelNumber + "_completed");

            var parameters = new Dictionary<string, object>
            {
                { "level_number", levelNumber },
                { "state", state.ToString() }
            };

            if (levelData != null)
            {
                foreach (var kvp in levelData)
                {
                    if (!parameters.ContainsKey(kvp.Key))
                        parameters[kvp.Key] = kvp.Value;
                }
            }

            TrackEvent("level_progression", parameters);
            PlayerPrefs.SetString(dedupeKey, "true");
        }

        private static void EnsureDispatcher()
        {
            // Allow events queued before Initialize() — they'll drain once Initialize() flips the flag.
            if (EventDispatcher.Instance == null)
                EventDispatcher.CreateAndStart();
        }

        /// <summary>
        /// Internal warn helper. Always logs to the console. When testMode is ON
        /// (via GameLyftSettings), also pushes the message to the on-screen overlay.
        /// </summary>
        internal static void Warn(string message)
        {
            Debug.LogWarning("[GameLyft] " + message);
            if (_testMode)
                GLDebugOverlay.Push(message);
        }

        /// <summary>
        /// Internal info helper. Always logs to the console so device logcat picks it up.
        /// Used for lifecycle milestones (auto-init polling, Initialize success) so
        /// integrators can verify startup on-device without extra instrumentation.
        /// </summary>
        internal static void Info(string message)
        {
            Debug.Log("[GameLyft] " + message);
        }

        /// <summary>
        /// Best-effort probe: does Firebase look initialized? We check FirebaseApp.DefaultInstance
        /// without forcing creation. Reflection used to avoid a hard compile-time dependency on
        /// any specific Firebase SDK version's API surface.
        /// </summary>
        internal static bool IsFirebaseAvailable()
        {
            try
            {
                var t = System.Type.GetType("Firebase.FirebaseApp, Firebase.App");
                if (t == null) return false;
                var prop = t.GetProperty("DefaultInstance",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop == null) return false;
                return prop.GetValue(null) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
