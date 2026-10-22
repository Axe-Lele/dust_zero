"""아이콘 생성기 (10.16-h, Claude 제작) — 재화·부품·재료·탭·버튼·전력 스킬 아이콘.

    python tools/art/gen_icons.py              → tools/art/out/icon_*.svg + jobs_icons.json
    node tools/art/render.mjs tools/art/out/jobs_icons.json
    python tools/art/gen_icons.py --webp       → art/icon/*.webp (96×96)
가전 조각 아이콘(shard_<가전ID>)은 게임의 SD를 합쳐야 해서 tools/art/shards.mjs 가 따로 만든다.
"""
import json, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
DEST = os.path.join(HERE, "..", "..", "art", "icon")
L = "#3B2A33"
S = 96


def P(d, fill, sw=4, stroke=L, extra=""):
    st = f' stroke="{stroke}" stroke-width="{sw}" stroke-linejoin="round" stroke-linecap="round"' if sw else ""
    return f'<path d="{d}" fill="{fill}"{st} {extra}/>'


def C(x, y, r, fill, sw=4, stroke=L, extra=""):
    st = f' stroke="{stroke}" stroke-width="{sw}"' if sw else ""
    return f'<circle cx="{x}" cy="{y}" r="{r}" fill="{fill}"{st} {extra}/>'


def R(x, y, w, h, rx, fill, sw=4, stroke=L, extra=""):
    st = f' stroke="{stroke}" stroke-width="{sw}" stroke-linejoin="round"' if sw else ""
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{fill}"{st} {extra}/>'


def shine(x, y, w=10, h=5, rot=-30): return f'<ellipse cx="{x}" cy="{y}" rx="{w}" ry="{h}" fill="#FFFFFF" opacity=".65" transform="rotate({rot} {x} {y})"/>'


def bolt(x, y, s, fill="#F2C14E", sw=3.5):
    return P(f"M{x + .1 * s},{y - .5 * s} L{x - .28 * s},{y + .06 * s} L{x + .02 * s},{y + .06 * s} L{x - .12 * s},{y + .5 * s} L{x + .3 * s},{y - .1 * s} L{x + .02 * s},{y - .1 * s} Z", fill, sw)


def star(x, y, r, fill, sw=3.5):
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5; rr = r if i % 2 == 0 else r * .45
        pts.append(f"{x + math.cos(a) * rr:.1f},{y + math.sin(a) * rr:.1f}")
    return P("M" + " L".join(pts) + " Z", fill, sw)


ICONS = {}
def icon(name):
    def deco(fn): ICONS[name] = fn; return fn
    return deco


# ---------------- 재화
@icon("coin")
def _():
    g = C(48, 50, 36, "#E9A92E") + C(48, 46, 36, "#F7CC52") + C(48, 46, 26, "none", 3, "#D8962A")
    g += P("M48,28 C52,38 58,42 66,46 C58,50 52,54 48,64 C44,54 38,50 30,46 C38,42 44,38 48,28 Z", "#FFF3C4", 3, "#C9852A")   # 반짝임(먼지 제로 문양)
    return g + shine(32, 30, 9, 5)


@icon("crystal")
def _():
    g = P("M48,8 L80,34 L48,90 L16,34 Z", "#5FB4F0")
    g += P("M16,34 L80,34", "none", 3) + P("M48,8 L36,34 L48,90 L60,34 Z", "#8FD0F5", 3)
    g += P("M48,8 L36,34 L16,34 Z", "#BFE6FF", 0) + P("M60,34 L80,34 L48,90 Z", "#3D8BD9", 0, extra='opacity=".55"')
    g += P("M48,8 L80,34 L48,90 L16,34 Z", "none") + P("M16,34 L80,34", "none", 3) + P("M48,8 L36,34 L48,90 L60,34", "none", 3)
    return g + star(74, 16, 7, "#FFFFFF", 2)


# ---------------- 부품(계열별)
@icon("part_filter")      # 101 필터 · 생활가전
def _():
    g = R(14, 20, 68, 58, 10, "#ECEFF2") + R(22, 28, 52, 42, 4, "#BFE3D8", 3)
    zz = "M22,70 " + " ".join(f"L{22 + 6.5 * i},{28 if i % 2 else 70}" for i in range(1, 9))
    g += P(zz, "none", 3, "#3FAE7A") + R(14, 20, 68, 58, 10, "none")
    return g + shine(28, 26, 8, 3, 0)


