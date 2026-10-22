"""가전별 소품·장식(일러스트). gen_art.py 가 불러 쓴다. 좌표는 전신(512×1080) 기준, 오른손 ≈ (318,452)"""
import math
from gen_art import P, mirror, clipped, shade, light, mix, LINE, SKIN, SH, SH2, bow, uid


def spinner(x, y, r, col, hub="#3FAE7A", n=3, rot=0):
    g = ""
    for i in range(n):
        a = rot + i * 2 * math.pi / n
        x2, y2 = x + math.cos(a) * r, y + math.sin(a) * r
        g += P(f"M{x},{y} L{x2:.1f},{y2:.1f}", "none", 3.2, col)
        for k in (-1, 0, 1):
            b = a + k * .25
            g += P(f"M{x2:.1f},{y2:.1f} l{math.cos(b) * r * .35:.1f},{math.sin(b) * r * .35:.1f}", "none", 1.6, col)
    return g + f'<circle cx="{x}" cy="{y}" r="{r * .28:.1f}" fill="{hub}" stroke="{LINE}" stroke-width="2"/>'


def steam(x, y, s, op=.85):
    g = ""
    for dx, dy, r in ((0, 0, .5), (.45, -.18, .4), (-.42, -.12, .38), (.12, -.5, .36), (-.2, -.42, .3)):
        g += f'<circle cx="{x + dx * s:.1f}" cy="{y + dy * s:.1f}" r="{r * s:.1f}" fill="#FFFFFF" opacity="{op}" stroke="#C9D3DA" stroke-width="1.2"/>'
    return g


def snow(x, y, r, col="#FFFFFF"):
    g = ""
    for i in range(3):
        a = i * math.pi / 3
        g += P(f"M{x - math.cos(a) * r:.1f},{y - math.sin(a) * r:.1f} L{x + math.cos(a) * r:.1f},{y + math.sin(a) * r:.1f}", "none", 2.2, col)
    return g


def bubble(x, y, r):
    return (f'<circle cx="{x}" cy="{y}" r="{r}" fill="rgba(190,230,250,.25)" stroke="#7FC4F5" stroke-width="2"/>'
            f'<path d="M{x - r * .5:.1f},{y - r * .2:.1f} A{r * .55:.1f},{r * .55:.1f} 0 0 1 {x - r * .1:.1f},{y - r * .55:.1f}" stroke="#FFFFFF" stroke-width="2" fill="none"/>')


def bolt(x, y, s, col="#1D2226"):
    return P(f"M{x + .1 * s:.1f},{y - .5 * s:.1f} L{x - .25 * s:.1f},{y + .05 * s:.1f} L{x + .02 * s:.1f},{y + .05 * s:.1f} L{x - .12 * s:.1f},{y + .5 * s:.1f} L{x + .28 * s:.1f},{y - .1 * s:.1f} L{x + .02 * s:.1f},{y - .1 * s:.1f} Z", col, 2)


def flame(x, y, h, col="#F0582A", inner="#FFB24A"):
    return (P(f"M{x - h * .32:.1f},{y} C{x - h * .4:.1f},{y - h * .55:.1f} {x - h * .05:.1f},{y - h * .7:.1f} {x},{y - h} C{x + h * .12:.1f},{y - h * .6:.1f} {x + h * .42:.1f},{y - h * .5:.1f} {x + h * .32:.1f},{y} Q{x},{y + h * .18:.1f} {x - h * .32:.1f},{y} Z", col, 2)
            + P(f"M{x - h * .15:.1f},{y - h * .02:.1f} C{x - h * .2:.1f},{y - h * .3:.1f} {x},{y - h * .4:.1f} {x + h * .02:.1f},{y - h * .58:.1f} C{x + h * .1:.1f},{y - h * .32:.1f} {x + h * .22:.1f},{y - h * .26:.1f} {x + h * .15:.1f},{y - h * .02:.1f} Z", inner, 0))


