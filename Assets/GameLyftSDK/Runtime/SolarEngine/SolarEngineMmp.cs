using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GameLyft.Sdk
{
    /// <summary>
    /// Solar Engine MMP integration. Auto-starts at app launch when GAMELYFT_SOLAR_ENGINE is
    /// defined (set via Tools → GameLyft → Settings → MMP → Solar Engine MMP). The whole
    /// assembly is excluded by defineConstraints when the toggle is off, so the Solar Engine
    /// SDK is not required for projects that don't use it.
    ///
    /// Flow:
    ///   1. Bootstrap at BeforeSceneLoad. Bail out early if 'mmp_install' has already fired
    ///      on this device (PlayerPrefs guard) — no point polling if we'll just no-op.
    ///   2. Coroutine polls SolarEngine.Analytics.getAttribution() every 2s. This implicitly
    ///      waits for SE init: getAttribution() returns null until SE is up AND attribution
    ///      has been fetched, so a single poll handles both waits.
    ///   3. On non-null attribution, extract channel_name / adgroup_name / adplan_name /
    ///      adcreative_name and forward to GameLyftAnalytics.Mmp.LogInstall() which fires
    ///      the shared 'mmp_install' Firebase event.
    ///   4. Times out after 3 minutes — matches the timeout used in the legacy
    ///      GameLyftCore.CheckForAttribution() coroutine.
    /// </summary>
    internal class SolarEngineMmp : MonoBehaviour
    {
        private const float POLL_INTERVAL_SECONDS = 2f;
        private const float TIMEOUT_SECONDS = 180f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Skip everything if the user already disabled the toggle but the asmdef is still
            // present (transient state during a recompile). Belt-and-suspenders with the
            // defineConstraints which is the primary gate.
            var settings = GameLyftSettings.LoadOrNull();
            if (settings == null || !settings.enableSolarEngineMmp) return;

            // Already reported on this device — don't bother spinning up the polling coroutine.
            if (GameLyftAnalytics.Mmp.IsInstallReported) return;

            var go = new GameObject("[GameLyft.SolarEngineMmp]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<SolarEngineMmp>();
            GLLog.Trace("SolarEngineMmp enabled — polling Solar Engine attribution (mmp_install pending).");
        }

        private void Start()
        {
            StartCoroutine(WaitForAttributionAndReport());
        }

        private IEnumerator WaitForAttributionAndReport()
        {
            float startTime = Time.time;

            // Phase 1: wait until the Solar Engine SDK is at least running.
            // We probe by checking for the SolarEngine.Analytics MonoBehaviour singleton,
            // which the SE SDK creates internally on init and DontDestroyOnLoads. Using
            // FindSeAnalytics() (which picks the right Find* API for the Unity version)
            // rather than Analytics.Instance so we don't accidentally CREATE the singleton
            // ourselves and trip a false positive — Analytics.Instance is a lazy-creating
            // accessor.
            //
            // Note: this signals SE init has *started*, not necessarily *completed*. The
            // only reliable "init complete" signal is the initCompletedCallback, but that
            // requires the consumer to wire us in. Singleton-existence is the best probe
            // we can do without external refs. Phase 2 below covers the gap by polling
            // attribution, which only returns non-null after SE is fully up.
            while (FindSeAnalytics() == null
                   && Time.time - startTime < TIMEOUT_SECONDS)
            {
                yield return new WaitForSeconds(POLL_INTERVAL_SECONDS);
            }

            if (FindSeAnalytics() == null)
            {
                // SE was never initialized within the timeout window — consumer probably
                // forgot to call initSeSdk(). Bail without firing; guard stays unset so
                // a future session can retry once they've fixed the integration.
                GLLog.Trace("SolarEngineMmp: Solar Engine SDK not initialized within "
                    + TIMEOUT_SECONDS + "s — bailing (did you call initSeSdk()?).");
                Destroy(gameObject);
                yield break;
            }

            // Phase 2: SE is alive — poll for attribution within the remaining time budget.
            Dictionary<string, object> attribution = null;
            while (attribution == null && Time.time - startTime < TIMEOUT_SECONDS)
            {
                attribution = SolarEngine.Analytics.getAttribution();
                if (attribution == null)
                    yield return new WaitForSeconds(POLL_INTERVAL_SECONDS);
            }

            if (attribution == null)
            {
                // Timed out — SE was up but never delivered attribution (no campaign data,
                // server unreachable, organic install with no fingerprint match, etc.).
                // No event fired, guard not set.
                GLLog.Trace("SolarEngineMmp: timed out — Solar Engine delivered no attribution this session.");
                Destroy(gameObject);
                yield break;
            }

            // Map SE attribution payload → standard MMP schema. Same field names the legacy
            // GameLyftCore.SendMmpInstallToFirebase2 uses.
            string source   = TryGetString(attribution, "channel_name");
            string campaign = TryGetString(attribution, "adgroup_name");
            string adSet    = TryGetString(attribution, "adplan_name");
            string creative = TryGetString(attribution, "adcreative_name");

            GameLyftAnalytics.Mmp.LogInstall(source, campaign, adSet, creative);

            Destroy(gameObject);
        }

        private static string TryGetString(Dictionary<string, object> dict, string key)
        {
            return dict.TryGetValue(key, out var v) && v != null ? v.ToString() : null;
        }

        // Version-gated singleton probe. FindObjectOfType is deprecated in Unity 2023.1+
        // (Unity 6) but is the only option on older LTS releases. FindAnyObjectByType
        // is faster on modern Unity and emits no deprecation warning.
        private static SolarEngine.Analytics FindSeAnalytics()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<SolarEngine.Analytics>();
#else
            return FindObjectOfType<SolarEngine.Analytics>();
#endif
        }
    }
}
