GameLyft SDK — Slim Analytics
==============================

A small Firebase-only analytics layer with persistent event queueing,
FTUE / level progression / ad-fill tracking, impression-level revenue
reporting for AdMob and AppLovin MAX, and one-shot install attribution
('mmp_install') from Solar Engine, AppsFlyer, Adjust, Singular, and Tenjin.

Sends events EXCLUSIVELY to Firebase Analytics. Does not touch any
other SDK except to read attribution data from MMPs you opt into.
You bring your own Firebase init.

------------------------------------------------------------
PREREQUISITES
------------------------------------------------------------

Your project must already have these SDKs imported. GameLyft SDK
does NOT install them for you.

REQUIRED:
  - Firebase Unity SDK (at minimum: Firebase.App + Firebase.Analytics)
    https://firebase.google.com/docs/unity/setup

OPTIONAL (only if you want the corresponding integration):
  - Google Mobile Ads Unity SDK
    Required when GAMELYFT_ADMOB is defined.
    https://developers.google.com/admob/unity/quick-start

  - AppLovin MAX Unity SDK
    Required when GAMELYFT_APPLOVIN is defined.
    https://dash.applovin.com/documentation/mediation/unity/getting-started/integration

  - Solar Engine Unity SDK
    Required when GAMELYFT_SOLAR_ENGINE is defined.
    https://help.solar-engine.com/en/docs/Unity-SDK-Integration-Guide

  - AppsFlyer Unity SDK
    Required when GAMELYFT_APPSFLYER is defined.
    https://dev.appsflyer.com/hc/docs/install-ios-unity-plugin

  - Adjust Unity SDK
    Required when GAMELYFT_ADJUST is defined.
    https://dev.adjust.com/en/sdk/unity/

  - Singular Unity SDK
    Required when GAMELYFT_SINGULAR is defined.
    https://support.singular.net/hc/en-us/articles/360037635452-Unity-SDK-Integration-Guide

  - Tenjin Unity SDK
    Required when GAMELYFT_TENJIN is defined.
    https://github.com/tenjin/tenjin-unity-sdk

If you enable a toggle in Settings but haven't imported the matching
SDK, the corresponding integration code will fail to compile. Disable
the toggle to drop the dependency.

You must call Firebase.FirebaseApp.CheckAndFixDependenciesAsync()
yourself (either manually, or let Auto Initialize wait for it).

------------------------------------------------------------
SETUP
------------------------------------------------------------

1. Import this package into your Unity project.

2. Open  Tools → GameLyft → Settings  and tick whichever integrations
   your project uses:

   MEDIATION (impression-level ad revenue):
   - AdMob Mediation         (writes GAMELYFT_ADMOB)
   - AppLovin MAX Mediation  (writes GAMELYFT_APPLOVIN)

   MMP (install attribution → 'mmp_install' Firebase event):
   - Solar Engine MMP        (writes GAMELYFT_SOLAR_ENGINE)
   - AppsFlyer MMP           (writes GAMELYFT_APPSFLYER)
   - Adjust MMP              (writes GAMELYFT_ADJUST)
   - Singular MMP            (writes GAMELYFT_SINGULAR)
   - Tenjin MMP              (writes GAMELYFT_TENJIN)

   Each toggle defines its scripting symbol so the matching integration
   code compiles in. Toggle off → code excluded → no SDK dependency.

3. Pick ONE of these two initialization styles:

   (a) AUTOMATIC — tick "Auto Initialize" in Tools → GameLyft → Settings.
       The SDK polls for Firebase readiness at app start and calls
       Initialize() for you. No code changes required. Times out after
       5 minutes if Firebase never comes up.

   (b) MANUAL — leave Auto Initialize OFF and call Initialize() yourself
       once Firebase is ready:

       var task = Firebase.FirebaseApp.CheckAndFixDependenciesAsync();
       task.ContinueWith(t =>
       {
           if (t.Result == Firebase.DependencyStatus.Available)
               GameLyft.Sdk.GameLyftAnalytics.Initialize();
       });

   Both can coexist — Initialize() is idempotent. If both fire, the
   second silently returns.

------------------------------------------------------------
USAGE — EVENTS
------------------------------------------------------------

using GameLyft.Sdk;
using System.Collections.Generic;