# ---------------------------------------------------------------- 로봇청소기
def robot_head(s):
    g = P("M168,54 C168,30 210,16 256,16 C302,16 344,30 344,54 C344,72 302,84 256,84 C210,84 168,72 168,54 Z", "#3E454B", 3)
    g += P("M176,48 C176,30 214,20 256,20 C298,20 336,30 336,48 C336,62 298,72 256,72 C214,72 176,62 176,48 Z", "#ECEFF2", 2.6)
    g += P("M196,46 C210,36 300,36 316,46", "none", 3, "#FFFFFF")
    g += P("M206,56 Q256,66 306,56", "none", 2, "#B8C2CA")
    g += '<circle cx="296" cy="42" r="6" fill="#3FAE7A" stroke="#1D2226" stroke-width="2"/><circle cx="294" cy="40" r="2" fill="#C8F5DC"/>'
    g += spinner(176, 86, 20, "#5D6871", "#3FAE7A", 3, .4) + spinner(336, 86, 20, "#5D6871", "#3FAE7A", 3, 1.2)
    return g


def robot_body(s):
    g = '<circle cx="256" cy="344" r="22" fill="#ECEFF2" stroke="#3B2A33" stroke-width="2.6"/><circle cx="256" cy="344" r="13" fill="#3E454B"/><circle cx="256" cy="344" r="5" fill="#3FAE7A"/>'
    g += P("M206,404 Q256,418 306,404", "none", 4, "#3E454B")
    return g


def robot_prop(s):
    g = P("M318,452 L354,330", "none", 7, "#3E454B") + P("M318,452 L354,330", "none", 3, "#8A949C")
    g += f'<circle cx="358" cy="318" r="30" fill="rgba(236,239,242,.6)" stroke="#5D6871" stroke-width="2.6"/>' + spinner(358, 318, 26, "#3E454B", "#ECEFF2", 3, .2)
    return g


def robot_fx(s):
    g = ""
    for (x, y, r) in ((90, 640, 8), (420, 700, 6), (110, 860, 5), (400, 900, 7), (70, 420, 5)):
        g += f'<circle cx="{x}" cy="{y}" r="{r}" fill="#B3B6B9" opacity=".7"/>'
    g += P("M70,980 Q140,960 200,990", "none", 3, "rgba(93,104,113,.35)") + P("M320,990 Q380,960 450,980", "none", 3, "rgba(93,104,113,.35)")
    return g


# ---------------------------------------------------------------- 에어컨
def ac_back(s):
    g = ""
    for side in (0, 1):
        k = ""
        for i in range(4):
            y = 300 + i * 46; ang = -8 - i * 5
            d = f"M220,{y} L60,{y - 30 + i * 4} Q46,{y - 14 + i * 4} 58,{y + 2 + i * 4} L220,{y + 26} Z"
            k += P(d, "#E3EAF0", 2.6) + P(f"M210,{y + 14} L70,{y - 10 + i * 4}", "none", 2, "#B8C6D2") + P(f"M60,{y - 30 + i * 4} Q46,{y - 14 + i * 4} 58,{y + 2 + i * 4}", "none", 3, "#3D8BD9")
        g += k if side == 0 else mirror(k)
    return g


def ac_head(s):
    return P("M306,92 C316,82 336,84 340,96 C334,108 314,110 306,100 Z", "#3D8BD9", 2.4) + snow(322, 96, 7, "#FFFFFF")


def ac_body(s):
    return snow(256, 336, 14, "#3D8BD9") + '<circle cx="256" cy="336" r="4" fill="#3D8BD9"/>'


def ac_prop(s):
    g = P("M300,470 L322,388 Q326,378 336,380 L350,384 Q358,388 356,398 L334,478 Q330,488 320,486 L306,482 Q298,480 300,470 Z", "#F4F7F9", 2.6)
    g += P("M318,396 L344,402 L338,424 L312,418 Z", "#BFE3FA", 2) + '<text x="318" y="418" font-size="13" font-family="monospace" fill="#2F6FA8" transform="rotate(15 328 412)">24°</text>'
    g += '<circle cx="320" cy="440" r="5" fill="#3D8BD9"/><circle cx="334" cy="444" r="5" fill="#E0524A"/><circle cx="316" cy="458" r="4" fill="#9AAEBD"/><circle cx="330" cy="462" r="4" fill="#9AAEBD"/>'
    return g


