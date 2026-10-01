using System.Collections.Generic;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace GameLyft.Sdk
{
    /// <summary>
    /// Airbridge destination (Settings → Destinations → Airbridge; GAMELYFT_AIRBRIDGE).
    /// Ready once the game calls MarkReady(GLDestination.Airbridge) (Airbridge starts natively from its
    /// own settings; call it at app start). Parameters are sent as custom attributes.
    /// </summary>
    internal sealed class GLAirbridgeDestination : IGLDestination
    {
        public string Id => "airbridge";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            var s = GameLyftSettings.LoadOrNull();
            if (s != null && s.sendToAirbridge) GLDestinationRegistry.Register(new GLAirbridgeDestination());
        }

        // Ready only once the game calls GameLyftAnalytics.MarkReady(GLDestination.Airbridge).
        public bool IsReady() => GLReadiness.IsReady(Id);

        public bool Send(GLEvent e)
        {
            var ps = GLRules.Params(e, GLRules.Airbridge, Id);
            var attrs = new Dictionary<string, object>(ps.Count);
            foreach (var p in ps) attrs[p.k] = p.IsLong ? (object)p.AsLong() : p.IsDouble ? p.AsDouble() : p.v;
            Airbridge.TrackEvent(GLRules.EventName(e.name, GLRules.Airbridge, Id), null, attrs);
            return true;
        }
    }
}
