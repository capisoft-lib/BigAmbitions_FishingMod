# Fishing Mod

Fishing Mod adds a one-click fishing cast to Big Ambitions.

Version 1.0.1 casts from the player's current position to the exact clicked water
coordinates. No shoreline path or automatic walking is required. The player must be within
10 horizontal metres of a mapped water polygon (X/Z projection, ignoring height).
Both click and F enforce this limit and display the measured distance if too far away. Outdoors, on foot
and empty-handed, click water normally or point at mapped water and press **F**
to cast using the same land and obstacle checks as a normal click. UI remains protected.
Invisible support layers are ignored only when their ray impact is also over mapped water.

The atlas contains 408 polygons, excluding wooden pontoons, the Yacht Club deck, the Industry City ParkingSea pier, Harbor park, waterfront avenue decks, StreetCurveHarbor and the four Street south-arms. The 21 verified support-layer filters are preserved.
Casting uses distance-aware flight timing, constrained two-hand grip targets,
calibrated wrists and finger closing, without late torso rotations that detach hands.

## Static water detection

Fishing uses 408 embedded open-water polygons for the city port, bridge sector,
Industry City and the Hamptons. A screen ray intersects the sea plane at Y = -2.8 m;
the mod checks that target's Unity X/Z coordinates, then rejects solid objects
between the camera and the water. No scene-wide water scan or periodic indexing
runs during gameplay. The mod remains self-contained; no boat library is required.

The polygons describe open surfaces from the existing atlas. They exclude projected
docks/bridges present in its ground export and do not cover arbitrary lakes, pools,
new terrain changes or every submerged passage. Real shoreline accuracy still
needs in-game validation. Normal land-click behavior remains enabled. The log records the selected sub-polygon on an accepted cast.

## Current interaction

1. Be outdoors, on foot, with empty hands and no menu open.
2. Left-click visible water, or point at mapped water and press F to cast from here.
3. The character stops in place, faces the clicked water and performs a long two-handed cast.
4. The cast has an 80% chance of attracting a fish. A selected fish bites after a random 2–20 second wait counted from the bobber's water impact (not from the end of the follow-through animation); otherwise the empty line is automatically reeled in after 20 seconds in the water. Menus, lost focus and game pause freeze this timer.
5. A bite starts the keyboard QTE at 30% line progress. Its round control wheel is centered on screen with no rectangular panel: one of four direction arrows turns black, or the centre circle turns black for Space, while the green outer ring shows the current progress. Press the matching arrow/WASD/ZQSD key or Space; Escape releases the fish.

During casting, waiting or empty-line retrieval, click, move or press Escape to cancel immediately without a sale or line-break charge. Native movement bindings are respected; QTE direction keys still reel the fish rather than cancelling. A cancelling ground click is forwarded to normal walking after the fishing navigation blocker is released.

The sequence creates its own rod, reel, line, bobber and splash at runtime. It does not include or redistribute a Big Ambitions character model. A land, UI, vehicle, building or interactable-object click keeps its normal behavior.

The cast, reel release, bobber splash, empty-line/QTE reeling, light QTE success and failure cues, landed fish and broken line each have a dedicated sound. They are loaded from the installed mod's `Sounds` folder, routed to the game's effects mixer when it is available, and remain optional so a missing file cannot make fishing unplayable. All eight packaged effects are redistributable CC0 assets; exact authors, sources and processing are recorded in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Every completed cast refreshes one native **+10 happiness** modifier for **48 in-game hours**. It never stacks duplicate fishing bonuses. A successful catch adds one best-catch modifier for **72 in-game hours**; catching a worse fish while a better bonus is active does not replace or refresh the better one.

| Fish | Share among hooked fish | Auto-sale | Happiness | Clean pulls | Key window |
| --- | ---: | ---: | ---: | ---: | ---: |
| Roach | 30% | $5 | +2 | 4 | 1.35 s |
| Perch | 24% | $8 | +3 | 5 | 1.25 s |
| Trout | 18% | $15 | +5 | 6 | 1.15 s |
| Carp | 13% | $25 | +7 | 8 | 1.05 s |
| Pike | 9% | $40 | +10 | 10 | 0.95 s |
| Sturgeon | 6% | $75 | +14 | 12 | 0.90 s |

Every successfully landed fish is immediately sold into the native bank balance, with a transaction in the financial history and a result notification. Each catch pays its own price, even if a better fish still supplies the active happiness bonus. There is no inventory item to sell manually.

A line break at **0% QTE progress** costs **up to $5**, once per fish. The charge is capped by the available positive balance: $2.25 remaining means a $2.25 charge, while zero or negative balances are left alone. Empty casts, Escape releases, interrupted/unloaded sessions and intermediate QTE mistakes are free. These recreational transactions do not count as casino wins/losses or tax-deductible business expenses. The existing happiness rules are unchanged.

**FR — Vente automatique :** gardon 5 $, perche 8 $, truite 15 $, carpe 25 $, brochet 40 $, esturgeon 75 $. Une casse à 0 % coûte au maximum 5 $, sans créer ni aggraver un découvert. Les lancers vides et les annulations restent gratuits ; les bonus de bonheur sont conservés selon les règles existantes.

Each QTE starts at 30% progress. A correct step reels in 3.5 m; a wrong displayed-direction/reel key or a timeout releases 1.75 m—exactly half a successful pull. Reaching 100% catches the fish, while falling back to 0% lets it escape. Rare fish remain achievable but demand a longer sequence.