def ac_fx(s):
    g = ""
    for i, (x, y) in enumerate(((60, 300), (440, 260), (80, 720), (430, 640), (60, 520), (450, 860))):
        g += snow(x, y, 9 + (i % 3) * 3, "#9ED0EC")
    for y in (560, 640, 720):
        g += P(f"M380,{y} C420,{y - 20} 460,{y + 10} 500,{y - 10}", "none", 3, "rgba(61,139,217,.35)")
        g += P(f"M130,{y + 30} C90,{y + 10} 50,{y + 40} 12,{y + 20}", "none", 3, "rgba(61,139,217,.35)")
    return g


# ---------------------------------------------------------------- 전자레인지
def micro_head(s):
    g = P("M182,94 C200,74 312,74 330,94 L326,114 C306,100 206,100 186,114 Z", "#2E3A40", 2.8)
    g += P("M270,84 L310,86 L308,100 L268,98 Z", "#FF9A3C", 1.6) + '<text x="274" y="97" font-size="12" font-family="monospace" fill="#2E3A40">0:30</text>'
    return g


def micro_body(s):
    return P("M232,300 L280,300 L280,330 L232,330 Z", "#2E3A40", 2.4) + P("M236,304 L262,304 L262,326 L236,326 Z", "#FF9A3C", 0, extra='opacity=".85"') + '<circle cx="271" cy="310" r="3.5" fill="#9AAEBD"/><circle cx="271" cy="321" r="3.5" fill="#9AAEBD"/>'


def micro_prop(s):
    g = '<ellipse cx="346" cy="436" rx="62" ry="22" fill="rgba(240,250,255,.55)" stroke="#5D6871" stroke-width="2.6"/>'
    g += '<ellipse cx="346" cy="432" rx="44" ry="13" fill="none" stroke="#B8C6D2" stroke-width="2"/><path d="M300,428 Q346,420 392,428" stroke="#FFFFFF" stroke-width="3" fill="none"/>'
    g += P("M340,432 L352,432 L350,446 L342,446 Z", "#9AAEBD", 1.6)
    return g


def micro_fx(s):
    g = ""
    for x, y in ((70, 340), (440, 300), (90, 760), (430, 820)):
        g += f'<circle cx="{x}" cy="{y}" r="22" fill="#FFB050" opacity=".18"/><circle cx="{x}" cy="{y}" r="8" fill="#FF9A3C" opacity=".5"/>'
    g += '<text x="380" y="210" font-size="46" font-family="Do Hyeon, sans-serif" fill="#F08A3E" stroke="#3B2A33" stroke-width="2" transform="rotate(12 400 200)">띵!</text>'
    return g


# ---------------------------------------------------------------- 세탁기
def washer_head(s):
    g = '<circle cx="256" cy="28" r="26" fill="#9AAEBD" stroke="#3B2A33" stroke-width="2.6"/><circle cx="256" cy="28" r="18" fill="#BFE0F2" stroke="#3B2A33" stroke-width="2"/>'
    g += P("M244,24 A13,13 0 0 1 266,20", "none", 3, "#3D8BD9") + P("M268,34 A13,13 0 0 1 246,38", "none", 3, "#3D8BD9")
    return g


def washer_body(s):
    return P("M232,330 C232,312 280,312 280,330 C280,348 244,350 240,336", "none", 3, "#3D8BD9") + P("M220,470 C220,450 260,446 262,464 C264,480 236,482 234,470", "none", 3, "#7FC4F5") + P("M276,520 C276,504 306,500 308,514", "none", 3, "#7FC4F5")


def washer_prop(s):
    g = P("M318,452 L352,350", "none", 5, "#3D8BD9")
    g += '<circle cx="358" cy="330" r="22" fill="none" stroke="#3D8BD9" stroke-width="5"/><circle cx="358" cy="330" r="17" fill="rgba(190,230,250,.4)"/>'
    return g


