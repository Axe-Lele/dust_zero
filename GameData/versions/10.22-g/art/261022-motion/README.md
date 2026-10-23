# 10.22-g appliance animation

16 appliance characters each have a 3-frame basic action and a 6-frame skill: 144 distinct painted poses. Computer, fan and generator use a basic support gesture, without adding damage attacks.

The built-in image_gen.imagegen tool edited each existing `art/261022/<id>_sd.webp` into a 3x3 transparent pose sheet. Original PNGs and exact prompts are preserved in `source/`; the combined specifications are in `manifest.json`.

Runtime files:
- `<id>_motion.webp`: normalized 960x960 atlas, nine 320x320 cells.
- `metrics.js`: generated character size calibration, keeping idle/action scale consistent.
- `motion.js`: cosmetic clocks, frame selection and deterministic appliance effects. No random calls or damage/cooldown changes.
- Frames 0-2: windup, action, follow-through. Frames 3-8: preparation, charge, release, two sustained poses, recovery.
- Attack duration .36 seconds; skill opening lasts at least .84 seconds. Sustained skills loop the two sustained poses until recovery. Iron's zero-duration gameplay skill still plays its complete animation.

Main battle and coop use real attack cooldown resets and useSkill events. League/tower use actual attacks and casts. Survivor and lobby keep persistent animation state behind their render wrappers. Simulation time advances the clocks, so pause and speed controls apply. Overheat overrides active motion. The existing reduced effects preference reduces added particles/arcs. Missing motion images fall back to the existing SD sprite.

In game: appliance illustration → 설정화 → 전투 SD shows animated attack and skill previews. `../../motion-preview.html` provides a standalone 16-character gallery with pause and speed controls. `motion-preview.gif` is an animated contact sheet.

Rebuild: `node tools/art/import_motion_261022.cjs` (sharp). The importer finds row/column gutters, removes isolated edge scraps, preserves internal effects, and applies one scale across the whole sequence with aligned feet. `node tools/art/preview_motion_261022.cjs` regenerates the animated preview.

Validation: `node tools/art/verify_motion_261022.cjs`; stage regression capture/compare via `tools/art/motion_regression_261022.cjs`. Results and screenshots are in `notes/motion_10.22-g/`. 75 stages at reference/+3/-3 levels, both seeds and all reference decks match 10.22-f exactly. Also compared league tiers 1/2/3, tower floors 1/10/20/30, both coop partners and the three survivor leaders; all tested outcomes match.
