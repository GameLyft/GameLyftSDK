using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace GameLyft.Sdk
{
    /// <summary>A typed event parameter. Kept as strings + a type tag so it serializes to the queue file.</summary>
    [Serializable]
    public class GLParam
    {
        public string k;   // key
        public string v;   // value (invariant culture for numbers)
        public string t;   // "s" string | "l" long | "d" double

        public GLParam() { }
        public GLParam(string key, string value, string type) { k = key; v = value ?? ""; t = type; }

        public bool IsLong => t == "l";
        public bool IsDouble => t == "d";
        public long AsLong() => long.TryParse(v, out var x) ? x : 0;
        public double AsDouble() => double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : 0;
    }

    /// <summary>
    /// One SDK event as it is stored in the queue file and handed to each destination.
    /// Carries the three standard parameters every SDK event has (GL_EVENT_CATALOG.csv):
    ///   gl_eid  unique event id, 10 chars   (dedupe of a crash resend; Adjust DeduplicationId)
    ///   gl_ts   real event time, Unix seconds (destinations stamp send time, not event time)
    ///   gl_sid  session id, 8 chars
    /// </summary>
    [Serializable]
    public class GLEvent
    {
        public string eid;
        public string name;
        public long ts;
        public string sid;
        public List<GLParam> ps = new List<GLParam>();
        /// <summary>Adjust event token for this event ("" = not sent to Adjust).</summary>
        public string adj = "";
        /// <summary>Destination ids still to deliver to. Fixed at creation.</summary>
        public List<string> dests = new List<string>();

        /// <summary>Game parameters followed by the three standard ones, as destinations send them.</summary>
        public List<GLParam> AllParams()
        {
            var all = new List<GLParam>(ps.Count + 3);
            all.AddRange(ps);
            all.Add(new GLParam("gl_eid", eid, "s"));
            all.Add(new GLParam("gl_ts", ts.ToString(System.Globalization.CultureInfo.InvariantCulture), "l"));
            all.Add(new GLParam("gl_sid", sid, "s"));
            return all;
        }
    }

    /// <summary>Unique ids: base-36 from a cryptographic RNG (no time component, no collisions in practice).</summary>
    public static class GLIds
    {
        private const string ALPHABET = "0123456789abcdefghijklmnopqrstuvwxyz";
        private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();
        private static readonly object Lock = new object();

        public static string New(int length)
        {
            var bytes = new byte[length];
            lock (Lock) Rng.GetBytes(bytes);
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++) sb.Append(ALPHABET[bytes[i] % ALPHABET.Length]);
            return sb.ToString();
        }

        public static long NowUnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
