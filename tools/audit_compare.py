# -*- coding: utf-8 -*-
"""원본과 이식본의 상자를 같은 단위로 늘어놓고 차이를 센다.

왜 "1px 일치" 가 목표가 아닌가
  원본은 CSS 430x932, 이식본은 uGUI 1080x1920 이다. 2026-10-19 에 두 축의 배율을
  하나(2.0601)로 합치고 내용을 가운데 886 기둥에 넣었으므로, 되돌릴 때도 그 자를 쓴다.
  글꼴 래스터라이저는 여전히 다르고, TMP 의 줄 상자 규칙은 CSS line-height 와 아예 다른
  물건이다(TMP 는 줄을 자르지 않고 **안 그린다**). 픽셀 단위 동일은 달성할 수 없다.
  그걸 목표로 걸면 영원히 "안 맞음" 만 뜨고 아무도 안 본다.

  대신 **CSS px 로 되돌려** 비교하고, 사람이 알아볼 수 있는 차이만 센다.
  이번 이식에서 실제로 틀렸던 것들은 전부 수십 px 짜리였다.

어떻게 짝을 찾나
  이름 규칙이 서로 다르므로(`#wCoin` vs `TopBar/Coin/Value`) 이름으로는 못 맞춘다.
  대신 **글자로** 맞춘다 — 같은 화면에서 같은 글자를 가진 상자는 같은 것이다.
  글자가 없는 상자(플레이트·아이콘)는 `PAIRS` 에 사람이 적은 것만 본다.

실행: python tools/audit_compare.py
"""
import io
import json
import io
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ORIG = os.path.join(ROOT, "production", "qa", "audit", "original")
UNITY = os.path.join(ROOT, "production", "qa", "audit", "unity")
OUT = os.path.join(ROOT, "production", "qa", "audit", "REPORT.md")

TOL = 3.0        # CSS px. 이보다 작으면 글꼴·반올림이라 보고하지 않는다.
CTOL = 4.0       # 색 채널 차이. 브라우저는 섞은 값, 우리는 칠한 값이라 1~2 는 늘 난다.

SCREENS = [
    ("lobby-home", "01-lobby", "홈"),
    ("lobby-stage", "09-lobby-stage", "스테이지"),
    ("lobby-content", "09b-lobby-content", "콘텐츠"),
    ("lobby-unit", "10-lobby-units", "가전"),
    ("lobby-deck", "10b-lobby-deck", "편성"),
    ("lobby-gacha", "11-lobby-gacha", "소환"),
    ("lobby-shop", "12-lobby-shop", "상점"),
    ("battle", "03-battle-deployed", "전투"),
    ("battle-brief", "08-battle-brief", "전투 브리핑"),
    ("surv", "23-survivor-arena", "대청소"),
    ("surv-pick", "22-survivor-pick", "대청소 리더 선택"),
    # 2026-10-24 에 10.22-g 로 새로 뜬 화면들.
    ("unit-detail", "13-unit-detail", "가전 상세"),
    ("gear-bag", "18-gear-bag", "장비 가방"),
    ("art-viewer", "15-art-profile", "일러스트 뷰어"),
    ("pass", "14-pass", "시즌 패스"),
]

UNITY_FOR = {o: u for o, u, _ in SCREENS}

# 글자가 없어 자동으로 못 맞추는 상자. (원본 path 조각, 이식 path 조각, 이름)
PAIRS = {
    "*": [
        ("header.topbar", "TopBar/Plate", "상단바 판"),
        ("nav.tabbar", "BottomBar/Plate", "하단 탭바 판"),
    ],
    "battle": [
        ("header.hud", "Hud", "전투 HUD 판"),
    ],
}

# 숫자가 달라 글자로는 못 맞추지만 같은 자리인 것 — 숫자를 지우고 맞춘다.
NUM = re.compile(r"[\d,]+")


RGB = re.compile(r"rgba?\(([^)]*)\)")


def css_rgb(v):
    """`rgb(a)` 문자열 -> (r,g,b,a). 투명하면 None — 비교할 색이 없다는 뜻이다."""
    m = RGB.match((v or "").strip())
    if not m:
        return None
    parts = [float(x) for x in m.group(1).replace("/", " ").split(",")]         if "," in m.group(1) else [float(x) for x in m.group(1).split()]
    if len(parts) < 3:
        return None
    a = parts[3] if len(parts) > 3 else 1.0
    return None if a < 0.02 else (parts[0], parts[1], parts[2], a)