Water polygons are built offline and loaded once. No city scan or periodic water indexing runs during play. Clicks query the atlas and a reused physics buffer to preserve live obstacle checks. Fishing visuals and materials are prepared when the character is ready, then reused between casts. Fixed line/ripple samples are precomputed and proximity results are cached while the player stays at the same X/Z position. Input, moving obstacles, animation and QTE timing remain dynamic. These changes have automated coverage; real-game click latency has not been profiled.

## Sound licenses and credits

All eight audio files shipped with Fishing Mod are distributed under [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/). CC0 permits copying, modification and redistribution, including commercial use, without requesting permission. Attribution is not required by CC0, but the credits and exact processing history are retained here for traceability and do not imply endorsement by the original creators.

| Packaged sound | Original source and author | Declared license | Fishing Mod processing |
| --- | --- | --- | --- |
| `Sounds/cast-whoosh.wav` | [Casting Fishing Rod for Game Fishing SFX](https://freesound.org/people/el_boss/sounds/853287/) by **el_boss**, assembled by its author from CC0 sounds | CC0 1.0 | HQ preview converted from MP3, folded to mono, filtered, faded and encoded as 44.1 kHz PCM 16-bit WAV. |
| `Sounds/bobber-splash.wav` | OpenMMO's `fishing-plop.ogg`, derived from `bubble_02` in [40 CC0 water / splash / slime SFX](https://opengameart.org/content/40-cc0-water-splash-slime-sfx) by **rubberduck**; [intermediate provenance](https://github.com/Julian-adv/OpenMMO/blob/master/doc/assets/sfx.md) | CC0 1.0 | Level adjusted, tail faded, resampled and encoded as mono PCM WAV. |
| `Sounds/reel-out.wav` and `Sounds/reel-in.wav` | OpenMMO's `fishing-reel.ogg`, built from `click_004` in [Kenney Interface Sounds](https://kenney.nl/assets/interface-sounds); [intermediate provenance](https://github.com/Julian-adv/OpenMMO/blob/master/doc/assets/sfx.md) | CC0 1.0 | Ratchet repeated, filtered and pitch/time adjusted into separate outgoing and incoming variants. |
| `Sounds/qte-success.wav` and `Sounds/qte-failure.wav` | `ui-confirm.wav` and `ui-error.wav` from [Arcade Interface SFX](https://colorosse.com/assets/audio/sfx/arcade-ui-sfx) by **Colorosse** | CC0 1.0 | Level reduced for frequent feedback; retained as 44.1 kHz mono PCM 16-bit WAV. |
| `Sounds/fish-landed.wav` | OpenMMO's `fishing-splash.ogg` from `splash_03` by **rubberduck**, mixed with `fishing-catch.ogg` from `jingles_PIZZI06` in [Kenney Music Jingles](https://kenney.nl/assets/music-jingles); [intermediate provenance](https://github.com/Julian-adv/OpenMMO/blob/master/doc/assets/sfx.md) | CC0 1.0 | Splash and short success accent mixed, limited, faded and encoded as mono PCM WAV. |
| `Sounds/line-snap.wav` | OpenMMO's `fishing-snap.ogg`, derived from `pluck_001` in [Kenney Interface Sounds](https://kenney.nl/assets/interface-sounds); [intermediate provenance](https://github.com/Julian-adv/OpenMMO/blob/master/doc/assets/sfx.md) | CC0 1.0 | Level adjusted and encoded as 44.1 kHz mono PCM 16-bit WAV. |

The distributable package also includes [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) so these references remain beside the compiled mod and sounds. Creative Commons notes that CC0 provides no warranty and does not affect third-party trademark, patent, privacy or publicity rights.

## Current scope

Version 1.0.1 includes casting, six fish, the optional QTE, automatic sales and saved native happiness modifiers. Happiness definitions register before save deserialization. Fish are sold directly rather than added to inventory. Fishing skill progression and a dedicated catch history are not included. Right-click camera control does not cancel fishing, and the final QTE key remains blocked until release.

## Installation

1. Close Big Ambitions.
2. Obtain a ready-to-use Fishing Mod package from Steam Workshop or GitHub Releases. Use only one installation method.
3. For a manual installation, extract the packaged `FishingMod` folder directly into `%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal`.
4. Confirm that the resulting path is `ModsLocal\FishingMod\FishingMod.dll` (not `ModsLocal\FishingMod\FishingMod\FishingMod.dll`).
5. Start Big Ambitions and enable Fishing Mod in the mods list if needed.

The release package contains the ready-to-use compiled mod, including its `Sounds` folder. Cloning the source repository into `ModsLocal` is not a substitute for installing a compiled package. A source build writes the equivalent package to `Output/FishingMod`.

## Build

From the Unity project root:

```powershell
powershell -NoProfile -File .\Assets\Mods\FishingMod\tools\test.ps1
powershell -NoProfile -File .\Assets\Mods\FishingMod\tools\build-official.ps1
powershell -NoProfile -File .\Assets\Mods\FishingMod\tools\verify-package.ps1
```

The official build writes `Output/FishingMod` and does not install or launch the game. `build.ps1` remains available as a faster player-profile compile while iterating.

## Mod options

- Enable QTE (default: on): when off, a biting fish is automatically reeled in and receives the normal catch rewards. Bite chance and waiting time are unchanged.
- Difficulty: 0.2 to 5.0 in 0.1 steps; default 1.0 preserves current behavior. Response time is divided by difficulty. Settings are saved and sampled when a fish bites; an ongoing fight is unchanged.