def washer_fx(s):
    g = ""
    for x, y, r in ((400, 260, 22), (440, 200, 14), (380, 170, 10), (80, 360, 18), (60, 280, 11), (100, 620, 24), (430, 560, 18), (70, 820, 14), (450, 880, 20), (130, 940, 10)):
        g += bubble(x, y, r)
    return g


# ---------------------------------------------------------------- 컴퓨터
def pc_head(s):
    g = P("M180,108 C176,50 214,18 256,18 C298,18 336,50 332,108", "none", 10, "#1D2226") + P("M180,108 C176,50 214,18 256,18 C298,18 336,50 332,108", "none", 4, "#3A4046")
    for side in (0, 1):
        k = P("M170,112 C162,112 156,126 156,146 C156,166 162,180 172,180 L190,178 L190,114 Z", "#3A4046", 2.8) + '<ellipse cx="166" cy="146" rx="6" ry="18" fill="#8EE3C8"/>'
        g += k if side == 0 else mirror(k)
    g += P("M178,170 C184,200 204,214 228,214", "none", 3, "#1D2226") + '<circle cx="230" cy="214" r="5" fill="#8EE3C8" stroke="#1D2226" stroke-width="2"/>'
    return g


def pc_body(s):
    g = ""
    for d in ("M216,300 L236,300 L236,330 L250,330", "M296,300 L276,300 L276,344 L262,344", "M220,360 L240,360 L248,372", "M292,366 L270,366"):
        g += P(d, "none", 2.2, "#8EE3C8")
    for x, y in ((250, 330), (262, 344), (248, 372), (270, 366)):
        g += f'<circle cx="{x}" cy="{y}" r="3" fill="#8EE3C8"/>'
    return g


def pc_prop(s):
    g = P("M350,300 L470,272 L476,382 L356,410 Z", "rgba(142,227,200,.28)", 2.4, "#3FAE9A")
    for i in range(5):
        y = 304 + i * 18
        g += P(f"M364,{y + 6 - i * 1} L{420 + (i * 37) % 40},{y - 6 - i * 1}", "none", 3, "#2FBF9A")
    g += P("M356,410 L340,440", "none", 2, "rgba(63,174,154,.6)")
    return g


def pc_fx(s):
    g = ""
    for x, y in ((60, 260), (70, 600), (440, 760), (460, 520), (90, 900)):
        g += f'<rect x="{x}" y="{y}" width="14" height="14" fill="#8EE3C8" opacity=".55"/><rect x="{x + 20}" y="{y + 8}" width="8" height="8" fill="#8EE3C8" opacity=".35"/>'
    return g


# ---------------------------------------------------------------- 발전기
def gen_back(s):
    g = P("M300,280 L392,276 Q404,276 404,290 L404,420 Q404,432 392,432 L300,436 Z", "#F2C14E", 3)
    g += clipped("M300,280 L392,276 Q404,276 404,290 L404,420 Q404,432 392,432 L300,436 Z", P("M370,270 L420,270 L420,440 L370,440 Z", SH, 0))
    g += bolt(360, 356, 70, "#1D2226") + P("M318,264 L386,262 L386,280 L318,282 Z", "#3A4046", 2.4)
    return g


def gen_head(s):
    g = P("M176,96 C176,40 214,14 256,14 C298,14 336,40 336,96 Z", "#F2C14E", 3)
    g += clipped("M176,96 C176,40 214,14 256,14 C298,14 336,40 336,96 Z", P("M280,0 L340,0 L340,100 L280,100 Z", SH, 0))
    g += P("M246,16 L266,16 L266,96 L246,96 Z", "#FFFFFF", 2.2)
    g += P("M162,96 Q256,118 350,96 L348,108 Q256,130 164,108 Z", "#E0A82E", 2.8)
    return g


def gen_body(s):
    g = P("M222,272 L232,272 L238,400 L226,400 Z", "#F2C14E", 2.4) + P("M290,272 L280,272 L274,400 L286,400 Z", "#F2C14E", 2.4)
    g += '<rect x="234" y="330" width="44" height="40" rx="6" fill="#3A4046" stroke="#3B2A33" stroke-width="2.4"/>' + bolt(256, 350, 30, "#F2C14E")
    return g


