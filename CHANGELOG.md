# Changelog

## 1.0.1

- Publish the 408-polygon static water atlas: wooden pontoons, the Yacht Club deck, the Industry City ParkingSea pier, Harbor park and waterfront decks stay excluded; channels between walkways remain fishable.
- Keep cast-in-place, the 10 m water distance check, optional QTE, automatic sales and happiness rewards.

## 1.0.0

- Exclude the native wooden pontoons and Yacht Club deck from fishing water polygons.
- Prepare fishing visuals when the character becomes available and reuse them between casts instead of recreating objects and materials on each click.
- Precompute fixed line/ripple curves and cache water proximity until the player's X/Z position changes. Keep live obstacle checks and fishing animation.

## 0.9.5

- Add saved Enable QTE option (on by default). When off, biting fish are automatically retrieved with normal rewards.
- Add Difficulty slider from 0.2 to 5.0, default 1.0. Higher values shorten the QTE response window; settings apply to the next bite.
- Adjust palm grip, wrist orientation, outward elbow bending and waiting posture.


## 0.9.4

- Right-click camera control no longer cancels fishing.
- Keep character controls blocked until the final QTE keys are released.
- Prevent Space used by the QTE from toggling game pause.


## 0.9.3

- Fix fishing startup and activity checks on Build 3680 by using FuneralHelper.PlayerDead instead of the removed PlayerHelper.playerDead field. Rebuild against current game DLLs.


## 0.9.2

- Renumber the current fishing release to 0.9.2 for the friends-only Steam Workshop release. Preserve all existing gameplay fixes and features.

## 1.1.2

- Require the player to be within 10 horizontal metres of mapped water before a click/F cast. Measure exact distance to polygon edges, including concave boundaries and excluded islands; ignore altitude.
- Show the distance and limit when too far away. Preserve target validation, cast-in-place and animation.

## 1.1.1

- F uses the same land, interaction and obstacle checks as clicking. Remove the blanket forced-ray bypass.
- Invisible support layers no longer allow land clicks to project through asphalt into water farther down the camera ray. Check atlas membership at each support-layer impact as well as the sea target.
- Preserve cast-in-place, exact target landing, animation changes and existing water polygons.

## 1.1.0

- Cast in place without shoreline navigation. F explicitly selects atlas water through colliders, leaving ordinary click protections enabled.
- Land the bobber at exact clicked X/Z, remove the 28 m cap, and time bite waiting from the actual distance-aware splash.
- Constrain two-hand grip reach, synchronize native IK targets before evaluation, calibrate wrist frames, close fingers and remove accumulated late torso rotations.

## 1.0.9

- Audit water-overlapping colliders in all six exterior atlas scenes. Add 21 verified native support-layer signatures across Hamptons and Industry/bridge approaches, preserving existing filters and all water polygons.
- Replay native support geometry with atlas-wide vertical and oblique water probes. Real piers, bridges, rocks, pools and building colliders remain blockers.

## 1.0.8

- Add exact fishing-only exclusions for eastern Hamptons RoadGroundPlane (2) and its rendererless Ground volume; preserve all marina filters and static polygons.
- Test all three newly reported targets through both layers, plus real obstacles and unrelated geometry.

## 1.0.7

- Filter the verified rendererless Hamptons marina Ground (2) volume after static water membership, alongside the mouse plane. Preserve real solid blockers.
- Regression checks cover both overlapping layers at all sixteen reported targets and reject unrelated ground geometry.

## 1.0.6 — Hamptons marina

- Ignore the verified invisible `TheHamptons/Roads/RoadGroundPlane (4)` mouse-ground mesh when testing occlusion above an accepted static water target. Its disabled renderer, Roads layer, flat shape, world position and dimensions must all match the native marina plane.
- Keep real piers, boats, visible geometry and other collision-only objects as blockers. No collider is disabled or modified, no water polygon is expanded, and native walking remains unchanged.
- Cover all nine rejected target coordinates from the real-game 1.0.5 trace with isolated physics regressions. Retain click refusal diagnostics for further reports.

## 1.0.5 — pontoon diagnosis

- Log the exact reason for rejected fishing clicks, at most once per second: player readiness, native interaction, polygon miss or physical occlusion. For occlusion, identify the nearest blocking collider, hierarchy, layer and bounds.
- Preserve 1.0.4 fishing rules and native interactions. This diagnostic release does not claim to fix the reported Hamptons pontoon refusal; the real-game rejection trace is still needed.

## 1.0.4 — static water detection

- Embed the 287 open-water polygons covering the city port, bridge sector, Industry City and the Hamptons. Resolve clicks by intersecting the sea plane at Y = -2.8 m and testing Unity X/Z coordinates.
- Stop scene water discovery and periodic indexing in gameplay. Water targets are available immediately after loading the mod, independently of water renderers and colliders.
- Preserve solid-obstacle occlusion, normal land clicks and complete shoreline navigation. Trigger volumes and the player's own colliders do not block casts; water-like object names cannot override the polygons.
- Log the selected static sub-polygon for diagnosis. Coastline data comes from the existing atlas; dynamic changes, uncharted lakes/pools and water under projected docks/bridges are outside its coverage.
- Preserve catch sales, line-break charges, input cancellation, audio, QTE and happiness registration.

