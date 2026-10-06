using System.Diagnostics;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Is a full-screen ad on screen right now? The OS pauses the app while an ad shows, which
    /// looks exactly like the player leaving; engagement uses this to keep ad time counted.
    /// Fed automatically by AppLovin MAX callbacks (GAMELYFT_APPLOVIN) and by the game's
    /// GameLyftAnalytics.AdStarted() / AdClosed() calls for AdMob (GAMELYFT_ADMOB).
    /// </summary>
    internal static class GLAdState
    {
        /// <summary>An ad "showing" longer than this is treated as closed (a lost close callback).</summary>
        internal const long MAX_AD_MS = 180000;

        private static readonly Stopwatch _since = new Stopwatch();
        private static readonly object _lock = new object();
        private static int _open;
        private static long _lastStartTs = long.MinValue;   // Stopwatch.GetTimestamp() of the last Started()
        private static long _lastCloseTs = long.MinValue;   // Stopwatch.GetTimestamp() of the last Closed()

        internal static bool IsShowing
        {
            get
            {
                lock (_lock) return _open > 0 && _since.ElapsedMilliseconds < MAX_AD_MS;
            }
        }

        internal static void Started(string source)
        {
            lock (_lock)
            {
                if (_open <= 0 || _since.ElapsedMilliseconds >= MAX_AD_MS) { _open = 0; _since.Restart(); }
                _open++;
                _lastStartTs = Stopwatch.GetTimestamp();
            }
            GLLog.Trace("Ad started (" + source + ") — engagement keeps counting while it shows.");
        }

        /// <summary>
        /// Was a full-screen ad shown AND closed during a pause that began at this
        /// Stopwatch.GetTimestamp() value? AppLovin MAX delivers "displayed" and "hidden" only once the
        /// app resumes, so both arrive right after resume for an ad that ran while the app was paused.
        /// An ad that opens on resume (e.g. app open) is still showing, so it does not match.
        /// </summary>
        internal static bool ShownAndClosedSince(long timestamp)
        {
            lock (_lock)
                return _lastStartTs != long.MinValue && _lastStartTs >= timestamp
                    && _lastCloseTs >= _lastStartTs && _open <= 0;
        }

        internal static void Closed(string source)
        {
            lock (_lock) { if (_open > 0) _open--; _lastCloseTs = Stopwatch.GetTimestamp(); }
            GLLog.Trace("Ad closed (" + source + ").");
        }
    }

#if GAMELYFT_APPLOVIN
    /// <summary>AppLovin MAX: full-screen ad shown / hidden, subscribed automatically.</summary>
    internal static class GLMaxAdHooks
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Subscribe()
        {
            try
            {
                MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += (unit, info) => GLAdState.Started("max");
                MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += (unit, info) => GLAdState.Closed("max");
                MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (unit, err, info) => GLAdState.Closed("max");
                MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += (unit, info) => GLAdState.Started("max");
                MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += (unit, info) => GLAdState.Closed("max");
                MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += (unit, err, info) => GLAdState.Closed("max");
                MaxSdkCallbacks.AppOpen.OnAdDisplayedEvent += (unit, info) => GLAdState.Started("max");
                MaxSdkCallbacks.AppOpen.OnAdHiddenEvent += (unit, info) => GLAdState.Closed("max");
                MaxSdkCallbacks.AppOpen.OnAdDisplayFailedEvent += (unit, err, info) => GLAdState.Closed("max");
            }
            catch (System.Exception e)
            {
                GLLog.Warn("Could not subscribe to AppLovin MAX ad callbacks: " + e.Message);
            }
        }
    }
#endif
}
