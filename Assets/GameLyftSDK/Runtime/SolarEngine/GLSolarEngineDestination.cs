using System.Collections.Generic;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace GameLyft.Sdk
{
    /// <summary>
    /// Solar Engine destination (Settings → Destinations → Solar Engine; GAMELYFT_SOLAR_ENGINE).
    /// Ready once the game calls MarkReady(GLDestination.SolarEngine) from Solar Engine's init-completed
    /// callback (or right after initSeSdk()).
    /// </summary>
    internal sealed class GLSolarEngineDestination : IGLDestination
    {
        public string Id => "solarengine";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            var s = GameLyftSettings.LoadOrNull();
            if (s != null && s.sendToSolarEngine) GLDestinationRegistry.Register(new GLSolarEngineDestination());
        }

        // Ready only once the game calls GameLyftAnalytics.MarkReady(GLDestination.SolarEngine).
        public bool IsReady() => GLReadiness.IsReady(Id);

        public bool Send(GLEvent e)
        {
            var ps = GLRules.Params(e, GLRules.SolarEngine, Id);
            var attrs = new Dictionary<string, object>(ps.Count);
            foreach (var p in ps) attrs[p.k] = p.IsLong ? (object)p.AsLong() : p.IsDouble ? p.AsDouble() : p.v;
            SolarEngine.Analytics.track(GLRules.EventName(e.name, GLRules.SolarEngine, Id), attrs);
            return true;
        }
    }
}
