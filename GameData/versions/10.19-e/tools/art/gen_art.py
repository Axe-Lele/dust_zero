"""가전 일러스트(벡터) 생성기 — 10.16-f, Claude 제작.

코드로 그린 셀 셰이딩 애니풍 일러스트. 가전마다 같은 몸체(리그)에 머리 모양·색·옷·소품·등 장식만 바꿔 끼운다.
SD(index.html 의 CHIBI 표)와 같은 디자인을 따른다.

    python tools/art/gen_art.py [가전ID ...]      → tools/art/out/*.svg 와 렌더 작업 목록(jobs.json)
    node tools/art/render.mjs tools/art/out/jobs.json   → PNG
    python tools/art/gen_art.py --webp             → PNG 를 art/<id>_*.webp 로 변환

만드는 파일(가전마다): _full(전신 510×1080) / _face1~3(표정 230×246) / _turn(정면·측면·후면 500×308) / _keys(주요 요소 470×228)
진짜 그림이 생기면 art/ 폴더의 같은 파일 이름으로 바꿔 끼우면 된다.
"""
import json, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
ART = os.path.join(HERE, "..", "..", "art")
LINE = "#3B2A33"
SKIN, SKIN_SH, SKIN_HL = "#FFE6D8", "#F5C4B4", "#FFF4EC"
W, H = 512, 1080


def mix(a, b, t):
    a = a.lstrip("#"); b = b.lstrip("#")
    ca = [int(a[i:i + 2], 16) for i in (0, 2, 4)]; cb = [int(b[i:i + 2], 16) for i in (0, 2, 4)]
    return "#" + "".join(f"{round(x + (y - x) * t):02X}" for x, y in zip(ca, cb))


def shade(c, t=0.22): return mix(c, "#2A1E3A", t)
def light(c, t=0.35): return mix(c, "#FFFFFF", t)


def P(d, fill, sw=3.0, stroke=LINE, extra=""):
    st = f' stroke="{stroke}" stroke-width="{sw}" stroke-linejoin="round" stroke-linecap="round"' if sw else ""
    return f'<path d="{d}" fill="{fill}"{st} {extra}/>'


def mirror(svg):
    return f'<g transform="translate({W},0) scale(-1,1)">{svg}</g>'


_uid = [0]
def uid(p="c"):
    _uid[0] += 1; return f"{p}{_uid[0]}"


def clipped(shape_d, inner):
    """shape_d 모양 안쪽에만 inner(그림자·하이라이트)를 그린다"""
    cid = uid("cl")
    return f'<clipPath id="{cid}"><path d="{shape_d}"/></clipPath><g clip-path="url(#{cid})">{inner}</g>'


# ------------------------------------------------------------------ 몸체 (정면)
FACE = "M190,124 C188,178 214,222 256,240 C298,222 324,178 322,124 C322,80 296,58 256,58 C216,58 190,80 190,124 Z"
NECK = "M242,220 L241,266 Q256,276 271,266 L270,220 Z"
SH = "rgba(60,30,70,.15)"     # 기본 그림자(곱하기 느낌)
SH2 = "rgba(60,30,70,.24)"


def lock(rx, ry, tx, ty, w, bend=0.0, fill="#000", sw=2.8, shadec=None, tipw=0.0):
    """머리카락 한 가닥: 뿌리(rx,ry) → 끝(tx,ty), 폭 w, bend 만큼 휘어짐. 오른쪽 절반에 그림자"""
    dx, dy = tx - rx, ty - ry; L = math.hypot(dx, dy) or 1; nx, ny = -dy / L, dx / L
    mx, my = rx + dx * .5 + nx * bend, ry + dy * .5 + ny * bend
    a = (rx - nx * w / 2, ry - ny * w / 2); b = (rx + nx * w / 2, ry + ny * w / 2)
    ml = (mx - nx * w * .48, my - ny * w * .48); mr = (mx + nx * w * .48, my + ny * w * .48)
    t1 = (tx - nx * tipw, ty - ny * tipw); t2 = (tx + nx * tipw, ty + ny * tipw)
    f = lambda p: f"{p[0]:.1f},{p[1]:.1f}"
    d = f"M{f(a)} Q{f(ml)} {f(t1)} L{f(t2)} Q{f(mr)} {f(b)} Z"
    g = P(d, fill, sw)
    if shadec:
        c0 = (rx + nx * w * .05, ry + ny * w * .05); mc = (mx + nx * w * .08, my + ny * w * .08)
        sd = f"M{f(c0)} Q{f(mc)} {f(t2)} Q{f(mr)} {f(b)} Z"
        g += P(sd, shadec, 0)
        g += P(d, "none", sw)
    return g


def legs(spec):
    s = spec
    sock = s.get("legs", SKIN)
    d = ("M196,556 C192,616 198,690 207,748 C202,790 203,842 213,892 C218,930 221,958 223,980 L241,980 "
         "C242,950 241,902 243,862 C247,822 245,792 242,756 C248,700 254,620 253,556 Z")
    g = P(d, SKIN)
    g += clipped(d, P("M232,556 C246,620 244,700 236,760 C236,820 236,900 236,990 L260,990 L260,556 Z", "rgba(190,100,100,.16)", 0)
                 + '<ellipse cx="224" cy="752" rx="10" ry="6" fill="rgba(255,160,160,.35)"/>')
    if sock != SKIN:
        top = s.get("sockTop", 700)
        sk = (f"M199,{top} C201,{top + 30} 204,{top + 60} 208,{max(top + 40, 748)} C204,792 205,842 214,892 C218,930 222,958 224,980 L240,980 "
              f"C240,950 238,902 240,862 C244,822 243,792 240,756 C244,{top + 50} 247,{top + 20} 249,{top} Z")
        if top < 640:
            sk = d
        g += P(sk, sock, 2.6) + clipped(sk, P("M230,550 C244,620 242,700 236,760 C236,820 236,900 236,990 L260,990 L260,550 Z", SH, 0))
        if s.get("sockBand"):
            g += P(f"M199,{top} L249,{top} L248,{top + 12} L200,{top + 12} Z", s["sockBand"], 2.2)
    g += P(d, "none", 3)
    shoe = s.get("shoes", "#3A4256")
    sh = "M220,970 C208,986 204,1016 218,1028 C232,1038 252,1032 252,1012 C252,994 248,980 244,970 Z"
    g += P(sh, shoe, 3) + clipped(sh, P("M236,966 L262,966 L262,1040 L236,1040 Z", SH2, 0)) + P("M214,1004 C224,1012 240,1014 250,1008", "none", 2.2, light(shoe, .5))
    if s.get("strap", True):
        g += P("M222,984 Q234,990 246,984", "none", 3, shade(shoe, .3))
    return g + mirror(g)


def torso(spec):
    s = spec
    d = ("M204,270 C190,276 184,294 186,316 C188,340 196,362 204,384 L208,400 Q256,410 304,400 L308,384 "
         "C316,362 324,340 326,316 C328,294 322,276 308,270 Q256,288 204,270 Z")
    inner = P("M292,270 C320,292 324,334 314,372 L306,402 L334,402 L334,270 Z", SH, 0)
    inner += P("M210,346 Q256,374 302,346 L300,360 Q256,388 212,360 Z", "rgba(60,30,70,.10)", 0)
    inner += P("M214,286 C222,300 226,320 224,340", "none", 5, "rgba(255,255,255,.35)")
    if s.get("topPattern"): inner += s["topPattern"]
    g = P(d, s["top"]) + clipped(d, inner) + P(d, "none", 3)
    if s.get("collar"):
        g += P("M220,266 L256,314 L292,266 Q256,282 220,266 Z", s["collar"], 2.6)
        g += P("M220,266 L234,300 L256,314", "none", 2, shade(s["collar"], .25))
    if s.get("bow"):
        g += bow(256, 300, 18, s["bow"])
    return g


