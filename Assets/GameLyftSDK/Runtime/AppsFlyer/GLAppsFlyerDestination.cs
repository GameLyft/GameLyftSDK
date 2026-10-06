using System;
using System.Collections.Generic;
using AppsFlyerSDK;
using UnityEngine;

// Nothing in the game references this assembly directly; keep the linker from stripping it.
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace GameLyft.Sdk
{
    /// <summary>
    /// AppsFlyer destination (Settings → Destinations → AppsFlyer; GAMELYFT_APPSFLYER).
    /// AppsFlyer takes string values only, so every parameter is sent as a string.
    ///
    /// Ready as soon as any of these is true (the game does not need to call MarkReady):
    ///  1. AppsFlyer reported its start: the SDK subscribes to AppsFlyer.OnRequestResponse before the
    ///     game calls startSDK(), so the plugin asks the native SDK for the start callback, which
    ///     arrives once startSDK() has run (any status code, even a failed request).
    ///  2. Fallback: AppsFlyer.initSDK() has run (instance.isInit) for FALLBACK_SECONDS — covers a
    ///     callback that never arrives (no callback object, launch blocked or cached offline).
    ///  3. The game called GameLyftAnalytics.MarkReady(GLDestination.AppsFlyer) (manual override).
    /// </summary>
    internal sealed class GLAppsFlyerDestination : IGLDestination
    {
        public string Id => "appsflyer";

        private const float FALLBACK_SECONDS = 10f;
        private const string RECEIVER_NAME = "GameLyft.AppsFlyerReceiver";

        private static volatile bool _started;    // signal 1
        private static volatile bool _fallback;   // signal 2

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            _started = false;
            _fallback = false;
            var s = GameLyftSettings.LoadOrNull();
            if (s == null || !s.sendToAppsFlyer) return;
            GLDestinationRegistry.Register(new GLAppsFlyerDestination());

            // Must happen before the game's startSDK(): the plugin only requests the native start
            // callback when a handler is subscribed at that moment.
            AppsFlyer.OnRequestResponse -= OnStartResponse;
            AppsFlyer.OnRequestResponse += OnStartResponse;
            // A game that passes no callback object to initSDK leaves the name empty; the native
            // callback then comes to our receiver. initSDK(…, gameObject) overrides it, which is fine.
            if (string.IsNullOrEmpty(AppsFlyer.CallBackObjectName)) AppsFlyer.CallBackObjectName = RECEIVER_NAME;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateWatch()
        {
            if (GLDestinationRegistry.Find("appsflyer") == null) return;
            var go = new GameObject(RECEIVER_NAME) { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AppsFlyer>();          // receives requestResponseReceived when the name is ours
            go.AddComponent<GLAppsFlyerWatch>();
        }

        private static void OnStartResponse(object sender, EventArgs args)
        {
            if (_started) return;
            _started = true;
            var r = args as AppsFlyerRequestEventArgs;
            GLLog.Trace("AppsFlyer reported its start (status " + (r != null ? r.statusCode.ToString() : "?") + ") — AppsFlyer is ready.");
        }

        internal static void MarkFallback()
        {
            if (_fallback || _started) return;
            _fallback = true;
            GLLog.Trace("AppsFlyer initSDK() ran " + FALLBACK_SECONDS + " s ago and no start callback arrived — treating AppsFlyer as ready.");
        }

        internal static bool Settled => _started || _fallback;
        internal static float FallbackSeconds => FALLBACK_SECONDS;
        internal static string ReceiverName => RECEIVER_NAME;

        public bool IsReady() => _started || _fallback || GLReadiness.IsReady(Id);

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
