using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Durable event store: an append-only file in persistentDataPath, written by ONE background
    /// thread so the game never blocks on disk.
    ///
    ///   E{json}            an event (written the moment it is tracked, before any send)
    ///   A {eid} {dest}     the event was handed to that destination
    ///   R {eid} {d1,d2}    an unassigned event was given these destinations
    ///
    /// On launch the file is replayed: every event with a destination it was not acknowledged for
    /// is delivered again. A crash between a hand-off and its "A" line can therefore resend ONE
    /// event; it carries the same gl_eid, which the platform (and Adjust) dedupe on. So delivery is
    /// at least once and nothing is lost. The file is compacted (rewritten with only undelivered
    /// events) from time to time.
    /// </summary>
    internal static class GLStore
    {
        private const string DIR = "GameLyft";
        private const string FILE = "queue.log";
        private const string LEGACY_PREFS_KEY = "GLSdk_FbQ";   // previous SDK's PlayerPrefs queue

        private static readonly BlockingCollection<string> _lines = new BlockingCollection<string>(new ConcurrentQueue<string>());
        private static readonly ConcurrentQueue<GLEvent> _incoming = new ConcurrentQueue<GLEvent>();
        private static readonly ManualResetEventSlim _idle = new ManualResetEventSlim(true);
        private static Thread _writer;
        private static string _path;
        private static volatile int _pendingWrites;
        private const string COMPACT = "\u0001COMPACT";

        internal static string FilePath => _path;

        /// <summary>Main thread, once (BeforeSceneLoad): resolve the path and start the writer.</summary>
        internal static void Start()
        {
            if (_writer != null) return;
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, DIR);
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, FILE);
            }
            catch (Exception e)
            {
                Debug.LogError("[GameLyft] Event store unavailable (" + e.Message + "). Events are kept in memory only this run.");
                _path = null;
            }
            _writer = new Thread(WriterLoop) { IsBackground = true, Name = "GameLyft.Store" };
            _writer.Start();
        }

        /// <summary>Any thread: persist a new event and hand it to the dispatcher.</summary>
        internal static void Add(GLEvent e)
        {
            Write("E" + JsonUtility.ToJson(e));
            _incoming.Enqueue(e);
            GLQueue.Signal();   // the dispatcher thread delivers it right away
        }

        internal static bool TryTakeIncoming(out GLEvent e) => _incoming.TryDequeue(out e);

        /// <summary>Main thread: record a delivery.</summary>
        internal static void Ack(string eid, string dest) => Write("A " + eid + " " + dest);

        /// <summary>Main thread: an unassigned event now goes to these destinations.</summary>
        internal static void Reassign(string eid, List<string> dests) => Write("R " + eid + " " + string.Join(",", dests));

        /// <summary>
        /// Rewrite the file with only the undelivered events. Done on the writer thread FROM THE
        /// FILE ITSELF, so every line written before this request is taken into account (an event
        /// tracked from another thread at the same moment cannot be left out).
        /// </summary>
        internal static void Compact() => Write(COMPACT);

        /// <summary>Main thread, on pause / quit: wait (bounded) until everything queued is on disk.</summary>
        internal static void FlushBlocking(int timeoutMs)
        {
            if (_writer == null) return;
            _idle.Wait(timeoutMs);
        }

        private static void Write(string line)
        {
            Interlocked.Increment(ref _pendingWrites);
            _idle.Reset();
            _lines.Add(line);
        }

        private static void WriterLoop()
        {
            StreamWriter w = null;
            try
            {
                foreach (var first in _lines.GetConsumingEnumerable())
                {
                    try
                    {
                        if (_path == null) { Done(1); continue; }
                        int n = 1;
                        string line = first;
                        while (true)
                        {
                            if (line == COMPACT)
                            {
                                if (w != null) { w.Flush(); w.Dispose(); w = null; }
                                var keep = LoadPending();
                                var lines = new List<string>(keep.Count);
                                foreach (var e in keep) lines.Add("E" + JsonUtility.ToJson(e));
                                RewriteFile(lines);
                            }
                            else
                            {
                                if (w == null) w = Open();
                                w.Write(line);
                                w.Write('\n');
                            }
                            if (n >= 256 || !_lines.TryTake(out line)) break;
                            n++;
                        }
                        if (w != null) w.Flush();
                        Done(n);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[GameLyft] Event store write failed: " + e.Message);
                        try { if (w != null) w.Dispose(); } catch { }
                        w = null;
                        Done(0);
                        Thread.Sleep(200);
                    }
                }
            }
            finally { try { if (w != null) w.Dispose(); } catch { } }
        }

        private static void Done(int n)
        {
            if (n > 0 && Interlocked.Add(ref _pendingWrites, -n) <= 0) _idle.Set();
        }

        private static StreamWriter Open()
        {
            // After a crash mid-write the last line may be unfinished. Start on a fresh line so
            // the next event is not glued onto it (the torn line is skipped on replay).
            bool needsNewline = false;
            if (File.Exists(_path))
            {
                using (var rf = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (rf.Length > 0) { rf.Seek(-1, SeekOrigin.End); needsNewline = rf.ReadByte() != '\n'; }
                }
            }
            var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
            var w = new StreamWriter(fs, new System.Text.UTF8Encoding(false));
            if (needsNewline) w.Write('\n');
            return w;
        }

        private static void RewriteFile(List<string> lines)
        {
            if (lines == null) return;
            string tmp = _path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs, new System.Text.UTF8Encoding(false)))
            {
                foreach (var l in lines) { w.Write(l); w.Write('\n'); }
                w.Flush();
                fs.Flush(true);
            }
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }

        /// <summary>Background thread: every event still owed to at least one destination, oldest first.</summary>
        internal static List<GLEvent> LoadPending()
        {
            var order = new List<GLEvent>();
            var byId = new Dictionary<string, GLEvent>();
            if (_path == null) return order;
            string tmp = _path + ".tmp";
            // A compaction interrupted after the tmp file was complete but before the swap.
            if (!File.Exists(_path) && File.Exists(tmp)) File.Move(tmp, _path);
            if (!File.Exists(_path)) return order;
            using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var r = new StreamReader(fs))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Length < 2) continue;
                    try
                    {
                        if (line[0] == 'E')
                        {
                            var e = JsonUtility.FromJson<GLEvent>(line.Substring(1));
                            if (e != null && !string.IsNullOrEmpty(e.eid) && !byId.ContainsKey(e.eid)) { byId[e.eid] = e; order.Add(e); }
                        }
                        else if (line[0] == 'A')
                        {
                            var parts = line.Split(' ');
                            if (parts.Length == 3 && byId.TryGetValue(parts[1], out var e)) e.dests.Remove(parts[2]);
                        }
                        else if (line[0] == 'R')
                        {
                            var parts = line.Split(' ');
                            if (parts.Length == 3 && byId.TryGetValue(parts[1], out var e) && e.dests.Remove(GLDestinationRegistry.UNASSIGNED))
                                foreach (var d in parts[2].Split(',')) if (d.Length > 0 && !e.dests.Contains(d)) e.dests.Add(d);
                        }
                    }
                    catch { /* a torn last line after a crash: skip it */ }
                }
            }
            order.RemoveAll(e => e.dests == null || e.dests.Count == 0);
            return order;
        }

        /// <summary>Main thread: the previous SDK queued its (Firebase-only) events in PlayerPrefs; move them over once.</summary>
        internal static List<GLEvent> TakeLegacyQueue(string sessionId)
        {
            var outp = new List<GLEvent>();
            try
            {
                if (!PlayerPrefs.HasKey(LEGACY_PREFS_KEY)) return outp;
                var old = JsonUtility.FromJson<LegacyQueue>(PlayerPrefs.GetString(LEGACY_PREFS_KEY));
                var dests = GLDestinationRegistry.Find("firebase") != null
                    ? new List<string> { "firebase" } : GLDestinationRegistry.Ids();
                if (old != null && old.events != null && dests.Count > 0)
                {
                    foreach (var le in old.events)
                    {
                        if (le == null || string.IsNullOrEmpty(le.eventName)) continue;
                        var e = new GLEvent { eid = GLIds.New(10), name = le.eventName, sid = sessionId, dests = new List<string>(dests) };
                        e.ts = DateTimeOffset.TryParse(le.timestamp, out var t) ? t.ToUnixTimeSeconds() : GLIds.NowUnixSeconds();
                        if (le.parameters != null)
                            foreach (var p in le.parameters)
                            {
                                if (p == null || string.IsNullOrEmpty(p.key) || p.key == "session" || p.key == "event_type") continue;
                                e.ps.Add(new GLParam(p.key, p.value, p.type == "long" ? "l" : p.type == "double" ? "d" : "s"));
                            }
                        outp.Add(e);
                    }
                }
                PlayerPrefs.DeleteKey(LEGACY_PREFS_KEY);
                PlayerPrefs.Save();
                if (outp.Count > 0) GLLog.Info("Moved " + outp.Count + " event(s) from the previous SDK's queue.");
            }
            catch (Exception e) { GLLog.Error("Could not read the previous SDK's event queue: " + e.Message); }
            return outp;
        }