def bow(x, y, r, col):
    g = P(f"M{x},{y} C{x - r * 1.4},{y - r * 1.1} {x - r * 1.9},{y + r * .2} {x - r * 1.3},{y + r * .9} C{x - r * .8},{y + r * 1.1} {x - r * .3},{y + r * .4} {x},{y} Z", col, 2.4)
    g += P(f"M{x},{y} C{x + r * 1.4},{y - r * 1.1} {x + r * 1.9},{y + r * .2} {x + r * 1.3},{y + r * .9} C{x + r * .8},{y + r * 1.1} {x + r * .3},{y + r * .4} {x},{y} Z", col, 2.4)
    g += P(f"M{x - 3},{y + 2} L{x - r * .9},{y + r * 2.1} L{x - r * .2},{y + r * 1.8} Z", col, 2.2)
    g += P(f"M{x + 3},{y + 2} L{x + r * .9},{y + r * 2.1} L{x + r * .2},{y + r * 1.8} Z", col, 2.2)
    g += f'<circle cx="{x}" cy="{y + 1}" r="{r * .38}" fill="{shade(col, .2)}" stroke="{LINE}" stroke-width="2.2"/>'
    return g


def skirt(spec):
    s = spec
    fl = s.get("flare", 1.0); hem = s.get("hem", 572)
    L, R = 256 - 118 * fl, 256 + 118 * fl
    pts = []
    n = 7
    for i in range(n + 1):
        t = i / n; x = R - (R - L) * t; y = hem + (8 if i % 2 else -6) + 14 * math.sin(math.pi * t)
        pts.append((x, y))
    hemd = " ".join(f"Q{(pts[i][0] + pts[i + 1][0]) / 2:.0f},{max(pts[i][1], pts[i + 1][1]) + 10:.0f} {pts[i + 1][0]:.0f},{pts[i + 1][1]:.0f}" for i in range(n))
    d = f"M206,392 Q256,404 306,392 C{R - 50:.0f},450 {R - 20:.0f},510 {pts[0][0]:.0f},{pts[0][1]:.0f} {hemd} C{L + 20:.0f},510 {L + 50:.0f},450 206,392 Z"
    inner = P(f"M262,400 C{R - 40:.0f},440 {R:.0f},520 {R + 20:.0f},{hem + 40} L{R + 30:.0f},380 Z", SH, 0)
    inner += P(f"M180,{hem - 40} Q256,{hem - 10} 340,{hem - 40} L340,{hem + 40} L180,{hem + 40} Z", "rgba(60,30,70,.08)", 0)
    if s.get("pleats", True):
        for i in range(1, n):
            x1, y1 = pts[i]; x0 = 206 + (306 - 206) * (n - i) / n
            inner += P(f"M{x0:.0f},{402} Q{(x0 + x1) / 2:.0f},{(402 + y1) / 2:.0f} {x1:.0f},{y1 + 4:.0f}", "none", 2, shade(s["skirt"], .28))
    if s.get("skirtPattern"): inner += s["skirtPattern"]
    if s.get("hemBand"):
        inner += P(f"M{L - 20:.0f},{hem - 26} Q256,{hem + 6} {R + 20:.0f},{hem - 26} L{R + 30:.0f},{hem + 40} L{L - 30:.0f},{hem + 40} Z", s["hemBand"], 0)
    g = P(d, s["skirt"]) + clipped(d, inner) + P(d, "none", 3)
    if s.get("apron"):
        ad = f"M220,404 Q256,410 292,404 C300,450 306,500 308,{hem - 34} Q256,{hem - 14} 204,{hem - 34} C206,500 212,450 220,404 Z"
        g += P(ad, s["apron"], 2.6) + clipped(ad, P(f"M262,400 L320,400 L320,{hem} L262,{hem} Z", SH, 0)) + P(ad, "none", 2.6)
        g += P(f"M214,{hem - 52} Q256,{hem - 36} 298,{hem - 52}", "none", 2, shade(s["apron"], .2))
    if s.get("belt"):
        bd = "M200,388 Q256,404 312,388 L314,406 Q256,422 198,406 Z"
        g += P(bd, s["belt"], 2.6) + P("M248,392 L264,392 L264,414 L248,414 Z", light(s["belt"], .35), 2.2)
    return g


def arm_left(spec):   # 화면 왼쪽 팔: 내려서 살짝 벌림
    s = spec; sl = s.get("sleeve", s["top"])
    d = ("M206,272 C190,276 180,292 178,316 C174,356 168,398 164,436 C162,456 160,468 160,478 L180,482 "
         "C182,462 186,440 190,420 C196,384 202,350 210,320 Z")
    g = P(d, SKIN) + clipped(d, P("M186,300 C190,360 184,420 178,486 L196,486 L214,300 Z", "rgba(190,100,100,.16)", 0)) + P(d, "none", 3)
    ln = s.get("sleeveLen", "short")
    if ln == "long":
        sd = "M206,270 C188,276 178,292 176,316 C172,356 166,398 162,436 C160,456 158,468 158,482 L182,486 C184,464 188,440 192,420 C198,384 204,350 212,320 Z"
        g += P(sd, sl) + clipped(sd, P("M190,300 C194,360 188,420 182,490 L200,490 L216,300 Z", SH, 0)) + P(sd, "none", 3)
        g += P("M154,476 L186,482 L184,500 L152,494 Z", s.get("cuff", light(sl, .4)), 2.4)
    elif ln == "puff":
        sd = "M208,268 C184,272 170,292 172,318 C176,338 194,344 210,334 C220,320 224,300 218,282 Z"
        g += P(sd, sl) + clipped(sd, P("M196,280 L230,280 L230,350 L196,350 Z", SH, 0)) + P(sd, "none", 3)
        g += P("M176,322 Q192,336 212,330", "none", 2.4, shade(sl, .25))
    elif ln == "short":
        sd = "M208,268 C190,276 180,300 178,330 L204,338 C206,316 212,300 220,290 Z"
        g += P(sd, sl) + P("M180,326 L206,334", "none", 3)
    hd = "M156,478 C146,488 146,508 154,516 C160,522 172,522 178,514 C184,506 184,492 180,482 Z"
    g += P(hd, SKIN, 2.6) + P("M158,494 C152,500 152,506 156,510", "none", 1.8)
    return g


def arm_right(spec):  # 화면 오른쪽 팔: 팔꿈치를 굽혀 손을 허리 앞(≈ 318,452)으로
    s = spec; sl = s.get("sleeve", s["top"])
    d = ("M306,272 C322,278 334,296 338,320 C344,352 350,380 352,396 C352,412 344,428 332,442 L316,430 "
         "C324,418 330,406 330,394 C326,368 316,340 306,316 Z")
    g = P(d, SKIN) + clipped(d, P("M330,300 C344,350 352,400 340,446 L360,446 L360,300 Z", "rgba(190,100,100,.18)", 0)) + P(d, "none", 3)
    ln = s.get("sleeveLen", "short")
    if ln == "long":
        sd = "M306,270 C324,276 336,296 340,320 C346,352 352,380 354,396 C354,414 346,430 334,446 L312,432 C320,420 328,406 328,394 C324,368 314,340 304,316 Z"
        g += P(sd, sl) + clipped(sd, P("M330,300 C344,350 354,400 342,450 L362,450 L362,300 Z", SH, 0)) + P(sd, "none", 3)
        g += P("M310,428 L336,446 L326,460 L300,442 Z", s.get("cuff", light(sl, .4)), 2.4)
    elif ln == "puff":
        sd = "M304,268 C328,272 342,292 340,318 C336,338 318,344 302,334 C292,320 288,300 294,282 Z"
        g += P(sd, sl) + clipped(sd, P("M318,270 L350,270 L350,350 L318,350 Z", SH, 0)) + P(sd, "none", 3)
        g += P("M336,322 Q320,336 300,330", "none", 2.4, shade(sl, .25))
    elif ln == "short":
        sd = "M304,268 C322,276 332,300 336,330 L310,338 C308,316 302,300 292,290 Z"
        g += P(sd, sl) + P("M334,326 L308,334", "none", 3)
    return g


