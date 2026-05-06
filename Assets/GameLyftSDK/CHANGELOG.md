# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/).

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
