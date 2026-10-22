# -*- coding: utf-8 -*-
"""관리툴을 혼자 돌아가게 묶어 보내고, 고쳐 온 것을 다시 합친다.

여기는 **엮는 층**이다. 실제 일은 셋이 나눠서 한다:
    dz_patch.py      무엇이 바뀌었는지 계산하고 합친다 (전송·화면 모름)
    dz_transport.py  패치를 옮긴다 (지금은 파일, 나중에 네트워크)
    dz_review.py     바뀐 내용을 화면에 그린다

쓰는 법
  개발 쪽:  python tools/dz_share.py pack   <보낼폴더>
  기획 쪽:  (받은 폴더의 DustZeroStudio.exe 로 작업)
            python GameData/tools/dz_share.py export <폴더 또는 파일>
  개발 쪽:  python tools/dz_share.py review <패치>      무엇이 바뀌는지만 본다
            python tools/dz_share.py apply  <패치>      합친다 (충돌은 건너뛴다)
            python tools/dz_share.py apply  <패치> --force
            python tools/dz_share.py baseline           기준점 다시 찍기

엑셀은 병합이 안 된다. 그래서 파일을 통째로 주고받지 않고 바뀐 행만 패치로 옮기며,
한 번에 한 사람만 고치는 것을 전제로 한다.
"""
import argparse
import datetime
import hashlib
import io
import json
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import dz_studio                                        # noqa: E402
import dz_patch                                         # noqa: E402
import dz_transport                                     # noqa: E402

ROOT = dz_studio.ROOT
GAMEDATA = dz_studio.GAMEDATA
TABLES = dz_studio.TABLES
BASELINE = os.path.join(GAMEDATA, "share-baseline.json")

PACK_DIRS = ["GameData/tables", "GameData/tools", "GameData/data"]
PACK_FILES = ["GameData/ACTIVE-VERSION.txt"]


# ---------------------------------------------------------------- 스냅샷

def file_digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def art_index():
    out = {}
    for folder, tag in ((dz_studio.SPRITE_DIR, "Sprites"),
                        (dz_studio.ILLUST_DIR, "Illust")):
        if not os.path.isdir(folder):
            continue
        for name in sorted(os.listdir(folder)):
            if name.lower().endswith(".png"):
                out["%s/%s" % (tag, name)] = file_digest(os.path.join(folder, name))
    return out


def art_path(rel):
    tag, name = rel.split("/", 1)
    folder = dz_studio.SPRITE_DIR if tag == "Sprites" else dz_studio.ILLUST_DIR
    return os.path.join(folder, name)


def current_snapshot():
    return dz_patch.snapshot(dz_studio.load_tables(), art_index())


def write_baseline(path=BASELINE, note=""):
    snap = current_snapshot()
    data = {"stamped": datetime.datetime.now().isoformat(timespec="seconds"),
            "note": note, "tables": snap["tables"], "art": snap["art"]}
    with io.open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)
    return data


def read_baseline(path=BASELINE):
    if not os.path.exists(path):
        raise SystemExit(
            "기준점(share-baseline.json)이 없습니다.\n"
            "개발 쪽에서  python tools/dz_share.py baseline  을 먼저 실행하거나,\n"
            "이 묶음이  dz_share.py pack  으로 만들어진 것인지 확인하세요.")
    with io.open(path, encoding="utf-8") as f:
        return json.load(f)


# ---------------------------------------------------------------- pack

READ_ME = """더스트 제로 — 콘텐츠 편집 묶음

1. DustZeroStudio.exe 를 더블클릭하면 열립니다.
   이 폴더 안에서 실행해야 합니다. 밖으로 옮기면 엑셀을 찾지 못합니다.

2. 캐릭터 · 상품 · 이미지 세트 · 스테이지 탭에서 고칩니다.
   왼쪽 목록에서 고르면 불러와서 고칠 수 있고, '새로 만들기' 로 추가합니다.

3. 다 고쳤으면 창 아래 '보낼 패치 만들기' 를 누르세요.
   바뀐 것만 담긴 작은 파일(.dzpatch)이 만들어집니다. 그 파일만 보내면 됩니다.
   엑셀을 통째로 보내지 마세요 — 그 사이 개발 쪽에서 고친 것이 날아갑니다.

4. 한 번에 한 사람만 고쳐 주세요. 엑셀은 두 사람의 수정을 합칠 수 없습니다.
"""