def hand_right(x=318, y=452):
    hd = f"M{x - 16},{y - 10} C{x - 22},{y + 4} {x - 14},{y + 20} {x},{y + 20} C{x + 14},{y + 20} {x + 20},{y + 6} {x + 16},{y - 6} C{x + 10},{y - 16} {x - 8},{y - 18} {x - 16},{y - 10} Z"
    return P(hd, SKIN, 2.6) + P(f"M{x - 6},{y - 4} C{x - 2},{y + 2} {x + 4},{y + 2} {x + 8},{y - 2}", "none", 1.8)


# ------------------------------------------------------------------ 머리카락
def hair_back(spec):
    s = spec; c = s["hair"]; st = s["style"]; sc = shade(c, .18); out = ""
    if st in ("long", "twin", "pony", "drill", "bob", "buns"):
        if st == "long":
            d = ("M180,104 C150,220 146,420 168,630 L186,600 L200,650 L222,606 L240,660 L258,612 L276,662 L294,608 L314,652 L326,600 L344,628 "
                 "C366,420 362,220 332,104 Z")
            inner = P("M256,90 C300,200 320,420 330,660 L380,660 L380,90 Z", "rgba(60,30,70,.16)", 0)
            if s.get("tipFlame"):
                gid = uid("fl")
                inner += (f'<defs><linearGradient id="{gid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#F0582A" stop-opacity="0"/>'
                          f'<stop offset=".35" stop-color="#F0582A"/><stop offset="1" stop-color="#FFB24A"/></linearGradient></defs>'
                          f'<rect x="120" y="470" width="280" height="200" fill="url(#{gid})"/>')
            for x in (196, 226, 286, 318):
                inner += P(f"M{x},{200} C{x - 6},{340} {x - 2},{480} {x + 4},{620}", "none", 2, sc)
            out += P(d, c) + clipped(d, inner) + P(d, "none", 3)
        elif st == "bob":
            d = "M180,100 C158,160 160,240 176,300 L196,288 L208,312 L226,292 L286,292 L304,312 L316,288 L336,300 C352,240 354,160 332,100 Z"
            out += P(d, c) + clipped(d, P("M256,90 L360,90 L360,320 L256,320 Z", "rgba(60,30,70,.16)", 0)) + P(d, "none", 3)
        else:
            d = "M184,108 C170,160 174,226 192,270 L320,270 C338,226 342,160 328,108 Z"
            out += P(d, c) + P(d, "none", 3)
    if st == "twin":
        for side in (0, 1):
            d = ("M196,92 C156,108 130,170 130,250 C130,350 116,450 124,550 C130,616 122,668 138,716 L150,688 L160,724 L172,690 "
                 "C166,640 176,580 172,520 C168,440 184,360 186,280 C188,210 198,150 222,116 Z")
            inner = P("M168,90 C190,250 176,500 180,740 L240,740 L240,90 Z", "rgba(60,30,70,.16)", 0)
            inner += P("M146,240 C142,330 136,420 140,500", "none", 3.2, light(c, .45))
            inner += P("M160,300 C156,400 152,500 156,600", "none", 2, sc)
            if s.get("tip"):
                inner += P("M100,600 Q150,580 200,600 L200,760 L100,760 Z", s["tip"], 0)
            g = P(d, c) + clipped(d, inner) + P(d, "none", 3)
            g += P("M186,98 C200,90 214,92 224,104 C216,114 200,118 188,112 Z", s.get("tie", shade(c, .3)), 2.4)
            out += g if side == 0 else mirror(g)
    if st == "drill":
        for side in (0, 1):
            g = P("M196,96 C164,126 146,186 152,250 L200,254 Z", c, 3)
            for i in range(5):
                cy = 296 + i * 70; rx = 42 - i * 3.5; cx = 158 - i * 1.5
                d = f"M{cx - rx},{cy} C{cx - rx},{cy - 44} {cx + rx},{cy - 44} {cx + rx},{cy} C{cx + rx},{cy + 44} {cx - rx},{cy + 44} {cx - rx},{cy} Z"
                inner = P(f"M{cx + 4},{cy - 60} L{cx + 70},{cy - 60} L{cx + 70},{cy + 60} L{cx - 10},{cy + 60} Z", "rgba(60,30,70,.18)", 0)
                inner += P(f"M{cx - rx + 8},{cy - 6} C{cx - 14},{cy - 30} {cx + 14},{cy - 30} {cx + rx - 10},{cy - 14}", "none", 3.4, light(c, .5))
                inner += P(f"M{cx - rx + 6},{cy + 14} C{cx - 10},{cy + 32} {cx + 16},{cy + 30} {cx + rx - 6},{cy + 10}", "none", 2, sc)
                g += P(d, c) + clipped(d, inner) + P(d, "none", 3)
            g += P("M178,236 C196,228 210,234 214,248 C204,258 186,258 176,250 Z", s.get("tie", "#C46A3A"), 2.4)
            out += g if side == 0 else mirror(g)
    if st == "pony":
        d = ("M314,90 C376,100 404,180 396,270 C390,350 404,420 392,500 L380,476 L372,516 L360,480 C366,420 352,360 354,290 "
             "C356,210 342,148 300,118 Z")
        inner = P("M372,90 C390,200 384,350 380,520 L430,520 L430,90 Z", "rgba(60,30,70,.16)", 0) + P("M370,170 C378,250 370,330 368,410", "none", 3.2, light(c, .45))
        out += P(d, c) + clipped(d, inner) + P(d, "none", 3)
        out += P("M304,96 C318,84 336,86 344,100 C336,114 316,116 304,108 Z", s.get("tie", "#FFD08A"), 2.4)
    if st == "buns":
        for side in (0, 1):
            d = "M166,78 C146,56 152,14 190,10 C226,8 238,42 226,70 Z"
            inner = P("M196,0 L250,0 L250,90 L196,90 Z", "rgba(60,30,70,.16)", 0) + P("M162,46 C168,30 180,22 194,22", "none", 3, light(c, .5))
            g = P(d, c) + clipped(d, inner) + P(d, "none", 3) + P("M176,62 C188,54 208,56 222,66", "none", 2, sc)
            out += g if side == 0 else mirror(g)
    return out


# 앞머리 아래쪽 가장자리: 오른쪽 관자놀이 → 왼쪽, (끝점, 골짜기) 번갈아
BANG_EDGE = [(332, 156), (320, 116), (312, 164), (298, 108), (287, 170), (272, 104), (258, 158), (246, 106), (230, 172), (216, 110), (203, 162), (194, 120), (180, 156)]
BANG_EDGE_SHORT = [(330, 140), (318, 108), (308, 148), (294, 102), (284, 150), (270, 98), (256, 142), (244, 100), (230, 152), (216, 104), (204, 146), (194, 112), (182, 140)]


def bang_path(edge):
    pts = edge
    d = f"M{pts[-1][0]},{pts[-1][1]} C168,96 206,34 256,34 C306,34 344,96 {pts[0][0]},{pts[0][1]}"
    for i in range(len(pts) - 1):
        a, b = pts[i], pts[i + 1]
        if b[1] < a[1]:   # 끝 → 골짜기(위로)
            d += f" C{a[0] - 2},{a[1] - 18} {b[0] + 6},{b[1] + 22} {b[0]},{b[1]}"
        else:             # 골짜기 → 끝(아래로, 바깥쪽으로 살짝 휨)
            d += f" C{a[0] - 4},{a[1] + 20} {b[0] + 8},{b[1] - 26} {b[0]},{b[1]}"
    return d + " Z"


