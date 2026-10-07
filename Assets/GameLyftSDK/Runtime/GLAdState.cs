using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Is a full-screen ad on screen right now? The OS pauses the app while an ad shows, which
    /// looks exactly like the player leaving; engagement uses this to keep ad time counted.
    /// Fed automatically by AppLovin MAX (GAMELYFT_APPLOVIN: in real time from MAX's native
    /// callback, see GLMaxRealtimeHook, plus MAX's C# events as a fallback) and by the game's
    /// GameLyftAnalytics.AdStarted() / AdClosed() calls for AdMob (GAMELYFT_ADMOB).
    /// Thread-safe: MAX reports from a background thread while the main thread is paused.
    /// Ads are tracked per ad unit, so the same ad reported twice is counted once.
    /// </summary>
    internal static class GLAdState
    {
        /// <summary>Ad time counted per ad at most; an ad "showing" longer than this is treated as
        /// closed (a lost close callback, or the player left the app while the ad was up).</summary>
        internal const long MAX_AD_MS = 120000;

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, long> _open = new Dictionary<string, long>();   // ad key -> start timestamp
        private static long _lastStartTs = long.MinValue;   // Stopwatch.GetTimestamp()
        private static long _lastCloseTs = long.MinValue;

        /// <summary>Raised on the reporting thread when an ad starts (not raised again for the same ad).</summary>
        internal static event Action<long> AdStarted;

        private static long MaxAdTicks => MAX_AD_MS * Stopwatch.Frequency / 1000;

        internal static bool IsShowing
        {
            get
            {
                lock (_lock)
                {
                    long now = Stopwatch.GetTimestamp();
                    foreach (var start in _open.Values) if (now - start < MaxAdTicks) return true;
                    return false;
                }
            }
        }

        internal static void Started(string source, string key = null)
        {
            key = source + ":" + (string.IsNullOrEmpty(key) ? "ad" : key);
            long now = Stopwatch.GetTimestamp();
            bool fresh;
            lock (_lock)
            {
                fresh = !_open.TryGetValue(key, out var since) || now - since >= MaxAdTicks;
                if (fresh) { _open[key] = now; _lastStartTs = now; }
            }
            if (!fresh) return;
            GLLog.Trace("Ad started (" + source + ") — engagement keeps counting while it shows.");
            try { AdStarted?.Invoke(now); } catch (Exception e) { GLLog.Warn("Ad start handler failed: " + e.Message); }
        }

        internal static void Closed(string source, string key = null)
        {
            key = source + ":" + (string.IsNullOrEmpty(key) ? "ad" : key);
            bool was;
            lock (_lock)
            {
                was = _open.Remove(key);
                if (was) _lastCloseTs = Stopwatch.GetTimestamp();
            }
            if (was) GLLog.Trace("Ad closed (" + source + ").");
        }

        /// <summary>
        /// Was a full-screen ad shown AND closed during a pause that began at this
        /// Stopwatch.GetTimestamp() value? Fallback for when the start could not be seen in real time
        /// (both callbacks then arrive right after resume). An ad that opens on resume (e.g. app open)
        /// is still showing, so it does not match.
        /// </summary>
        internal static bool ShownAndClosedSince(long timestamp)
        {
            lock (_lock)
                return _lastStartTs != long.MinValue && _lastStartTs >= timestamp
                    && _lastCloseTs >= _lastStartTs && _open.Count == 0;
        }
    }

#if GAMELYFT_APPLOVIN
    /// <summary>AppLovin MAX: full-screen ad shown / hidden through MAX's C# events. These are
    /// delivered on the main thread, i.e. only after an ad that paused the app closes; the real-time
    /// signal comes from GLMaxRealtimeHook, and these stay as the fallback (same ad = counted once).</summary>
    internal static class GLMaxAdHooks
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Subscribe()
        {
            try
            {
                MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += (unit, info) => GLAdState.Started("max", unit);
                MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += (unit, info) => GLAdState.Closed("max", unit);
                MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (unit, err, info) => GLAdState.Closed("max", unit);
                MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += (unit, info) => GLAdState.Started("max", unit);
                MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += (unit, info) => GLAdState.Closed("max", unit);
                MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += (unit, err, info) => GLAdState.Closed("max", unit);
                MaxSdkCallbacks.AppOpen.OnAdDisplayedEvent += (unit, info) => GLAdState.Started("max", unit);
                MaxSdkCallbacks.AppOpen.OnAdHiddenEvent += (unit, info) => GLAdState.Closed("max", unit);
                MaxSdkCallbacks.AppOpen.OnAdDisplayFailedEvent += (unit, err, info) => GLAdState.Closed("max", unit);
            }
            catch (Exception e)
            {
                GLLog.Warn("Could not subscribe to AppLovin MAX ad callbacks: " + e.Message);
            }
        }
    }
#endif
}
