# -*- coding: utf-8 -*-
"""10.22-f SD 스프라이트와 10.22-g 모션 아틀라스를 Unity 로 가져온다.

왜 따로 있나
  `convert_art.py` 는 낱장을 그대로 png 로 바꾼다. 모션은 **960x960 한 장에 320 칸
  아홉**이 들어 있는 아틀라스라 잘라야 하고, 자른 뒤에도 **아홉 칸의 크기를 한
  배율로 맞춰야** 한다(원본 import_motion_261022.cjs 가 하는 일). 칸마다 따로
  다듬으면 재생할 때 캐릭터가 커졌다 작아졌다 한다.

무엇을 만드나
  Art/Sprites/unit_<id>.png      16종 SD (원본 art/261022/<id>_sd.webp)
  Art/Motion/<id>_<0..8>.png     16종 x 9 칸 = 144장
  그리고 `--write-table` 을 주면 17_Character.xlsx 의 unit_art.motionScale 을 채운다
  (원본 metrics.js 의 DZ_MOTION_SCALE — 눈대중이 아니라 생성된 보정값이다).

실행:
    python tools/convert_motion.py              # 변환만
    python tools/convert_motion.py --dry-run
    python tools/convert_motion.py --write-table
"""
import argparse
import io
import json
import os
import pathlib
import re
import sys

from PIL import Image

REPO = pathlib.Path(__file__).resolve().parent.parent
SEP = chr(92)


def active_version() -> str:
    """변환 원본 버전. 소스에 박지 않는다 — tools/check_upstream_drift.py 가 본다."""
    return (REPO / "GameData" / "ACTIVE-VERSION.txt").read_text(encoding="utf-8").strip()


def motion_scales(motion_dir: pathlib.Path) -> dict:
    """metrics.js 의 `window.DZ_MOTION_SCALE={...}` 를 그대로 읽는다."""
    text = (motion_dir / "metrics.js").read_text(encoding="utf-8")
    m = re.search(r"DZ_MOTION_SCALE\s*=\s*(\{.*?\})\s*;", text, re.S)
    if not m:
        raise SystemExit("metrics.js 에서 DZ_MOTION_SCALE 을 찾지 못했습니다.")
    return json.loads(m.group(1))


def save(img: Image.Image, dest: pathlib.Path, dry: bool) -> str:
    if dry:
        return f"{dest.name} {img.size} (dry-run)"
    dest.parent.mkdir(parents=True, exist_ok=True)
    img.save(dest, "PNG", optimize=True)
    return f"{dest.name} {img.size}"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default=None,
                    help="version copy to read; defaults to GameData/ACTIVE-VERSION.txt")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--write-table", action="store_true",
                    help="17_Character.xlsx 의 unit_art.motionScale 을 metrics.js 값으로 채운다")
    args = ap.parse_args()

    version = args.version or active_version()
    art = REPO / "GameData" / "versions" / version / "art"
    sd_dir, motion_dir = art / "261022", art / "261022-motion"
    if not sd_dir.is_dir() or not motion_dir.is_dir():
        sys.stderr.write(f"{version} 에 261022 / 261022-motion 폴더가 없습니다.\n")
        return 1

    manifest = json.loads((motion_dir / "manifest.json").read_text(encoding="utf-8"))
    cell, cols, rows = manifest["frameSize"], manifest["columns"], manifest["rows"]
    frames = cols * rows
    scales = motion_scales(motion_dir)

    sprites = REPO / "DustZeroUnity" / "Assets" / "DustZero" / "Art" / "Sprites"
    motion_out = REPO / "DustZeroUnity" / "Assets" / "DustZero" / "Art" / "Motion"

    ids = sorted(p.stem[:-3] for p in sd_dir.glob("*_sd.webp"))
    if not ids:
        sys.stderr.write("SD 스프라이트가 없습니다.\n")
        return 1

    made = 0
    for uid in ids:
        sd = Image.open(sd_dir / f"{uid}_sd.webp").convert("RGBA")
        print("  " + save(sd, sprites / f"unit_{uid}.png", args.dry_run))
        made += 1

        atlas_path = motion_dir / f"{uid}_motion.webp"
        if not atlas_path.exists():
            print(f"  !! {uid}: 모션 아틀라스가 없습니다 — SD 만 가져옵니다.")
            continue
        atlas = Image.open(atlas_path).convert("RGBA")
        want = (cols * cell, rows * cell)
        if atlas.size != want:
            sys.stderr.write(f"{uid}: 아틀라스가 {atlas.size} 입니다. {want} 를 기대했습니다.\n")
            return 2
        for i in range(frames):
            cx, cy = (i % cols) * cell, (i // cols) * cell
            frame = atlas.crop((cx, cy, cx + cell, cy + cell))
            print("  " + save(frame, motion_out / f"{uid}_{i}.png", args.dry_run))
            made += 1

    print(f"\n{made} images from {version}")
    missing = [u for u in ids if u not in scales]
    if missing:
        print("보정값이 없는 가전: " + ", ".join(missing) + " (1 로 둡니다)")

    if args.write_table:
        if args.dry_run:
            print("--dry-run 이라 표는 건드리지 않았습니다.")
            return 0
        sys.path.insert(0, str(REPO / "GameData" / "tools"))
        import xlsx_io  # noqa: E402
        tables_dir = str(REPO / "GameData" / "tables")
        tables = xlsx_io.read_workbooks(tables_dir)
        for row in tables["unit_art"]:
            row["motionScale"] = round(float(scales.get(row["id"], 1.0)), 6)
        xlsx_io.write_workbooks(tables, tables_dir, only=["17_Character.xlsx"])
        back = xlsx_io.read_workbooks(tables_dir)
        for row in back["unit_art"]:
            got, want_v = row.get("motionScale"), round(float(scales.get(row["id"], 1.0)), 6)
            assert abs(float(got) - want_v) < 1e-6, (row["id"], got, want_v)
        print(f"unit_art.motionScale {len(back['unit_art'])}행 기록·검증 완료")
    return 0


if __name__ == "__main__":
    sys.exit(main())
