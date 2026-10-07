#if GAMELYFT_FIREBASE
using System;
using System.Collections;
using System.Reflection;
using Firebase;
using Firebase.Analytics;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Firebase Analytics destination (Settings → Destinations → Firebase; GAMELYFT_FIREBASE).
    /// GA4 limits are applied per event (GLRules.Firebase): name rules, 25 parameters, 40-char keys,
    /// 100-char values.
    ///
    /// Ready as soon as either is true (the game does not need to call MarkReady):
    ///  1. Detected: no Firebase dependency check is running AND the game has already created the
    ///     default Firebase app (its first FirebaseApp.DefaultInstance / FirebaseAnalytics call after
    ///     CheckAndFixDependenciesAsync). Read from FirebaseApp's own private state
    ///     (IsCheckDependenciesRunning, nameToProxy) — the SDK never calls a Firebase API that can
    ///     throw "Don't call Firebase functions before CheckDependencies has finished", never runs the
    ///     check and never creates the app. Verified with Firebase Unity SDK 13.10.0; if those members
    ///     are missing, detection is off and MarkReady is needed.
    ///  2. The game called GameLyftAnalytics.MarkReady(GLDestination.Firebase) (manual override).
    /// </summary>
    internal sealed class GLFirebaseDestination : IGLDestination
    {
        public string Id => "firebase";

        private static volatile bool _detected;
        private static bool _probeBroken;
        private static MethodInfo _isCheckRunning;   // private static bool FirebaseApp.IsCheckDependenciesRunning()
        private static FieldInfo _nameToProxy;       // private static Dictionary<string, FirebaseApp> FirebaseApp.nameToProxy
        private static readonly object _probeLock = new object();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            _detected = false;
            var s = GameLyftSettings.LoadOrNull();
            if (s != null && s.sendToFirebase) GLDestinationRegistry.Register(new GLFirebaseDestination());
        }

        public bool IsReady() => GLReadiness.IsReady(Id) || _detected || Detect();

        /// <summary>Any thread. Reads FirebaseApp's private state only; never calls into Firebase.</summary>
        private static bool Detect()
        {
            lock (_probeLock)
            {
                if (_detected) return true;
                if (_probeBroken) return false;
                try
                {
                    if (_isCheckRunning == null || _nameToProxy == null)
                    {
                        var t = typeof(FirebaseApp);
                        const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Static;
                        _isCheckRunning = t.GetMethod("IsCheckDependenciesRunning", F, null, Type.EmptyTypes, null);
                        _nameToProxy = t.GetField("nameToProxy", F);
                        if (_isCheckRunning == null || _isCheckRunning.ReturnType != typeof(bool) || _nameToProxy == null)
                        {
                            _probeBroken = true;
                            GLLog.Warn("Firebase readiness can't be detected with this Firebase version: call "
                                + "GameLyftAnalytics.MarkReady(GLDestination.Firebase) once CheckAndFixDependenciesAsync succeeds.");
                            return false;
                        }
                    }

                    // 1) the game's dependency check must not be running (Firebase takes its own lock here)
                    if ((bool)_isCheckRunning.Invoke(null, null)) return false;

                    // 2) the game must already have created a Firebase app (Firebase locks this map itself)
                    var map = _nameToProxy.GetValue(null) as ICollection;
                    if (map == null) return false;
                    int count;
                    lock (map) count = map.Count;
                    if (count == 0) return false;

                    _detected = true;
                    GLLog.Trace("Firebase is ready (no dependency check running, the game has created its Firebase app).");
                    return true;
                }
                catch (Exception e)
                {
                    _probeBroken = true;
                    GLLog.Warn("Firebase readiness detection failed (" + e.Message + "): call "
                        + "GameLyftAnalytics.MarkReady(GLDestination.Firebase) once CheckAndFixDependenciesAsync succeeds.");
                    return false;
                }
            }
        }

        public bool Send(GLEvent e)
        {
            var ps = GLRules.Params(e, GLRules.Firebase, Id);
            var fp = new Parameter[ps.Count];
            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                fp[i] = p.IsLong ? new Parameter(p.k, p.AsLong())
                      : p.IsDouble ? new Parameter(p.k, p.AsDouble())
                      : new Parameter(p.k, p.v);
            }
            FirebaseAnalytics.LogEvent(GLRules.EventName(e.name, GLRules.Firebase, Id), fp);
            return true;
        }
    }
}
#endif