## Unreleased

- 1.0.3: fix the global click freeze caused by synchronous water-cache reconstruction on non-water clicks. Runtime re-indexing now traverses scene hierarchies incrementally with an object/time budget and publishes complete snapshots; clicks never trigger a full scan. Native reproduction measured 923 ms inside the old scan during a 940 ms frame.

- Restrict water identity to real surfaces, recognize camel-case/lake/pond assets, read finite/infinite HDRP geometry, preserve irregular mesh outlines and recover streamed/additive-scene water without per-frame scans.
- Search reachable shoreline at the player's elevation, including elevated bridges, and retain a connected fallback when a closer disconnected island would previously make fishing fail.
- Cancel casting/waiting/empty retrieval on a click, movement or Escape without financial penalty; retain the normal movement destination and QTE direction controls.
- Count the retained random 2–20 second bite delay from bobber impact, include the remaining casting animation in that wait, pause with menus/game pause and log actual elapsed water time. Empty casts still retrieve after 20 seconds.
- Automatically sell each landed fish for $5 / $8 / $15 / $25 / $40 / $75 as rarity increases, independently of the best active happiness bonus.
- Charge up to $5 once when QTE progress reaches 0% and the line breaks; never overdraw the available balance. Empty casts, cancellation and intermediate mistakes remain free.
- Record native financial transactions and show English/French sale or line-replacement amounts in the result notification; preserve existing happiness and load-order fixes.

## 1.0.2

- Register Fishing Mod happiness definitions immediately after the native `HappinessHelper.OnHappinessModifiersLoaded` callback, before the selected save is deserialized and `PlayerController.Awake` runs.
- Replace the too-early 1.0.1 initialization attempt, which could run while the native happiness registry was still unavailable.
- Bundle Harmony 2.4.2 so the load-order fix remains self-contained and does not depend on any other installed mod.

## 1.0.1

- Register every Fishing Mod happiness definition during the persistent initialization scope, before `PlayerController.Awake` converts saved modifiers.
- Recover saves that already contain an active fishing or best-catch happiness modifier without interrupting player initialization or keyboard movement.
- Keep the city-load entry dedicated to fishing runtime behavior while retaining idempotent happiness registration as a safety check.

## 1.0.0

- Promote the complete fishing loop to its first stable release for Big Ambitions 1.0.
- Walk automatically to the closest reachable shoreline after a guarded click on outdoor water, then play a long procedural two-handed cast.
- Give each cast an 80% fish chance, with a 2–20 second bite delay or an automatic empty-line retrieve after 20 seconds.
- Start the centered circular keyboard QTE at 30% line progress; successful inputs reel in 3.5 m, while mistakes release 1.75 m and reaching 0% lets the fish escape.
- Add six increasingly rare fish with longer but achievable fights and temporary best-catch happiness bonuses.
- Refresh a non-stacking +10 fishing happiness modifier for 48 in-game hours, plus the best active caught-fish bonus for 72 hours.
- Ship dedicated CC0 sound effects for the full cast, bite, reel, QTE, catch and escape sequence, with complete source and licence notices.
- Add English and French game text, bilingual Steam release copy and a dedicated Workshop preview icon.

## 0.2.0

- Add eight CC0 fishing effects for casting, reel-out, bobber impact, reel-in, subtle QTE success/failure, landing a fish and a snapped line.
- Load WAV files directly from the installed mod, use a small overlapping 2D source pool, and route playback through the native effects mixer when available.
- Keep complete per-file source, license and processing notices in the shipped package.
- Give each cast an 80% chance of a fish, decide the result up front, wait 2–20 seconds for a bite, and automatically retrieve an empty line after 20 seconds.
- Start hooked-fish QTEs at 30% progress and let the fish escape if mistakes reduce progress to 0%.
- Redesign the QTE as a polished circular control wheel centered on screen, with no rectangular panel: four direction arrows, a central Space control and a green outer ring that fills as the line is reeled in.
- Start the cast within 25 cm of the selected shoreline even if native navigation omits its arrival callback.
- Add six weighted fish with strictly decreasing odds as quality increases.
- Add a keyboard QTE where each success reels 3.5 m and each error releases 1.75 m.
- Increase fight length and mildly shorten response windows with fish rarity while keeping every catch achievable.
- Refresh one +10 fishing happiness modifier for 48 hours after each completed cast.
- Keep exactly one 72-hour caught-fish happiness modifier; only the best active catch counts and worse catches cannot refresh it.
- Add English and French fish, QTE and happiness text.

## 0.1.0

- Detect guarded clicks on colliders, HDRP-style water surfaces and water renderers.
- Walk to the closest reachable NavMesh shoreline point.
- Add a procedural two-hand long cast with body motion, flexible rod, line, bobber arc and splash.
- Restore IK, animator ownership and navigation after completion, cancellation or unload.
- Fix the rod ribbon using absolute centimetre-scale taper values and the HDRP unlit color property.
- Index tiled water by local bounds and elevation, cache it per scene and use a reusable click-ray buffer without merging distant surfaces.
