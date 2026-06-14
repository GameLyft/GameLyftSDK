# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [1.0.5] - 2026-06-14

### Fixed

- **Tenjin MMP sub-assembly is now actually imported (follow-up to 1.0.4).** v1.0.4 shipped the `Runtime/Tenjin/` folder's *file* metas (`TenjinMmp.cs.meta`, `GameLyft.Sdk.Tenjin.asmdef.meta`) but omitted the **folder's own meta** (`Runtime/Tenjin.meta`). In an immutable (UPM) package Unity does not generate a missing folder meta, so it ignored the entire folder — the `GameLyft.Sdk.Tenjin` asmdef never imported or compiled, and the 1.0.4 fix didn't take effect. Added the missing folder meta. (Audited the rest of the package: every other file and folder already has a tracked `.meta`.)

## [1.0.4] - 2026-06-14

### Fixed

- **Tenjin MMP now actually runs when the SDK is consumed as a UPM package.** The Tenjin module previously lived as a loose script at `Assets/GameLyftSDK/Tenjin/TenjinMmp.cs` with no asmdef — a layout that only compiles when the SDK is *vendored* into a project's `Assets/`. Imported as a UPM git package, Unity does **not** add loose package scripts to `Assembly-CSharp`, so `TenjinMmp` was never compiled, its `BeforeSceneLoad` bootstrap never ran, and Tenjin attribution (`mmp_install` / `tenjin_attribution`) silently never fired (no `TenjinMmp enabled…` log even with `GAMELYFT_TENJIN` set). It now lives in its own package sub-assembly **`GameLyft.Sdk.Tenjin`** (`Runtime/Tenjin/`, `defineConstraints: GAMELYFT_TENJIN`) and resolves `BaseTenjin` + `GetAttributionInfo` via **reflection** — so it compiles as part of the package without referencing `Assembly-CSharp` (which an asmdef cannot do), keeps the zero-consumer-code auto-poll, and degrades to a clean no-op (with a one-time warning) if the Tenjin SDK isn't present. The other MMPs were unaffected — each already has its own `Runtime/` sub-asmdef.

## [1.0.3] - 2026-06-13

### Added

- **Verbose Logging** setting (*Tools → GameLyft → Settings → Debug*). When ON, the SDK prints detailed `[GameLyft]`-prefixed console logs of all activity: every event tracked (with parameters), queue enqueue + flush to Firebase (showing the exact params incl. injected `event_type`/`session`), level-progression dedupe skips, purchases (`gl_purchase`), ad-impression revenue (`gl_ad_impression`), MMP attribution lifecycle (poll start / found / timeout / `mmp_install` fired-or-skipped / `*_attribution` schema), persisted-queue restore, and send retries/persist errors. Backed by a new central `GLLog` logger: `Trace` is gated by the setting; lifecycle milestones (`Info`), warnings (`Warn`), and errors (`Error`) always log. Off by default — keep off in production (high volume).

## [1.0.2] - 2026-06-13

### Added

- **One-click "Wire AppsFlyer Handler" button** in *Tools → GameLyft → Settings* (under the AppsFlyer MMP toggle). Injects the 3 PlayerPrefs bridge lines at the **start** of every `onConversionDataSuccess(string)` handler in the project (existing handler code untouched), delimited by `GAMELYFT_APPSFLYER_BRIDGE_BEGIN/END` markers so it is idempotent and reversible — an **Unwire** button removes exactly the injected block, and **Re-scan** refreshes status. Re-run after an AppsFlyer SDK upgrade overwrites the handler file. Backed by the Editor-only `AppsFlyerConversionWirer`.

### Changed

- **AppsFlyer MMP is now automatic.** `AppsFlyerMmp` became a `BeforeSceneLoad` MonoBehaviour that auto-polls PlayerPrefs for AppsFlyer's conversion payload and fires the one-shot `mmp_install` itself — matching the auto-start pattern of every other MMP module (it was previously the *only* MMP requiring a manual `HandleConversionData(...)` call). The consumer's `onConversionDataSuccess` now just stashes the raw payload in PlayerPrefs (`AppsflyerGameLyftConversionData` + `isAppsflyerGameLyftConversionSet`), which crosses the AppsFlyer-SDK ↔ Assembly-CSharp asmdef boundary that a direct call cannot. PlayerPrefs persistence means a callback that lands after the poll window is still picked up on the next launch. `HandleConversionData(string)` / `(Dictionary)` remain public as a manual fallback.
- Corrected the **AppsFlyer MMP** settings tooltip — it previously claimed the SDK called `AppsFlyer.getConversionData()` automatically after init, which was never implemented (the real path was a manual hookup). It now describes the PlayerPrefs auto-poll mechanism.

## [1.0.1] - 2026-06-13

### Changed

- `event_type` is now injected on **every** Firebase event at flush time (alongside `session`) with the value `gl_analytics` — previously it was `progression_analytics` and only added by `TrackEvent`. This unifies the parameter across all SDK output (`gl_purchase`, `gl_ad_impression`, `mmp_install`, FTUE / level / ad-fill, and the `*_attribution` diagnostics). Events queued/persisted by 1.0.0 are rewritten with the new value on flush.
- Settings editor (`Tools → GameLyft → Settings`): integration toggles are now **staged** behind an **Apply** button. Toggle multiple integrations, then Apply once to save the asset and update all scripting defines in a single recompile (previously each toggle wrote its define immediately, causing a recompile per click). Added a **Revert** button and per-toggle "(pending Apply)" indicators. Define writes are batched to one `SetScriptingDefineSymbols` call per build target.

## [1.0.0] - 2026-05-06

### Added

- Initial release.
- Firebase-only analytics core with persistent PlayerPrefs-backed event queue.
- `TrackEvent`, `TrackFTUE`, `TrackLevelProgression`, `TrackAdFill`, `TrackPurchase` APIs.
- `gl_purchase` Firebase event for in-app purchases (productId / currency / value / success / optional product_name).
- Auto-deduplication for level progression events.
- Auto Initialize option that polls for Firebase readiness.
- Test Mode with on-screen IMGUI warning overlay.
- Ad revenue: AdMob `Report` extension, AppLovin MAX `Report` extension, generic `Log` primitive — all firing the unified `gl_ad_impression` Firebase event.
- MMP attribution (`mmp_install` event) with one-shot PlayerPrefs guard, sharing a single `Mmp.LogInstall(source, campaign, adSet, creative)` API across integrations:
  - Solar Engine — auto-poll, zero-config
  - AppsFlyer — one-line consumer hookup
  - Adjust — auto-poll, zero-config
  - Singular — auto-attaching callback handler
  - Tenjin — auto-poll observer
- Schema-discovery diagnostic events (`singular_attribution`, `appsflyer_attribution`, `adjust_attribution`, `tenjin_attribution`) for verifying MMP payloads in production.
- `GAMELYFT_*` scripting defines per integration with sub-assembly isolation where the MMP SDK has its own asmdef.
- Editor settings UI (`Tools → GameLyft → Settings`) with mediation, MMP, init, and debug toggles. Drift reconciliation in both `OnEnable` and `[InitializeOnLoadMethod]`.
