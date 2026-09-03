# Changelog

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
