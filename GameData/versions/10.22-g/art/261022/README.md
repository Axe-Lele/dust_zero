# 10.22-f character art

Built-in `image_gen.imagegen` was used with `Resource/char1.png` as the reference.
The character descriptions and prompt specifications are in `manifest.json`.

- 16 transparent SD sprites: `<id>_sd.webp`.
- 7 new full illustrations: vac, fridge, fan, blender, purifier, router, tv.
- The other 9 original illustrations remain paired with SDs based on the supplied reference sheet.
- New illustrations also include a face crop and the full illustration/SD design sheet. These sheets are not three-view turnarounds or three independent expressions.
- `source/` preserves all 16 original generated PNGs. Game assets are local WebP files; no external requests are required.
- `sd-preview.png` and `illustration-preview.png` show the complete asset set.

Reimport with `node tools/art/import_261022.cjs` from the game folder (requires sharp).
The importer finds the actual transparent gutter in paired sheets before splitting them, preserving complete hair and props.
Regenerate previews with `node tools/art/preview_261022.cjs`.
Run browser/art checks with `node tools/art/verify_261022.cjs` (requires sharp, Playwright and installed Chrome).

Runtime: `SD_READY` loads the 16 images before initial canvases render. `sdDraw` uses the transparent sprite, preserves transforms, adds breathing/attack motion, skill rings and overheat steam, and falls back to the previous procedural drawing if a local asset fails. The shared entry point covers lobby, icons, battle, survivor, league and tower.