// Generic event
GameLyftAnalytics.TrackEvent("button_clicked", new Dictionary<string, object>
{
    { "screen", "main_menu" },
    { "button", "play" }
});

// FTUE funnel step
GameLyftAnalytics.TrackFTUE(1, "tutorial_intro", FTUEState.ftue_start);
GameLyftAnalytics.TrackFTUE(1, "tutorial_intro", FTUEState.ftue_complete);

// Level progression (auto-deduped per level + state)
GameLyftAnalytics.TrackLevelProgression(5, LevelState.level_start);
GameLyftAnalytics.TrackLevelProgression(5, LevelState.level_complete,
    new Dictionary<string, object>
    {
        { "score", 12500 },
        { "stars", 3 }
    });

// Ad fill tracking — call at placement time, before/instead of showing
if (myAdSdk.IsInterstitialReady())
{
    GameLyftAnalytics.TrackAdFill(GLAdFormat.Interstitial, "level_complete", GLAdResult.Available);
    myAdSdk.ShowInterstitial();
}
else
{
    GameLyftAnalytics.TrackAdFill(GLAdFormat.Interstitial, "level_complete", GLAdResult.NotAvailable);
}

// Session count (auto-attached to every event as 'session' param at flush time)
int n = GameLyftAnalytics.SessionCount;

------------------------------------------------------------
USAGE — AD REVENUE
------------------------------------------------------------

// AdMob impression-level revenue (only available if GAMELYFT_ADMOB is on).
// Fires a single 'gl_ad_impression' Firebase event.
interstitialAd.OnAdPaid += (adValue) =>
{
    GameLyftAnalytics.AdRevenue.Report(
        adValue,
        interstitialAd.GetResponseInfo(),
        "interstitial",
        interstitialAd.GetAdUnitID());
};

// AppLovin MAX revenue (only available if GAMELYFT_APPLOVIN is on).
// Fires the same 'gl_ad_impression' schema so dashboards stay unified.
MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += (adUnitId, adInfo) =>
{
    GameLyftAnalytics.AdRevenue.Report(adInfo);
};

// For mediations without first-class support (ironSource, Unity Ads, TopOn, etc.)
// use the low-level Log() primitive with the same 'gl_ad_impression' schema:
GameLyftAnalytics.AdRevenue.Log(
    platform: "ironsource",
    source:   "vungle",
    format:   "rewarded",
    adUnit:   "my_unit_id",
    currency: "USD",
    revenue:  0.014);

------------------------------------------------------------
USAGE — MMP / INSTALL ATTRIBUTION
------------------------------------------------------------

When any MMP toggle is on, the SDK fires a one-shot 'mmp_install'
Firebase event with these 4 fields:

  source    — acquisition channel (defaults to "Organic" if missing)
  campaign  — campaign name
  ad_set    — ad set / ad group name
  creative  — creative / ad name