def hex_rgb(v):
    """`#RRGGBBAA` -> (r,g,b,a)."""
    v = (v or "").strip().lstrip("#")
    if len(v) not in (6, 8):
        return None
    try:
        r, g, b = (int(v[i:i + 2], 16) for i in (0, 2, 4))
    except ValueError:
        return None
    a = int(v[6:8], 16) / 255.0 if len(v) == 8 else 1.0
    return None if a < 0.02 else (r, g, b, a)


def parse_ink(v):
    try:
        x, y, w, h = (float(t) for t in (v or "").split(","))
    except ValueError:
        return None
    return None if w <= 0 or h <= 0 else {"x": x, "y": y, "w": w, "h": h}


def norm(t):
    return NUM.sub("#", (t or "").strip()).replace(" ", "")


# 시트로 열리는 화면은 **시트 안만** 비교한다.
#
# 왜: 두 쪽의 모달 캡처는 뒤에 깔린 탭이 서로 다르다(원본은 가전 탭, 이식본 덤프는
# 그때 열려 있던 탭). 배경까지 같이 재면 "원본에만 있는 글자" 가 50개씩 나오고 그게
# 전부 허수다. 시트 바깥을 걷어내면 남는 숫자가 실제 작업 대상이다.
#
# 원본 쪽 기준 상자는 캡처 안에 그대로 들어 있다(`#mbox` 등). 이식본 쪽은 시트 뿌리의
# 경로 조각으로 고른다.
SHEETS = {
    "unit-detail": ("#mbox", "/UnitDetail"),
    "gear-bag": ("#mbox", "/GearBag"),
    "art-viewer": ("#artOv", "/ArtViewer"),
    "pass": ("#passOv", "/Pass"),
    "surv-pick": ("#mbox", "/LeaderSelect"),
}


def inside(box, clip, slack=2.0):
    """상자가 기준 상자 안에 (거의) 들어가는가."""
    return (box["x"] >= clip["x"] - slack
            and box["y"] >= clip["y"] - slack
            and box["x"] + box["w"] <= clip["x"] + clip["w"] + slack
            and box["y"] + box["h"] <= clip["y"] + clip["h"] + slack)


def load_original(name):
    p = os.path.join(ORIG, name + ".json")
    if not os.path.exists(p):
        return None
    nodes = json.load(io.open(p, encoding="utf-8")).get("nodes", [])
    for n in nodes:
        n["colour"] = css_rgb(n.get("style", {}).get("bg", ""))
        n["fg"] = css_rgb(n.get("style", {}).get("color", ""))
    sheet = SHEETS.get(name)
    if sheet:
        root = next((n for n in nodes if n.get("path") == sheet[0]), None)
        # 전체 화면을 덮는 뿌리(#artOv 등)는 자르는 의미가 없다 — 그때는 그대로 둔다.
        if root and root["box"]["h"] < 900:
            clip = root["box"]
            nodes = [n for n in nodes if inside(n["box"], clip)]
    return nodes


def load_unity(name):
    p = os.path.join(UNITY, name + ".tsv")
    if not os.path.exists(p):
        return None
    rows = []
    for i, line in enumerate(io.open(p, encoding="utf-8")):
        if i == 0:
            continue
        c = line.rstrip("\n").split("\t")
        if len(c) < 5:
            continue
        try:
            rows.append({"path": c[0],
                         "box": {"x": float(c[1]), "y": float(c[2]),
                                 "w": float(c[3]), "h": float(c[4])},
                         "text": c[7] if len(c) > 7 else "",
                         "ink": parse_ink(c[8]) if len(c) > 8 else None,
                         "colour": hex_rgb(c[5] if len(c) > 5 else ""),
                         "fg": None})
        except ValueError:
            pass
    sheet = next((v for k, v in SHEETS.items() if UNITY_FOR.get(k) == name), None)
    if sheet:
        kept = [r for r in rows if sheet[1] in r["path"]]
        if kept:
            rows = kept
    return rows


def by_text(nodes):
    out = {}
    for n in nodes:
        k = norm(n.get("text"))
        if len(k) < 2:
            continue
        out.setdefault(k, []).append(n)
    return out