def gen_prop(s):
    g = P("M318,452 C350,520 300,560 330,620 C360,680 420,640 430,700", "none", 6, "#1D2226")
    g += P("M300,430 L336,430 L336,470 L300,470 Z", "#ECEFF2", 2.6) + P("M308,418 L308,430 M328,418 L328,430", "none", 4, "#9AAEBD")
    return g


def gen_fx(s):
    g = ""
    for x, y, a in ((70, 300, 0), (440, 220, 1), (90, 700, 2), (440, 820, 3)):
        g += bolt(x, y, 40 + a * 4, "#F2C14E")
    return g


# ---------------------------------------------------------------- 다리미
def iron_head(s):
    g = bow(200, 84, 20, "#FFFFFF")
    for side in (0, 1):
        k = '<circle cx="194" cy="300" r="8" fill="#C9B6F2" stroke="#3B2A33" stroke-width="2"/>'
        g += k if side == 0 else mirror(k)
    g += steam(330, 52, 30, .8)
    return g


def iron_body(s):
    return bow(256, 290, 16, "#8C6BD6")


def iron_prop(s):
    # 큰 스팀 다리미: 바닥판이 아래를 향하게 들고 있다
    g = P("M268,560 C266,520 290,488 330,486 L414,486 C432,488 440,512 432,540 L424,560 Z", "#E9E4F5", 3)
    g += clipped("M268,560 C266,520 290,488 330,486 L414,486 C432,488 440,512 432,540 L424,560 Z", P("M380,480 L450,480 L450,570 L380,570 Z", SH, 0) + P("M300,500 C320,494 360,494 380,498", "none", 4, "#FFFFFF"))
    g += P("M262,556 L432,556 L428,574 L262,574 Z", "#B8BEC6", 2.6) + P("M280,566 L420,566", "none", 1.6, "#FFFFFF")
    g += P("M318,488 C314,452 330,436 360,436 L392,436 C404,436 410,446 410,460 L410,488", "none", 14, LINE)
    g += P("M318,488 C314,452 330,436 360,436 L392,436 C404,436 410,446 410,460 L410,488", "none", 9, "#8C6BD6")
    g += '<circle cx="356" cy="520" r="9" fill="#8C6BD6" stroke="#3B2A33" stroke-width="2"/>'
    return g


def iron_hand(s):
    from gen_art import hand_right
    return hand_right(334, 442)


def iron_fx(s):
    return steam(250, 590, 34, .8) + steam(450, 520, 26, .75) + steam(80, 360, 24, .5) + steam(440, 280, 20, .5)


# ---------------------------------------------------------------- 가스레인지
def stove_back(s):
    g = ""
    for x, y, h in ((120, 1000, 90), (400, 1000, 110), (70, 960, 60), (450, 960, 70)):
        g += flame(x, y, h)
    return g


def stove_head(s):
    g = P("M180,112 C176,56 214,26 256,26 C298,26 336,56 332,112", "none", 12, "#1D2226")
    for i in range(7):
        a = math.pi * (1.12 + i * .127)
        x, y = 256 + math.cos(a) * 80, 112 + math.sin(a) * 86
        g += f'<circle cx="{x:.1f}" cy="{y:.1f}" r="5" fill="#4A8FE0" stroke="#1D2226" stroke-width="1.5"/>'
    g += flame(318, 70, 40)
    return g


def stove_body(s):
    return P("M236,300 L276,300 L276,322 L236,322 Z", "#F4E9DC", 2.2) + '<circle cx="256" cy="311" r="6" fill="#E0582E"/>'


def stove_prop(s):
    g = P("M318,452 L380,380", "none", 9, "#5A4030") + P("M318,452 L380,380", "none", 5, "#7A5A44")
    g += '<ellipse cx="408" cy="348" rx="56" ry="40" transform="rotate(-38 408 348)" fill="#2B2F33" stroke="#3B2A33" stroke-width="3"/>'
    g += '<ellipse cx="410" cy="346" rx="40" ry="27" transform="rotate(-38 410 346)" fill="#454B51"/>'
    g += '<path d="M392,330 Q404,318 422,322" stroke="#8A9096" stroke-width="3" fill="none"/>'
    g += flame(410, 330, 36) + flame(392, 342, 24)
    return g