def verify_bundle(dest):
    """묶은 폴더를 **그 폴더의 코드로** 열어 본다.

    묶는 중에 프로젝트가 바뀌면(워크북 이름이 바뀌는 등) 테이블과 변환기가 서로
    어긋난 묶음이 나온다. 겉보기에는 멀쩡해서 그대로 보내게 되고, 받은 사람이
    저장을 누르는 순간 깨진다. 보내기 전에 여기서 걸러야 한다.
    """
    import subprocess
    code = (
        "import sys, os;"
        "sys.path.insert(0, os.path.join(os.getcwd(), 'GameData', 'tools'));"
        "import xlsx_io;"
        "t = xlsx_io.read_workbooks(os.path.join('GameData', 'tables'));"
        "print('SHEETS', len(t))"
    )
    try:
        r = subprocess.run([sys.executable, "-c", code], cwd=dest,
                           capture_output=True, text=True, timeout=300)
    except Exception as exc:
        return ["묶음을 확인하지 못했습니다: %s" % exc]
    if r.returncode != 0:
        tail = (r.stderr or "").strip().splitlines()[-3:]
        return ["묶음의 엑셀을 읽지 못합니다:"] + ["     " + t for t in tail]

    # 변환기가 모르는 여분의 엑셀이 섞여 있으면, 나중에 그것이 시트를 가로챈다.
    import xlsx_io as mine
    known = {f for f, _ in mine.WORKBOOKS}
    folder = os.path.join(dest, "GameData", "tables")
    stray = sorted(n for n in os.listdir(folder)
                   if n.endswith(".xlsx") and n not in known)
    if stray:
        return ["변환기가 모르는 엑셀이 섞여 있습니다: " + ", ".join(stray)]
    return []


def cmd_pack(dest, author=""):
    if os.path.exists(dest) and os.listdir(dest):
        raise SystemExit("'%s' 가 비어 있지 않습니다. 빈 폴더를 지정하세요." % dest)
    os.makedirs(dest, exist_ok=True)

    for rel in PACK_DIRS:
        src = os.path.join(ROOT, rel)
        if os.path.isdir(src):
            shutil.copytree(src, os.path.join(dest, rel),
                            ignore=shutil.ignore_patterns("__pycache__", "*.bak", "*.pyc"))
    for rel in PACK_FILES:
        src = os.path.join(ROOT, rel)
        if os.path.exists(src):
            os.makedirs(os.path.dirname(os.path.join(dest, rel)), exist_ok=True)
            shutil.copy2(src, os.path.join(dest, rel))

    # 그림은 기획자가 보고 고를 수 있어야 하므로 대기 폴더로 같이 보낸다.
    staging = os.path.join(dest, "GameData", "art-staging")
    for folder, tag in ((dz_studio.SPRITE_DIR, "Sprites"),
                        (dz_studio.ILLUST_DIR, "Illust")):
        if os.path.isdir(folder):
            shutil.copytree(folder, os.path.join(staging, tag),
                            ignore=shutil.ignore_patterns("*.meta"))

    out_tools = os.path.join(dest, "GameData", "tools")
    os.makedirs(out_tools, exist_ok=True)
    for name in ("dz_studio.py", "dz_share.py", "dz_patch.py",
                 "dz_transport.py", "dz_review.py"):
        shutil.copy2(os.path.join(HERE, name), os.path.join(out_tools, name))

    exe = os.path.join(HERE, "DustZeroStudio.exe")
    if os.path.exists(exe):
        shutil.copy2(exe, os.path.join(dest, "DustZeroStudio.exe"))
    else:
        print("  주의: DustZeroStudio.exe 가 없습니다. "
              "python tools/build_studio.py 로 먼저 만드세요.")

    data = write_baseline(os.path.join(dest, "GameData", "share-baseline.json"),
                          note="pack for " + (author or "(미지정)"))
    with io.open(os.path.join(dest, "읽어주세요.txt"), "w", encoding="utf-8") as f:
        f.write(READ_ME)

    problems = verify_bundle(dest)
    if problems:
        print("묶음이 스스로 열리지 않습니다 — 보내지 마세요:")
        for p in problems:
            print("   " + p)
        print()
        print("프로젝트가 바뀌는 중에 묶었을 수 있습니다. 잠시 뒤 빈 폴더에 다시 묶으세요.")
        return 1

    rows = sum(len(v) for v in data["tables"].values())
    print("묶음 완성: %s" % dest)
    print("  시트 %d개 / 행·키 %d개 / 그림 %d장"
          % (len(data["tables"]), rows, len(data["art"])))
    print("  이 폴더를 통째로 보내세요.")
    return 0


# ---------------------------------------------------------------- export

def cmd_export(out, author=""):
    base = read_baseline()
    patch = dz_patch.make_patch(base, current_snapshot(),
                                author=author or os.environ.get("USERNAME", ""),
                                parent=base.get("stamped"))
    if dz_patch.is_empty(patch):
        print("바뀐 것이 없습니다. 패치를 만들지 않았습니다.")
        return 1

    blobs = {}
    for rel in patch["art"]:
        p = art_path(rel)
        if os.path.exists(p):
            with open(p, "rb") as f:
                blobs[rel] = f.read()

    # 폴더를 주면 그 안에 쌓고, 파일 이름을 주면 그 파일로 쓴다. 공유 드라이브를
    # 가리키면 그대로 '자동 동기화' 처럼 쓸 수 있다.
    if out.endswith(dz_transport.SUFFIX):
        dz_transport.write_bundle(out, patch, blobs)
        where = out
    else:
        t = dz_transport.FileTransport(out)
        t.push(patch, blobs)
        where = os.path.join(out, patch["id"] + dz_transport.SUFFIX)

    rows = sum(len(v.get("upsert", v.get("rows", v.get("set", []))))
               for v in patch["tables"].values())
    print("패치 완성: %s" % where)
    print("  id %s / 시트 %d개 / 바뀐 행·키 %d개 / 그림 %d장"
          % (patch["id"], len(patch["tables"]), rows, len(blobs)))
    return 0