def find(nodes, needle):
    if not needle:
        return None
    hits = [n for n in nodes if needle in n["path"]]
    return hits[0] if hits else None



def text_anchor(node):
    """글자가 상자의 어느 모서리에 맞춰졌는지. ink 가 상자 안에서 앉은 자리로 읽는다.

    왼쪽 정렬이면 ink 왼쪽이 상자 왼쪽에 붙고, 오른쪽 정렬이면 오른쪽이 붙고,
    가운데면 양쪽 여백이 같다. 여백이 거의 없으면(상자가 글자에 딱 맞으면)
    어느 쪽으로 읽어도 결과가 같으므로 왼쪽으로 본다.
    """
    ink, box = node.get("ink"), node.get("box")
    if not ink or not box or box["w"] <= 0:
        return "l"
    left = ink["x"] - box["x"]
    right = (box["x"] + box["w"]) - (ink["x"] + ink["w"])
    if left <= 1.5:
        return "l"
    if right <= 1.5:
        return "r"
    return "c" if abs(left - right) <= max(2.5, box["w"] * 0.04) else "l"


def edge(rect, anchor):
    """그 정렬이 기준으로 삼는 가로 좌표."""
    if anchor == "l":
        return rect["x"]
    if anchor == "r":
        return rect["x"] + rect["w"]
    return rect["x"] + rect["w"] / 2


def compare(a, b, text_pair):
    """글자는 **실제로 찍히는 자리(ink)** 로, 판은 상자로 본다.

    상자끼리 비교하면 멀쩡한 화면이 전부 어긋난 것으로 나온다. 원본의
    `<b><img 아이콘>출석부</b>` 는 상자가 아이콘에서 시작하고 우리 라벨은 글자에서
    시작하기 때문이다. 실제로 그렇게 비교해서 홈 화면 7건을 '진짜 차이' 로 잘못
    보고했다. ink 는 양쪽 다 글자만 재므로 공평하다.

    ink 가 없으면(옛 수집본) 상자의 시작 위치만 보고, 그 사실을 표시한다.

    **글자는 왼쪽 모서리가 아니라 중심으로 잰다.** ink 의 너비는 두 쪽이 서로 다른
    것을 재기 때문이다 — CSS 의 Range 는 advance 박스(좌우 사이드베어링 포함)를,
    TMP 의 textBounds 는 글자가 칠해진 자리만 돌려준다. 그래서 우리 ink 는 늘 좁고,
    가운데 정렬된 글자는 그 차이의 **절반만큼 오른쪽에서 시작한다.** 왼쪽 모서리로
    재면 멀쩡한 가운데 정렬이 전부 "오른쪽으로 밀렸다" 로 나온다.

    실제로 그렇게 나왔다. 10.19-e 기준 135건 중 36건이 dx = −dw/2 와 ±1px 안에서
    일치했다 — 자리가 아니라 자의 문제였다. 중심끼리 빼면 너비 차이가 상쇄된다.
    """
    src = "ink" if (text_pair and a.get("ink") and b.get("ink")) else "box"
    pa, pb = a.get(src) or a["box"], b.get(src) or b["box"]
    d = {k: round(pb[k] - pa[k], 1) for k in ("x", "y", "w", "h")}

    if text_pair and src == "ink":
        # **글자가 실제로 맞춰진 모서리**로 잰다.
        #
        # ink 너비는 두 쪽이 서로 다른 것을 재므로(CSS advance 박스 vs TMP ink 박스)
        # 어느 한 기준을 고정으로 쓰면 반대쪽 정렬이 전부 틀어진다. 실제로 두 번 겪었다:
        # 모서리로 재니 가운데 정렬이 −dw/2 만큼 밀려 보였고, 중심으로 바꾸니 이번엔
        # 왼쪽 정렬이 같은 크기만큼 반대로 밀려 보였다(스테이지 좌측 라벨 10여 건).
        #
        # 정렬은 따로 받아 적지 않아도 ink 가 상자 안에서 어디에 앉았는지로 알 수 있다.
        ax, ay = text_anchor(a), text_anchor(b)
        # 두 쪽이 다르게 읽히면(한쪽만 꽉 찬 상자 등) 중심으로 떨어뜨린다.
        d["x"] = round(edge(pb, ax if ax == ay else "c") - edge(pa, ax if ax == ay else "c"), 1)
        d["y"] = round((pb["y"] + pb["h"] / 2) - (pa["y"] + pa["h"] / 2), 1)

    keys = ("x", "y") if text_pair else ("x", "y", "w", "h")
    return d, max(abs(d[k]) for k in keys), src