def hair_front(spec):
    s = spec; c = s["hair"]; sc = shade(c, .22)
    edge = BANG_EDGE_SHORT if s.get("shortBangs") else BANG_EDGE
    d = bang_path(edge)
    inner = P("M256,30 C310,36 346,90 340,180 L256,180 Z", "rgba(60,30,70,.10)", 0)
    # 끝 쪽 그림자: 가닥마다 오른쪽 아래
    for i in range(0, len(edge) - 1, 2):
        t = edge[i]; v = edge[i + 1] if i + 1 < len(edge) else edge[i]
        inner += P(f"M{t[0]},{t[1]} C{t[0] - 2},{t[1] - 16} {v[0] + 4},{v[1] + 14} {v[0] + 2},{v[1] - 4} L{t[0] + 6},{v[1] - 6} Z", "rgba(60,30,70,.16)", 0)
    # 가닥 나눔 선(정수리에서 골짜기 쪽으로)
    for i in range(1, len(edge) - 1, 2):
        v = edge[i]
        inner += P(f"M{v[0]},{v[1] + 2} C{v[0] + (v[0] - 256) * .1:.0f},{v[1] - 24} {256 + (v[0] - 256) * .55:.0f},{70} {256 + (v[0] - 256) * .35:.0f},{52}", "none", 2.2, sc)
    # 천사의 고리
    ring = "M200,98 C222,80 290,80 312,100 L306,108 L298,100 L288,108 L278,98 L266,106 L254,98 L242,106 L232,98 L220,108 L212,100 L204,108 Z"
    lum = sum(int(c.lstrip("#")[i:i + 2], 16) for i in (0, 2, 4)) / 765
    inner += P(ring, light(c, .55) if lum > .35 else mix(c, s.get("tip") or s["key"], .55), 0, extra=f'opacity="{.85 if lum > .35 else .6}"')
    g = P(d, c, 3) + clipped(d, inner) + P(d, "none", 3)
    # 옆머리(얼굴 옆으로 내려오는 가닥)
    for side in (0, 1):
        sd = "M194,110 C176,170 174,250 188,320 C194,300 198,284 204,262 C206,280 210,292 214,300 C212,240 206,180 214,126 Z"
        sl = P(sd, c, 2.8) + clipped(sd, P("M200,110 L230,110 L230,330 L200,330 Z", "rgba(60,30,70,.16)", 0) + P("M188,170 C184,220 186,260 190,300", "none", 2.4, light(c, .45))) + P(sd, "none", 2.8)
        g += sl if side == 0 else mirror(sl)
    if s.get("ahoge"):
        g += P("M252,40 C244,10 270,0 284,12 C272,12 264,22 262,40 Z", c, 2.6)
    return g


# ------------------------------------------------------------------ 얼굴
def eye(cx, cy, iris, expr, side):
    """side = -1(화면 왼쪽 눈) / 1(오른쪽 눈). 바깥 눈꼬리 쪽이 side 방향"""
    s = side; g = ""
    if expr in ("happy",):
        return P(f"M{cx - 20},{cy + 2} Q{cx},{cy - 20} {cx + 20},{cy + 2}", "none", 4.2)
    if expr == "trouble":
        return P(f"M{cx - 16 * s},{cy - 12} L{cx + 12 * s},{cy} L{cx - 16 * s},{cy + 12}", "none", 4.5)
    sleepy = expr == "sleepy"
    g += P(f"M{cx - 23},{cy} C{cx - 22},{cy - 25} {cx + 22},{cy - 25} {cx + 23},{cy} C{cx + 22},{cy + 27} {cx - 22},{cy + 27} {cx - 23},{cy} Z", "#FFFFFF", 0)
    iid = uid("ir")
    g += (f'<defs><linearGradient id="{iid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{shade(iris, .55)}"/>'
          f'<stop offset=".55" stop-color="{iris}"/><stop offset="1" stop-color="{light(iris, .55)}"/></linearGradient></defs>')
    g += f'<ellipse cx="{cx}" cy="{cy + 4}" rx="17" ry="22" fill="url(#{iid})" stroke="{shade(iris, .6)}" stroke-width="1.6"/>'
    g += f'<ellipse cx="{cx}" cy="{cy + 6}" rx="8" ry="11.5" fill="{shade(iris, .72)}" opacity=".85"/>'
    g += f'<path d="M{cx - 12},{cy + 16} Q{cx},{cy + 25} {cx + 12},{cy + 16}" stroke="{light(iris, .7)}" stroke-width="2.6" fill="none" opacity=".85"/>'
    g += f'<circle cx="{cx - 7}" cy="{cy - 6}" r="6.2" fill="#FFFFFF"/><circle cx="{cx + 7}" cy="{cy + 13}" r="2.6" fill="#FFFFFF" opacity=".9"/>'
    if expr == "spark":
        g += f'<path d="M{cx + 2},{cy - 4} l3,7 7,3 -7,3 -3,7 -3,-7 -7,-3 7,-3 Z" fill="#FFFFFF"/>'
    # 윗눈꺼풀·속눈썹
    if sleepy:
        g += P(f"M{cx - 22},{cy - 2} C{cx - 21},{cy - 22} {cx + 21},{cy - 22} {cx + 22},{cy - 2} L{cx + 22},{cy + 2} L{cx - 22},{cy + 2} Z", SKIN, 0)
        g += P(f"M{cx - 23},{cy + 2} Q{cx},{cy - 2} {cx + 24},{cy + 2}", "none", 4.5)
    else:
        o = s
        g += P(f"M{cx - 24 * o},{cy + 2} C{cx - 20 * o},{cy - 23} {cx + 15 * o},{cy - 27} {cx + 26 * o},{cy - 9} L{cx + 33 * o},{cy - 16} L{cx + 27 * o},{cy - 2}",
               "none", 5.4)
        g += P(f"M{cx - 18 * o},{cy - 30} Q{cx + 2 * o},{cy - 36} {cx + 20 * o},{cy - 26}", "none", 1.8)   # 쌍꺼풀 선
        g += P(f"M{cx - 13 * o},{cy + 25} Q{cx + 6 * o},{cy + 29} {cx + 19 * o},{cy + 18}", "none", 2)
    return g


def face(spec, expr="smile"):
    s = spec; iris = s["eye"]; g = ""
    ex = s.get("sleepy") and expr == "smile" and "sleepy" or expr
    eyeExpr = {"smile": "open", "sleepy": "sleepy", "spark": "spark", "trouble": "trouble", "happy": "happy"}[ex]
    g += eye(223, 168, iris, eyeExpr, -1) + eye(289, 168, iris, eyeExpr, 1)
    # 눈썹
    if expr == "trouble":
        g += P("M200,128 Q214,124 236,134", "none", 3) + P("M312,128 Q298,124 276,134", "none", 3)
    elif expr == "spark":
        g += P("M200,130 Q216,122 238,128", "none", 3) + P("M312,130 Q296,122 274,128", "none", 3)
    else:
        g += P("M202,130 Q218,122 238,128", "none", 2.8) + P("M310,130 Q294,122 274,128", "none", 2.8)
    # 볼터치
    for x in (208, 304):
        g += f'<ellipse cx="{x}" cy="{196}" rx="15" ry="7" fill="#FF8FA3" opacity="{.55 if expr == "trouble" else .42}"/>'
        g += P(f"M{x - 8},{198} l4,-6 M{x - 1},{198} l4,-6 M{x + 6},{198} l4,-6", "none", 1.6, "#E86A82")
    g += P("M254,196 l3,2", "none", 2.2, "#D99A8A")
    if expr == "smile":
        g += P("M244,212 Q256,222 268,212", "none", 2.8)
    elif expr in ("spark", "happy"):
        g += P("M242,208 Q256,232 270,208 Z", "#C2485C", 2.6) + P("M248,220 Q256,226 264,220", "none", 0, "") + '<path d="M248,221 Q256,227 264,221 Q256,216 248,221 Z" fill="#FF8FA3"/>'
    elif expr == "trouble":
        g += P("M242,216 Q248,210 254,216 Q260,222 266,214", "none", 2.6)
        g += P("M330,118 C340,134 342,146 334,152 C326,156 320,146 322,138 Z", "#9ED4F5", 2.2)
    return g


