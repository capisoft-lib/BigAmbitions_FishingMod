# Fishing Mod

Fishing Mod adds a one-click fishing cast to Big Ambitions.

## Current interaction

1. Be outdoors, on foot, with empty hands and no menu open.
2. Left-click visible water.
3. The mod selects the closest shoreline point that is both on the current player NavMesh and reachable by a complete path.
4. The character walks there, faces the clicked water and performs a long two-handed cast.
5. The cast has an 80% chance of attracting a fish. A selected fish bites after a random 2–20 second wait; otherwise the empty line is automatically reeled in after 20 seconds.
6. A bite starts the keyboard QTE at 30% line progress. Its round control wheel is centered on screen with no rectangular panel: one of four direction arrows turns black, or the centre circle turns black for Space, while the green outer ring shows the current progress. Press the matching arrow/WASD/ZQSD key or Space; Escape releases the fish.

The sequence creates its own rod, reel, line, bobber and splash at runtime. It does not include or redistribute a Big Ambitions character model. A land, UI, vehicle, building or interactable-object click keeps its normal behavior.

The cast, reel release, bobber splash, empty-line/QTE reeling, light QTE success and failure cues, landed fish and broken line each have a dedicated sound. They are loaded from the installed mod's `Sounds` folder, routed to the game's effects mixer when it is available, and remain optional so a missing file cannot make fishing unplayable. All eight packaged effects are redistributable CC0 assets; exact authors, sources and processing are recorded in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Every completed cast refreshes one native **+10 happiness** modifier for **48 in-game hours**. It never stacks duplicate fishing bonuses. A successful catch adds one best-catch modifier for **72 in-game hours**; catching a worse fish while a better bonus is active does not replace or refresh the better one.

| Fish | Share among hooked fish | Happiness | Clean pulls | Key window |
| --- | ---: | ---: | ---: | ---: |
| Roach | 30% | +2 | 4 | 1.35 s |
| Perch | 24% | +3 | 5 | 1.25 s |
| Trout | 18% | +5 | 6 | 1.15 s |
| Carp | 13% | +7 | 8 | 1.05 s |
| Pike | 9% | +10 | 10 | 0.95 s |
| Sturgeon | 6% | +14 | 12 | 0.90 s |

Each QTE starts at 30% progress. A correct step reels in 3.5 m; a wrong displayed-direction/reel key or a timeout releases 1.75 m—exactly half a successful pull. Reaching 100% catches the fish, while falling back to 0% lets it escape. Rare fish remain achievable but demand a longer sequence.

Water renderers are indexed once per scene in a local spatial grid. Each tile keeps its real bounds, and normal clicks inspect only nearby cells through a reusable raycast buffer instead of rescanning the city.

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

## Scope of 0.2.0

This version implements navigation, casting, six fish, the QTE and saved native happiness modifiers. It does not add fish as inventory items, sale value, fishing skill progression or a catch history yet.

## Installation

1. Close Big Ambitions.
2. Download `FishingMod-0.2.0.zip` from the [GitHub release](https://github.com/capisoft-lib/BigAmbitions_FishingMod/releases/tag/v0.2.0).
3. Extract the archive directly into `%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal`.
4. Confirm that the resulting path is `ModsLocal\FishingMod\FishingMod.dll` (not `ModsLocal\FishingMod\FishingMod\FishingMod.dll`).
5. Start Big Ambitions and enable Fishing Mod in the local mods list if needed.

The release archive contains the ready-to-use compiled mod, including its `Sounds` folder. Cloning the source repository into `ModsLocal` is not a substitute for downloading the release archive.

## Build

From the Unity project root:

```powershell
powershell -NoProfile -File .\Assets\Mods\FishingMod\tools\test.ps1
powershell -NoProfile -File .\Assets\Mods\FishingMod\tools\build-official.ps1
powershell -NoProfile -File .\Assets\Mods\FishingMod\tools\verify-package.ps1
```

The official build writes `Output/FishingMod` and does not install or launch the game. `build.ps1` remains available as a faster player-profile compile while iterating.
