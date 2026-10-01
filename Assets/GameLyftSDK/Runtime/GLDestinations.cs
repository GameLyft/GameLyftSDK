using System.Collections.Generic;
using System.Text;

namespace GameLyft.Sdk
{
    /// <summary>
    /// A platform the SDK forwards events to (Firebase or an MMP SDK the studio ticked in
    /// Tools → GameLyft → Settings). Each destination lives in its own sub-assembly that only
    /// compiles when its define is on, and registers itself at BeforeSceneLoad.
    /// </summary>
    public interface IGLDestination
    {
        /// <summary>Stable id stored in the queue file: "firebase", "appsflyer", ….</summary>
        string Id { get; }

        /// <summary>Has the game declared this platform initialized (GameLyftAnalytics.MarkReady)?
        /// Events wait on disk until then. Must not touch the platform SDK.</summary>
        bool IsReady();

        /// <summary>
        /// Hand the event to the SDK. Called on the main thread. Return true once the SDK has
        /// accepted it (or the event does not apply to this destination, e.g. Adjust without a
        /// token); false to retry later. Must not throw — the dispatcher also guards it.
        /// </summary>
        bool Send(GLEvent e);
    }

    /// <summary>
    /// Which destinations the GAME has declared initialized (GameLyftAnalytics.MarkReady). The SDK
    /// never probes a platform SDK to find out — probing Firebase while its dependency check runs
    /// throws (FirebaseApp.Finalize → ThrowIfCheckDependenciesRunning), and the MMPs offer no
    /// reliable "initialized" signal. Thread-safe.
    /// </summary>
    public static class GLReadiness
    {
        private static readonly HashSet<string> _ready = new HashSet<string>();

        public static string IdOf(GLDestination d)
        {
            switch (d)
            {
                case GLDestination.Firebase: return "firebase";
                case GLDestination.AppsFlyer: return "appsflyer";
                case GLDestination.Adjust: return "adjust";
                case GLDestination.SolarEngine: return "solarengine";
                case GLDestination.Singular: return "singular";
                default: return "airbridge";
            }
        }

        internal static bool Mark(GLDestination d)
        {
            lock (_ready) return _ready.Add(IdOf(d));
        }

        public static bool IsReady(string id)
        {
            lock (_ready) return _ready.Contains(id);
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            lock (_ready) _ready.Clear();   // editor with domain reload off: start each Play run clean
        }
    }

    /// <summary>Destinations registered by the compiled-in sub-assemblies.</summary>
    public static class GLDestinationRegistry
    {
        private static readonly List<IGLDestination> _all = new List<IGLDestination>();

        public static IReadOnlyList<IGLDestination> All => _all;

        public static void Register(IGLDestination d)
        {
            if (d == null) return;
            foreach (var x in _all) if (x.Id == d.Id) return;
            _all.Add(d);
            GLLog.Trace("Destination registered: " + d.Id);
        }

        public static IGLDestination Find(string id)
        {
            foreach (var x in _all) if (x.Id == id) return x;
            return null;
        }

        /// <summary>Placeholder for an event tracked while no destination is enabled: it is kept and
        /// assigned to the destinations once one is (never dropped).</summary>
        public const string UNASSIGNED = "unassigned";

        /// <summary>Ids of every registered destination — the delivery list fixed on a new event.</summary>
        public static List<string> Ids()
        {
            var ids = new List<string>(_all.Count);
            foreach (var x in _all) ids.Add(x.Id);
            return ids;
        }

        /// <summary>Delivery list for a new event: every registered destination, or UNASSIGNED if none.</summary>
        public static List<string> DeliveryList()
        {
            var ids = Ids();
            if (ids.Count == 0) ids.Add(UNASSIGNED);
            return ids;
        }
    }

    /// <summary>
    /// Per-destination limits, applied when an event is sent. Events are ADAPTED, never dropped:
    /// names are cleaned, long values truncated, extra parameters cut (the three standard ones
    /// are always kept). In Test Mode each adaptation is warned once.
    /// </summary>
    public static class GLRules
    {
        public sealed class Limits
        {
            public int maxEventName;     // 0 = no limit
            public int maxParams;        // including gl_eid / gl_ts / gl_sid; 0 = no limit
            public int maxParamName;
            public int maxStringValue;
            public string[] reservedPrefixes = new string[0];
        }

