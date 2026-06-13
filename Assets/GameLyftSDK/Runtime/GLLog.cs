using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Central GameLyft SDK logger. All output is prefixed "[GameLyft]".
    ///
    ///   Trace  — detailed, high-volume per-activity logging (events tracked, queue
    ///            enqueue/flush, Firebase sends with params, attribution polling).
    ///            Logged ONLY when "Verbose Logging" is enabled in GameLyft Settings.
    ///   Info   — low-volume lifecycle milestones (Initialize, session, auto-init).
    ///            Always logged.
    ///   Warn   — integration/misuse warnings. Always logged; also pushed to the
    ///            on-screen Test Mode overlay when Test Mode is on.
    ///   Error  — failures. Always logged.
    ///
    /// Cached settings flags are loaded lazily on first use (so MMP modules that run at
    /// BeforeSceneLoad log correctly before Initialize()) and refreshed by
    /// GameLyftAnalytics.Initialize() via Configure().
    /// </summary>
    public static class GLLog
    {
        private const string PREFIX = "[GameLyft] ";

        private static bool _verbose;
        private static bool _testMode;
        private static bool _loaded;

        /// <summary>Refresh cached flags from the resolved settings (called by Initialize()).</summary>
        internal static void Configure(bool verboseLogging, bool testMode)
        {
            _verbose = verboseLogging;
            _testMode = testMode;
            _loaded = true;
        }

        /// <summary>True when Verbose Logging is enabled. Guard expensive log-string building with this.</summary>
        public static bool IsVerbose
        {
            get { EnsureLoaded(); return _verbose; }
        }

        /// <summary>Detailed activity log. No-op unless Verbose Logging is enabled in Settings.</summary>
        public static void Trace(string message)
        {
            EnsureLoaded();
            if (_verbose) Debug.Log(PREFIX + message);
        }

        /// <summary>Lifecycle milestone. Always logged.</summary>
        public static void Info(string message)
        {
            Debug.Log(PREFIX + message);
        }

        /// <summary>Integration/misuse warning. Always logged; mirrored to the Test Mode overlay.</summary>
        public static void Warn(string message)
        {
            EnsureLoaded();
            Debug.LogWarning(PREFIX + message);
            if (_testMode) GLDebugOverlay.Push(message);
        }

        /// <summary>Failure. Always logged.</summary>
        public static void Error(string message)
        {
            Debug.LogError(PREFIX + message);
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            var s = GameLyftSettings.LoadOrNull();
            _verbose = s != null && s.verboseLogging;
            _testMode = s != null && s.testMode;
            _loaded = true;
        }
    }
}
