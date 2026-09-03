# Fishing Mod

Fishing Mod adds a one-click fishing cast to Big Ambitions.

## Current interaction

1. Be outdoors, on foot, with empty hands and no menu open.
2. Left-click visible water.
3. The mod selects the closest shoreline point that is both on the current player NavMesh and reachable by a complete path.
4. The character walks there, faces the clicked water and performs a long two-handed cast.
5. A weighted random fish bites and a keyboard QTE starts. Its round control wheel is centered on screen with no rectangular panel: one of four direction arrows turns black, or the centre circle turns black for Space, while the green outer ring fills as the line is reeled in. Press the matching arrow/WASD/ZQSD key or Space; Escape releases the fish.

The sequence creates its own rod, reel, line, bobber and splash at runtime. It does not include or redistribute a Big Ambitions character model. A land, UI, vehicle, building or interactable-object click keeps its normal behavior.

Every completed cast refreshes one native **+10 happiness** modifier for **48 in-game hours**. It never stacks duplicate fishing bonuses. A successful catch adds one best-catch modifier for **72 in-game hours**; catching a worse fish while a better bonus is active does not replace or refresh the better one.

| Fish | Chance | Happiness | Clean pulls | Key window |
| --- | ---: | ---: | ---: | ---: |
| Roach | 30% | +2 | 4 | 1.35 s |
| Perch | 24% | +3 | 5 | 1.25 s |
| Trout | 18% | +5 | 6 | 1.15 s |
| Carp | 13% | +7 | 8 | 1.05 s |
| Pike | 9% | +10 | 10 | 0.95 s |
| Sturgeon | 6% | +14 | 12 | 0.90 s |

Each correct QTE step reels in 3.5 m. A wrong displayed-direction/reel key or a timeout releases 1.75 m—exactly half a successful pull—and the line can never become longer than its initial length. There is no failure limit, so every fish remains catchable; rare fish are mainly harder because the fight lasts longer.

Water renderers are indexed once per scene in a local spatial grid. Each tile keeps its real bounds, and normal clicks inspect only nearby cells through a reusable raycast buffer instead of rescanning the city.

## Scope of 0.2.0

This version implements navigation, casting, six fish, the QTE and saved native happiness modifiers. It does not add fish as inventory items, sale value, fishing skill progression or a catch history yet.

## Installation

1. Close Big Ambitions.
2. Download `FishingMod-0.2.0.zip` from the [GitHub release](https://github.com/capisoft-lib/BigAmbitions_FishingMod/releases/tag/v0.2.0).
3. Extract the archive directly into `%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal`.
4. Confirm that the resulting path is `ModsLocal\FishingMod\FishingMod.dll` (not `ModsLocal\FishingMod\FishingMod\FishingMod.dll`).
5. Start Big Ambitions and enable Fishing Mod in the local mods list if needed.

The release archive contains the ready-to-use compiled mod. Cloning the source repository into `ModsLocal` is not a substitute for downloading the release archive.

## Build

From the Unity project root:

```powershell
powershell -NoProfile -File .\Assets\Mods\FishingMod\tools\test.ps1
powershell -NoProfile -File .\Assets\Mods\FishingMod\tools\build-official.ps1
powershell -NoProfile -File .\Assets\Mods\FishingMod\tools\verify-package.ps1
```

The official build writes `Output/FishingMod` and does not install or launch the game. `build.ps1` remains available as a faster player-profile compile while iterating.
