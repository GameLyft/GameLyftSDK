#if GAMELYFT_FIREBASE
using System.Collections.Generic;
using Firebase.Analytics;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Firebase Analytics destination (Settings → Destinations → Firebase; GAMELYFT_FIREBASE).
    /// Ready once the game calls MarkReady(GLDestination.Firebase) after CheckAndFixDependenciesAsync
    /// reports DependencyStatus.Available — the SDK never touches FirebaseApp itself. GA4 limits are applied
    /// per event (GLRules.Firebase): name rules, 25 parameters, 40-char keys, 100-char values.
    /// </summary>
    internal sealed class GLFirebaseDestination : IGLDestination
    {
        public string Id => "firebase";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            var s = GameLyftSettings.LoadOrNull();
            if (s != null && s.sendToFirebase) GLDestinationRegistry.Register(new GLFirebaseDestination());
        }

        // Ready only once the game calls GameLyftAnalytics.MarkReady(GLDestination.Firebase).
        public bool IsReady() => GLReadiness.IsReady(Id);

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
