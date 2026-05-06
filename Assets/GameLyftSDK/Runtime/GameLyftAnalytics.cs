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
        /// MMP (mobile measurement partner) sub-surface. Per-MMP integration scripts
        /// (GameLyft.Sdk.SolarEngine, GameLyft.Sdk.AppsFlyer, GameLyft.Sdk.Adjust, ...)
        /// extract source/campaign/ad_set/creative from their SDK's attribution payload
        /// and call LogInstall(). Fires a one-shot 'mmp_install' Firebase event.
        /// </summary>
        public static readonly MmpSurface Mmp = new MmpSurface();

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

        /// <summary>
        /// MMP attribution receiver. Each MMP integration (Solar Engine, AppsFlyer, Adjust,
        /// Singular, Tenjin, ...) extracts its own SDK's attribution into the 4 standard
        /// fields and calls LogInstall(). One-shot guarded across runs so multiple MMPs in
        /// the same project can't double-fire 'mmp_install' — whichever MMP delivers
        /// attribution first wins, the rest no-op.
        /// </summary>
        public sealed class MmpSurface
        {
            private const string GUARD_KEY = "GLSdk_mmp_install_sent";

            internal MmpSurface() { }

            /// <summary>
            /// Fires 'mmp_install' once per device install with the 4 standard attribution
            /// fields. Subsequent calls (this run or any future run on the same device)
            /// are silent no-ops thanks to the PlayerPrefs guard.
            ///
            /// Falls back to "Organic" when source is null/empty so dashboards never see a
            /// blank acquisition channel.
            /// </summary>
            public void LogInstall(string source, string campaign, string adSet, string creative)
            {
                if (PlayerPrefs.GetInt(GUARD_KEY) == 1) return;

                EnsureDispatcher();

                var parameters = new List<EventDispatcher.QueuedParameter>
                {
                    EventDispatcher.StringParam("source", string.IsNullOrEmpty(source) ? "Organic" : source),
                    EventDispatcher.StringParam("campaign", campaign ?? ""),
                    EventDispatcher.StringParam("ad_set", adSet ?? ""),
                    EventDispatcher.StringParam("creative", creative ?? ""),
                };

                EventDispatcher.Instance.LogEvent("mmp_install", parameters);

                PlayerPrefs.SetInt(GUARD_KEY, 1);
                PlayerPrefs.Save();
            }

            /// <summary>True if 'mmp_install' has already been fired on this device. MMP
            /// integration scripts can use this to skip work (e.g. don't bother polling
            /// for attribution if we already reported the install).</summary>
            public bool IsInstallReported => PlayerPrefs.GetInt(GUARD_KEY) == 1;

            /// <summary>
            /// DIAGNOSTIC: emit the entire raw attribution payload from an MMP as a
            /// Firebase event so the actual SDK schema can be discovered from production
            /// data via BigQuery. Each MMP integration calls this with its own event name
            /// (e.g. "singular_attribution", "adjust_attribution", "appsflyer_attribution").
            ///
            /// Firebase guardrails baked in:
            ///   - 25-param limit: any keys past 24 are dropped, with a "_dropped" count param.
            ///   - 100-char value limit: longer string values are truncated.
            ///   - null values are skipped (Firebase rejects them anyway).
            ///   - non-string keys with disallowed chars (Firebase requires [A-Za-z0-9_]) are
            ///     sanitized to underscore.
            ///
            /// REMOVE THIS once the production schemas are confirmed and per-MMP field
            /// mappings have been hardened against the real payloads. It's a discovery
            /// tool, not a production telemetry stream.
            /// </summary>
            public void LogAttributionSchema(string firebaseEventName, Dictionary<string, object> attributionPayload)
            {
                if (string.IsNullOrEmpty(firebaseEventName)) return;
                if (attributionPayload == null || attributionPayload.Count == 0) return;

                EnsureDispatcher();

                var firebaseParams = new List<EventDispatcher.QueuedParameter>();
                int included = 0;
                int dropped = 0;

                foreach (var kvp in attributionPayload)
                {
                    if (kvp.Value == null) continue;

                    // Reserve one slot for the "_dropped" count if we hit the cap. GA4's hard
                    // limit is 25 params per event; going over silently drops on the server.
                    if (included >= 24)
                    {
                        dropped++;
                        continue;
                    }

                    string key = SanitizeKey(kvp.Key);
                    if (string.IsNullOrEmpty(key)) continue;

                    string val = kvp.Value.ToString();
                    if (val.Length > 100) val = val.Substring(0, 100);

                    firebaseParams.Add(EventDispatcher.StringParam(key, val));
                    included++;
                }

                if (dropped > 0)
                    firebaseParams.Add(EventDispatcher.LongParam("_dropped", dropped));

                EventDispatcher.Instance.LogEvent(firebaseEventName, firebaseParams);
            }

            // Firebase param keys must match [A-Za-z_][A-Za-z0-9_]{0,39}. Quick coercion:
            // replace anything illegal with '_' and prefix with '_' if first char is a digit.
            private static string SanitizeKey(string key)
            {
                if (string.IsNullOrEmpty(key)) return null;

                var chars = new System.Text.StringBuilder(key.Length);
                for (int i = 0; i < key.Length && chars.Length < 40; i++)
                {
                    char c = key[i];
                    bool isAlpha = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
                    bool isDigit = c >= '0' && c <= '9';
                    if (isAlpha || isDigit || c == '_')
                        chars.Append(c);
                    else
                        chars.Append('_');
                }
                if (chars.Length > 0 && chars[0] >= '0' && chars[0] <= '9')
                    chars.Insert(0, '_');
                return chars.ToString();
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

        /// <summary>
        /// Track a successful in-app purchase. Fires the 'gl_purchase' Firebase event.
        /// Call from your IAP callback AFTER the receipt has been validated. The SDK
        /// does not validate receipts itself.
        /// </summary>
        /// <param name="productId">SKU / product identifier from the store, e.g. "com.studio.game.coins_pack_small".</param>
        /// <param name="currency">ISO 4217 currency code, e.g. "USD". Falls back to "USD" if null/empty.</param>
        /// <param name="revenue">Revenue amount in the specified currency (the localized price the user paid).</param>
        /// <param name="productName">Optional human-readable product name. Omitted from the event if null/empty.</param>
        public static void TrackPurchase(
            string productId,
            string currency,
            double revenue,
            string productName = null)
        {
            if (string.IsNullOrEmpty(productId)) return;

            EnsureDispatcher();

            var parameters = new List<EventDispatcher.QueuedParameter>
            {
                EventDispatcher.StringParam("product_id", productId),
                EventDispatcher.StringParam("currency", string.IsNullOrEmpty(currency) ? "USD" : currency),
                EventDispatcher.DoubleParam("value", revenue),
                EventDispatcher.LongParam("success", 1),
            };

            if (!string.IsNullOrEmpty(productName))
                parameters.Add(EventDispatcher.StringParam("product_name", productName));

            EventDispatcher.Instance.LogEvent("gl_purchase", parameters);
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
