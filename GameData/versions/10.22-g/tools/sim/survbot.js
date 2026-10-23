// 대청소 모드 자동 테스트 봇: 적에게서 멀어지고(거리 제곱 반비례), 가까운 먼지 조각은 줍고, 벽은 피하고, 날아오는 포자는 옆으로 비킨다.
// 레벨업 카드는 협동기 > 동료 합류 > 리더 무기 > 동료 강화 > 공용 강화 순서로 고른다
(function(){
const D=window.__DZ;
function rng(seed){ let s=seed>>>0||1; return ()=>{ s=(s*1664525+1013904223)>>>0; return s/4294967296; }; }
window.survRun=function(cfg){
  const realRandom=Math.random; Math.random=rng(cfg.seed||1);
  try {
  const UNITS=D.UNITS();
  const S={v:2,items:{},owned:{},deck:cfg.deck.slice(),best:999,stars:{},starClaimed:{},tut:{},tutOff:1,speed:1,idleBank:{coin:0,part:0,crystal:0},idleSince:0,guide:null,guideDone:{first:1,levelup:1},gear:{next:1,inv:[]},excl:cfg.excl||{},pass:{},surv:null};
  for (const t of new Set([cfg.leader,...cfg.deck])) S.owned[t]={lv:cfg.lv||1,star:cfg.star||0};
  D.S=S; window.__DZ_GEAR=cfg.gear?(t=>({atk:cfg.gear,heat:cfg.gear,sp:0})):null;
  D.startSurv(cfg.leader); const V=D.V; const A=V.A;
  const PRI={combo:100,join:50,weapon:40,up:30,passive:10};
  V.auto=V=>{ const L=V.leader; let mx=0,my=0;
    for (const e of V.enemies){ const dx=L.x-e.x, dy=L.y-e.y, d=Math.hypot(dx,dy)||.01; if (d<5){ const w=(e.def.boss?3:1)/(d*d); mx+=dx/d*w; my+=dy/d*w; } }
    for (const q of V.eshots){ const dx=L.x-q.x, dy=L.y-q.y, d=Math.hypot(dx,dy); if (d<3){ const vx=q.vx, vy=q.vy, vl=Math.hypot(vx,vy)||1; const px=-vy/vl, py=vx/vl, side=Math.sign(px*dx+py*dy)||1; mx+=px*side*2/d; my+=py*side*2/d; } }
    let g=null,gd=6; for (const q of V.gems){ const d=Math.hypot(q.x-L.x,q.y-L.y); if (d<gd){ gd=d; g=q; } }
    if (g){ mx+=(g.x-L.x)/gd*.35; my+=(g.y-L.y)/gd*.35; }
    const edge=2.5; if (L.x<edge) mx+=(edge-L.x)*.6; if (L.x>A-edge) mx-=(L.x-A+edge)*.6; if (L.y<edge) my+=(edge-L.y)*.6; if (L.y>A-edge) my-=(L.y-A+edge)*.6;
    const l=Math.hypot(mx,my); if (l<.05){ mx=A/2-L.x; my=A/2-L.y; const l2=Math.hypot(mx,my)||1; return {x:mx/l2*.3,y:my/l2*.3}; } return {x:mx/l,y:my/l}; };
  const dt=1/30, maxT=cfg.maxT||900;
  while (!V.over && V.t<maxT){
    while (V.pend>0){ const opts=D.survOptions().sort((a,b)=>(PRI[b.kind]||0)-(PRI[a.kind]||0)); D.applyOpt(opts[0]); V.pend--; }
    V.picking=false;
    D.survUpdate(dt); V.parts.length=0; V.texts.length=0;
  }
  return { t:Math.round(V.t), kills:V.kills, lv:V.lv, comps:V.comps.map(c=>c.type+c.lv).join(' '), combos:Object.keys(V.combos).join(','), leaderLv:V.leader.lv };
  } finally { Math.random=realRandom; }
};
})();
