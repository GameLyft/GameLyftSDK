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
    /// pauses the app then too), which stays counted (see GLAdState).
    ///
    /// Lives on the GameLyft prefab in the first scene; one instance for the whole app run.
    /// </summary>
    [DisallowMultipleComponent]
    public class GL_Engagement : MonoBehaviour
    {
        private const float HEARTBEAT_SECONDS = 30f;

        [Tooltip("Local debugging: logs every step of engagement tracking to the console "
            + "([GameLyft.Engagement] prefix) — start, each heartbeat, pause / resume, ad state, "
            + "excluded ad time, every gl_engagement sent with its time_ms and gl_eid. Turn OFF for release builds.")]
        [SerializeField] private bool debugLogs = false;

        private static GL_Engagement _instance;
        private readonly Stopwatch _foreground = new Stopwatch();
        private readonly Stopwatch _pausedForAd = new Stopwatch();
        private long _excludedMs;      // ad pauses longer than GLAdState.MAX_AD_MS
        private bool _started;

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
            StartCoroutine(Heartbeat());
        }

        private IEnumerator Heartbeat()
        {
            var wait = new WaitForSecondsRealtime(HEARTBEAT_SECONDS);
            int beat = 0;
            while (true)
            {
                yield return wait;
                beat++;
                Dbg("Heartbeat #" + beat + ": clock " + (_foreground.IsRunning ? "running" : "STOPPED")
                    + ", Time.timeScale " + Time.timeScale + ", ad showing " + GLAdState.IsShowing + ".");
                Send("heartbeat #" + beat);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!_started) return;
            if (paused)
            {
                if (GLAdState.IsShowing)
                {
                    // A full-screen ad paused the app: keep counting.
                    _pausedForAd.Restart();
                    Dbg("Pause: a full-screen ad is showing — clock KEEPS running (ad time counts as engagement).");
                    return;
                }
                Dbg("Pause: app went to the background — sending, then stopping the clock.");
                Send("background");
                _foreground.Stop();
            }
            else
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
                    + ", time_ms now " + TimeMs() + ".");
            }
        }

        private void OnApplicationQuit()
        {
            Dbg("Quit: sending final event.");
            if (_started) Send("quit");
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                Dbg("OnDestroy: engagement instance destroyed — no more heartbeats this session "
                    + "(was the GameLyft prefab destroyed or its scene unloaded without DontDestroyOnLoad?).");
            }
        }

        private long TimeMs()
        {
            long ms = _foreground.ElapsedMilliseconds - _excludedMs;
            return ms < 0 ? 0 : ms;
        }

        private void Send(string reason)
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
                Dbg("Sent gl_engagement (" + reason + "): time_ms " + ms + " (foreground " + _foreground.ElapsedMilliseconds
                    + " ms − excluded " + _excludedMs + " ms), session_number " + GLSession.Number
                    + ", gl_eid " + e.eid + ", destinations [" + string.Join(", ", e.dests)
                    + "], queue holds " + GLQueue.PendingCount + " undelivered event(s).");
            }
            catch (System.Exception ex) { GLLog.Error("gl_engagement failed (" + reason + "): " + ex.Message); }
        }

        /// <summary>Local debugging log (the "Debug Logs" checkbox on this component).</summary>
        private void Dbg(string message)
        {
            if (!debugLogs) return;
            UnityEngine.Debug.Log("[GameLyft.Engagement] t=" + Time.realtimeSinceStartup.ToString("0.0") + "s  " + message);
        }
    }
}
