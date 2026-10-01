using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Main-thread helpers so every public call is safe from any thread (e.g. a Firebase or
    /// store callback on a worker thread). Unity APIs such as Resources.Load, PlayerPrefs and
    /// internetReachability may only be touched on the main thread.
    /// </summary>
    internal static class GLMain
    {
        private static int _mainThreadId = -1;
        private static readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();
        private static volatile bool _online = true;

        internal static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        /// <summary>Last known connectivity (refreshed every frame on the main thread).</summary>
        internal static bool Online => IsMainThread ? Application.internetReachability != NetworkReachability.NotReachable : _online;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Capture()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            while (_actions.TryDequeue(out _)) { }   // editor: drop leftovers of a previous Play run
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Preload()
        {
            // Load settings (and the logger flags) on the main thread before any game code runs.
            var _ = GameLyftAnalytics.Settings;
            GLLog.IsVerbose.ToString();
        }

        /// <summary>Run now when on the main thread, otherwise on the next frame.</summary>
        internal static void Run(Action a)
        {
            if (a == null) return;
            if (IsMainThread) { a(); return; }
            _actions.Enqueue(a);
        }

        /// <summary>Called every frame by the dispatcher.</summary>
        internal static void Pump()
        {
            _online = Application.internetReachability != NetworkReachability.NotReachable;
            int n = 0;
            while (n++ < 64 && _actions.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception e) { GLLog.Error("Deferred call failed: " + e.Message); }
            }
        }
    }
}