# ------------------------------------------------------------------ 캐릭터별 디자인
# 공통 키: style(long/twin/bob/buns/pony/drill), hair, eye, top, skirt, sleeve, legs, shoes, key(대표 색), bg
CHARS = {
    "robot": dict(style="bob", hair="#F1F1EE", eye="#6B7785", sleepy=True, top="#8A949C", skirt="#8A949C", sleeve="#ECEFF2", sleeveLen="puff",
                  legs="#3E454B", sockTop=720, shoes="#3E454B", collar="#ECEFF2", hemBand="#ECEFF2", key="#6B7785", bg="#EEF1F4", flare=.9, hem=560),
    "ac": dict(style="long", hair="#F7F9FB", eye="#3D8BD9", top="#F4F7F9", skirt="#F4F7F9", sleeve="#F4F7F9", sleeveLen="long", cuff="#3D8BD9",
               shoes="#3D8BD9", belt="#3D8BD9", hemBand="#3D8BD9", key="#2FA9C9", bg="#E2F4F8", legs="#FFFFFF", sockTop=690, ahoge=True),
    "micro": dict(style="pony", hair="#F08A3E", eye="#C0561E", top="#6B7680", skirt="#6B7680", sleeve="#6B7680", sleeveLen="short",
                  shoes="#3A4046", hemBand="#FF9A3C", belt="#FF9A3C", legs="#FFFFFF", sockTop=730, sockBand="#FF9A3C", key="#F08A3E", bg="#FFF0E3", ahoge=True),
    "washer": dict(style="buns", hair="#7CC8E8", eye="#2F7FC0", top="#F4F7F9", skirt="#F4F7F9", sleeve="#F4F7F9", sleeveLen="puff",
                   shoes="#3D8BD9", hemBand="#7FC4F5", collar="#3D8BD9", legs="#FFFFFF", sockTop=740, key="#4AA8E0", bg="#E4F2FB", flare=1.1),
    "pc": dict(style="twin", hair="#2B3136", tip="#8EE3C8", eye="#3FAE9A", top="#3A4046", skirt="#2B3136", sleeve="#3A4046", sleeveLen="long", cuff="#8EE3C8",
               legs="#2B3136", sockTop=640, shoes="#8EE3C8", hemBand="#8EE3C8", key="#2FBF9A", bg="#E3F7F1", pleats=True),
    "gen": dict(style="pony", hair="#F2C14E", eye="#B7791F", top="#F2C14E", skirt="#3A4046", sleeve="#F2C14E", sleeveLen="short",
                shoes="#3A4046", legs="#3A4046", sockTop=760, belt="#1D2226", key="#E9A92E", bg="#FFF5DE", pleats=False, hem=540, flare=.8),
    "iron": dict(style="bob", hair="#8C6BD6", eye="#6A48C0", top="#FFFFFF", skirt="#3B3566", sleeve="#FFFFFF", sleeveLen="puff", collar="#FFFFFF",
                 legs="#FFFFFF", sockTop=700, shoes="#3B3566", key="#7A55C8", bg="#EEE8FA"),
    "stove": dict(style="long", hair="#2E2828", eye="#E0582E", top="#33383D", skirt="#33383D", sleeve="#33383D", sleeveLen="short", apron="#FFFFFF",
                  shoes="#1D2226", legs="#33383D", sockTop=760, key="#E0582E", bg="#FDEAE2", tipFlame=True),
    "boiler": dict(style="drill", hair="#D4875A", eye="#B5562E", top="#F4E9DC", skirt="#F4E9DC", sleeve="#F4E9DC", sleeveLen="long", cuff="#C46A3A",
                   shoes="#8E4A22", hemBand="#C46A3A", belt="#C46A3A", legs="#FFFFFF", sockTop=700, key="#C46A3A", bg="#FBEDE4", flare=1.15, hem=600),
}


# ------------------------------------------------------------------ 배경
def background(spec, w=W, h=H):
    k = spec["key"]; bg = spec["bg"]; gid = uid("bg")
    g = (f'<defs><linearGradient id="{gid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{light(bg, .4)}"/>'
         f'<stop offset="1" stop-color="{bg}"/></linearGradient></defs><rect width="{w}" height="{h}" fill="url(#{gid})"/>')
    # 붓 터치 같은 사선 띠(기존 일러스트 느낌)
    g += f'<path d="M-40,{h * .18} L{w + 40},{h * .02} L{w + 40},{h * .12} L-40,{h * .3} Z" fill="{k}" opacity=".10"/>'
    g += f'<path d="M-40,{h * .62} L{w + 40},{h * .48} L{w + 40},{h * .53} L-40,{h * .68} Z" fill="{k}" opacity=".08"/>'
    g += f'<circle cx="{w * .82}" cy="{h * .2}" r="{w * .32}" fill="#FFFFFF" opacity=".45"/>'
    return g


def ground_shadow():
    return '<ellipse cx="256" cy="1036" rx="120" ry="16" fill="rgba(40,30,60,.16)"/>'


def compose(cid, expr="smile", bg=True, acc=True):
    sys.path.insert(0, HERE); from accessories import ACC   # 가전별 소품(같은 폴더 accessories.py)
    s = CHARS[cid]; A = ACC.get(cid, {}) if acc else {}
    parts = []
    if bg: parts.append(background(s))
    parts.append(ground_shadow())
    parts.append(A.get("back", lambda s: "")(s))
    parts.append(hair_back(s))
    parts.append(legs(s))
    parts.append(arm_left(s))
    parts.append(skirt(s))
    parts.append(torso(s))
    parts.append(A.get("body", lambda s: "")(s))
    parts.append(P(NECK, SKIN) + clipped(NECK, P("M230,218 L282,218 L282,244 Q256,258 230,244 Z", "rgba(190,100,100,.26)", 0)) + P(NECK, "none", 3))
    parts.append(arm_right(s))
    parts.append(A.get("prop", lambda s: "")(s))
    parts.append(A.get("hand", lambda s: hand_right())(s))
    parts.append(P(FACE, SKIN) + clipped(FACE, P("M180,100 L332,100 L332,152 Q256,176 180,152 Z", "rgba(190,100,100,.14)", 0) + P("M300,120 C312,170 300,210 262,240 L330,240 L330,120 Z", "rgba(190,100,100,.10)", 0)) + P(FACE, "none", 3))
    parts.append(face(s, expr))
    parts.append(hair_front(s))
    parts.append(A.get("head", lambda s: "")(s))
    parts.append(A.get("fx", lambda s: "")(s))
    return "".join(parts)


def svg(inner, w=W, h=H, vb=None):
    vb = vb or f"0 0 {W} {H}"
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="{vb}">{inner}</svg>'




if __name__ == "__main__" and "--test" in sys.argv:
    os.makedirs(OUT, exist_ok=True)
    jobs = []
    for cid in [a for a in sys.argv[1:] if not a.startswith("--")] or list(CHARS):
        f = os.path.join(OUT, f"{cid}_full.svg"); open(f, "w").write(svg(compose(cid)))
        jobs.append({"svg": f, "out": os.path.join(OUT, f"{cid}_full.png"), "w": W, "h": H})
    json.dump(jobs, open(os.path.join(OUT, "jobs.json"), "w"))


