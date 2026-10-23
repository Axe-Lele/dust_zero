"""10.18-a 색 바꾸기: 지정한 색상 범위(HSV)의 화소만 다른 색상으로 돌린다. 명도·채도는 그대로.
    python recolor.py <그림...> --from 75,100 --to 138 [--minsat 25]
OpenCV 색상값(0~180). 예) 컴퓨터: 청록(75~100) → 보라(138). 특정 캐릭터를 떠올리게 하는 '검은 트윈테일 + 청록 포인트' 조합을 피하려고 씀.
범위 가장자리 10은 부드럽게 섞어 띠가 생기지 않게 한다.
"""
import sys, cv2, numpy as np
from PIL import Image
args = sys.argv[1:]
def opt(k, d=None):
    if k in args: i = args.index(k); v = args[i + 1]; del args[i:i + 2]; return v
    return d
h0, h1 = map(float, opt('--from').split(',')); to = float(opt('--to')); minsat = float(opt('--minsat', 25))
for p in args:
    im = Image.open(p); rgb = np.asarray(im.convert('RGB'))
    hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV).astype(np.float32)
    H, S = hsv[..., 0], hsv[..., 1]
    mid = (h0 + h1) / 2; half = (h1 - h0) / 2
    d = np.abs(H - mid); w = np.clip((half + 10 - d) / 10, 0, 1) * np.clip((S - minsat) / 15, 0, 1)
    nh = (H + (to - mid)) % 180
    # 색상 섞기(원 위에서): 가중치만큼 새 색상으로
    ang0 = H / 90 * np.pi; ang1 = nh / 90 * np.pi
    x = np.cos(ang0) * (1 - w) + np.cos(ang1) * w; y = np.sin(ang0) * (1 - w) + np.sin(ang1) * w
    hsv[..., 0] = (np.arctan2(y, x) / np.pi * 90) % 180
    out = cv2.cvtColor(hsv.astype(np.uint8), cv2.COLOR_HSV2RGB)
    Image.fromarray(out).save(p, 'WEBP', quality=90, method=6); print('recolor', p)
