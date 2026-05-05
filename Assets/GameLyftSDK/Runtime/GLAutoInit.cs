using System.Collections;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Optional auto-initialization. When GameLyftSettings.autoInitialize is ON, this
    /// fires at app start, polls for Firebase readiness, and calls
    /// GameLyftAnalytics.Initialize() automatically when detected.
    ///
    /// Consumer-safe: does not initiate any Firebase work itself. Purely observes.
    /// If consumer also calls Initialize() manually, the second call is a silent
    /// idempotent no-op.
    /// </summary>
    internal class GLAutoInit : MonoBehaviour
    {
        private const float POLL_INTERVAL_SECONDS = 0.5f;
        private const float TIMEOUT_SECONDS = 300f;

        // Run BeforeSceneLoad so the polling flag is set before any scene script's Awake.
        // Without this, a TrackEvent() called from a scene Awake would race ahead of the flag
        // (Bootstrap at AfterSceneLoad fires *after* scene Awakes/OnEnables) and emit a spurious
        // "called before Initialize" warning even though auto-init is in flight.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var settings = GameLyftSettings.LoadOrNull();
            if (settings == null || !settings.autoInitialize) return;

            // If the consumer already beat us to Initialize(), do nothing.
            if (GameLyftAnalytics.IsInitialized) return;

            // Set the flag synchronously here so it's visible to anyone who runs before our
            // coroutine gets a chance to start (i.e. any scene script Awake/OnEnable).
            GameLyftAnalytics._autoInitPolling = true;

            var go = new GameObject("[GameLyft.AutoInit]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<GLAutoInit>();
            GameLyftAnalytics.Info("Auto Initialize: polling started, waiting for Firebase...");
        }

        private void Start()
        {
            StartCoroutine(WaitForFirebaseAndInitialize());
        }

        private IEnumerator WaitForFirebaseAndInitialize()
        {
            float elapsed = 0f;
            while (elapsed < TIMEOUT_SECONDS)
            {
                // Consumer's manual Initialize() may win the race — stop polling.
                if (GameLyftAnalytics.IsInitialized)
                {
                    GameLyftAnalytics._autoInitPolling = false;
                    Destroy(gameObject);
                    yield break;
                }

                if (GameLyftAnalytics.IsFirebaseAvailable())
                {
                    GameLyftAnalytics._autoInitPolling = false;
                    GameLyftAnalytics.Info("Auto Initialize: Firebase detected after "
                        + elapsed.ToString("0.0") + "s, calling Initialize()...");
                    GameLyftAnalytics.Initialize();
                    Destroy(gameObject);
                    yield break;
                }

                yield return new WaitForSeconds(POLL_INTERVAL_SECONDS);
                elapsed += POLL_INTERVAL_SECONDS;
            }

            GameLyftAnalytics._autoInitPolling = false;
            GameLyftAnalytics.Warn("Auto Initialize timed out after " + TIMEOUT_SECONDS
                + "s — Firebase never became available. Call Firebase's "
                + "CheckAndFixDependenciesAsync() from your bootstrap code, or disable "
                + "Auto Initialize and call GameLyftAnalytics.Initialize() manually.");

            Destroy(gameObject);
        }
    }
}
