"""Render the SD appliance and enemy sprites out of a web build.

The old GameData/spriteexport/export.html held a hand-copied snapshot of the build's canvas
drawing code, which is why it still only knew nine appliances after 10.16-n added three more.
This pulls the drawing code straight out of the build instead, so re-running it after an
engine update cannot silently miss a new subject.

Usage, from the repo root:

    python tools/export_sprites.py                  # 10.16-n -> Assets/DustZero/Art/Sprites
    python tools/export_sprites.py --version 10.16-n --list

Needs playwright (`pip install playwright && playwright install chromium`).
"""
import argparse
import asyncio
import base64
import io
import json
import pathlib
import re
import sys

from PIL import Image

REPO = pathlib.Path(__file__).resolve().parent.parent
SPRITES = REPO / "DustZeroUnity" / "Assets" / "DustZero" / "Art" / "Sprites"

# The drawing code runs from the first geometry helper through drawEnemy. Anchored on code
# rather than offsets so it survives edits above and below it.
START = "function rr("
END_AFTER = "function drawEnemy"


def extract(build: pathlib.Path) -> str:
    src = io.open(build / "index.html", encoding="utf-8").read()
    a = src.index(START)
    b = src.index(END_AFTER)
    # drawEnemy ends at the next top-level function declaration.
    c = src.index("\nfunction ", b + len(END_AFTER))
    return src[a:c]


def enemy_jobs(build: pathlib.Path):
    """One sprite per distinct look, plus a boss variant for each."""
    rows = json.loads(io.open(build / "data" / "enemy.json", encoding="utf-8").read())
    rows = rows if isinstance(rows, list) else rows.get("rows", rows)
    jobs, seen = [], set()
    for r in rows:
        look, boss = r.get("look"), int(r.get("boss") or 0)
        if not look:
            continue
        name = f"boss_{look}" if boss else look
        if name in seen:
            continue
        seen.add(name)
        jobs.append((f"enemy_{name}", look, float(r.get("radius") or 0.24),
                     int(r.get("fly") or 0), boss))
    return jobs


PAGE = """<!doctype html><meta charset="utf-8"><body style="margin:0">
<script>
%(draw)s

// Globals drawEnemy reads from the running game; stubbed for offline rendering.
// hpBar is the in-battle health bar - a sprite sheet entry wants the body alone, so it
// is deliberately a no-op rather than a drawn bar.
let TS = 1;
const epos = e => ({x: e.x, y: e.y});
const segDir = () => ({x: 0, y: 1});
const hpBar = () => {};

const OUT = {};
function shot(size, paint) {
  const c = document.createElement('canvas');
  c.width = c.height = size;
  const x = c.getContext('2d');
  x.translate(size / 2, size / 2);
  paint(x, size);
  return c.toDataURL('image/png');
}
window.renderUnit = (id, size) => shot(size, (x, s) =>
  DRAW[id](x, 0, 0, s * 0.78, {skillT: 0, id: 7, kick: 0, over: false}, 0));
// drawEnemy reads a live enemy's whole state. Leaving any of the offsets or timers out
// makes the coordinates NaN and the canvas silently stays empty - which is exactly how the
// first run produced seventeen blank PNGs that still looked like a success.
window.renderEnemy = (look, radius, fly, boss, size) => shot(size, (x, s) => {
  TS = s * 0.74 / Math.max(0.18, radius * (boss ? 1.6 : 1));
  const e = {
    x: 0, y: 0, off: 0, d: 0, pi: 0, seed: 1.2,
    dir: {x: 0, y: 1},
    hp: 1, maxHp: 1,
    slowT: 0, frozenT: 0, stunT: 0, hitT: 0, burnT: 0, poisonT: 0,
    marks: [], def: {look, r: radius, radius, fly, boss, breaker: 0},
  };
  drawEnemy(x, e, 0);
});
window.unitIds = () => Object.keys(DRAW);
window.READY = true;
</script></body>"""


async def main() -> int:
    ap = argparse.ArgumentParser()
    # 버전을 박아 두지 않는다. 기준은 GameData/ACTIVE-VERSION.txt 한 곳이다 —
    # 여기 10.16-n 이 박혀 있던 동안 그 사본은 이미 지워져 있었다.
    ap.add_argument("--version", default=None,
                    help="version copy to render; defaults to GameData/ACTIVE-VERSION.txt")
    ap.add_argument("--size", type=int, default=256)
    ap.add_argument("--list", action="store_true", help="only report what would be rendered")
    args = ap.parse_args()

    version = args.version or (REPO / "GameData" / "ACTIVE-VERSION.txt").read_text(
        encoding="utf-8").strip()
    build = REPO / "GameData" / "versions" / version
    if not (build / "index.html").is_file():
        return int(bool(sys.stderr.write(f"no build at {build}\n")))

    try:
        from playwright.async_api import async_playwright
    except ImportError:
        return int(bool(sys.stderr.write("playwright is required: pip install playwright\n")))

    page_html = PAGE % {"draw": extract(build)}

    async with async_playwright() as pw:
        browser = await pw.chromium.launch()
        page = await browser.new_page()
        errors = []
        page.on("pageerror", lambda e: errors.append(str(e)))
        await page.set_content(page_html)
        if errors:
            await browser.close()
            return int(bool(sys.stderr.write("drawing code failed:\n  " + "\n  ".join(errors) + "\n")))

        units = await page.evaluate("window.unitIds()")
        enemies = enemy_jobs(build)
        print(f"appliances: {len(units)}  enemy sprites: {len(enemies)}")
        if args.list:
            print("  ", ", ".join(units))
            print("  ", ", ".join(n for n, *_ in enemies))
            await browser.close()
            return 0

        SPRITES.mkdir(parents=True, exist_ok=True)
        written, blank = 0, []

        def save(name: str, data_url: str) -> None:
            nonlocal written
            raw = base64.b64decode(data_url.split(",", 1)[1])
            # A transparent PNG means the draw call silently did nothing. Writing it would
            # leave an invisible sprite in the game that no test would notice.
            img = Image.open(io.BytesIO(raw))
            if img.convert("RGBA").getextrema()[3][1] == 0:
                blank.append(name)
                return
            (SPRITES / f"{name}.png").write_bytes(raw)
            written += 1
        for uid in units:
            save(f"unit_{uid}",
                 await page.evaluate("([i,s]) => window.renderUnit(i,s)", [uid, args.size]))
        for name, look, radius, fly, boss in enemies:
            save(name, await page.evaluate(
                "([l,r,f,b,s]) => window.renderEnemy(l,r,f,b,s)", [look, radius, fly, boss, args.size]))
        await browser.close()

    if blank:
        sys.stderr.write(
            "{} sprite(s) drew nothing and were NOT written:\n  {}\n".format(
                len(blank), "\n  ".join(blank)))
        return 1

    print(f"wrote {written} sprites to {SPRITES.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))
