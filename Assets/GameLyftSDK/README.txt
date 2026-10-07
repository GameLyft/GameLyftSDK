GameLyft SDK
============

One call per event, delivered to every platform you tick: Firebase, AppsFlyer, Adjust,
Solar Engine, Singular and Airbridge. The SDK does not replace those SDKs — your game imports
and initializes them as usual; GameLyft sends its events through them.

  - Every event is written to disk first, then delivered to each ticked platform once that
    platform's SDK has started and the device is online. Nothing is lost on a crash, a quit or
    no network; the game never waits.
  - Every event carries gl_eid (unique id), gl_ts (event time, Unix seconds) and gl_sid
    (session id). Event names and parameters: GL_EVENT_CATALOG.csv in the SDK repository.
  - Engagement (gl_engagement) is measured by the GameLyft prefab.
  - All calls are safe from any thread and before initialization.

------------------------------------------------------------
SETUP
------------------------------------------------------------

1. Import the package (Package Manager → Add package from git URL:
   https://github.com/GameLyft/GameLyftSDK.git?path=Assets/GameLyftSDK).

2. Tools → GameLyft → Settings:
     Destinations   tick each platform to send events to. A platform is selectable only
                    when its SDK is in the project.
     Ad mediation   tick AdMob and/or AppLovin MAX to enable AdRevenue.Report(...).
     Level milestones (optional) up to 10 level numbers; completing one fires
                    gl_level_<N>_completed once per install (for MMP campaign optimisation).
     Adjust event tokens (when Adjust is ticked) the token of each SDK event you want in
                    Adjust; leave empty to skip that event for Adjust.
   Press Apply.

3. Tools → GameLyft → Add GameLyft Prefab to Scene, in the FIRST scene of the game (the one
   loaded once per launch, usually the loading scene, Build Settings index 0). The prefab
   initializes the SDK, survives scene loads and measures engagement.

4. Initialize Firebase / AppsFlyer / Adjust / … as you already do. Until a platform is ready its
   events wait on disk (nothing is lost).

   AppsFlyer is detected automatically: nothing to add. GameLyft listens for AppsFlyer's own
   start callback (AppsFlyer.OnRequestResponse) and delivers once startSDK() has run; if that
   callback never comes, it delivers 10 s after initSDK(). Keep GameLyft initialized before
   startSDK() (the prefab in the first scene does this).

   For the other platforms, tell GameLyft each one is ready. GameLyft never probes Firebase
   itself — probing it while its dependency check runs throws
   "Don't call Firebase functions before CheckDependencies has finished".

     FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(t => {
         if (t.Result == DependencyStatus.Available) GameLyftAnalytics.MarkReady(GLDestination.Firebase);
     });
     Adjust.InitSdk(adjustConfig);  GameLyftAnalytics.MarkReady(GLDestination.Adjust);
     // Solar Engine: in its init-completed callback (code 0), or right after initSeSdk()
     GameLyftAnalytics.MarkReady(GLDestination.SolarEngine);
     // Singular: after initializing it
     GameLyftAnalytics.MarkReady(GLDestination.Singular);
     // Airbridge starts natively from its settings: mark it at app start
     GameLyftAnalytics.MarkReady(GLDestination.Airbridge);
     // Optional for AppsFlyer: mark it yourself to deliver straight away
     AppsFlyer.startSDK();          GameLyftAnalytics.MarkReady(GLDestination.AppsFlyer);

   MarkReady is safe from any thread. In Test Mode, a ticked platform that is still not ready
   after 60 seconds is reported.

------------------------------------------------------------
EVENTS
------------------------------------------------------------

using GameLyft.Sdk;

// Custom event. Pass the Adjust token to also send it to Adjust (Adjust only accepts events
// created in its dashboard); without it the event goes to every other ticked platform.
GameLyftAnalytics.TrackEvent("shop_opened", new Dictionary<string, object> { { "tab", "coins" } });
GameLyftAnalytics.TrackEvent("shop_opened", parameters, adjustToken: "abc123");

// Level — fires gl_level on EVERY call (starts, fails, retries are all counted).
GameLyftAnalytics.TrackLevelProgression(5, LevelState.level_start);
GameLyftAnalytics.TrackLevelProgression(5, LevelState.level_complete,
    new Dictionary<string, object> { { "score", 12500 }, { "stars", 3 } });

// Onboarding step — gl_ftue
GameLyftAnalytics.TrackFTUE(1, "tutorial_intro", FTUEState.ftue_complete);

// Was an ad available when the game asked? — gl_ad_fill
GameLyftAnalytics.TrackAdFill(GLAdFormat.Interstitial, "level_complete", GLAdResult.Available);

// Validated purchase — gl_purchase
GameLyftAnalytics.TrackPurchase(product.definition.id, product.metadata.isoCurrencyCode,
    (double)product.metadata.localizedPrice, "Coin Pack - Small");

------------------------------------------------------------
AD REVENUE  (gl_ad_impression)
------------------------------------------------------------

// AdMob (Ad mediation → AdMob)
ad.OnAdPaid += v => GameLyftAnalytics.AdRevenue.Report(v, ad.GetResponseInfo(), "interstitial", ad.GetAdUnitID());

// AppLovin MAX (Ad mediation → AppLovin MAX)
MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += (unit, info) => GameLyftAnalytics.AdRevenue.Report(info);

// Any other mediation
GameLyftAnalytics.AdRevenue.Log("ironsource", "vungle", "rewarded", "unit_x", "USD", 0.014);

------------------------------------------------------------
ENGAGEMENT  (gl_engagement)
------------------------------------------------------------

The prefab fires gl_engagement { session_number, session_id, time_ms }, where time_ms is the
total FOREGROUND time of this session: at session start (0), every 30 seconds, and when the app
goes to the background. A session is one app launch. Sessions per user = distinct session_id;
session length = the largest time_ms of the session.

The background event is handed to the platforms immediately (not on the next frame), so the
session's final time is delivered even if the game is closed while in the background. The 30 s
heartbeat restarts when the app returns, so coming back never fires an extra event at once.

It runs on real time (Time.timeScale = 0 does not stop it) and pauses in the background.
Full-screen ads pause the app too, but that time keeps counting:
  - AppLovin MAX: automatic and in real time. GameLyft reads MAX's "ad displayed" the moment the ad
    is on screen (MAX's Unity callbacks only arrive after the ad closes), so the clock keeps running
    through the ad. Capped at 2 minutes per ad.
  - AdMob: call these from your full-screen ads' callbacks:
        ad.OnAdFullScreenContentOpened += () => GameLyftAnalytics.AdStarted();
        ad.OnAdFullScreenContentClosed += () => GameLyftAnalytics.AdClosed();
        ad.OnAdFullScreenContentFailed += e  => GameLyftAnalytics.AdClosed();

------------------------------------------------------------
HOW EACH PLATFORM RECEIVES EVENTS
------------------------------------------------------------

  Firebase      LogEvent. GA4 limits applied: name rules, 25 parameters (the 3 standard ones
                always kept), 40-char names, 100-char values.
  AppsFlyer     sendEvent; parameter values as strings.
  Adjust        TrackEvent with the event's token (Settings table / adjustToken); events without
                a token are skipped for Adjust. Parameters as callback parameters.
                DeduplicationId = gl_eid.
  Solar Engine  track(name, attributes).
  Singular      Event(attributes, name).
  Airbridge     TrackEvent(name, null, custom attributes).

Events are adapted to each platform's limits, never dropped. Test Mode shows each adaptation once.

------------------------------------------------------------
DEBUG
------------------------------------------------------------

Test Mode      SDK warnings on an on-screen panel too.
Verbose        every event tracked and delivered, in the console ([GameLyft] prefix).
Turn both OFF before shipping. The queue file is <persistentDataPath>/GameLyft/queue.log.