@icon("part_coil")        # 102 열선 · 주방가전
def _():
    g = R(12, 66, 72, 14, 6, "#8A949C")
    path = "M18,62 " + " ".join(f"C{22 + 12 * i},{24} {28 + 12 * i},{24} {30 + 12 * i},{62}" for i in range(5))
    g += P(path, "none", 11, L) + P(path, "none", 6, "#F08A3E") + P(path, "none", 2, "#FFD08A")
    g += f'<ellipse cx="48" cy="40" rx="34" ry="14" fill="#FF9A3C" opacity=".18"/>'
    return g


@icon("part_chip")        # 103 칩 · 디지털
def _():
    g = ""
    for i in range(4):
        y = 28 + i * 12
        g += P(f"M8,{y} L20,{y}", "none", 4) + P(f"M76,{y} L88,{y}", "none", 4)
        g += P(f"M{28 + i * 12},8 L{28 + i * 12},20", "none", 4) + P(f"M{28 + i * 12},76 L{28 + i * 12},88", "none", 4)
    g += R(18, 18, 60, 60, 8, "#2F3438") + R(28, 28, 40, 40, 5, "#3FAE9A", 3) + P("M34,48 L44,48 L48,40 L56,56 L60,48 L64,48", "none", 3, "#C8F5E6")
    return g + shine(30, 26, 7, 3, 0)


@icon("part_motor")       # 104 모터 · 계절가전
def _():
    g = R(10, 26, 56, 46, 10, "#3D8BD9") + R(10, 26, 56, 46, 10, "none")
    for i in range(4): g += P(f"M{20 + i * 11},28 L{20 + i * 11},70", "none", 3, "#2A6AAE")
    g += R(62, 34, 14, 30, 4, "#9AAEBD") + P("M76,49 L90,49", "none", 6) + P("M76,49 L90,49", "none", 3, "#DCE2E6")
    g += R(18, 72, 40, 10, 3, "#5D6871")
    return g + shine(24, 34, 9, 4, 0)


@icon("parts")            # 부품 묶음
def _():
    g = P("M14,40 L48,26 L82,40 L82,74 L48,88 L14,74 Z", "#C89B5E") + P("M14,40 L48,54 L82,40", "none") + P("M48,54 L48,88", "none")
    g += P("M14,40 L48,26 L82,40 L48,54 Z", "#E0B878", 3)
    g += C(34, 34, 9, "#3FAE9A", 3) + C(50, 28, 9, "#F08A3E", 3) + C(64, 36, 9, "#3D8BD9", 3)
    return g + P("M28,62 L40,68", "none", 3, "#9C7244")


# ---------------- 장비 재료
@icon("stone")            # 장비 강화석
def _():
    g = P("M48,8 L80,26 L80,66 L48,88 L16,66 L16,26 Z", "#8B6BD9") + P("M48,8 L48,48 L80,26 M48,48 L16,26 M48,48 L48,88", "none", 3, "#5E43A8")
    g += P("M48,8 L16,26 L48,48 Z", "#B49BF0", 0) + P("M48,8 L80,26 L80,66 L48,88 L16,66 L16,26 Z", "none")
    g += P("M48,30 L60,46 L53,46 L53,62 L43,62 L43,46 L36,46 Z", "#FFFFFF", 3)
    return g


@icon("essence")          # 전용 정수
def _():
    g = f'<circle cx="48" cy="50" r="38" fill="#FFD54A" opacity=".25"/>'
    g += P("M36,14 L60,14 L58,30 C74,38 80,52 76,64 C70,82 26,82 20,64 C16,52 22,38 38,30 Z", "#FFF1C2")
    g += P("M22,58 C30,52 66,52 74,58 C74,74 62,84 48,84 C34,84 22,74 22,58 Z", "#E8A23A", 0) + P("M36,14 L60,14 L58,30 C74,38 80,52 76,64 C70,82 26,82 20,64 C16,52 22,38 38,30 Z", "none")
    g += R(32, 8, 32, 10, 3, "#C46A3A") + star(48, 64, 12, "#FFFFFF", 2.5)
    return g + shine(32, 40, 6, 10, 20)


@icon("gearbox")          # 장비 상자
def _():
    g = R(12, 40, 72, 44, 8, "#7A5636") + R(10, 28, 76, 20, 8, "#9C7244")
    g += R(42, 28, 12, 56, 2, "#E8B23A", 3) + R(40, 44, 16, 14, 4, "#F2C14E", 3)
    return g + shine(26, 34, 8, 3, 0)


@icon("star")
def _():
    return star(48, 50, 38, "#FF9A3C") + shine(36, 34, 8, 4)


