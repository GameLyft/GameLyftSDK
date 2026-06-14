using System.Collections;
using AdjustSdk;
using UnityEngine;

// Reached ONLY through [RuntimeInitializeOnLoadMethod] — no static reference from consumer
// code, so the IL2CPP/device linker could drop the whole DLL as unreferenced and the bootstrap
// would never run (it works in the editor, which doesn't strip). AlwaysLinkAssembly forces the
// linker to keep the assembly; once kept, Unity roots the RuntimeInitializeOnLoadMethod itself.
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace GameLyft.Sdk
{
    /// <summary>
    /// Adjust MMP integration. Auto-starts at app launch when GAMELYFT_ADJUST is
    /// defined (set via Tools → GameLyft → Settings → MMP → Adjust MMP). The whole
    /// assembly is excluded by defineConstraints when the toggle is off, so the Adjust
    /// Unity SDK is not required for projects that don't use it.
    ///
    /// Flow (mirrors the Solar Engine integration since Adjust also exposes a pull
    /// API for attribution — no consumer code change needed):
    ///   1. Bootstrap at BeforeSceneLoad. Bail if 'mmp_install' is already reported.
    ///   2. Phase 1: poll until the AdjustSdk.Adjust MonoBehaviour singleton appears
    ///      in the scene (signals the Adjust SDK has been initialized by the consumer).
    ///   3. Phase 2: call Adjust.GetAttribution(callback) every 2s. The callback fires
    ///      with null pre-attribution and with an AdjustAttribution object once Adjust
    ///      has resolved attribution. Loop exits on first non-null payload.
    ///   4. Map Network/Campaign/Adgroup/Creative to the standard schema and call
    ///      GameLyftAnalytics.Mmp.LogInstall(). The shared one-shot guard inside the
    ///      surface ensures 'mmp_install' fires at most once per device install
    ///      regardless of how many MMPs are enabled.
    ///
    /// Note: Adjust.GetAttribution short-circuits to a no-op in Unity Editor, so this
    /// integration only really exercises on device. That's intentional in Adjust's SDK,
    /// not a bug here.
    /// </summary>
    internal class AdjustMmp : MonoBehaviour
    {
        private const float POLL_INTERVAL_SECONDS = 2f;
        private const float TIMEOUT_SECONDS = 180f;

        // Latest attribution captured by the Adjust callback. Read by the coroutine on
        // the next iteration after each GetAttribution call. Field rather than local so
        // the lambda can write to it from the Adjust callback thread (Adjust marshals
        // back to main thread via its own dispatcher, so no extra sync needed).
        private AdjustAttribution _latestAttribution;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Belt-and-suspenders: defineConstraints in the asmdef is the primary gate,
            // but a flipped toggle mid-recompile could leave a window where this still runs.
            var settings = GameLyftSettings.LoadOrNull();
            if (settings == null || !settings.enableAdjustMmp) return;

            // Already reported on this device — don't spin up the polling coroutine.
            if (GameLyftAnalytics.Mmp.IsInstallReported) return;

            var go = new GameObject("[GameLyft.AdjustMmp]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<AdjustMmp>();
            GLLog.Trace("AdjustMmp enabled — waiting for Adjust SDK + attribution (mmp_install pending).");
        }

        private void Start()
        {
            StartCoroutine(WaitForAttributionAndReport());
        }

        private IEnumerator WaitForAttributionAndReport()
        {
            float startTime = Time.time;

            // Phase 1: wait until the Adjust SDK MonoBehaviour exists in the scene.
            // The consumer's call to Adjust.InitSdk() registers a singleton GameObject;
            // its presence signals init has at least started. FindAdjust() picks the
            // right Find* API based on Unity version (deprecation in 2023.1+).
            while (FindAdjust() == null && Time.time - startTime < TIMEOUT_SECONDS)
            {
                yield return new WaitForSeconds(POLL_INTERVAL_SECONDS);
            }

            if (FindAdjust() == null)
            {
                // Adjust never came up within the timeout — consumer probably forgot to
                // call Adjust.InitSdk(). Bail without firing; guard stays unset so a
                // future session can retry once the integration is fixed.
                GLLog.Trace("AdjustMmp: Adjust SDK not detected within " + TIMEOUT_SECONDS
                    + "s — bailing (did you call Adjust.InitSdk()?).");
                Destroy(gameObject);
                yield break;
            }

            // Phase 2: SDK is alive — poll GetAttribution() until it returns non-null.
            // Each call fires the callback exactly once (synchronously or asynchronously
            // depending on platform), so we re-issue every POLL_INTERVAL until we get a
            // populated AdjustAttribution.
            while (_latestAttribution == null && Time.time - startTime < TIMEOUT_SECONDS)
            {
                Adjust.GetAttribution(attr =>
                {
                    if (attr != null)
                        _latestAttribution = attr;
                });
                yield return new WaitForSeconds(POLL_INTERVAL_SECONDS);
            }

            if (_latestAttribution == null)
            {
                // Timed out — Adjust was up but never delivered attribution (organic
                // install with no tracker, server unreachable, editor short-circuit, etc.).
                GLLog.Trace("AdjustMmp: timed out — Adjust delivered no attribution this session.");
                Destroy(gameObject);
                yield break;
            }

            // DIAGNOSTIC: AdjustAttribution is a typed object (not a dict), so we manually
            // flatten its fields for the schema-discovery event. Remove this block once
            // the production payload shape has been verified across Adjust SDK versions.
            var schemaDump = new Dictionary<string, object>
            {
                { "tracker_token",       _latestAttribution.TrackerToken },
                { "tracker_name",        _latestAttribution.TrackerName },
                { "network",             _latestAttribution.Network },
                { "campaign",            _latestAttribution.Campaign },
                { "adgroup",             _latestAttribution.Adgroup },
                { "creative",            _latestAttribution.Creative },
                { "click_label",         _latestAttribution.ClickLabel },
                { "cost_type",           _latestAttribution.CostType },
                { "cost_amount",         _latestAttribution.CostAmount },
                { "cost_currency",       _latestAttribution.CostCurrency },
                { "fb_install_referrer", _latestAttribution.FbInstallReferrer },
            };
            GameLyftAnalytics.Mmp.LogAttributionSchema("adjust_attribution", schemaDump);

            // Map AdjustAttribution → standard MMP schema.
            //   Network  → source   (acquisition channel — null/empty falls back to "Organic" in MmpSurface)
            //   Campaign → campaign
            //   Adgroup  → ad_set   (Adjust uses "Adgroup" terminology, our schema uses "ad_set")
            //   Creative → creative
            GameLyftAnalytics.Mmp.LogInstall(
                _latestAttribution.Network,
                _latestAttribution.Campaign,
                _latestAttribution.Adgroup,
                _latestAttribution.Creative);

            Destroy(gameObject);
        }

        // Version-gated singleton probe. Same pattern as SolarEngineMmp.FindSeAnalytics().
        // FindObjectOfType is deprecated in Unity 2023.1+ (Unity 6); FindAnyObjectByType
        // is the modern replacement.
        private static Adjust FindAdjust()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<Adjust>();
#else
            return FindObjectOfType<Adjust>();
#endif
        }
    }
}
