# -*- coding: utf-8 -*-
"""상류 빌드에서 파생된 우리 파일이 뒤처졌는지 본다.

왜 있나
  `GameData/tools/gen_tables.py` 가 10.14-d 시절 사본(673줄 · CHAPTERS=3)으로
  남아 있는 것을 2026-10-23 에 발견했다. 그 사이 상류는 10.22-g(1719줄 · 15챕터)
  까지 갔다. 아무도 몰랐던 이유는 **컴파일도 테스트도 그 파일을 건드리지 않기**
  때문이다. 한 번 실행했으면 워크북 16권이 3챕터 내용으로 덮여 썼을 것이다.
  같은 종류로 `tools/convert_art.py` 의 기본값이 `10.16-n` 을 가리키고 있었고,
  그 버전 사본은 같은 날 지워졌다 — 도구가 조용히 못 쓰는 물건이 되어 있었다.

무엇을 보나
  1. 우리 트리의 파일 중 **상류 최신 사본에 같은 이름이 있는 것**이 그 사본과
     다른지. 다르면 '일부러 고친 것'인지 밝혀야 한다(아래 LOCAL_EDITS).
  2. 소스 안에 **버전 문자열을 박아 둔 곳**이 있는지. 기준은 ACTIVE-VERSION.txt
     한 곳이어야 한다.
  3. 사라진 버전 사본을 가리키는 경로가 있는지.

실행:  python tools/check_upstream_drift.py
돌아오는 값이 0 이 아니면 고칠 것이 있다는 뜻이다.
"""
import hashlib
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERSIONS = os.path.join(ROOT, "GameData", "versions")
SEP = chr(92)

# 상류와 같은 이름이지만 **일부러** 다르게 두는 파일. 왜 다른지 적는다.
# 여기에 올리는 것은 "확인했고 그래도 된다"는 뜻이다. 그냥 조용히 넘기지 않는다.
LOCAL_EDITS = {
    "GameData/tools/xlsx_io.py":
        "상류 것 + 우리 두 줄(17_Character 워크북, write_workbooks 의 only 걸러내기)",
}

# 생성물이라 내용이 달라도 되는 곳(우리 build.py 가 쓴 결과).
GENERATED_DIRS = ("GameData/data",)

# 우리 트리에서 살펴볼 곳
OURS_ROOTS = ("GameData/tools", "GameData/tables", "tools")
SKIP_DIRS = {"versions", "__pycache__", ".git", "node_modules", "art-staging"}

VER_RE = re.compile(r"10\.\d+-[a-z]")


def norm_hash(path):
    with open(path, "rb") as fh:
        return hashlib.sha256(fh.read().replace(b"\r\n", b"\n")).hexdigest()


def active_version():
    p = os.path.join(ROOT, "GameData", "ACTIVE-VERSION.txt")
    return io.open(p, encoding="utf-8").read().strip()


def version_dirs():
    return sorted(d for d in os.listdir(VERSIONS)
                  if os.path.isdir(os.path.join(VERSIONS, d)))


def upstream_index(version):
    base = os.path.join(VERSIONS, version)
    out = {}
    for dp, dn, fn in os.walk(base):
        dn[:] = [d for d in dn if d not in SKIP_DIRS]
        for f in fn:
            out.setdefault(f, os.path.join(dp, f))
    return out


def our_files():
    for r in OURS_ROOTS:
        base = os.path.join(ROOT, r)
        if not os.path.isdir(base):
            continue
        for dp, dn, fn in os.walk(base):
            dn[:] = [d for d in dn if d not in SKIP_DIRS]
            if os.path.abspath(dp).startswith(os.path.abspath(VERSIONS)):
                continue
            for f in fn:
                p = os.path.join(dp, f)
                yield os.path.relpath(p, ROOT).replace(SEP, "/"), p


def main():
    bad = []
    active = active_version()
    versions = version_dirs()
    if active not in versions:
        print("!! ACTIVE-VERSION 이 %s 인데 그 사본이 없다" % active)
        return 1
    print("활성 버전 %s · 사본 %s" % (active, ", ".join(versions)))
    up = upstream_index(active)

    # 1. 뒤처진 사본
    print()
    print("== 1. 상류와 같은 이름인데 내용이 다른 파일 ==")
    found = 0
    for rel, path in our_files():
        name = os.path.basename(rel)
        if name not in up or rel.startswith(GENERATED_DIRS):
            continue
        if norm_hash(path) == norm_hash(up[name]):
            continue
        found += 1
        if rel in LOCAL_EDITS:
            print("   %s  (허용: %s)" % (rel, LOCAL_EDITS[rel]))
        else:
            print("!! %s  — %s 의 것과 다르다. 올리거나 LOCAL_EDITS 에 까닭을 적어라."
                  % (rel, active))
            bad.append(rel)
    if not found:
        print("   없음")

    # 2. 박아 둔 버전 문자열
    print()
    print("== 2. 소스에 박아 둔 버전 문자열 ==")
    hits = 0
    for rel, path in our_files():
        if not rel.endswith((".py", ".cs", ".sh", ".mjs", ".js")):
            continue
        try:
            text = io.open(path, encoding="utf-8").read()
        except UnicodeDecodeError:
            continue
        for i, line in enumerate(text.splitlines(), 1):
            if not VER_RE.search(line):
                continue
            # 주석·docstring 안의 설명은 봐준다. 경로와 기본값만 잡는다.
            if ("versions" in line or "default=" in line) and not line.lstrip().startswith("#"):
                print("!! %s:%d  %s" % (rel, i, line.strip()[:100]))
                bad.append(rel)
                hits += 1
    if not hits:
        print("   없음 — 전부 ACTIVE-VERSION.txt 를 거친다")

    # 3. 사라진 사본을 가리키는 경로
    print()
    print("== 3. 없는 버전 사본을 가리키는 곳 ==")
    gone = 0
    for rel, path in our_files():
        if not rel.endswith((".py", ".cs", ".sh", ".mjs", ".js", ".md")):
            continue
        try:
            text = io.open(path, encoding="utf-8").read()
        except UnicodeDecodeError:
            continue
        for m in re.finditer(r"versions[/\\]+(10\.\d+-[a-z])", text):
            if m.group(1) not in versions:
                print("!! %s  ->  versions/%s (없다)" % (rel, m.group(1)))
                bad.append(rel)
                gone += 1
    if not gone:
        print("   없음")

    print()
    if bad:
        print("고칠 것 %d건" % len(set(bad)))
        return 1
    print("통과 — 상류에서 파생된 파일이 전부 %s 기준이다" % active)
    return 0


if __name__ == "__main__":
    sys.exit(main())
