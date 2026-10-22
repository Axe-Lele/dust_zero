"""10.18-a 전신 그림 글자 지운 자리 다시 채우기 (art/<id>_full.webp 에 바로 적용)
cleantext.py 가 채운 자리(주변 질감 + 바탕색)는 업스케일을 거치며 가로 줄무늬·네모 경계가 생겼다.
이 스크립트는 그 자리를 '바탕색 장(場)'으로 덮고 가장자리를 길게 풀어 그림이 바탕으로 번져 사라지게 만든다(수채화 가장자리처럼).
    python refill.py <art 폴더> [가전ID ...]
    build_art.py 로 전신을 새로 만든 직후 한 번만 실행하세요(두 번 돌리면 가장자리가 한 번 더 번짐).
좌표: textmask.TEXT(패널 기준) → 전신 좌표 = (패널 원점 + 박스 - FULL 원점) × 3 (EDSR 2배 × 1.5배)
"""
import sys, cv2, numpy as np
from PIL import Image
from textmask import TEXT
GRID = [['boiler', 'pc', 'washer'], ['stove', 'iron', 'micro'], ['ac', 'gen', 'robot']]
PANEL_O = [0, 512, 1024]; PANEL_OY = [0, 325, 677]
FULL_X = [(8, 276), (520, 788), (1041, 1306)]; FULL_Y = [(8, 306), (326, 656), (687, 1008)]
SC = 3.0
MODE = {'gen': 'in'}   # 가전별 가장자리 방식(아래 refill 참고). 기본 'out'
def where(t):
    for r, row in enumerate(GRID):
        if t in row: return r, row.index(t)
def refill(img, t):
    r, c = where(t); H, W = img.shape[:2]
    m = np.zeros((H, W), np.uint8)
    for b in TEXT[t].values():
        x0 = int((PANEL_O[c] + b[0] - 2 - FULL_X[c][0]) * SC); y0 = int((PANEL_OY[r] + b[1] - 2 - FULL_Y[r][0]) * SC)
        x1 = int((PANEL_O[c] + b[2] + 2 - FULL_X[c][0]) * SC); y1 = int((PANEL_OY[r] + b[3] + 2 - FULL_Y[r][0]) * SC)
        cv2.rectangle(m, (max(0, x0), max(0, y0)), (min(W - 1, x1), min(H - 1, y1)), 255, -1)
    # cleantext 의 blob(σ6 → 0.35)을 전신 배율로: 지운 자리 전체 + 줄무늬가 번진 폭
    blob = (cv2.GaussianBlur(m.astype(np.float32) / 255, (0, 0), 6 * SC) > 0.25).astype(np.uint8)
    blob = cv2.dilate(blob, np.ones((25, 25), np.uint8))
    R = img.astype(np.float32)
    # 바탕색 장: 지운 자리 바깥 띠에서 밝고 채도 낮은(=바탕일 가능성이 큰) 화소만 모아 크게 흐림
    band = (cv2.dilate(blob, np.ones((161, 161), np.uint8)) > 0) & (blob == 0)
    hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV).astype(np.float32)
    lum = R.mean(axis=2); sat = hsv[..., 1]
    cand = band & (lum >= np.percentile(lum[band], 50)) & (sat <= np.percentile(sat[band], 60))
    # 그림 테두리(맨 위·맨 왼쪽 줄)는 거의 바탕이라 함께 씀
    edge = np.zeros_like(cand); edge[:6, :] = True; edge[:, :6] = True; cand |= edge & (blob == 0)
    w = cand.astype(np.float32)
    num = cv2.GaussianBlur(R * w[..., None], (0, 0), 70); den = cv2.GaussianBlur(w, (0, 0), 70)[..., None]
    med = np.median(R[cand], axis=0)
    field = np.where(den > 1e-3, num / np.maximum(den, 1e-3), med)
    # 종이 질감: 아주 약한 얼룩(바탕이 플라스틱처럼 납작해 보이지 않게)
    rng = np.random.default_rng(7)
    tex = cv2.GaussianBlur(rng.normal(0, 1, (H, W)).astype(np.float32), (0, 0), 9); tex = tex / (tex.std() + 1e-6) * 2.2
    field = field + tex[..., None]
    # 알파 두 방식(가전별 MODE):
    #  'out' 지운 자리 바깥으로 약 40px 길게 풀려서 잘린 머리카락 등이 바탕으로 번져 사라짐(대부분)
    #  'in'  지운 자리 안쪽 22px 에서만 넘어감. 지운 자리가 가방처럼 큰 소품을 가로지르는 경우(발전기) 바깥을 흐리면 뭉개져서
    dist_in = cv2.distanceTransform(blob, cv2.DIST_L2, 5)
    if MODE.get(t, 'out') == 'out':
        dist_out = cv2.distanceTransform(1 - blob, cv2.DIST_L2, 5)
        a = np.where(blob > 0, np.clip(0.75 + dist_in / 60, 0, 1), np.clip(0.75 - dist_out / 40, 0, 1))
        a = cv2.GaussianBlur(a.astype(np.float32), (0, 0), 6)
    else:
        a = cv2.GaussianBlur(np.clip(dist_in / 22, 0, 1).astype(np.float32), (0, 0), 5)
    a = a[..., None]
    out = R * (1 - a) + field * a
    return np.clip(out, 0, 255).astype(np.uint8)
if __name__ == '__main__':
    art = sys.argv[1]; ids = sys.argv[2:] or [t for row in GRID for t in row]
    for t in ids:
        src = f'{art}/{t}_full.webp'
        img = cv2.cvtColor(np.asarray(Image.open(src).convert('RGB')), cv2.COLOR_RGB2BGR)
        out = refill(img, t)
        Image.fromarray(cv2.cvtColor(out, cv2.COLOR_BGR2RGB)).save(src, 'WEBP', quality=88, method=6)
        print('refill', t, flush=True)
