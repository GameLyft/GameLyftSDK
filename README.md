# GameLyft SDK

Slim Firebase-only analytics for Unity. Persistent event queue, FTUE / level / ad-fill tracking, impression-level revenue reporting for AdMob and AppLovin MAX, and one-shot install attribution from Solar Engine, AppsFlyer, Adjust, Singular, and Tenjin.

## Install

### Unity Package Manager (recommended)

In Unity: **Window → Package Manager → + → Add package from git URL**, paste:

```
https://github.com/GameLyft/GameLyftSDK.git?path=Assets/GameLyftSDK
```

Pinned to a specific release:

```
https://github.com/GameLyft/GameLyftSDK.git?path=Assets/GameLyftSDK#v1.0.0
```

### .unitypackage

Download the latest `.unitypackage` from [Releases](https://github.com/GameLyft/GameLyftSDK/releases) and import via **Assets → Import Package → Custom Package**.

## Documentation

See [Assets/GameLyftSDK/README.txt](Assets/GameLyftSDK/README.txt) for full integration docs, prerequisites, the public API reference, and per-MMP setup.

## Prerequisites

- **Firebase Unity SDK** (App + Analytics) — required.
- **Optional**, depending on which integrations you enable: Google Mobile Ads, AppLovin MAX, Solar Engine, AppsFlyer, Adjust, Singular, Tenjin.

GameLyft SDK does not install these for you. Toggle integrations in **Tools → GameLyft → Settings**; matching `GAMELYFT_*` scripting defines are written automatically.

## License

MIT — see [LICENSE](LICENSE).
