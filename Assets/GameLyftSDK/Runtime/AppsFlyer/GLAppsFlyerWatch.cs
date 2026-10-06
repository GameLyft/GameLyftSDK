using System;
using AppsFlyerSDK;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Watches AppsFlyer until it is ready: makes sure the object named in AppsFlyer.CallBackObjectName
    /// can receive the native start callback (the stock AppsFlyerObject prefab only carries
    /// AppsFlyerObjectScript, so an AppsFlyer component is added to it), and runs the isInit fallback.
    /// Only reads AppsFlyer's public static state; disables itself once settled.
    /// </summary>
    internal sealed class GLAppsFlyerWatch : MonoBehaviour
    {
        private string _attachedTo;
        private float _initSeenAt = -1f;

        private void Update()
        {
            try
            {
                if (GLAppsFlyerDestination.Settled || GLReadiness.IsReady("appsflyer")) { enabled = false; return; }
                EnsureReceiver();
                var inst = AppsFlyer.instance;
                if (inst != null && inst.isInit)
                {
                    if (_initSeenAt < 0f) _initSeenAt = Time.realtimeSinceStartup;
                    else if (Time.realtimeSinceStartup - _initSeenAt >= GLAppsFlyerDestination.FallbackSeconds)
                        GLAppsFlyerDestination.MarkFallback();
                }
            }
            catch (Exception e) { GLLog.Warn("AppsFlyer readiness watch: " + e.Message); enabled = false; }
        }

        private void EnsureReceiver()
        {
            string name = AppsFlyer.CallBackObjectName;
            if (string.IsNullOrEmpty(name) || name == _attachedTo || name == GLAppsFlyerDestination.ReceiverName) return;
            var target = GameObject.Find(name);
            if (target == null) return;   // not in the scene yet; retry next frame
            if (target.GetComponent<AppsFlyer>() == null)
            {
                target.AddComponent<AppsFlyer>();
                GLLog.Trace("Added the AppsFlyer callback receiver to '" + name + "' so AppsFlyer can report its start.");
            }
            _attachedTo = name;
        }
    }
}
