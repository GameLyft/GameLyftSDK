using AdjustSdk;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace GameLyft.Sdk
{
    /// <summary>
    /// Adjust destination (Settings → Destinations → Adjust; GAMELYFT_ADJUST).
    /// Adjust only accepts events created in its dashboard, identified by a token:
    ///   - the SDK's own events use the tokens entered in Settings (Adjust event tokens table);
    ///   - custom events use the token passed to TrackEvent(name, params, adjustToken).
    /// Events without a token are skipped for Adjust (and still go to every other destination).
    /// gl_eid is set as Adjust's DeduplicationId, so a crash resend of the SAME event is ignored
    /// by Adjust; separate occurrences have separate ids and are all counted.
    /// Ready once the game calls MarkReady(GLDestination.Adjust) after Adjust.InitSdk(config).
    /// </summary>
    internal sealed class GLAdjustDestination : IGLDestination
    {
        public string Id => "adjust";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            var s = GameLyftSettings.LoadOrNull();
            if (s != null && s.sendToAdjust) GLDestinationRegistry.Register(new GLAdjustDestination());
        }

        // Ready only once the game calls GameLyftAnalytics.MarkReady(GLDestination.Adjust).
        public bool IsReady() => GLReadiness.IsReady(Id);

        public bool Send(GLEvent e)
        {
            if (string.IsNullOrEmpty(e.adj)) return true;   // no token: not an Adjust event
            var ae = new AdjustEvent(e.adj) { DeduplicationId = e.eid };
            foreach (var p in GLRules.Params(e, GLRules.Adjust, Id)) ae.AddCallbackParameter(p.k, p.v);
            Adjust.TrackEvent(ae);
            return true;
        }
    }
}
