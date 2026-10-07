# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [1.1.4] - 2026-10-07

### Added
- **Firebase is detected automatically — no `MarkReady` needed.** The SDK reads two values of
  `FirebaseApp`'s own private state, about once a second: whether a dependency check is running
  (`IsCheckDependenciesRunning`) and whether the game has created a Firebase app (`nameToProxy`).
  Firebase is ready once no check is running and the app exists. The SDK never calls a Firebase
  API that can throw "Don't call Firebase functions before CheckDependencies has finished", never
  runs the check and never creates the app, so it cannot interfere with the game's Firebase setup.
  Verified with Firebase Unity SDK 13.10.0; if those members are missing in another version,
  detection turns itself off with one warning and `MarkReady(GLDestination.Firebase)` is needed.
  `MarkReady` still works as an immediate override.

## [1.1.3] - 2026-10-07

### Fixed
- **AppLovin MAX "ad displayed" is now recognised in real time.** The 1.1.2 hook received the
  event on time but read its name with a text search, which picked up a "name" field from the ad
  info nested inside "displayed" events, so the start was missed (hidden was seen) and ad time was
  only credited on resume. The hook now parses the event with MAX's own JSON reader and reads the
  top-level name and ad unit.

## [1.1.2] - 2026-10-07

### Changed
- **Events are delivered from a background thread ("GameLyft.Dispatch").** Every event goes to
  Firebase / AppsFlyer / Adjust / … the moment it is tracked, also while Unity's main thread is
  paused (a full-screen ad on screen, the app in the background) — before, delivery ran in
  `Update()` and waited for the game to resume. On Android the thread is attached to the JNI VM.
  A destination whose SDK throws when called off the main thread is switched to main-thread
  delivery automatically for the rest of the run.
- **AppLovin MAX "ad displayed" is seen the moment the ad is on screen.** MAX's Unity plugin
  queues displayed / hidden for the main thread, which is paused while the ad shows, so they used
  to arrive only after the ad closed. The SDK now takes MAX's native background-callback slot
  (`MaxUnityPlugin.setBackgroundCallback` / `_MaxSetBackgroundCallback`), reads displayed /
  hidden / failed-to-display on arrival, and forwards every event unchanged to
  `MaxSdkCallbacks.ForwardEvent`, so the game's own MAX callbacks behave exactly as before. When an
  ad is reported within 3 s of a pause, engagement counts that pause as ad time immediately and
  the clock keeps running. If the hook cannot be installed (an unexpected MAX plugin version), the
  previous behaviour applies: ad time is credited when the game resumes. Verified with MAX 8.6.4.
- **Ad time is capped at 2 minutes per ad** (was 3), so a player who leaves the app while an ad is
  up is credited at most 2 minutes.
- Ads are tracked per ad unit, so an ad reported both in real time and through MAX's C# events is
  counted once.

## [1.1.1] - 2026-10-06

### Fixed
- **The session's final engagement event is no longer stranded.** The background (and quit)
  `gl_engagement` is handed to every ready platform immediately, then flushed to disk; before,
  it waited for the next frame and was only delivered on the next launch when the game was closed
  in the background. It stays in the durable queue for platforms that are not ready yet.
- **No near-duplicate heartbeat on resume.** The 30 s heartbeat restarts when the app returns to
  the foreground; before, an overdue heartbeat fired right after the background event.
- **Interstitial / rewarded time with AppLovin MAX is counted again.** MAX delivers "displayed"
  and "hidden" only after the app resumes, so the pause an ad causes looked like the player
  leaving and the ad time was dropped (e.g. a 62 s interstitial). A pause in which an ad was shown
  and closed is now credited back on resume (capped at 3 minutes per ad). An ad that opens on
  resume (app open) is not mistaken for this.

## [1.1.0] - 2026-10-06

### Added
- **AppsFlyer is detected automatically — no `MarkReady` needed.** The SDK subscribes to
  `AppsFlyer.OnRequestResponse` at startup, before the game calls `startSDK()`, so the AppsFlyer
  plugin requests its native start callback; the first callback (any status code) marks AppsFlyer
  ready. If it never arrives (no callback object, launch blocked, offline), AppsFlyer is treated as
  ready 10 s after `initSDK()` (`AppsFlyer.instance.isInit`). `MarkReady(GLDestination.AppsFlyer)`
  still works and wins immediately.
- The object named in `AppsFlyer.CallBackObjectName` gets an `AppsFlyer` component if it lacks one
  (the stock `AppsFlyerObject` prefab only carries `AppsFlyerObjectScript`, so the native start
  callback had no receiver). Games that pass no callback object to `initSDK` get a hidden
  `GameLyft.AppsFlyerReceiver` instead.

### Changed
- The Test Mode "never marked ready" warning (60 s) now checks each destination's own readiness,
  so an auto-detected AppsFlyer is no longer reported.

## [1.0.0] - 2026-10-01

First release of the GameLyft SDK as an events helper: one call per event, delivered to every
platform ticked in Settings, through a durable on-disk queue, with built-in engagement.

### Added
- **Destinations.** Tick Firebase and/or AppsFlyer, Adjust, Solar Engine, Singular, Airbridge in
  Tools → GameLyft → Settings; every event is delivered to each through that platform's own SDK
  (initialized by the game as usual). A platform can only be ticked when its SDK is in the project.
- **`GameLyftAnalytics.MarkReady(GLDestination)`.** The game declares each platform initialized;
  its events are delivered from then on. The SDK never probes Firebase or the MMP SDKs (probing
  `FirebaseApp.DefaultInstance` while `CheckAndFixDependenciesAsync` runs throws
  `Don't call Firebase functions before CheckDependencies has finished`).
- **Durable queue.** Every event is appended to `persistentDataPath/GameLyft/queue.log` on a
  background thread the moment it is tracked, then delivered per destination once that platform
  is marked ready and the device is online. At-least-once delivery; a crash resend carries the
  same `gl_eid`. Undelivered events survive restarts and are never dropped.
- **Standard parameters on every event:** `gl_eid` (10-char unique id), `gl_ts` (event time,
  Unix seconds), `gl_sid` (8-char session id).
- **GameLyft prefab** (`Runtime/Prefabs/GameLyft.prefab`; Tools → GameLyft → Add GameLyft Prefab
  to Scene) for the first scene: initializes the SDK and hosts **GL_Engagement**, which fires
  `gl_engagement { session_number, session_id, time_ms }` at session start, every 30 s and on
  background — total foreground ms, on real time (unaffected by `Time.timeScale`). Full-screen ad
  time stays counted: automatic for AppLovin MAX; `GameLyftAnalytics.AdStarted()` / `AdClosed()`
  for AdMob. A "Debug Logs" checkbox on the component logs every step.
- **Events:** `TrackEvent(name, params, adjustToken)`, `TrackLevelProgression` (`gl_level`, every
  call), `TrackFTUE` (`gl_ftue`), `TrackAdFill` (`gl_ad_fill`), `TrackPurchase` (`gl_purchase`),
  `AdRevenue.Report` for AdMob / AppLovin MAX and `AdRevenue.Log` for others (`gl_ad_impression`).
- **Level milestones** (Settings, up to 10 levels): `gl_level_<N>_completed` once per install.
- **Adjust tokens:** a token table for the SDK's events in Settings; `adjustToken` for custom events.
- **Per-destination validation** (GA4 limits for Firebase, string values for AppsFlyer, …): events
  are adapted, never dropped; Test Mode warns once per adaptation.
- All public calls are thread-safe and may be made before initialization.
- Event catalog: `GL_EVENT_CATALOG.csv` at the repository root.
