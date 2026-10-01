using System.Collections.Generic;
using Singular;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace GameLyft.Sdk
{
    /// <summary>
    /// Singular destination (Settings → Destinations → Singular; GAMELYFT_SINGULAR).
    /// Ready once the game calls MarkReady(GLDestination.Singular) after initializing Singular.
    /// </summary>
    internal sealed class GLSingularDestination : IGLDestination
    {
        public string Id => "singular";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            var s = GameLyftSettings.LoadOrNull();
            if (s != null && s.sendToSingular) GLDestinationRegistry.Register(new GLSingularDestination());
        }

        // Ready only once the game calls GameLyftAnalytics.MarkReady(GLDestination.Singular).
        public bool IsReady() => GLReadiness.IsReady(Id);

        public bool Send(GLEvent e)
        {
            var ps = GLRules.Params(e, GLRules.Singular, Id);
            var args = new Dictionary<string, object>(ps.Count);
            foreach (var p in ps) args[p.k] = p.IsLong ? (object)p.AsLong() : p.IsDouble ? p.AsDouble() : p.v;
            SingularSDK.Event(args, GLRules.EventName(e.name, GLRules.Singular, Id));
            return true;
        }
    }
}
