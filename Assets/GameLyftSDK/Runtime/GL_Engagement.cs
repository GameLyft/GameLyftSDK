using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Engagement: fires 'gl_engagement' { session_number, session_id, time_ms } where time_ms is the
    /// TOTAL foreground time of this session (not since the last event):
    ///   - at session start (time_ms = 0), so even the shortest session is counted;
    ///   - every 30 seconds while in the foreground;
    ///   - when the app goes to the background (so the last seconds count).
    /// Sessions per user = distinct session_id; session length = the largest time_ms per session.
    ///
    /// Runs on real time, so Time.timeScale = 0 (pause menus) does not stop it. The clock pauses in
    /// the background and resumes on return — except while a full-screen ad is showing (the OS
    /// pauses the app then too), which stays counted (see GLAdState). AppLovin MAX reports an ad as
    /// "displayed" to Unity's main thread only after the app resumes; GLMaxRealtimeHook reports it
    /// the moment the ad is on screen (about half a second after the pause it causes), and the pause
    /// then becomes ad time on the spot: the clock runs again. Without the hook, a pause is re-checked
    /// on resume and credited back. Either way ad time is capped at GLAdState.MAX_AD_MS per ad.
    ///
    /// The background event is handed to the platform SDKs immediately (the game may be killed in
    /// the background), and the 30 s heartbeat restarts on resume (no near-duplicate on return).
    ///
    /// Lives on the GameLyft prefab in the first scene; one instance for the whole app run.
    /// </summary>
    [DisallowMultipleComponent]
    public class GL_Engagement : MonoBehaviour
    {
        private const float HEARTBEAT_SECONDS = 30f;
        private const float AD_CALLBACK_GRACE_SECONDS = 3f;   // how long after resume a late "ad displayed" may arrive
        private const double AD_START_MARGIN_SECONDS = 2.0;     // an ad that started just before the pause also counts
        private const double AD_LIVE_WINDOW_SECONDS = 3.0;      // an ad reported this soon after a pause caused it

        [Tooltip("Local debugging: logs every step of engagement tracking to the console "
            + "([GameLyft.Engagement] prefix) — start, each heartbeat, pause / resume, ad state, "
            + "excluded ad time, every gl_engagement sent with its time_ms and gl_eid. Turn OFF for release builds.")]
        [SerializeField] private bool debugLogs = false;

        private static GL_Engagement _instance;
        private static readonly Stopwatch _sinceStart = Stopwatch.StartNew();   // log clock, any thread
        private readonly object _clock = new object();   // the stopwatches are touched from MAX's thread too
        private readonly Stopwatch _foreground = new Stopwatch();
        private readonly Stopwatch _pausedForAd = new Stopwatch();
        private readonly Stopwatch _background = new Stopwatch();   // length of the current/last real pause
        private long _excludedMs;      // ad pauses longer than GLAdState.MAX_AD_MS
        private long _creditedMs;      // pauses found afterwards to be full-screen ads (late MAX callback)
        private long _pauseStartTs;    // Stopwatch.GetTimestamp() when the last real pause began
        private bool _started;
        private int _beat;
        private Coroutine _heartbeat;
        private Coroutine _adCheck;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Dbg("Awake: another GL_Engagement already runs (on '" + _instance.gameObject.name
                    + "') — removing this duplicate on '" + gameObject.name + "'.");
                Destroy(this);
                return;
            }
            _instance = this;
            GLAdState.AdStarted += OnAdStartedAnyThread;
            Dbg("Awake: engagement instance on '" + gameObject.name + "' (scene '" + gameObject.scene.name
                + "'). Session " + GLSession.Number + ", id " + GLSession.Id + ".");
        }

        private void Start()
        {
            if (_instance != this) return;
            _foreground.Start();
            _started = true;
            Dbg("Start: foreground clock started. Heartbeat every " + HEARTBEAT_SECONDS + "s (real time). Sending session-start event.");
            Send("session start");
            RestartHeartbeat();
        }

        /// <summary>(Re)start the 30 s heartbeat from now. Called at start and on every resume, so
        /// coming back from the background never fires an overdue beat right away.</summary>
        private void RestartHeartbeat()
        {
            if (_heartbeat != null) StopCoroutine(_heartbeat);
            _heartbeat = StartCoroutine(Heartbeat());
        }

        private IEnumerator Heartbeat()
        {
            var wait = new WaitForSecondsRealtime(HEARTBEAT_SECONDS);
            while (true)
            {
                yield return wait;
                _beat++;
                Dbg("Heartbeat #" + _beat + ": clock " + (_foreground.IsRunning ? "running" : "STOPPED")
                    + ", Time.timeScale " + Time.timeScale + ", ad showing " + GLAdState.IsShowing + ".");
                Send("heartbeat #" + _beat);
            }
        }

        /// <summary>After a pause that looked like leaving the app: if a full-screen ad turns out to have
        /// run during it (MAX reports "displayed" and "hidden" only after resume), count the paused time.</summary>
        private IEnumerator CheckPauseWasAd(long pausedMs, long since)
        {
            float until = Time.realtimeSinceStartup + AD_CALLBACK_GRACE_SECONDS;
            while (Time.realtimeSinceStartup < until)
            {
                if (GLAdState.ShownAndClosedSince(since))
                {
                    long credit = pausedMs < GLAdState.MAX_AD_MS ? pausedMs : GLAdState.MAX_AD_MS;
                    lock (_clock) _creditedMs += credit;
                    Dbg("The pause was a full-screen ad (its callback arrived after resume) — counting " + credit
                        + " ms of it (total credited " + _creditedMs + " ms), time_ms now " + TimeMs() + ".");
                    _adCheck = null;
                    yield break;
                }
                yield return null;
            }
            Dbg("No ad started during the " + pausedMs + " ms pause — the player left the app (not counted).");
            _adCheck = null;
        }

        /// <summary>
        /// Any thread (AppLovin MAX reports from its own thread while the main thread is paused): an ad
        /// went on screen. If the app was paused a moment ago, the ad is why — count from the pause on.
        /// </summary>
        private void OnAdStartedAnyThread(long timestamp)
        {
            long gap;
            lock (_clock)
            {
                if (!_started || !_background.IsRunning) return;
                if (_background.Elapsed.TotalSeconds > AD_LIVE_WINDOW_SECONDS) return;   // the player had left: a later ad is unrelated
                gap = _background.ElapsedMilliseconds;
                _background.Reset();
                _creditedMs += gap;          // the moment between the pause and "displayed"
                _foreground.Start();
                _pausedForAd.Restart();
            }
            Dbg("Ad displayed " + gap + " ms after the pause (reported live) — the pause is an ad: clock running again, "
                + "time_ms now " + TimeMs() + ".");
        }

        private void OnApplicationPause(bool paused)
        {
            if (!_started) return;
            if (paused)
            {
                lock (_clock)
                {
                    if (GLAdState.IsShowing)
                    {
                        // A full-screen ad paused the app: keep counting.
                        _pausedForAd.Restart();
                        Dbg("Pause: a full-screen ad is showing — clock KEEPS running (ad time counts as engagement).");
                        return;
                    }
                    Dbg("Pause: app went to the background — stopping the clock, sending now.");
                    _foreground.Stop();
                    _pauseStartTs = Stopwatch.GetTimestamp();
                    _background.Restart();
                }
                if (_adCheck != null) { StopCoroutine(_adCheck); _adCheck = null; }
                Send("background", deliverNow: true);
            }
            else
            {
                bool lateCheck = false;
                long pausedMs = 0, since = 0;
                lock (_clock)
                {
                if (_pausedForAd.IsRunning)
                {
                    _pausedForAd.Stop();
                    // The player may have left while the ad was up: count at most MAX_AD_MS of it.
                    long over = _pausedForAd.ElapsedMilliseconds - GLAdState.MAX_AD_MS;
                    if (over > 0) _excludedMs += over;
                    Dbg("Resume after an ad pause of " + _pausedForAd.ElapsedMilliseconds + " ms"
                        + (over > 0 ? " — longer than the " + GLAdState.MAX_AD_MS + " ms ad cap, excluding " + over
                            + " ms (total excluded " + _excludedMs + " ms)." : " (counted in full)."));
                }
                bool wasStopped = !_foreground.IsRunning;
                if (wasStopped) _foreground.Start();
                Dbg("Resume: back in the foreground — clock " + (wasStopped ? "restarted" : "was already running")
                    + ", time_ms now " + TimeMsLocked() + ". Heartbeat restarts (next in " + HEARTBEAT_SECONDS + "s).");
                if (_background.IsRunning)
                {
                    // no ad was reported live during the pause: check whether one shows up late
                    _background.Stop();
                    lateCheck = true;
                    pausedMs = _background.ElapsedMilliseconds;
                    since = _pauseStartTs - (long)(AD_START_MARGIN_SECONDS * Stopwatch.Frequency);
                }
                }
                if (lateCheck) _adCheck = StartCoroutine(CheckPauseWasAd(pausedMs, since));
                RestartHeartbeat();
            }
        }

        private void OnApplicationQuit()
        {
            Dbg("Quit: sending final event.");
            if (_started) Send("quit", deliverNow: true);
        }

        private void OnDestroy()
        {
            GLAdState.AdStarted -= OnAdStartedAnyThread;
            if (_instance == this)
            {
                _instance = null;
                Dbg("OnDestroy: engagement instance destroyed — no more heartbeats this session "
                    + "(was the GameLyft prefab destroyed or its scene unloaded without DontDestroyOnLoad?).");
            }
        }

        private long TimeMs()
        {
            lock (_clock) return TimeMsLocked();
        }

        private long TimeMsLocked()
        {
            long ms = _foreground.ElapsedMilliseconds + _creditedMs - _excludedMs;
            return ms < 0 ? 0 : ms;
        }

        private void Send(string reason, bool deliverNow = false)
        {
            try
            {
                long ms = TimeMs();
                var e = GameLyftAnalytics.Emit("gl_engagement", new List<GLParam>
                {
                    GameLyftAnalytics.L("session_number", GLSession.Number),
                    GameLyftAnalytics.S("session_id", GLSession.Id),
                    GameLyftAnalytics.L("time_ms", ms),
                }, null);
                if (deliverNow) GLQueue.DeliverNow(e);
                Dbg("Sent gl_engagement (" + reason + "): time_ms " + ms + " (foreground " + _foreground.ElapsedMilliseconds
                    + " ms + ad credit " + _creditedMs + " ms − excluded " + _excludedMs + " ms), session_number " + GLSession.Number
                    + ", gl_eid " + e.eid + ", destinations [" + string.Join(", ", e.dests)
                    + "], queue holds " + GLQueue.PendingCount + " undelivered event(s).");
            }
            catch (System.Exception ex) { GLLog.Error("gl_engagement failed (" + reason + "): " + ex.Message); }
        }

        /// <summary>Local debugging log (the "Debug Logs" checkbox on this component).</summary>
        private void Dbg(string message)
        {
            if (!debugLogs) return;
            UnityEngine.Debug.Log("[GameLyft.Engagement] t=" + _sinceStart.Elapsed.TotalSeconds.ToString("0.0") + "s  " + message);
        }
    }
}
