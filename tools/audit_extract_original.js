// 원본 화면의 상자를 전부 숫자로 뽑는다. Playwright 로 주입해 쓴다.
//
// 눈으로 비교하면 놓친다. 이번 세션에서 실제로 놓친 것들 — 전체 화면 vs 330px 카드,
// 퍼센트 vs 생명 수, 가로 배열 vs 세로 배열 — 은 전부 "보면 알 것 같은데 안 보이던"
// 종류다. 그래서 숫자로 뽑아 둔다.
//
// 돌려주는 것: 화면에 실제로 그려지는 요소마다
//   path   조상 체인으로 만든 안정적인 이름 (id 가 있으면 #id, 없으면 태그.클래스:순번)
//   box    CSS px 기준 x/y/w/h (요소 상자)
//   ink    글자가 실제로 찍히는 자리 — 비교는 이걸로 한다
//   style  배경·글자색·크기·굵기·모서리·여백·간격·정렬
//   text   글자 (앞 40자)
//
// 안 뽑는 것: 크기가 0이거나 display:none 인 것, 순수 장식용 빈 div 중 배경도 테두리도
// 없는 것. 그런 것까지 넣으면 목록이 수천 줄이 되고 아무도 안 본다.

// 기준 기기. 이걸 틀리면 **전부** 틀린다 — 390 으로 재고 모든 띠가 40px 넓다고
// 보고할 뻔했고, navigate 한 번에 뷰포트가 390 으로 되돌아가 좋은 수집본을 덮어쓰기도
// 했다. 그래서 틀리면 수집 자체를 거부한다. 조용히 틀린 숫자를 내놓는 것보다 낫다.
window.DZ_AUDIT_VIEWPORT = { w: 430, h: 932 };

window.DZ_AUDIT = function captureScreen(rootSelector) {
  const want = window.DZ_AUDIT_VIEWPORT;
  if (innerWidth !== want.w || innerHeight !== want.h) {
    return { error: "뷰포트가 " + innerWidth + "x" + innerHeight + " 입니다. "
      + want.w + "x" + want.h + " 로 맞춘 뒤 다시 하세요.", nodes: [] };
  }
  const root = document.querySelector(rootSelector);
  if (!root) return { error: rootSelector + " 를 찾지 못했습니다" };

  const seen = new Map();
  function pathOf(el) {
    if (el.id) return "#" + el.id;
    const parent = el.parentElement;
    const base = (parent && parent !== root ? pathOf(parent) + " > " : "")
      + el.tagName.toLowerCase()
      + (el.className && typeof el.className === "string"
        ? "." + el.className.trim().split(/\s+/).join(".") : "");
    const n = (seen.get(base) || 0) + 1;
    seen.set(base, n);
    return n > 1 ? base + ":" + n : base;
  }

  const out = [];
  const walk = (el) => {
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    const visible = s.display !== "none" && s.visibility !== "hidden"
      && r.width > 0.5 && r.height > 0.5;
    if (visible) {
      const ownText = [...el.childNodes]
        .filter(n => n.nodeType === 3).map(n => n.textContent.trim()).join(" ").trim();
      const decorated = s.backgroundColor !== "rgba(0, 0, 0, 0)"
        || s.borderTopWidth !== "0px" || ownText.length > 0 || el.tagName === "CANVAS"
        || el.tagName === "IMG" || el.tagName === "BUTTON" || el.tagName === "INPUT";
      if (decorated) {
        // 글자가 **실제로 찍히는** 자리. 상자가 아니다 — 원본의 <b> 는 아이콘까지
        // 감싸고 우리 라벨은 글자만 담아서, 상자끼리 비교하면 멀쩡한 화면이 전부
        // 어긋난 것으로 나온다. Range 로 글자 노드만 재야 공평하다.
        let ink = null;
        if (ownText.length > 0) {
          const range = document.createRange();
          for (const n of el.childNodes) {
            if (n.nodeType !== 3 || !n.textContent.trim()) continue;
            range.selectNodeContents(n);
            const q = range.getBoundingClientRect();
            if (q.width < 0.5 || q.height < 0.5) continue;
            ink = ink ? { x: Math.min(ink.x, q.x), y: Math.min(ink.y, q.y),
                          r: Math.max(ink.r, q.right), b: Math.max(ink.b, q.bottom) }
                      : { x: q.x, y: q.y, r: q.right, b: q.bottom };
          }
        }
        out.push({
          path: pathOf(el),
          ink: ink ? { x: +ink.x.toFixed(1), y: +ink.y.toFixed(1),
                       w: +(ink.r - ink.x).toFixed(1), h: +(ink.b - ink.y).toFixed(1) } : null,
          box: { x: +r.x.toFixed(1), y: +r.y.toFixed(1),
                 w: +r.width.toFixed(1), h: +r.height.toFixed(1) },
          style: {
            bg: s.backgroundColor, color: s.color,
            fs: parseFloat(s.fontSize), fw: s.fontWeight,
            radius: s.borderTopLeftRadius,
            pad: s.padding, gap: s.gap === "normal" ? "" : s.gap,
            border: s.borderTopWidth === "0px" ? "" : s.borderTopWidth + " " + s.borderTopColor,
            align: s.textAlign, disp: s.display,
          },
          text: ownText.slice(0, 40),
        });
      }
    }
    if (s.display !== "none") for (const c of el.children) walk(c);
  };
  walk(root);
  return { root: rootSelector, viewport: { w: innerWidth, h: innerHeight }, nodes: out };
};
