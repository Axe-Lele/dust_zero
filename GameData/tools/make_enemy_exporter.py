# -*- coding: utf-8 -*-
"""10.16-n 의 실제 drawEnemy 코드를 잘라내 적 스프라이트 내보내기 페이지를 만든다.

기존 export.html 은 drawEnemy 를 **손으로 베낀 옛 사본**이라 생김새가 3종(dust/grease/
mold)뿐이었다. 실제 10.16-n 은 11종(mite/hair/scale/rust/emperor/moth 추가)을 그리고
반지름도 enemy.json 의 radius 를 쓴다. 베끼지 말고 원본에서 잘라 쓴다.
"""
import io, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "GameData", "versions", "10.16-n", "index.html")
OUT = os.path.join(ROOT, "GameData", "spriteexport", "enemies.html")

NEEDED = ["rr", "circ", "ell", "eyes", "drawLook", "drawMarks", "drawEnemy", "hpBar"]


def grab(src, name):
    """`function name(` 부터 중괄호 짝이 맞는 곳까지를 그대로 잘라낸다."""
    m = re.search(r"^function %s\s*\(" % re.escape(name), src, re.M)
    if not m:
        raise SystemExit("원본에서 %s 를 찾지 못했습니다" % name)
    i = src.index("{", m.start())
    depth, j = 0, i
    while j < len(src):
        if src[j] == "{":
            depth += 1
        elif src[j] == "}":
            depth -= 1
            if depth == 0:
                return src[m.start():j + 1]
        j += 1
    raise SystemExit("%s 의 닫는 중괄호를 찾지 못했습니다" % name)


def main():
    src = io.open(SRC, encoding="utf-8").read()
    parts = [grab(src, n) for n in NEEDED]
    newlook = re.search(r"^const NEW_LOOK=.*$", src, re.M).group(0)

    rows = json.load(io.open(os.path.join(ROOT, "GameData", "versions", "10.16-n",
                                          "data", "enemy.json"), encoding="utf-8"))
    if isinstance(rows, dict):
        rows = rows.get("rows", [])
    defs = [{"id": r["id"], "look": r["look"], "r": r["radius"],
             "fly": int(r.get("fly") or 0), "boss": int(r.get("boss") or 0),
             "breaker": 0} for r in rows]

    page = PAGE % {
        "newlook": newlook,
        "draw": "\n".join(parts),
        "defs": json.dumps(defs, ensure_ascii=False),
    }
    io.open(OUT, "w", encoding="utf-8").write(page)
    print("wrote %s (%d defs, %d looks)" % (OUT, len(defs),
                                            len(set(d["look"] for d in defs))))


PAGE = """<!doctype html><meta charset="utf-8"><title>working</title>
<body style="background:#222"><div id=grid></div><script>
// 이 파일은 make_enemy_exporter.py 가 10.16-n index.html 에서 잘라내 생성한다. 직접 고치지 않는다.
let TS = 1;
const epos = e => ({x: e.x, y: e.y});
const segDir = () => ({x:0, y:1});
%(newlook)s
%(draw)s

// ---- 내보내기 ----
// 캔버스는 SPAN 타일만큼을 담는다. 유니티는 스프라이트를 정확히 SPAN 타일로 그린다.
const SPAN = 2.6;
const DEFS = %(defs)s;
const EXPORTS = {};
function render(def, px){
  const c = document.createElement('canvas'); c.width = c.height = px;
  const x = c.getContext('2d');
  TS = px / SPAN;
  x.translate(px/2, px/2);
  // 멈춰 있는 대표 자세: 흔들림/피격/빙결 없이, 체력은 가득(= hp 바 안 그림).
  drawEnemy(x, { def, x:0, y:0, d:0, pi:0, dir:{x:0,y:1}, off:0, seed:1.7,
                 hp:1, max:1, hitT:0, slowT:0, frozenT:0, stunT:0, marks:null }, 0);
  return c;
}
function bbox(c){
  const d = c.getContext('2d').getImageData(0,0,c.width,c.height).data;
  let x0=1e9,y0=1e9,x1=-1,y1=-1;
  for (let p=0;p<d.length;p+=4) if (d[p+3]>8){
    const i=(p/4)|0, px=i%%c.width, py=(i/c.width)|0;
    if(px<x0)x0=px; if(px>x1)x1=px; if(py<y0)y0=py; if(py>y1)y1=py; }
  return [x0,y0,x1,y1];
}
const REPORT = [];
for (const def of DEFS){
  const c = render(def, 256);
  const b = bbox(c);
  // 잘림 검사: 캔버스 가장자리에 불투명 픽셀이 닿으면 그림이 넘친 것이다.
  const clipped = b[0] <= 0 || b[1] <= 0 || b[2] >= 255 || b[3] >= 255;
  REPORT.push({ id: def.id, box: b, clipped });
  EXPORTS['enemy_' + def.id] = c.toDataURL('image/png');
  const f = document.createElement('figure');
  f.style.cssText = 'display:inline-block;margin:4px;color:#ddd;font:11px sans-serif;text-align:center';
  const img = new Image(); img.src = EXPORTS['enemy_' + def.id]; img.width = img.height = 96;
  img.style.outline = clipped ? '2px solid red' : '1px solid #555';
  f.appendChild(img);
  const cap = document.createElement('figcaption'); cap.textContent = def.id + (clipped ? ' CLIPPED' : '');
  f.appendChild(cap); document.getElementById('grid').appendChild(f);
}
window.DUSTZERO_SPRITES = EXPORTS;
window.DUSTZERO_REPORT = REPORT;
window.DUSTZERO_SPAN = SPAN;
const bad = REPORT.filter(r => r.clipped).length;
document.title = bad ? ('clipped:' + bad) : ('ready:' + REPORT.length);
</script></body>
"""

if __name__ == "__main__":
    main()