The event is guarded with a PlayerPrefs flag — fires AT MOST ONCE per
device install regardless of how many MMP toggles are enabled. Whichever
MMP delivers attribution first wins; the rest no-op.

  -- SOLAR ENGINE: zero-config --------------------------------------

  Tick the Solar Engine MMP toggle. The SDK polls for the SE Analytics
  singleton, then calls getAttribution() every 2s within a 3-minute
  budget. On non-null attribution, fields are mapped:

    channel_name     → source
    adgroup_name     → campaign
    adplan_name      → ad_set
    adcreative_name  → creative

  -- ADJUST: zero-config --------------------------------------------

  Tick the Adjust MMP toggle. The SDK polls for the AdjustSdk.Adjust
  singleton, then calls Adjust.GetAttribution() every 2s within the
  3-minute budget. On the first non-null AdjustAttribution:

    Network   → source
    Campaign  → campaign
    Adgroup   → ad_set
    Creative  → creative

  Caveat: Adjust.GetAttribution short-circuits to a no-op in Unity
  Editor by Adjust SDK design. Test on device for real attribution.

  -- TENJIN: zero-config --------------------------------------------

  Tick the Tenjin MMP toggle. The SDK observes the BaseTenjin singleton
  your existing Tenjin.getInstance(apiKey) call creates, then invokes
  GetAttributionInfo() within the 3-minute budget. On the first
  populated callback:

    ad_network     → source     (with "(not set)" → null → falls back
                                  to "Organic" via the surface default)
    campaign_name  → campaign
    (no equivalent) → ad_set    (Tenjin has no first-class adgroup)
    creative_name  → creative

  Schema is documented by Tenjin so the mapping is authoritative.
  Requires that you call Tenjin.getInstance(apiKey) somewhere in your
  app (which you do as part of normal Tenjin integration). If Tenjin
  init is delayed past ~3 minutes of foreground time, this session is
  skipped — the next session retries.

  -- APPSFLYER: one-line consumer hookup ----------------------------

  Tick the AppsFlyer MMP toggle, then add ONE line to your existing
  IAppsFlyerConversionData handler:

      using AppsFlyerSDK;
      using GameLyft.Sdk;

      public class MyAppsFlyerHandler : MonoBehaviour, IAppsFlyerConversionData
      {
          public void onConversionDataSuccess(string conversionData)
          {
              AppsFlyer.AFLog("didReceiveConversionData", conversionData);
              var dict = AppsFlyer.CallbackStringToDictionary(conversionData);
              // add deferred deeplink logic here
              AppsFlyerMmp.HandleConversionData(conversionData);  // ← this line
          }
          public void onConversionDataFail(string error) { /* ... */ }
          public void onAppOpenAttribution(string data)   { /* ... */ }
          public void onAppOpenAttributionFailure(string error) { /* ... */ }
      }

  Field mapping:

    media_source  → source
    campaign      → campaign
    adset         → ad_set
    af_ad         → creative

  HandleConversionData() also has an overload taking the parsed
  Dictionary<string, object> if you've already called
  AppsFlyer.CallbackStringToDictionary upstream.

  -- SINGULAR: zero-config (with caveats) ---------------------------

  Tick the Singular MMP toggle. The SDK auto-registers a
  SingularDeviceAttributionCallbackHandler at app launch.

  IMPORTANT: SingularSDK.SetSingularDeviceAttributionCallbackHandler
  is single-slot. Enabling Singular MMP REPLACES any handler your
  project already registered. Move that handler's logic into the
  diagnostic event below or fork SingularMmp.cs if you need it.

  Field mapping is BEST-GUESS — Singular's on-device callback schema
  is not publicly documented. Keys below are based on Singular's REST
  Attribution API:

    network         → source
    campaign_name   → campaign
    (no equivalent) → ad_set     (no first-class adgroup on device side)
    creative_name   → creative

  Verify against the singular_attribution diagnostic event after the
  first production install and update the mapping in SingularMmp.cs
  if needed.

  Caveat: on iOS with ATT denied, Singular's deviceAttributionCallback
  often does not fire (SKAN-only flow). For iOS attribution coverage
  AppsFlyer or Adjust tend to be more reliable client-side.

  -- OTHER MMPS (Branch, Kochava, ...) ------------------------------

  Same pattern as AppsFlyer — extract source / campaign / ad_set /
  creative from your MMP's attribution payload and call directly:

      GameLyftAnalytics.Mmp.LogInstall(source, campaign, adSet, creative);

  The shared one-shot guard handles dedup automatically.

  -- DIAGNOSTIC EVENTS (TEMPORARY) ----------------------------------

  Singular, AppsFlyer, Adjust, and Tenjin each emit an additional
  diagnostic Firebase event alongside 'mmp_install' so the actual
  schema can be confirmed via BigQuery:

    singular_attribution    — full raw Singular payload
    appsflyer_attribution   — full raw AppsFlyer conversionData payload
    adjust_attribution      — flattened AdjustAttribution fields
    tenjin_attribution      — full raw Tenjin attributionInfoData payload

  Each event flattens its native attribution payload into Firebase
  parameters with GA4 limits enforced (capped at 24 keys + 1 "_dropped"
  count, value truncation at 100 chars, key sanitization). Once you've
  confirmed the keys in production, harden the field mappings in the
  per-MMP scripts and remove the LogAttributionSchema() calls.

  Solar Engine isn't included because its schema was confirmed when
  the integration was built.

------------------------------------------------------------
NOTES
------------------------------------------------------------

- Events fired before Initialize() are queued and dispatched once
  Initialize() is called.