#pragma warning disable 0649   // filled by JsonUtility
        [Serializable] private class LegacyQueue { public List<LegacyEvent> events; }
        [Serializable] private class LegacyEvent { public string eventName; public List<LegacyParam> parameters; public string timestamp; }
        [Serializable] private class LegacyParam { public string key; public string value; public string type; }
#pragma warning restore 0649
    }

    /// <summary>
    /// Dispatcher: hands queued events to each destination once that destination's SDK has started
    /// and the device is online. Runs on its own background thread ("GameLyft.Dispatch"), so events
    /// go out the moment they are tracked, also while Unity's main thread is paused (a full-screen
    /// ad is on screen, the app is backgrounded). A destination whose SDK throws when called off the
    /// main thread is switched to main-thread delivery for the rest of the run. Every call is guarded
    /// so a failing destination can never break the game or the other destinations.
    /// </summary>
    internal class GLQueue : MonoBehaviour
    {
        private const int MAX_SENDS_PER_PASS = 50;
        private const int READY_RECHECK_MS = 1000;
        private const int IDLE_WAIT_MS = 250;
        private const float NOT_MARKED_WARNING_SECONDS = 60f;
        private const int COMPACT_AFTER_ACKS = 2000;
        private const int LARGE_QUEUE_WARNING = 10000;

        private static GLQueue _instance;
        private static readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private static volatile int _pendingCount;

        private readonly object _sync = new object();                 // guards everything below
        private readonly List<GLEvent> _pending = new List<GLEvent>();
        private readonly HashSet<string> _ids = new HashSet<string>();
        private readonly Dictionary<string, bool> _ready = new Dictionary<string, bool>();
        private readonly HashSet<string> _mainOnly = new HashSet<string>();   // SDKs that must be called on the main thread
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private long _nextReadyCheckMs;
        private int _acksSinceCompact;
        private volatile bool _loaded;
        private volatile bool _running;
        private List<GLEvent> _loadedFromDisk;
        private bool _warnedLarge;
        private bool _checkedPrefab;
        private bool _checkedMarks;
        private Thread _worker;

        internal static int PendingCount => _pendingCount;

        /// <summary>Any thread: an event was tracked — deliver it now.</summary>
        internal static void Signal() => _wake.Set();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            GLStore.Start();
            var go = new GameObject("[GameLyft.Queue]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            _instance = go.AddComponent<GLQueue>();
        }

        private void Start()
        {
            // Destinations register at BeforeSceneLoad too; by Start they are all in.
            foreach (var e in GLStore.TakeLegacyQueue(GLSession.Id)) GLStore.Add(e);
            var t = new Thread(() =>
            {
                try { _loadedFromDisk = GLStore.LoadPending(); }
                catch (Exception e) { Debug.LogError("[GameLyft] Event store read failed: " + e.Message); _loadedFromDisk = new List<GLEvent>(); }
                _loaded = true;
                Signal();
            }) { IsBackground = true, Name = "GameLyft.Load" };
            t.Start();

            _running = true;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "GameLyft.Dispatch" };
            _worker.Start();
        }

        private void WorkerLoop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android SDKs are called through JNI: this thread must be attached to the VM.
            try { AndroidJNI.AttachCurrentThread(); } catch (Exception e) { Debug.LogWarning("[GameLyft] JNI attach failed: " + e.Message); }