@icon("pass")             # 시즌 패스 · 경험치
def _():
    g = P("M10,26 L86,26 L86,40 C80,40 78,44 78,48 C78,52 80,56 86,56 L86,70 L10,70 L10,56 C16,56 18,52 18,48 C18,44 16,40 10,40 Z", "#F08A3E")
    g += P("M62,26 L62,70", "none", 3, "#FFFFFF", 'stroke-dasharray="5 5"') + star(38, 48, 14, "#FFF1C2", 3)
    return g


@icon("power")            # 전력 W
def _():
    return C(48, 48, 38, "#1D2226") + C(48, 48, 30, "#2F3438", 0) + bolt(48, 48, 56, "#F2C14E")


# ---------------- 탭·버튼
@icon("tab_home")
def _():
    g = P("M14,46 L48,16 L82,46", "none", 7) + P("M22,42 L48,20 L74,42 L74,84 L22,84 Z", "#F7F1E6") + R(40, 58, 16, 26, 3, "#E8842A", 3)
    return g + P("M14,46 L48,16 L82,46", "none", 5, "#E0524A")


@icon("tab_stage")
def _():
    g = P("M24,86 L24,12", "none", 6) + P("M26,14 L80,22 L66,36 L80,50 L26,46 Z", "#E0524A") + star(48, 32, 8, "#FFF1C2", 2.5)
    return g + f'<ellipse cx="34" cy="86" rx="20" ry="5" fill="#3B2A33" opacity=".25"/>'


@icon("tab_content")      # 대청소 모드
def _():
    g = P("M62,12 L36,64", "none", 7) + P("M62,12 L36,64", "none", 3, "#C89B5E")
    g += P("M24,58 L46,70 L36,88 C26,88 14,80 14,72 Z", "#F2C14E") + P("M22,66 L30,84 M30,62 L38,82", "none", 2.5, "#B7791F")
    g += C(70, 64, 16, "#9A9DA1") + C(66, 60, 5, "#C9CDD1", 0) + star(78, 30, 8, "#FFFFFF", 2.5)
    return g


@icon("tab_roster")       # 가전 도감
def _():
    g = R(18, 12, 60, 74, 10, "#F4F7F9") + R(26, 22, 44, 30, 6, "#BFE0F2", 3) + C(48, 66, 10, "#E0436B", 3)
    return g + C(40, 34, 3, L, 0) + C(56, 34, 3, L, 0) + P("M42,42 Q48,46 54,42", "none", 2.5)


@icon("tab_deck")         # 편성(카드 묶음)
def _():
    g = R(12, 22, 40, 56, 8, "#DCE9F7", extra='transform="rotate(-12 32 50)"') + R(30, 18, 40, 56, 8, "#FFE6CF", extra='transform="rotate(4 50 46)"')
    g += R(46, 22, 40, 56, 8, "#FFFFFF", extra='transform="rotate(16 66 50)"')
    return g + star(64, 48, 10, "#FF9A3C", 2.5)


@icon("tab_gacha")        # 소환: 캡슐 뽑기 기계
def _():
    g = C(48, 38, 30, "#DDF1FD") 
    for x, y, c in ((36, 46, "#FF9A3C"), (54, 50, "#8B6BD9"), (46, 30, "#3FAE7A"), (62, 34, "#E0436B"), (30, 30, "#3D8BD9")): g += C(x, y, 8, c, 2.5)
    g += C(48, 38, 30, "none") + shine(36, 22, 8, 4)
    g += P("M22,62 L74,62 L80,90 L16,90 Z", "#E8842A") + R(40, 70, 16, 12, 3, "#1D2226", 3) + C(66, 76, 5, "#F2C14E", 3)
    return g


@icon("tab_shop")         # 상점 가방
def _():
    g = P("M34,34 C34,16 62,16 62,34", "none", 6) + P("M16,32 L80,32 L74,86 L22,86 Z", "#3FAE7A") + P("M16,32 L80,32", "none")
    return g + star(48, 58, 12, "#FFF1C2", 3)


@icon("attend")           # 출석부
def _():
    g = R(14, 18, 68, 66, 10, "#FFFFFF") + R(14, 18, 68, 18, 8, "#E0524A") + P("M30,12 L30,26 M66,12 L66,26", "none", 6)
    for i in range(3):
        for j in range(2): g += R(24 + i * 18, 44 + j * 16, 12, 10, 2, "#DCE2E6", 0)
    return g + C(60, 64, 14, "none", 4, "#E0524A") + P("M54,64 L59,69 L68,58", "none", 4, "#E0524A")