- The queue is persisted to PlayerPrefs and survives app pause/quit.
- Failed Firebase sends are retried with backoff.
- TrackLevelProgression de-duplicates per (level, state) pair so the
  same level_complete is never reported twice.
- The 'session' parameter is injected at flush time, not queue time —
  events queued before Initialize() (or carried over from a prior run)
  report the live session count rather than a stale 0.
- 'mmp_install' is one-shot per device install via PlayerPrefs guard
  (key: GLSdk_mmp_install_sent). To force re-fire during testing,
  clear PlayerPrefs or delete that specific key.

------------------------------------------------------------
TEST MODE
------------------------------------------------------------

Tools → GameLyft → Settings has a "Test Mode" toggle.

When OFF (default, use for production):
  - SDK integration warnings go to Debug.LogWarning only.

When ON (use during integration / QA):
  - Warnings ALSO appear as a stacked on-screen IMGUI panel in the
    top-left corner. Each warning has an × close button. A "Clear All"
    button drops the whole stack.

Warnings are fired for:
  - TrackEvent/Track* called before Initialize()
    (suppressed automatically while Auto Initialize is polling)
  - Initialize() called when Firebase does not appear to be ready
  - AdRevenue.Report called with null AdValue / AdInfo
  - Any event with more than 25 parameters (GA4 server-side limit)
  - Auto Initialize timed out waiting for Firebase (5 min)

Turn Test Mode OFF before shipping — the overlay is a developer tool.

------------------------------------------------------------
PUBLIC API
------------------------------------------------------------

namespace GameLyft.Sdk
{
    public static class GameLyftAnalytics
    {
        public static bool IsInitialized { get; }
        public static int  SessionCount  { get; }

        public static readonly AdRevenueSurface AdRevenue;
        public static readonly MmpSurface       Mmp;

        public static void Initialize();
        public static void TrackEvent(string eventName, Dictionary<string, object> parameters = null);
        public static void TrackFTUE(int stepNumber, string stepName, FTUEState state);
        public static void TrackLevelProgression(int levelNumber, LevelState state, Dictionary<string, object> levelData = null);
        public static void TrackAdFill(GLAdFormat adFormat, string placement, GLAdResult result);

        public sealed class AdRevenueSurface
        {
            // Low-level — use for mediations without first-class support.
            public void Log(string platform, string source, string format,
                            string adUnit, string currency, double revenue);
        }

        public sealed class MmpSurface
        {
            // Shared low-level entry point used by every MMP integration.
            // Fires 'mmp_install' Firebase event ONCE per device install
            // (subsequent calls no-op via PlayerPrefs guard).
            public void LogInstall(string source, string campaign, string adSet, string creative);

            // True if 'mmp_install' has already been fired on this device.
            public bool IsInstallReported { get; }

            // Diagnostic — emit a raw attribution payload as a Firebase event for
            // schema discovery via BigQuery. Used internally by the per-MMP
            // integrations (singular_attribution, appsflyer_attribution,
            // adjust_attribution, tenjin_attribution events). Public so consumers
            // can capture an unmodeled MMP's schema the same way.
            public void LogAttributionSchema(string firebaseEventName, Dictionary<string, object> attributionPayload);
        }
    }

    // Extension methods on AdRevenue (attached to the surface)

    // Available only when GAMELYFT_ADMOB is defined.
    public static void Report(this GameLyftAnalytics.AdRevenueSurface s,
        AdValue adValue, ResponseInfo responseInfo, string adFormat, string adUnitId);

    // Available only when GAMELYFT_APPLOVIN is defined.
    public static void Report(this GameLyftAnalytics.AdRevenueSurface s, MaxSdkBase.AdInfo adInfo);

    // AppsFlyer integration helper. Available only when GAMELYFT_APPSFLYER is defined.
    public static class AppsFlyerMmp
    {
        public static void HandleConversionData(string conversionDataJson);
        public static void HandleConversionData(Dictionary<string, object> conversionData);
    }

    public enum FTUEState   { ftue_start, ftue_complete }
    public enum LevelState  { level_start, level_complete, level_fail, level_skip, level_restart, level_pause, level_resume }
    public enum GLAdFormat  { Banner, Mrec, Interstitial, Rewarded, AppOpen }
    public enum GLAdResult  { Available, NotAvailable }
}
