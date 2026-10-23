"""Convert the web build's .webp art into .png for Unity.

Unity has no native .webp importer, so the browser build's art cannot be dropped into
Assets as-is. This re-encodes it losslessly to PNG and mirrors the source folder names,
because index.html addresses icons dynamically as art/icon/<name>.webp - renaming them
would silently break that mapping when the tables are ported.

Run from the repo root:

    python tools/convert_art.py                     # ACTIVE-VERSION -> Unity
    python tools/convert_art.py --version 10.19-e   # pick a different version copy
    python tools/convert_art.py --dry-run

Source is a version copy under GameData/versions/, never the Downloads folder, so a
re-run reproduces the same bytes without depending on anything outside the repo.
"""
import argparse
import pathlib
import sys


def active_version(repo: pathlib.Path) -> str:
    """The version GameData is currently converted from.

    Hardcoding a literal here is how this tool broke before: the default said
    10.16-n long after the project had moved on, and the folder it named was
    eventually deleted. The one source of truth is ACTIVE-VERSION.txt.
    """
    return (repo / "GameData" / "ACTIVE-VERSION.txt").read_text(encoding="utf-8").strip()

try:
    from PIL import Image, features
except ImportError:
    sys.exit("Pillow is required:  pip install Pillow")

REPO = pathlib.Path(__file__).resolve().parent.parent
UNITY_ART = REPO / "DustZeroUnity" / "Assets" / "DustZero" / "Art"

# Where each group of source images lands. Icons keep their alpha and are addressed by
# bare name; the per-unit illustrations are opaque plates used only by the art viewer.
ROUTES = [
    ("icon/*.webp", UNITY_ART / "Icon"),
    ("*.webp", UNITY_ART / "Illust"),
]


def convert(src: pathlib.Path, dest: pathlib.Path, dry: bool) -> str:
    with Image.open(src) as im:
        # Keep alpha only where the source actually has it. Promoting an opaque plate to
        # RGBA would add a wasted channel to every illustration.
        mode = "RGBA" if im.mode in ("RGBA", "LA") or "transparency" in im.info else "RGB"
        out = im.convert(mode)
        size = out.size
        if dry:
            return f"{src.name} -> {dest.name} {size} {mode} (dry-run)"
        dest.parent.mkdir(parents=True, exist_ok=True)
        out.save(dest, format="PNG", optimize=True)
    return f"{src.name} -> {dest.relative_to(REPO)} {size} {mode}"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default=None,
                    help="version copy to read; defaults to GameData/ACTIVE-VERSION.txt")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    version = args.version or active_version(REPO)

    if not features.check("webp"):
        return int(bool(sys.stderr.write("Pillow was built without webp support.\n")))

    root = REPO / "GameData" / "versions" / version / "art"
    if not root.is_dir():
        return int(bool(sys.stderr.write(f"no art folder at {root}\n")))

    total = 0
    for pattern, dest_dir in ROUTES:
        for src in sorted(root.glob(pattern)):
            print("  " + convert(src, dest_dir / (src.stem + ".png"), args.dry_run))
            total += 1

    print(f"\n{total} images from {args.version}")
    if total == 0:
        return int(bool(sys.stderr.write("nothing converted - check the version name\n")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