# ------------------------------------------------------------------ 후면 · 측면 (3면도용, 작게 쓰이므로 단순하게)
def back_hair(s):
    c = s["hair"]; st = s["style"]; sc = shade(c, .2); g = ""
    cap = "M176,150 C168,84 206,36 256,36 C306,36 344,84 336,150 C340,200 330,250 316,280 L196,280 C182,250 172,200 176,150 Z"
    if st == "long":
        d = "M178,110 C150,240 150,440 168,640 L196,610 L220,652 L244,612 L268,656 L292,612 L316,650 L344,640 C362,440 362,240 334,110 Z"
        g += P(d, c) + clipped(d, "".join(P(f"M{x},140 C{x - 6},300 {x},480 {x + 4},630", "none", 2.2, sc) for x in (204, 236, 268, 300)) + P("M256,90 L380,90 L380,680 L256,680 Z", SH, 0)) + P(d, "none", 3)
    if st == "bob":
        d = "M178,100 C156,170 160,250 178,304 L334,304 C352,250 356,170 334,100 Z"
        g += P(d, c) + clipped(d, P("M256,90 L360,90 L360,320 L256,320 Z", SH, 0)) + P(d, "none", 3)
    g += P(cap, c, 3) + clipped(cap, P("M256,30 L350,30 L350,290 L256,290 Z", SH, 0) + "".join(P(f"M256,46 C{256 + k * 30},120 {256 + k * 44},200 {256 + k * 40},270", "none", 2.2, sc) for k in (-1.4, -.6, .6, 1.4))) + P(cap, "none", 3)
    return g


def compose_back(cid):
    s = CHARS[cid]
    sys.path.insert(0, HERE); from accessories import ACC
    A = ACC.get(cid, {})
    t = dict(s); t.pop("collar", None); t.pop("bow", None); t.pop("apron", None); t.pop("belt", None)
    parts = [ground_shadow(), legs(t), arm_left(t), mirror(arm_left(t)), skirt(t), torso(t), mirror(hand_right(194, 452)) if False else "",
             P(NECK, SKIN) + P(NECK, "none", 3), P(FACE, SKIN)]
    if s["style"] in ("twin", "drill", "pony", "buns"): parts.append(hair_back(s))
    parts.append(back_hair(s))
    if s.get("apron"): parts.append(P("M214,398 Q256,412 298,398 L300,412 Q256,426 212,412 Z", s["apron"], 2.4) + bow(256, 412, 14, s["apron"]))
    parts.append(A.get("back", lambda s: "")(s))
    if cid in ("robot", "gen", "washer", "micro", "pc", "stove", "boiler"): parts.append(A.get("head", lambda s: "")(s))
    return "".join(parts)


def compose_side(cid):
    s = CHARS[cid]; c = s["hair"]; st = s["style"]; sc = shade(c, .2)
    sock = s.get("legs", SKIN); shoe = s.get("shoes", "#3A4256")
    g = ground_shadow()
    # 뒷머리(등 쪽으로)
    if st == "long":
        g += P("M262,40 C190,40 158,100 164,172 C168,300 168,480 192,640 L214,610 L226,648 L250,600 C238,480 234,320 240,236 Z", c) + P("M262,40 C190,40 158,100 164,172 C168,300 168,480 192,640", "none", 3)
    elif st in ("twin", "pony"):
        g += P("M200,92 C150,130 134,240 136,360 C138,460 128,560 146,680 L168,650 C166,560 174,460 174,360 C174,260 190,170 226,120 Z", c, 3) + P("M196,96 C206,86 222,88 228,100 C220,110 204,112 196,104 Z", s.get("tie", shade(c, .3)), 2.2)
    elif st == "drill":
        for i in range(5):
            cy = 290 + i * 70; rx = 40 - i * 3
            g += f'<ellipse cx="{176 - i}" cy="{cy}" rx="{rx}" ry="40" fill="{c}" stroke="{LINE}" stroke-width="3"/>'
    elif st == "buns":
        g += f'<circle cx="214" cy="40" r="34" fill="{c}" stroke="{LINE}" stroke-width="3"/>'
    # 다리 두 개(뒤쪽 다리는 조금 어둡게)
    leg = ("M224,556 C214,620 214,690 222,748 C216,800 222,860 228,900 C230,940 230,966 230,980 L246,980 C248,950 252,900 254,860 "
           "C260,820 256,790 252,756 C260,700 262,620 256,556 Z")
    for dx, dark in ((-14, True), (8, False)):
        lg = P(leg, shade(SKIN, .08) if dark else SKIN)
        if sock != SKIN:
            top = s.get("sockTop", 700); sk = f"M210,{top} L262,{top} L262,990 L210,990 Z"
            lg += clipped(leg, P(sk, shade(sock, .12) if dark else sock, 0)) + P(leg, "none", 3)
        lg += P("M228,972 C222,988 222,1008 234,1012 L280,1012 C292,1010 292,996 280,988 C270,982 258,978 250,972 Z", shade(shoe, .15) if dark else shoe, 3)
        g += f'<g transform="translate({dx},0)">{lg}</g>'
    # 치마·몸통·팔
    fl = s.get("flare", 1); hem = s.get("hem", 572)
    sk = f"M224,392 L292,392 C{306 + 40 * fl:.0f},450 {316 + 50 * fl:.0f},520 {320 + 52 * fl:.0f},{hem} Q256,{hem + 22} {196 - 50 * fl:.0f},{hem} C{200 - 40 * fl:.0f},520 {210 - 20 * fl:.0f},450 224,392 Z"
    g += P(sk, s["skirt"]) + clipped(sk, P(f"M180,{hem - 30} L360,{hem - 30} L360,{hem + 40} L180,{hem + 40} Z", SH, 0) + (P(f"M160,{hem - 26} L360,{hem - 26} L360,{hem + 40} L160,{hem + 40} Z", s["hemBand"], 0) if s.get("hemBand") else "")) + P(sk, "none", 3)
    tor = "M232,272 C220,300 220,340 228,396 L290,396 C298,360 306,330 300,300 C296,282 284,272 268,268 Z"
    g += P(tor, s["top"]) + clipped(tor, P("M200,270 L246,270 L246,400 L200,400 Z", SH, 0)) + P(tor, "none", 3)
    if s.get("apron"): g += P("M276,404 C290,450 300,500 304,550 L292,552 C286,500 278,450 270,404 Z", s["apron"], 2.4)
    arm = "M246,280 C238,330 236,390 240,440 L258,442 C262,390 266,330 264,286 Z"
    g += P(arm, SKIN) + P("M236,436 C230,452 236,470 250,470 C262,470 266,456 262,442 Z", SKIN, 2.6)
    sl = s.get("sleeve", s["top"]); ln = s.get("sleeveLen", "short")
    if ln == "long": g += P("M244,276 C234,330 232,390 236,442 L262,444 C266,390 268,330 266,282 Z", sl) + P("M234,436 L264,438 L264,452 L234,450 Z", s.get("cuff", light(sl, .4)), 2.2)
    elif ln == "puff": g += P("M240,272 C226,284 228,316 246,326 C262,330 272,310 268,284 Z", sl)
    elif ln == "short": g += P("M242,272 C232,290 232,314 236,330 L264,330 C268,310 268,290 262,274 Z", sl)
    # 목·얼굴(오른쪽을 봄)
    g += P("M246,222 L246,272 L270,272 L268,222 Z", SKIN) + P("M246,222 L246,272 L270,272 L268,222 Z", "none", 3)
    head = ("M250,58 C298,58 326,90 330,140 C332,158 336,172 342,186 L332,192 C334,200 330,206 324,210 C326,220 318,230 306,236 "
            "C290,242 268,238 254,226 C220,218 196,190 194,150 C192,96 214,58 250,58 Z")
    g += P(head, SKIN) + clipped(head, P("M180,40 L330,40 L330,150 Q250,170 180,150 Z", "rgba(190,100,100,.12)", 0)) + P(head, "none", 3)
    g += P("M234,168 C226,160 222,176 228,186 C232,192 240,190 242,184", SKIN, 2.4)   # 귀
    iris = s["eye"]
    if s.get("sleepy"):
        g += P("M294,170 Q306,166 318,170", "none", 4)
    else:
        g += P("M296,160 C300,150 314,150 318,160 L318,182 C312,188 300,188 296,180 Z", "#FFFFFF", 0)
        g += f'<ellipse cx="310" cy="170" rx="7" ry="12" fill="{iris}"/><circle cx="308" cy="164" r="3" fill="#FFFFFF"/>'
        g += P("M294,160 C300,148 316,146 322,156 L326,152", "none", 4.2)
    g += P("M296,138 Q308,132 320,138", "none", 2.6) + f'<ellipse cx="298" cy="198" rx="12" ry="5" fill="#FF8FA3" opacity=".45"/>' + P("M318,214 Q324,216 326,212", "none", 2.4)
    # 앞머리·정수리(측면)
    cap = "M174,156 C166,84 210,38 262,40 C308,42 338,76 340,124 L330,150 L322,124 L312,160 L302,120 L290,150 L280,116 C254,118 236,140 232,176 C220,190 196,194 182,182 Z"
    g += P(cap, c, 3) + clipped(cap, P("M174,150 C200,90 240,70 280,70 L280,200 L174,200 Z", SH, 0) + P("M206,90 C230,70 290,70 316,90", "none", 5, light(c, .55))) + P(cap, "none", 3)
    if st == "bob": g += P("M180,150 C176,210 184,260 196,300 L226,300 C224,250 226,200 232,176 Z", c, 3)
    elif st != "buns": g += P("M232,176 C226,230 228,280 236,330 L250,326 C246,280 246,230 248,190 Z", c, 2.8)
    return g


