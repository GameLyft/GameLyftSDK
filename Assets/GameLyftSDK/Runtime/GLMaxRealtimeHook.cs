#if GAMELYFT_APPLOVIN
using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// AppLovin MAX "ad displayed / hidden" at the moment they happen.
    ///
    /// MAX's native SDK reports every event to Unity through ONE background callback
    /// (Android: MaxUnityPlugin.setBackgroundCallback, iOS: _MaxSetBackgroundCallback). Its C# side
    /// then queues displayed / hidden for Unity's main thread — which is paused while a full-screen
    /// ad is up, so the game's handlers only see "displayed" after the ad closes.
    ///
    /// This hook takes that callback slot after MAX has set it up, reads displayed / hidden /
    /// failed-to-display on the background thread the instant they arrive (→ GLAdState), and hands
    /// EVERY event on, unchanged, to MaxSdkCallbacks.ForwardEvent — exactly what MAX's own callback
    /// does — so the game's ad callbacks behave as before. If anything about MAX's plugin is not as
    /// expected, the hook is not installed and the C# events (GLMaxAdHooks) remain the signal.
    /// Verified against AppLovin MAX Unity plugin 8.6.4.
    /// </summary>
    internal static class GLMaxRealtimeHook
    {
        private static bool _installed;
        private static readonly Regex NameRe = new Regex("\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex UnitRe = new Regex("\"adUnitId\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled);

        internal static bool Installed => _installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (_installed) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Let MAX's own static setup (its callback, its event executor) run first, so ours is the last one set.
                RuntimeHelpers.RunClassConstructor(typeof(MaxSdkAndroid).TypeHandle);
                _androidProxy = new AndroidProxy();   // kept: native code calls it for the life of the app
                using (var plugin = new AndroidJavaClass("com.applovin.mediation.unity.MaxUnityPlugin"))
                    plugin.CallStatic("setBackgroundCallback", _androidProxy);
                _installed = true;
                GLLog.Trace("AppLovin MAX real-time ad hook installed (Android).");
            }
            catch (Exception e)
            {
                GLLog.Warn("AppLovin MAX real-time ad hook not installed (" + e.Message + "); ad time is credited when the game resumes instead.");
            }
#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(MaxSdkiOS).TypeHandle);
                _MaxSetBackgroundCallback(IosCallback);
                _installed = true;
                GLLog.Trace("AppLovin MAX real-time ad hook installed (iOS).");
            }
            catch (Exception e)
            {
                GLLog.Warn("AppLovin MAX real-time ad hook not installed (" + e.Message + "); ad time is credited when the game resumes instead.");
            }
#endif
        }

        /// <summary>Background thread: note the ad state, then give the event to MAX unchanged.</summary>
        private static void OnEvent(string propsStr)
        {
            try { Peek(propsStr); } catch (Exception e) { Debug.LogWarning("[GameLyft] MAX hook: " + e.Message); }
            try
            {
                MaxSdkCallbacks.ForwardEvent(propsStr);
            }
            catch (Exception e)
            {
                // same handling as MAX's own callback (MaxSdkBase.HandleBackgroundCallback)
                Debug.LogError("[AppLovin MAX] Unable to notify ad delegate due to an error in the publisher callback: " + e.Message);
                Debug.LogException(e);
            }
        }

        private static void Peek(string propsStr)
        {
            if (string.IsNullOrEmpty(propsStr)) return;
            if (propsStr.IndexOf("Displayed", StringComparison.Ordinal) < 0
                && propsStr.IndexOf("Hidden", StringComparison.Ordinal) < 0
                && propsStr.IndexOf("FailedToDisplay", StringComparison.Ordinal) < 0) return;
            var m = NameRe.Match(propsStr);
            if (!m.Success) return;
            string name = m.Groups[1].Value;
            var u = UnitRe.Match(propsStr);
            string unit = u.Success ? u.Groups[1].Value : null;
            switch (name)
            {
                case "OnInterstitialDisplayedEvent":
                case "OnRewardedAdDisplayedEvent":
                case "OnAppOpenAdDisplayedEvent":
                    GLAdState.Started("max", unit);
                    break;
                case "OnInterstitialHiddenEvent":
                case "OnRewardedAdHiddenEvent":
                case "OnAppOpenAdHiddenEvent":
                case "OnInterstitialAdFailedToDisplayEvent":
                case "OnRewardedAdFailedToDisplayEvent":
                case "OnAppOpenAdFailedToDisplayEvent":
                    GLAdState.Closed("max", unit);
                    break;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidProxy _androidProxy;

        private sealed class AndroidProxy : AndroidJavaProxy
        {
            public AndroidProxy() : base("com.applovin.mediation.unity.MaxUnityAdManager$BackgroundCallback") { }

            // called by MAX's native plugin on its own thread
            public void onEvent(string propsStr) => OnEvent(propsStr);
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        private delegate void IosBackgroundCallback(string args);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _MaxSetBackgroundCallback(IosBackgroundCallback backgroundCallback);

        // kept in a static field: native code holds the pointer for the life of the app
        private static readonly IosBackgroundCallback IosCallback = OnIosEvent;

        [AOT.MonoPInvokeCallback(typeof(IosBackgroundCallback))]
        private static void OnIosEvent(string propsStr) => OnEvent(propsStr);
#endif
    }
}
#endif
