GameLyft SDK — Slim Analytics
==============================

A small Firebase-only analytics layer with persistent event queueing,
FTUE / level progression / ad-fill tracking, and impression-level
revenue reporting for AdMob and AppLovin MAX.

Sends events EXCLUSIVELY to Firebase Analytics. Does not touch any
other SDK. You bring your own Firebase init.

------------------------------------------------------------
PREREQUISITES
------------------------------------------------------------

Your project must already have these SDKs imported. GameLyft SDK
does NOT install them for you.

REQUIRED:
  - Firebase Unity SDK (at minimum: Firebase.App + Firebase.Analytics)
    https://firebase.google.com/docs/unity/setup

OPTIONAL (only if you want the corresponding ad revenue overload):
  - Google Mobile Ads Unity SDK
    Required when GAMELYFT_ADMOB is defined.
    https://developers.google.com/admob/unity/quick-start

  - AppLovin MAX Unity SDK
    Required when GAMELYFT_APPLOVIN is defined.
    https://dash.applovin.com/documentation/mediation/unity/getting-started/integration

If you enable AdMob Mediation in Settings but haven't imported the
Google Mobile Ads Unity SDK, the project will fail to compile.
Same for AppLovin MAX.

You must call Firebase.FirebaseApp.CheckAndFixDependenciesAsync()
yourself (either manually, or let Auto Initialize wait for it).

------------------------------------------------------------
SETUP
------------------------------------------------------------

1. Import this package into your Unity project.

2. Open  Tools → GameLyft → Settings  and tick whichever ad
   mediation(s) your project uses:

   - AdMob Mediation         (writes GAMELYFT_ADMOB)
   - AppLovin MAX Mediation  (writes GAMELYFT_APPLOVIN)

   Enabling a checkbox defines the matching symbol so the relevant
   AdRevenue.Report extension method compiles in. You can enable both.

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

   Both can coexist — Initialize() is idempotent. If both fire, the second
   silently returns. Safe to have auto-init on AND keep your manual call.

------------------------------------------------------------
USAGE
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

// Session count (auto-attached to every event as 'session' param)
int n = GameLyftAnalytics.SessionCount;

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
NOTES
------------------------------------------------------------

- Events fired before Initialize() are queued and dispatched once
  Initialize() is called.
- The queue is persisted to PlayerPrefs and survives app pause/quit.
- Failed Firebase sends are retried with backoff.
- TrackLevelProgression de-duplicates per (level, state) pair so the
  same level_complete is never reported twice.

------------------------------------------------------------
TEST MODE
------------------------------------------------------------

Tools → GameLyft → Settings has a "Test Mode" toggle.

When OFF (default, use for production):
  - SDK integration warnings go to Debug.LogWarning only.

When ON (use during integration / QA):
  - Warnings also appear as a stacked on-screen IMGUI panel in the
    top-left corner. Each warning has an × close button. A "Clear All"
    button drops the whole stack.

Warnings are fired for:
  - TrackEvent/Track* called before Initialize()
    (suppressed automatically while Auto Initialize is polling)
  - Initialize() called when Firebase does not appear to be ready
  - AdRevenue.Report called with null AdValue / AdInfo
  - Any event with more than 25 parameters (GA4 server-side limit)
  - Auto Initialize timed out waiting for Firebase (5 min)

Remember to turn Test Mode OFF before shipping — the overlay is a
developer tool, not a production feature.

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
    }

    // Extension methods (attached to GameLyftAnalytics.AdRevenue)

    // Available only when GAMELYFT_ADMOB is defined.
    public static void Report(this GameLyftAnalytics.AdRevenueSurface s,
        AdValue adValue, ResponseInfo responseInfo, string adFormat, string adUnitId);

    // Available only when GAMELYFT_APPLOVIN is defined.
    public static void Report(this GameLyftAnalytics.AdRevenueSurface s, MaxSdkBase.AdInfo adInfo);

    public enum FTUEState   { ftue_start, ftue_complete }
    public enum LevelState  { level_start, level_complete, level_fail, level_skip, level_restart, level_pause, level_resume }
    public enum GLAdFormat  { Banner, Mrec, Interstitial, Rewarded, AppOpen }
    public enum GLAdResult  { Available, NotAvailable }
}
