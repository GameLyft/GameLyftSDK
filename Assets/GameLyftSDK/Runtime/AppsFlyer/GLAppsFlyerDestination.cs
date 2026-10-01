using System.Collections.Generic;
using AppsFlyerSDK;
using UnityEngine;

// Nothing in the game references this assembly directly; keep the linker from stripping it.
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace GameLyft.Sdk
{
    /// <summary>
    /// AppsFlyer destination (Settings → Destinations → AppsFlyer; GAMELYFT_APPSFLYER).
    /// Ready once the game calls MarkReady(GLDestination.AppsFlyer) after AppsFlyer.startSDK().
    /// AppsFlyer takes string values only, so every parameter is sent as a string.
    /// </summary>
    internal sealed class GLAppsFlyerDestination : IGLDestination
    {
        public string Id => "appsflyer";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            var s = GameLyftSettings.LoadOrNull();
            if (s != null && s.sendToAppsFlyer) GLDestinationRegistry.Register(new GLAppsFlyerDestination());
        }

        // Ready only once the game calls GameLyftAnalytics.MarkReady(GLDestination.AppsFlyer).
        public bool IsReady() => GLReadiness.IsReady(Id);

        public bool Send(GLEvent e)
        {
            var ps = GLRules.Params(e, GLRules.AppsFlyer, Id);
            var values = new Dictionary<string, string>(ps.Count);
            foreach (var p in ps) values[p.k] = p.v;
            AppsFlyer.sendEvent(GLRules.EventName(e.name, GLRules.AppsFlyer, Id), values);
            return true;
        }
    }
}