def stove_fx(s):
    g = ""
    for x, y in ((60, 300), (450, 240), (80, 560), (460, 640)):
        g += f'<circle cx="{x}" cy="{y}" r="4" fill="#FF8A3C" opacity=".8"/><circle cx="{x + 14}" cy="{y + 22}" r="3" fill="#FFB24A" opacity=".7"/>'
    return g


# ---------------------------------------------------------------- 보일러
def boiler_back(s):
    g = ""
    for side in (0, 1):
        k = P("M220,300 C150,250 70,260 54,340 C40,410 90,470 70,540", "none", 22, LINE)
        k += P("M220,300 C150,250 70,260 54,340 C40,410 90,470 70,540", "none", 15, "#C46A3A")
        k += P("M200,290 C150,262 90,270 74,330", "none", 4, "#E8A47A")
        for (x, y) in ((150, 268), (62, 360), (78, 470)):
            k += f'<circle cx="{x}" cy="{y}" r="12" fill="#A9552B" stroke="{LINE}" stroke-width="2.4"/>'
        k += '<circle cx="70" cy="548" r="14" fill="#A9552B" stroke="#3B2A33" stroke-width="2.6"/>'
        g += k if side == 0 else mirror(k)
    return g


def boiler_head(s):
    g = P("M206,62 C206,34 236,22 266,24 C296,26 314,42 312,64 Q260,76 206,62 Z", "#C46A3A", 2.8)
    g += P("M214,50 C230,40 262,36 290,40", "none", 3, "#E8A47A")
    g += '<circle cx="300" cy="46" r="9" fill="#F4E9DC" stroke="#3B2A33" stroke-width="2"/>' + P("M300,46 L305,41", "none", 2)
    return g


def boiler_body(s):
    g = '<circle cx="256" cy="320" r="20" fill="#C46A3A" stroke="#3B2A33" stroke-width="2.6"/><circle cx="256" cy="320" r="13" fill="#FFFFFF" stroke="#3B2A33" stroke-width="1.6"/>'
    g += P("M256,320 L263,312", "none", 2.2) + P("M248,328 L264,328", "none", 1.4, "#C46A3A")
    return g


def boiler_prop(s):
    g = '<circle cx="340" cy="424" r="34" fill="none" stroke="#3B2A33" stroke-width="10"/><circle cx="340" cy="424" r="34" fill="none" stroke="#C46A3A" stroke-width="6"/>'
    for i in range(4):
        a = i * math.pi / 2 + .4
        g += P(f"M340,424 L{340 + math.cos(a) * 32:.1f},{424 + math.sin(a) * 32:.1f}", "none", 5, "#A9552B")
    g += '<circle cx="340" cy="424" r="8" fill="#A9552B" stroke="#3B2A33" stroke-width="2"/>'
    return g


def boiler_fx(s):
    return steam(70, 560, 30, .75) + steam(442, 560, 30, .75) + steam(90, 240, 22, .55) + steam(430, 220, 22, .55)


ACC = {
    "robot": dict(head=robot_head, body=robot_body, prop=robot_prop, fx=robot_fx),
    "ac": dict(back=ac_back, head=ac_head, body=ac_body, prop=ac_prop, fx=ac_fx),
    "micro": dict(head=micro_head, body=micro_body, prop=micro_prop, fx=micro_fx),
    "washer": dict(head=washer_head, body=washer_body, prop=washer_prop, fx=washer_fx),
    "pc": dict(head=pc_head, body=pc_body, prop=pc_prop, fx=pc_fx),
    "gen": dict(back=gen_back, head=gen_head, body=gen_body, prop=gen_prop, fx=gen_fx),
    "iron": dict(head=iron_head, body=iron_body, prop=iron_prop, hand=iron_hand, fx=iron_fx),
    "stove": dict(back=stove_back, head=stove_head, body=stove_body, prop=stove_prop, fx=stove_fx),
    "boiler": dict(back=boiler_back, head=boiler_head, body=boiler_body, prop=boiler_prop, fx=boiler_fx),
}