        /// <summary>Firebase / GA4: name [A-Za-z][A-Za-z0-9_]{0,39}, 25 params, 40-char keys, 100-char values.</summary>
        public static readonly Limits Firebase = new Limits
        {
            maxEventName = 40, maxParams = 25, maxParamName = 40, maxStringValue = 100,
            reservedPrefixes = new[] { "firebase_", "google_", "ga_" },
        };
        /// <summary>AppsFlyer: event name up to 45 chars; values are sent as strings.</summary>
        public static readonly Limits AppsFlyer = new Limits { maxEventName = 45, maxParamName = 40, maxStringValue = 1000 };
        public static readonly Limits Adjust = new Limits { maxParamName = 40, maxStringValue = 1000 };
        public static readonly Limits SolarEngine = new Limits { maxEventName = 40, maxParamName = 40, maxStringValue = 1000 };
        public static readonly Limits Singular = new Limits { maxEventName = 32, maxParamName = 40, maxStringValue = 1000 };
        public static readonly Limits Airbridge = new Limits { maxEventName = 128, maxParamName = 40, maxStringValue = 1000 };

        private static readonly HashSet<string> _warned = new HashSet<string>();

        /// <summary>Event name adapted to the destination's rules.</summary>
        public static string EventName(string name, Limits l, string dest)
        {
            string n = CleanIdentifier(name);
            foreach (var p in l.reservedPrefixes)
                if (n.StartsWith(p, System.StringComparison.OrdinalIgnoreCase)) n = "gl_" + n;
            if (l.maxEventName > 0 && n.Length > l.maxEventName) n = n.Substring(0, l.maxEventName);
            if (n != name) WarnOnce(dest + ":name:" + name, "Event '" + name + "' sent to " + dest + " as '" + n + "' (name rules).");
            return n;
        }

        /// <summary>Parameters adapted to the destination's rules (standard ones always kept).</summary>
        public static List<GLParam> Params(GLEvent e, Limits l, string dest)
        {
            var src = e.AllParams();
            var outp = new List<GLParam>(src.Count);
            int gameParams = src.Count - 3;
            int maxGame = l.maxParams > 0 ? l.maxParams - 3 : int.MaxValue;
            if (gameParams > maxGame)
                WarnOnce(dest + ":count:" + e.name, "Event '" + e.name + "' has " + gameParams + " parameters; "
                    + dest + " keeps the first " + maxGame + " (plus gl_eid, gl_ts, gl_sid).");
            for (int i = 0; i < src.Count; i++)
            {
                bool standard = i >= gameParams;
                if (!standard && i >= maxGame) continue;
                var p = src[i];
                string key = CleanIdentifier(p.k);
                if (l.maxParamName > 0 && key.Length > l.maxParamName) key = key.Substring(0, l.maxParamName);
                string val = p.v ?? "";
                if (p.t == "s" && l.maxStringValue > 0 && val.Length > l.maxStringValue)
                {
                    WarnOnce(dest + ":val:" + e.name + ":" + p.k, "Event '" + e.name + "': '" + p.k + "' truncated to "
                        + l.maxStringValue + " chars for " + dest + ".");
                    val = val.Substring(0, l.maxStringValue);
                }
                outp.Add(new GLParam(key, val, p.t));
            }
            return outp;
        }

        /// <summary>[A-Za-z0-9_], starting with a letter.</summary>
        public static string CleanIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return "gl_unnamed";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' ? c : '_');
            char h = sb[0];
            if (!((h >= 'A' && h <= 'Z') || (h >= 'a' && h <= 'z'))) sb.Insert(0, 'k');
            return sb.ToString();
        }

        private static void WarnOnce(string key, string message)
        {
            lock (_warned) { if (!_warned.Add(key)) return; }
            GLLog.Warn(message);
        }
    }
}