# ---------------------------------------------------------------- review / apply

def open_transport(target):
    if os.path.isdir(target):
        return dz_transport.FileTransport(target)
    return dz_transport.SingleFileTransport(target)


def each_patch(target):
    t = open_transport(target)
    heads = t.pending() if isinstance(t, dz_transport.FileTransport) \
        and os.path.isdir(target) else t.list()
    for h in heads:
        patch, blobs = t.fetch(h["id"])
        yield t, patch, blobs


def cmd_review(target):
    import dz_review
    tables = dz_studio.load_tables()
    seen = 0
    for _, patch, _ in each_patch(target):
        seen += 1
        plan = dz_patch.plan_apply(patch, tables)
        print("=== %s · %s · %s" % (patch["id"], patch.get("author") or "?",
                                    patch.get("made") or ""))
        print(dz_review.as_text(plan))
        print()
    if not seen:
        print("합칠 패치가 없습니다.")
    return 0


def cmd_apply(target, force=False, gui=False):
    import dz_review
    tables = dz_studio.load_tables()
    total, skipped = 0, 0

    for transport, patch, blobs in each_patch(target):
        plan = dz_patch.plan_apply(patch, tables)
        if not plan.changes and not plan.art_changed:
            transport.ack(patch["id"])
            continue

        if gui:
            import tkinter as tk
            root = tk.Tk()
            root.withdraw()
            ok = dz_review.show(root, plan, "받은 패치 — %s" % (patch.get("author") or ""))
            root.destroy()
            if not ok:
                print("%s: 취소했습니다." % patch["id"])
                continue
        else:
            print("=== %s · %s" % (patch["id"], patch.get("author") or "?"))
            print(dz_review.as_text(plan))
            for c in plan.changes:
                if c.status == "conflict":
                    c.selected = bool(force)

        conflicts = [c for c in plan.changes if c.status == "conflict" and not c.selected]
        skipped += len(conflicts)

        touched = dz_patch.apply_plan(plan, tables)
        for rel, data in blobs.items():
            p = art_path(rel)
            os.makedirs(os.path.dirname(p), exist_ok=True)
            with open(p, "wb") as f:
                f.write(data)

        if touched:
            books = {dz_studio.workbook_of(s) for s in touched}
            dz_studio.xlsx_io.write_workbooks(tables, TABLES, only=books)
            print("  적용 — 수정한 엑셀: %s" % ", ".join(sorted(books)))
        if blobs:
            print("  그림 %d장 복사" % len(blobs))
        total += sum(1 for c in plan.changes if c.selected)
        transport.ack(patch["id"])

    if not total and not skipped:
        print("합칠 것이 없습니다.")
        return 0
    print("\n항목 %d개 적용%s" % (total, (", 충돌 %d개 건너뜀" % skipped) if skipped else ""))
    if skipped:
        print("받은 쪽 값으로 덮어쓰려면 --force 를 붙이세요.")
    print("다음: python GameData/tools/sync_to_unity.py 로 Unity 에 반영하세요.")
    print("기준점을 지금 상태로 다시 찍으려면: python tools/dz_share.py baseline")
    return 0


def cmd_baseline():
    data = write_baseline()
    print("기준점을 지금 상태로 찍었습니다: %s" % BASELINE)
    print("  시트 %d개 / 그림 %d장" % (len(data["tables"]), len(data["art"])))
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description="관리툴 묶음 만들기 / 패치 주고받기")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("pack", help="기획자에게 보낼 폴더를 만든다")
    p.add_argument("dest")
    p.add_argument("--author", default="")

    e = sub.add_parser("export", help="기준점 대비 바뀐 것만 패치로 뽑는다")
    e.add_argument("out", help="폴더(공유 드라이브) 또는 .dzpatch 파일 이름")
    e.add_argument("--author", default="")

    r = sub.add_parser("review", help="합치지 않고 무엇이 바뀌는지만 본다")
    r.add_argument("patch", help=".dzpatch 파일 또는 패치가 쌓인 폴더")

    a = sub.add_parser("apply", help="받은 패치를 합친다")
    a.add_argument("patch")
    a.add_argument("--force", action="store_true", help="충돌도 받은 값으로 덮어쓴다")
    a.add_argument("--gui", action="store_true", help="확인 창을 띄운다")

    sub.add_parser("baseline", help="지금 상태를 기준점으로 다시 찍는다")

    args = ap.parse_args(argv)
    if args.cmd == "pack":
        return cmd_pack(args.dest, args.author)
    if args.cmd == "export":
        return cmd_export(args.out, args.author)
    if args.cmd == "review":
        return cmd_review(args.patch)
    if args.cmd == "apply":
        return cmd_apply(args.patch, args.force, args.gui)
    return cmd_baseline()


if __name__ == "__main__":
    raise SystemExit(main())
