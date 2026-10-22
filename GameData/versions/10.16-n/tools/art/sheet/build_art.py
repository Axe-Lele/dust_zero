# 일러스트 시트(1536×1024, 3×3 패널) → 가전 9종의 art 파일
#   <id>_full.webp (글자 블록 지우고 EDSR 2배 + 1.5배), _face1~3.webp (230×246), _turn.webp (500×308), _keys.webp (470×228)
# python3 build_art.py <sheet.png|webp> <출력 폴더> <unit_profile.json> <string_ko.json> [--no-full]
# 이 폴더에서 실행. 필요: pip install opencv-contrib-python-headless pillow, EDSR_x2.pb(github.com/Saafke/EDSR_Tensorflow models/)를 이 폴더에
# 좌표는 sheet_261016 기준. 다른 시트면 FULL_X/Y · FACE_X/Y · FIG_Y · KEY_Y · TURN_X 와 textmask.py 를 맞추세요
import sys, json, cv2, numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from textmask import TEXT
from cleantext import clean
sheet, outdir, profp, strp = sys.argv[1:5]
S = cv2.cvtColor(np.asarray(Image.open(sheet).convert('RGB')), cv2.COLOR_RGB2BGR)
STR = json.load(open(strp, encoding='utf-8')); PROF = {p['unitId']: p for p in json.load(open(profp, encoding='utf-8'))}
GRID = [['boiler', 'pc', 'washer'], ['stove', 'iron', 'micro'], ['ac', 'gen', 'robot']]
PANEL_O = [0, 512, 1024]; PANEL_OY = [0, 325, 677]          # 글자 블록 좌표 기준
FULL_X = [(8, 276), (520, 788), (1041, 1306)]; FULL_Y = [(8, 306), (326, 656), (687, 1008)]
RIGHT_O = [0, 512, 1032]                                     # 오른쪽(표정·3면도·소품) 기준
FACE_X = [(286, 341), (362, 417), (436, 491)]   # 액자 테두리 안쪽
FACE_Y = [(11, 74), (336, 399), (689, 754)]
FIG_Y = [(84, 171), (412, 513), (768, 867)]; KEY_Y = [(200, 272), (548, 622), (900, 972)]
STRIP_X = (285, 497); TURN_X = (303, 497)   # 3면도 칸은 왼쪽에 전신 그림이 비쳐 들어와 조금 더 안쪽부터
sr = cv2.dnn_superres.DnnSuperResImpl_create(); sr.readModel('EDSR_x2.pb'); sr.setModel('edsr', 2)
up = lambda im: sr.upsample(im)
FONT = '/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc'
def font(px): return ImageFont.truetype(FONT, px, index=1)  # KR
def pil(bgr): return Image.fromarray(cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB))
def save(img, name, q=90): img.save(f'{outdir}/{name}.webp', 'WEBP', quality=q, method=6)
def bg_of(img):  # 테두리 픽셀 중앙값 = 바탕색
    a = np.asarray(img).reshape(-1, 3) if False else np.concatenate([np.asarray(img)[0], np.asarray(img)[-1], np.asarray(img)[:, 0], np.asarray(img)[:, -1]])
    return tuple(int(v) for v in np.median(a, axis=0))
def strip(y, ro, labels, W, H, top, art_h, label_y, lab_px, color=(59, 53, 64), sx=STRIP_X):
    x0, x1 = sx[0] + ro, sx[1] + ro
    crop = S[y[0]:y[1], x0:x1]
    big = pil(cv2.resize(up(crop), None, fx=1.0, fy=1.0))
    bg = bg_of(big)
    scale = min(art_h / big.height, (W - 16) / big.width)   # 폭에 맞춤(잘림 방지)
    big = big.resize((round(big.width * scale), round(big.height * scale)), Image.LANCZOS)
    top = top + (art_h - big.height) // 2
    canvas = Image.new('RGB', (W, H), bg)
    # 가장자리를 부드럽게(바탕과 이음새 없애기)
    m = Image.new('L', big.size, 0); ImageDraw.Draw(m).rectangle([6, 6, big.width - 7, big.height - 7], fill=255); m = m.filter(ImageFilter.GaussianBlur(5))
    ox = (W - big.width) // 2; canvas.paste(big, (ox, top), m)
    d = ImageDraw.Draw(canvas); f = font(lab_px); cw = big.width / 3
    for i, t in enumerate(labels):
        tw = d.textlength(t, font=f); d.text((ox + cw * (i + .5) - tw / 2, label_y), t, font=f, fill=color)
    return canvas
for r in range(3):
    for c in range(3):
        t = GRID[r][c]
        # 전신: 글자 블록을 지우고(cleantext.clean) 잘라서 키운다. --no-full 이면 건너뜀
        if '--no-full' not in sys.argv:
            ox, oy = PANEL_O[c], PANEL_OY[r]
            reg = S[oy:oy + 330, ox:ox + 300].copy()
            fixed = clean(reg, t)   # 가장자리는 주변 질감, 안쪽은 바탕색으로 부드럽게
            S2 = S.copy(); S2[oy:oy + 330, ox:ox + 300] = fixed
            fx0, fx1 = FULL_X[c]; fy0, fy1 = FULL_Y[r]
            full = up(S2[fy0:fy1, fx0:fx1]); full = cv2.resize(full, None, fx=1.5, fy=1.5, interpolation=cv2.INTER_LANCZOS4)
            save(pil(full), f'{t}_full', 88)
        # 표정 3종
        ro = RIGHT_O[c]; fy = FACE_Y[r]
        for i, (a, b) in enumerate(FACE_X):
            f = up(S[fy[0]:fy[1], a + ro:b + ro]); save(pil(f).resize((230, 246), Image.LANCZOS), f'{t}_face{i + 1}')
        # 3면도 (글자는 새로 씀)
        save(strip(FIG_Y[r], ro, ['정면', '측면', '후면'], 500, 308, 10, 248, 266, 22, sx=TURN_X), f'{t}_turn')
        # 주요 요소 3종 (이름은 unit_profile 의 주요 요소 문구)
        keys = [k.strip() for k in STR[PROF[t]['motifKey']].split('·')]
        save(strip(KEY_Y[r], ro, keys, 470, 228, 8, 166, 186, 19), f'{t}_keys')
        print('done', t, flush=True)