def faces_svg(cid, expr):
    s = CHARS[cid]
    inner = f'<rect x="0" y="0" width="{W}" height="{H}" fill="{light(s["bg"], .3)}"/>' + compose(cid, expr, bg=False)
    return svg(inner, 230, 246, "128 0 256 274")


def turn_svg(cid):
    s = CHARS[cid]
    bgc = light(s["bg"], .45)
    inner = f'<rect width="500" height="308" fill="#FFFFFF"/><rect width="500" height="308" fill="{bgc}"/>'
    sc = 0.262
    for i, (lab, body) in enumerate((("정면", compose(cid, "smile", bg=False)), ("측면", compose_side(cid)), ("후면", compose_back(cid)))):
        x0 = 30 + i * 155
        inner += f'<g transform="translate({x0},4) scale({sc})">{body}</g>'
        inner += f'<text x="{x0 + 256 * sc:.0f}" y="300" text-anchor="middle" font-size="15" font-weight="700" font-family="Do Hyeon, Apple SD Gothic Neo, Noto Sans KR, sans-serif" fill="#3B2A33">{lab}</text>'
    return svg(inner, 500, 308, "0 0 500 308")


# ------------------------------------------------------------------ 주요 요소(원래 가전 + 소품 2개)
def appliance(cid):
    """200×200 안에 그린 원래 가전"""
    L = LINE
    if cid == "robot":
        return ('<ellipse cx="100" cy="112" rx="84" ry="70" fill="#3E454B" stroke="#3B2A33" stroke-width="3"/>'
                '<ellipse cx="100" cy="104" rx="80" ry="64" fill="#ECEFF2" stroke="#3B2A33" stroke-width="2.6"/><ellipse cx="100" cy="98" rx="56" ry="40" fill="#DCE2E6"/>'
                '<circle cx="132" cy="80" r="7" fill="#3FAE7A" stroke="#3B2A33" stroke-width="2"/><path d="M40,120 Q100,150 160,120" stroke="#9AAEBD" stroke-width="3" fill="none"/>')
    if cid == "ac":
        return ('<rect x="14" y="58" width="172" height="74" rx="14" fill="#F4F7F9" stroke="#3B2A33" stroke-width="3"/><rect x="14" y="112" width="172" height="20" rx="8" fill="#3D8BD9" stroke="#3B2A33" stroke-width="2.4"/>'
                '<path d="M32,84 L168,84" stroke="#B7C6D1" stroke-width="3"/><circle cx="168" cy="98" r="5" fill="#3FAE7A"/>'
                '<path d="M50,140 q6,16 0,30 M100,140 q6,16 0,30 M150,140 q6,16 0,30" stroke="#9ED0EC" stroke-width="4" fill="none" stroke-linecap="round"/>')
    if cid == "micro":
        return ('<rect x="14" y="50" width="172" height="110" rx="12" fill="#F1F3F4" stroke="#3B2A33" stroke-width="3"/><rect x="26" y="62" width="110" height="86" rx="8" fill="#2E3A40" stroke="#3B2A33" stroke-width="2.4"/>'
                '<ellipse cx="81" cy="132" rx="40" ry="8" fill="#5D6871"/><rect x="146" y="64" width="30" height="16" rx="3" fill="#FF9A3C"/><circle cx="161" cy="102" r="9" fill="#9AAEBD" stroke="#3B2A33" stroke-width="2"/><circle cx="161" cy="132" r="9" fill="#9AAEBD" stroke="#3B2A33" stroke-width="2"/>')
    if cid == "washer":
        return ('<rect x="34" y="20" width="132" height="164" rx="14" fill="#F4F7F9" stroke="#3B2A33" stroke-width="3"/><rect x="34" y="20" width="132" height="34" rx="12" fill="#DCE2E6" stroke="#3B2A33" stroke-width="2.4"/>'
                '<circle cx="146" cy="37" r="7" fill="#FF9A3C"/><circle cx="100" cy="118" r="50" fill="#9AAEBD" stroke="#3B2A33" stroke-width="3"/><circle cx="100" cy="118" r="38" fill="#BFE0F2"/>'
                '<path d="M78,110 A24,24 0 0 1 118,96" stroke="#3D8BD9" stroke-width="5" fill="none"/><path d="M122,128 A24,24 0 0 1 82,140" stroke="#3D8BD9" stroke-width="5" fill="none"/>')
    if cid == "pc":
        return ('<rect x="20" y="30" width="160" height="106" rx="10" fill="#2B3136" stroke="#3B2A33" stroke-width="3"/><rect x="30" y="40" width="140" height="84" rx="4" fill="#8EE3C8"/>'
                '<rect x="62" y="70" width="12" height="12" fill="#1D2226"/><rect x="126" y="70" width="12" height="12" fill="#1D2226"/><rect x="88" y="96" width="24" height="6" fill="#1D2226"/>'
                '<rect x="90" y="136" width="20" height="26" fill="#3A4046" stroke="#3B2A33" stroke-width="2.4"/><rect x="60" y="160" width="80" height="12" rx="6" fill="#3A4046" stroke="#3B2A33" stroke-width="2.4"/>')
    if cid == "gen":
        return ('<rect x="24" y="60" width="152" height="100" rx="14" fill="#F2C14E" stroke="#3B2A33" stroke-width="3"/><rect x="44" y="46" width="112" height="22" rx="8" fill="#3A4046" stroke="#3B2A33" stroke-width="2.4"/>'
                '<path d="M104,74 L80,112 L98,112 L90,146 L122,100 L104,100 Z" fill="#1D2226"/><circle cx="50" cy="164" r="12" fill="#1D2226"/><circle cx="150" cy="164" r="12" fill="#1D2226"/>')
    if cid == "iron":
        return ('<path d="M20,150 C18,96 60,62 112,62 L170,62 C186,64 194,90 186,120 L178,150 Z" fill="#E9E4F5" stroke="#3B2A33" stroke-width="3"/><path d="M14,146 L182,146 L178,166 L14,166 Z" fill="#B8BEC6" stroke="#3B2A33" stroke-width="2.6"/>'
                '<path d="M70,64 C66,30 84,20 112,20 L148,20 C162,20 168,32 168,46 L168,64" stroke="#3B2A33" stroke-width="14" fill="none"/><path d="M70,64 C66,30 84,20 112,20 L148,20 C162,20 168,32 168,46 L168,64" stroke="#8C6BD6" stroke-width="9" fill="none"/>'
                '<circle cx="110" cy="104" r="9" fill="#8C6BD6" stroke="#3B2A33" stroke-width="2"/>')
    if cid == "stove":
        return ('<rect x="12" y="70" width="176" height="90" rx="10" fill="#3A3F45" stroke="#3B2A33" stroke-width="3"/><rect x="8" y="62" width="184" height="16" rx="6" fill="#B8BEC6" stroke="#3B2A33" stroke-width="2.4"/>'
                '<circle cx="62" cy="70" r="26" fill="#1D2226"/><circle cx="138" cy="70" r="26" fill="#1D2226"/>'
                + "".join(f'<circle cx="{cx + math.cos(i / 8 * 6.283) * 20:.1f}" cy="{70 + math.sin(i / 8 * 6.283) * 8:.1f}" r="3.5" fill="#4A8FE0"/>' for cx in (62, 138) for i in range(8))
                + '<circle cx="50" cy="132" r="9" fill="#F2C14E" stroke="#3B2A33" stroke-width="2"/><circle cx="100" cy="132" r="9" fill="#F2C14E" stroke="#3B2A33" stroke-width="2"/><circle cx="150" cy="132" r="9" fill="#F2C14E" stroke="#3B2A33" stroke-width="2"/>')
    if cid == "boiler":
        return ('<rect x="46" y="16" width="108" height="150" rx="14" fill="#F4E9DC" stroke="#3B2A33" stroke-width="3"/><circle cx="100" cy="70" r="24" fill="#FFFFFF" stroke="#C46A3A" stroke-width="4"/>'
                '<path d="M100,70 L112,58" stroke="#1D2226" stroke-width="3"/><circle cx="130" cy="130" r="5" fill="#3FAE7A"/>'
                '<path d="M70,166 L70,190 M100,166 L100,190 M130,166 L130,190" stroke="#C46A3A" stroke-width="9"/>')
    return ""