#endif
            try
            {
                while (_running)
                {
                    try { Pass(onMainThread: false); }
                    catch (Exception e) { GLLog.Error("Dispatcher error: " + e.Message); }
                    _wake.WaitOne(IDLE_WAIT_MS);
                }
            }
            finally
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try { AndroidJNI.DetachCurrentThread(); } catch { }
#endif
            }
        }

        private void Update()
        {
            try
            {
                GLMain.Pump();
                if (!_checkedPrefab && Time.realtimeSinceStartup > 10f)
                {
                    _checkedPrefab = true;
                    if (!GameLyftManager.Exists)
                        GLLog.Warn("The GameLyft prefab is not in the scene: engagement (gl_engagement) is not measured. "
                            + "Add it to the first scene (Tools → GameLyft → Add GameLyft Prefab to Scene).");
                }
                if (!_checkedMarks && Time.realtimeSinceStartup > NOT_MARKED_WARNING_SECONDS)
                {
                    _checkedMarks = true;
                    foreach (var d in GLDestinationRegistry.All)
                    {
                        bool ready;
                        try { ready = d.IsReady(); } catch { ready = false; }
                        if (!ready)
                            GLLog.Warn(d.Id + " is ticked in GameLyft Settings but was never marked ready: its events wait on disk. "
                                + "Call GameLyftAnalytics.MarkReady(GLDestination." + EnumName(d.Id) + ") once you have initialized it.");
                    }
                }
                // destinations that can only be called on the main thread are delivered from here
                bool anyMainOnly;
                lock (_sync) anyMainOnly = _mainOnly.Count > 0;
                if (anyMainOnly) Pass(onMainThread: true);
            }
            catch (Exception e) { GLLog.Error("Dispatcher error: " + e.Message); }
        }

        /// <summary>One delivery pass. The worker thread delivers every destination except the
        /// main-thread-only ones; the main thread (Update) delivers only those.</summary>
        private void Pass(bool onMainThread)
        {
            if (!_loaded) return;
            lock (_sync)
            {
                if (_loadedFromDisk != null)
                {
                    foreach (var e in _loadedFromDisk) if (_ids.Add(e.eid)) _pending.Add(e);
                    if (_loadedFromDisk.Count > 0) GLLog.Trace("Restored " + _loadedFromDisk.Count + " undelivered event(s) from disk.");
                    _loadedFromDisk = null;
                }
                while (GLStore.TryTakeIncoming(out var inc))
                    if (_ids.Add(inc.eid)) _pending.Add(inc);
                _pendingCount = _pending.Count;

                if (!_warnedLarge && _pending.Count > LARGE_QUEUE_WARNING)
                {
                    _warnedLarge = true;
                    GLLog.Warn(_pending.Count + " events are waiting to be delivered. Is every ticked destination's SDK initialized?");
                }
                if (_pending.Count == 0) return;
                if (!GLMain.Online) return;   // last known connectivity (refreshed every frame on the main thread)

                if (_clock.ElapsedMilliseconds >= _nextReadyCheckMs) RefreshReadiness();
                Deliver(onMainThread);
                _pendingCount = _pending.Count;

                if (_acksSinceCompact >= COMPACT_AFTER_ACKS)
                {
                    _acksSinceCompact = 0;
                    GLStore.Compact();
                }
            }
        }

        private static string EnumName(string id)
        {
            foreach (GLDestination g in System.Enum.GetValues(typeof(GLDestination)))
                if (GLReadiness.IdOf(g) == id) return g.ToString();
            return id;
        }

        private void RefreshReadiness()
        {
            _nextReadyCheckMs = _clock.ElapsedMilliseconds + READY_RECHECK_MS;
            foreach (var d in GLDestinationRegistry.All)
            {
                if (_ready.TryGetValue(d.Id, out var was) && was) continue;   // once started, stays started
                bool now;
                try { now = d.IsReady(); } catch { now = false; }
                _ready[d.Id] = now;
                if (now) GLLog.Trace(d.Id + " is ready — delivering its queued events.");
            }
        }

        private void Deliver(bool onMainThread)
        {
            int sends = 0;
            for (int i = 0; i < _pending.Count && sends < MAX_SENDS_PER_PASS; i++)
            {
                var e = _pending[i];
                for (int j = e.dests.Count - 1; j >= 0 && sends < MAX_SENDS_PER_PASS; j--)
                {
                    string id = e.dests[j];
                    if (id == GLDestinationRegistry.UNASSIGNED)
                    {
                        // Tracked while no destination was enabled: hand it to today's destinations.
                        var now = GLDestinationRegistry.Ids();
                        if (now.Count == 0) continue;
                        e.dests.RemoveAt(j);
                        foreach (var x in now) if (!e.dests.Contains(x)) e.dests.Add(x);
                        GLStore.Reassign(e.eid, now);
                        j = e.dests.Count;   // restart this event's destinations
                        continue;
                    }
                    var d = GLDestinationRegistry.Find(id);
                    if (d == null)
                    {
                        // The destination was turned off in Settings since this event was stored:
                        // it can never be delivered there, so stop waiting for it.
                        e.dests.RemoveAt(j);
                        GLStore.Ack(e.eid, id);
                        continue;
                    }
                    if (_mainOnly.Contains(id) != onMainThread) continue;
                    if (!_ready.TryGetValue(id, out var ok) || !ok) continue;

                    bool sent;
                    try { sent = d.Send(e); }
                    catch (Exception ex)
                    {
                        sent = false;
                        if (!onMainThread)
                        {
                            // this SDK does not accept calls off the main thread: deliver it from Update from now on
                            _mainOnly.Add(id);
                            GLLog.Warn(id + " cannot be called from a background thread (" + ex.Message + "); its events are now sent on the main thread.");
                        }
                        else GLLog.Warn(id + " rejected '" + e.name + "': " + ex.Message);
                    }
                    sends++;
                    if (!sent) continue;
                    e.dests.RemoveAt(j);
                    GLStore.Ack(e.eid, id);
                    _acksSinceCompact++;
                    if (GLLog.IsVerbose) GLLog.Trace("-> " + id + " '" + e.name + "' (gl_eid " + e.eid + (onMainThread ? ", main thread)" : ")"));
                }
                if (e.dests.Count == 0)
                {
                    _pending.RemoveAt(i);
                    _ids.Remove(e.eid);
                    i--;
                }
            }
        }

        /// <summary>
        /// Main thread, on pause / quit: wake the dispatcher, give it a moment to hand this event to the
        /// platform SDKs (they store and send it on their own threads), then flush the acknowledgements
        /// to disk. The game may be killed in the background right after. At least once, as always.
        /// </summary>
        internal static void DeliverNow(GLEvent e)
        {
            if (e == null) return;
            Signal();
            if (_instance != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 300)
                {
                    bool done;
                    lock (_instance._sync) done = e.dests.Count == 0;
                    if (done) break;
                    Thread.Sleep(5);
                }
                if (GLLog.IsVerbose && e.dests.Count == 0) GLLog.Trace("'" + e.name + "' (gl_eid " + e.eid + ") delivered on pause in " + sw.ElapsedMilliseconds + " ms.");
            }
            GLStore.FlushBlocking(400);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) GLStore.FlushBlocking(400);
        }

        private void OnApplicationQuit()
        {
            GLStore.FlushBlocking(400);
            _running = false;
            Signal();
        }

        private void OnDestroy()
        {
            _running = false;   // editor: stop the worker when Play mode ends
            Signal();
        }
    }
}