@icon("idle")             # 자동 청소(로봇청소기)
def _():
    g = f'<ellipse cx="48" cy="58" rx="40" ry="26" fill="#3E454B" stroke="{L}" stroke-width="4"/><ellipse cx="48" cy="52" rx="38" ry="24" fill="#ECEFF2" stroke="{L}" stroke-width="3.5"/>'
    g += C(64, 44, 5, "#3FAE7A", 3) + P("M24,58 Q48,70 72,58", "none", 3, "#9AAEBD")
    return g + P("M64,18 L70,24 M78,14 L78,24 M86,20 L80,26", "none", 3.5, "#F2C14E")


@icon("settings")
def _():
    g = ""
    for i in range(8):
        a = i * math.pi / 4
        g += P(f"M{48 + math.cos(a) * 28:.1f},{48 + math.sin(a) * 28:.1f} L{48 + math.cos(a) * 40:.1f},{48 + math.sin(a) * 40:.1f}", "none", 12)
        g += P(f"M{48 + math.cos(a) * 28:.1f},{48 + math.sin(a) * 28:.1f} L{48 + math.cos(a) * 39:.1f},{48 + math.sin(a) * 39:.1f}", "none", 6, "#9AAEBD")
    return g + C(48, 48, 28, "#9AAEBD") + C(48, 48, 11, "#ECEFF2")


@icon("survival")         # 대청소 모드(빗자루+청소기)
def _():
    return ICONS["tab_content"]()


@icon("ad")               # 광고
def _():
    g = R(10, 20, 76, 54, 8, "#2F3438") + R(16, 26, 64, 42, 4, "#8FD0F5", 3) + P("M42,36 L58,47 L42,58 Z", "#FFFFFF", 3)
    return g + P("M34,86 L62,86 M48,74 L48,86", "none", 5)


@icon("multitap")         # 멀티탭
def _():
    g = R(8, 40, 80, 30, 10, "#F4F7F9") + C(28, 55, 7, "#DCE2E6", 3) + C(52, 55, 7, "#DCE2E6", 3) + C(74, 55, 4, "#E0524A", 0)
    return g + P("M8,55 C-2,55 2,30 20,24", "none", 5) + bolt(70, 22, 30, "#F2C14E", 3)


@icon("ps_storm")         # 과전류 방출
def _():
    g = f'<circle cx="48" cy="48" r="44" fill="#2B2F44"/>'
    g += P("M20,40 C14,40 12,30 20,26 C22,14 38,12 44,20 C50,10 70,12 72,26 C82,26 84,40 74,42 Z", "#8A8FA8", 3.5)
    g += bolt(36, 62, 34, "#FFF27A", 3) + bolt(60, 66, 40, "#F2C14E", 3)
    return g + C(48, 48, 44, "none", 4)


@icon("ps_boost")         # 전력 증폭
def _():
    g = f'<circle cx="48" cy="48" r="44" fill="#FFF1C2"/>'
    g += R(34, 40, 28, 30, 6, "#3A4046") + P("M40,40 L40,26 M56,40 L56,26", "none", 6) + P("M48,70 C48,80 40,84 30,86", "none", 5)
    g += P("M74,52 L74,24 M66,32 L74,22 L82,32", "none", 5, "#E0524A") + P("M20,58 L20,34 M13,42 L20,32 L27,42", "none", 5, "#E0524A")
    return g + bolt(48, 54, 22, "#F2C14E", 2.5) + C(48, 48, 44, "none", 4)


@icon("month")            # 구독
def _():
    return ICONS["attend"]().replace("#E0524A", "#3D8BD9") + ""


@icon("noads")
def _():
    return ICONS["ad"]() + C(48, 48, 36, "none", 7, "#E0524A") + P("M23,73 L73,23", "none", 7, "#E0524A")


@icon("idleboost")        # 자동 청소 즉시
def _():
    return ICONS["idle"]() + C(72, 72, 16, "#F2C14E") + P("M72,62 L72,73 L80,77", "none", 3.5)


def svg(inner):
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="{S}" height="{S}" viewBox="0 0 96 96">{inner}</svg>'


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True); os.makedirs(DEST, exist_ok=True)
    if "--webp" in sys.argv:
        from PIL import Image
        for n in ICONS:
            Image.open(os.path.join(OUT, f"icon_{n}.png")).save(os.path.join(DEST, f"{n}.webp"), "WEBP", quality=92, method=6, lossless=False)
        print("webp:", len(ICONS))
    else:
        jobs = []
        for n, fn in ICONS.items():
            f = os.path.join(OUT, f"icon_{n}.svg"); open(f, "w", encoding="utf-8").write(svg(fn()))
            jobs.append({"svg": f, "out": f[:-4] + ".png", "w": S, "h": S, "transparent": True})
        json.dump(jobs, open(os.path.join(OUT, "jobs_icons.json"), "w"))
        print("icons:", len(jobs))
