# Changelog

## 0.2.0

- Redesign the QTE as a polished circular control wheel centered on screen, with no rectangular panel: four direction arrows, a central Space control and a green outer ring that fills as the line is reeled in.
- Start the cast within 25 cm of the selected shoreline even if native navigation omits its arrival callback.
- Add six weighted fish with strictly decreasing odds as quality increases.
- Add an always-recoverable keyboard QTE: each success reels 3.5 m and each error releases 1.75 m.
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