KEY_ITEMS = {   # 주요 요소 칸 3개(unit_profile motif 순서와 같게): ("app") 원래 가전 / (함수 이름, 잘라 올 영역 x, y, w, h) 전신 좌표
    "robot": [("app",), ("prop", 318, 280, 84, 84), ("head", 160, 8, 196, 110)],
    "ac": [("back", 40, 262, 186, 200), ("app",), ("prop", 292, 372, 70, 120)],
    "micro": [("prop", 280, 408, 132, 56), ("app",), ("head", 176, 70, 160, 50)],
    "washer": [("app",), ("prop", 312, 300, 76, 160), ("head", 224, 0, 64, 60)],
    "pc": [("head", 150, 14, 212, 210), ("prop", 336, 262, 146, 186), ("app",)],
    "gen": [("back", 294, 256, 116, 186), ("head", 158, 10, 196, 124), ("prop", 292, 412, 146, 296)],
    "iron": [("prop", 254, 428, 192, 152), ("fx", 210, 548, 80, 80), ("body", 226, 278, 60, 56)],
    "stove": [("head", 166, 20, 180, 104), ("hair", 120, 430, 280, 250), ("prop", 300, 280, 172, 180)],
    "boiler": [("back", 36, 252, 196, 316), ("body", 232, 296, 48, 48), ("hair", 108, 236, 100, 400)],
}


def keys_svg(cid, motif):
    sys.path.insert(0, HERE); from accessories import ACC
    s = CHARS[cid]; A = ACC[cid]
    labels = [m.strip() for m in motif.split("·")]
    inner = f'<rect width="470" height="228" fill="{light(s["bg"], .45)}"/>'
    slots = KEY_ITEMS[cid]
    for i, it in enumerate(slots[:3]):
        cx = 78 + i * 157
        inner += f'<rect x="{cx - 70}" y="12" width="140" height="170" rx="16" fill="{mix(s["bg"], s["key"], .12)}" stroke="{mix(s["bg"], s["key"], .3)}" stroke-width="1.5"/>'
        if it[0] == "app":
            inner += f'<g transform="translate({cx - 62},30) scale(.62)">{appliance(cid)}</g>'
        else:
            fn, x, y, w, h = it; k = min(128 / w, 150 / h)
            body = hair_back(s) if fn == "hair" else A[fn](s)
            cid2 = uid("kc")
            inner += (f'<clipPath id="{cid2}"><rect x="{x}" y="{y}" width="{w}" height="{h}"/></clipPath>'
                      f'<g transform="translate({cx - w * k / 2:.1f},{97 - h * k / 2:.1f}) scale({k:.3f}) translate({-x},{-y})"><g clip-path="url(#{cid2})">{body}</g></g>')
        lab = labels[i] if i < len(labels) else ""
        inner += f'<text x="{cx}" y="210" text-anchor="middle" font-size="15" font-weight="700" font-family="Do Hyeon, Apple SD Gothic Neo, Noto Sans KR, sans-serif" fill="#3B2A33">{lab}</text>'
    return svg(inner, 470, 228, "0 0 470 228")


def build(ids, webp=False):
    import re
    prof = {}
    src = open(os.path.join(HERE, "..", "gen_tables.py"), encoding="utf-8").read()
    for m in re.finditer(r'\("(\w+)", "[^"]*", "#\w+", "#\w+", "[^"]*",\s*"[^"]*",\s*"([^"]*)"\)', src): prof[m.group(1)] = m.group(2)
    os.makedirs(OUT, exist_ok=True); jobs = []
    for cid in ids:
        files = {"full": (svg(compose(cid)), W, H), "turn": (turn_svg(cid), 500, 308), "keys": (keys_svg(cid, prof.get(cid, "")), 470, 228)}
        for i, e in enumerate(("smile", "spark", "trouble")): files[f"face{i + 1}"] = (faces_svg(cid, e), 230, 246)
        for k, (sv, w, h) in files.items():
            f = os.path.join(OUT, f"{cid}_{k}.svg"); open(f, "w", encoding="utf-8").write(sv)
            jobs.append({"svg": f, "out": f[:-4] + ".png", "w": w, "h": h})
    json.dump(jobs, open(os.path.join(OUT, "jobs.json"), "w"))
    return jobs


def to_webp(ids):
    from PIL import Image
    for cid in ids:
        for k in ("full", "turn", "keys", "face1", "face2", "face3"):
            im = Image.open(os.path.join(OUT, f"{cid}_{k}.png")).convert("RGB")
            im.save(os.path.join(ART, f"{cid}_{k}.webp"), "WEBP", quality=88, method=6)


if __name__ == "__main__" and "--test" not in sys.argv:
    ids = [a for a in sys.argv[1:] if not a.startswith("--")] or list(CHARS)
    if "--webp" in sys.argv: to_webp(ids); print("webp 저장:", ", ".join(ids))
    else: build(ids); print("SVG 생성 →", OUT, "/ 다음: node tools/art/render.mjs tools/art/out/jobs.json")