def colour_gap(a, b):
    """두 색의 거리. 채널 최대 차이로 본다 — 사람이 보는 차이에 가장 가깝다.

    알파도 센다. 반투명 판을 불투명하게 칠해 놓으면 뒤가 안 비쳐서 눈에 띄는데
    RGB 만 보면 0 으로 나온다.
    """
    if a is None or b is None:
        return None
    return max(abs(a[0] - b[0]), abs(a[1] - b[1]), abs(a[2] - b[2]),
               abs(a[3] - b[3]) * 255.0)


def hexs(c):
    return "투명" if c is None else "#%02X%02X%02X@%.2f" % (
        int(round(c[0])), int(round(c[1])), int(round(c[2])), c[3])


def main():
    ACTIVE = io.open(os.path.join(ROOT, "GameData", "ACTIVE-VERSION.txt"),
                     encoding="utf-8").read().strip()
    L = [f"# 디자인 전수 대조 — 원본 {ACTIVE} vs 이식본", ""]
    L.append("원본 CSS 430x932, 이식본 uGUI 1080x1920 을 **CSS px 로 되돌려** 비교한다.")
    L.append(f"{TOL:g}px 를 넘는 차이만 적는다. 이식본은 두 축 모두 2.0601 배율에")
    L.append("가운데 886 기둥이고, 같은 자로 되돌려 잰다. 글꼴 래스터라이저와")
    L.append("줄 상자 규칙이 달라 픽셀 단위 동일은 달성할 수 없다.")
    L.append("")
    L.append("짝은 **글자로** 찾는다 — 같은 화면에서 같은 글자를 가진 상자는 같은 것이다.")
    L.append("숫자는 `#` 로 바꿔 비교하므로 `코인 2,000` 과 `코인 66.6K` 도 짝이 된다.")
    L.append("")

    table, details, missing = [], [], []
    tot_pair = tot_bad = 0

    for oname, uname, label in SCREENS:
        o, u = load_original(oname), load_unity(uname)
        if o is None:
            missing.append(f"{label}: 원본 미수집 ({oname})")
            continue
        if u is None:
            missing.append(f"{label}: 이식본 미수집 ({uname})")
            continue

        ot, ut = by_text(o), by_text(u)
        shared = [k for k in ot if k in ut]
        only_orig = [k for k in ot if k not in ut]

        bad = []
        for k in shared:
            a, b = ot[k][0], ut[k][0]
            d, worst, src = compare(a, b, True)
            tot_pair += 1
            if worst > TOL:
                tot_bad += 1
                bad.append((a.get("text", "")[:22] or k[:22], worst, a, b, d, src))

        for op, up, nm in PAIRS.get("*", []) + PAIRS.get(oname, []):
            a, b = find(o, op), find(u, up)
            if a is None or b is None:
                continue
            tot_pair += 1
            d, worst, src = compare(a, b, False)
            if worst > TOL:
                tot_bad += 1
                bad.append((nm, worst, a, b, d, src))

        bad.sort(key=lambda r: -r[1])
        table.append((label, len(o), len(u), len(shared) + len(PAIRS.get("*", [])),
                      len(bad), len(only_orig)))
        if bad:
            details.append((label, bad))

    L.append("## 화면별")
    L.append("")
    L.append("| 화면 | 원본 상자 | 이식 상자 | 맞춘 짝 | 차이 | 원본에만 있는 글자 |")
    L.append("| --- | ---: | ---: | ---: | ---: | ---: |")
    for row in table:
        L.append("| %s | %d | %d | %d | %d | %d |" % row)
    L.append("")
    L.append(f"**합계: 짝 {tot_pair}개 중 {TOL:g}px 초과 차이 {tot_bad}개.**")
    L.append("")

    L.append("## 차이 (큰 것부터)")
    L.append("")
    if not details:
        L.append("맞춘 짝에서는 차이가 없다.")
    for label, bad in details:
        L.append(f"### {label}")
        L.append("")
        L.append("| 요소 | 차이 | 잰 것 | 원본 | 이식 |")
        L.append("| --- | ---: | --- | --- | --- |")
        for name, worst, a, b, d, src in bad[:25]:
            pa = a.get(src) or a["box"]
            pb = b.get(src) or b["box"]
            L.append("| %s | %.0fpx | %s | %gx%g @%g,%g | %gx%g @%g,%g (dx%+g dy%+g dw%+g dh%+g) |"
                     % (name.replace("|", "/"), worst,
                        "글자" if src == "ink" else "상자",
                        pa["w"], pa["h"], pa["x"], pa["y"],
                        pb["w"], pb["h"], pb["x"], pb["y"],
                        d["x"], d["y"], d["w"], d["h"]))
        if len(bad) > 25:
            L.append(f"\n... 외 {len(bad) - 25}개")
        L.append("")

    # ---- 색 ----
    L.append("## 색")
    L.append("")
    L.append(f"판의 배경색을 채널 최대 차이로 본다(알파 포함). {CTOL:g} 이하는 같다고 본다 —")
    L.append("브라우저는 섞은 결과를 돌려주고 우리는 칠한 값을 돌려줘서 1~2 는 늘 난다.")
    L.append("")
    L.append("| 화면 | 요소 | 원본 | 이식 | 차이 |")
    L.append("| --- | --- | --- | --- | ---: |")
    cpair = cbad = 0
    for oname, uname, label in SCREENS:
        o, u = load_original(oname), load_unity(uname)
        if o is None or u is None:
            continue
        for op, up, nm in PAIRS.get("*", []) + PAIRS.get(oname, []):
            a, b = find(o, op), find(u, up)
            if a is None or b is None:
                continue
            g = colour_gap(a.get("colour"), b.get("colour"))
            if g is None:
                continue
            cpair += 1
            if g > CTOL:
                cbad += 1
                L.append("| %s | %s | %s | %s | %.0f |"
                         % (label, nm, hexs(a["colour"]), hexs(b["colour"]), g))
    if cbad == 0:
        L.append("| — | 맞춘 판 전부 일치 | | | 0 |")
    L.append("")
    L.append(f"**색: 판 {cpair}개 중 {cbad}개 다름.**")
    L.append("")

    if missing:
        L.append("## 수집하지 못한 화면")
        L.append("")
        for m in missing:
            L.append(f"- {m}")
        L.append("")

    L.append("## 이 보고서가 보지 못하는 것")
    L.append("")
    L.append("- **글자의 너비·높이** — 두 쪽이 서로 다른 것을 잰다. CSS 의 Range 는")
    L.append("  advance 박스(사이드베어링 포함), TMP 의 textBounds 는 칠해진 자리만이다.")
    L.append("  그래서 우리 쪽 dw 가 늘 음수로 나오는데 글꼴이 좁은 게 아니다.")
    L.append("  **글자는 맞춰진 모서리로 비교한다** — 왼쪽 정렬은 왼쪽, 오른쪽은 오른쪽,")
    L.append("  가운데는 중심. 정렬은 ink 가 상자 안에서 앉은 자리로 읽는다. 한 기준으로")
    L.append("  고정하면 반대쪽 정렬이 너비 차이의 절반만큼 통째로 밀린 것으로 나온다.")
    L.append("  dy 는 세로 중심 차이다.")
    L.append("  표의 dw·dh 는 참고용이다.")
    L.append("- **글자가 없는 상자** — 판·아이콘·구분선은 글자로 못 맞춘다.")
    L.append("  `PAIRS` 에 적은 것만 본다. 지금은 상단바·하단바·전투 HUD 판 셋.")
    L.append("- **글자색** — 원본은 `color` 를 뽑아 두었지만 이식본 쪽은 TMP 의 색을")
    L.append("  아직 안 적는다. 그래서 배경색만 비교한다.")
    L.append("- **보드와 아레나** — 이식본은 카메라로 그려서 UI 상자가 없다.")
    L.append("- 원본에만 있는 글자 수가 큰 화면은 그만큼 못 본 것이다.")

    io.open(OUT, "w", encoding="utf-8").write("\n".join(L) + "\n")
    print("보고서:", OUT)
    print("  자리: 짝 %d개 중 차이 %d개" % (tot_pair, tot_bad))
    print("  색:   판 %d개 중 다름 %d개" % (cpair, cbad))
    for m in missing:
        print("  ", m)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
