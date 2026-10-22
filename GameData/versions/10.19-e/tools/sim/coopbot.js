// 협동 청소(고리 판 · 랜덤 소환) 밸런스 봇 (10.19-c). index.html#sim 에 주입해서 window.coopRun(cfg) 로 한 판을 끝까지 돌린다.
// '나'는 보통 사람 규칙: 전력이 되면 소환(칸은 자동 = 고리에서 가까운 칸) → 자리가 차면 합치기(같은 가전 짝 먼저, 낮은 ★부터)
//   → 공격 안 하는 가전(발전기 등)이 3대를 넘으면 바꾸기 → 남는 전력으로 가장 많이 깔린 종류 강화.
// 파트너는 게임 안 규칙(coopBotAct) 그대로. cfg: { pid, seed, best(진행 스테이지 수), lv, star, gear, deck:[…], solo: 파트너 없이, idle: 나는 아무것도 안 함 }
(function(){
const D=window.__DZ;
function rng(seed){ let s=seed>>>0||1; return ()=>{ s=(s*1664525+1013904223)>>>0; return s/4294967296; }; }
window.coopRun=function(cfg){
  const UNITS=D.UNITS(), CFG=D.CFG(), realRandom=Math.random; Math.random=rng(cfg.seed||1);
  try {
  const S={v:2,items:{},owned:{},deck:cfg.deck.slice(),best:cfg.best,stars:{},starClaimed:{},tut:{},tutOff:1,speed:1,idleBank:{coin:0,part:0,crystal:0},idleSince:0,guide:null,guideDone:{first:1,levelup:1,gacha:1},gear:{next:1,inv:[]},excl:{},pass:{},pool:null,coop:null};
  for (const t of cfg.deck) S.owned[t]={lv:cfg.lv,star:cfg.star};
  D.S=S; const g=cfg.gear||0; window.__DZ_GEAR=t=>UNITS[t].tile==='P'?{atk:g*.3,heat:g,sp:0}:{atk:g,heat:0,sp:0};
  D.coopNew(cfg.pid); const G=D.G; G.sim=true; G.running=true; G.speed=1;
  if (cfg.solo) G.coop.power=-1e9;   // 파트너가 아무것도 못 함
  const room=()=>D.coopRoom(false);
  const mine=()=>G.units.filter(u=>!u.owner);
  function act(){
    if (cfg.idle) return false;
    if (room() && G.power>=D.coopSumCost(false)) return !!D.coopSummon(false);
    const pairs=D.coopPairs(false);   // 같은 가전 짝 먼저, 낮은 ★ 먼저
    if (!room() && pairs.length){ D.coopMerge(pairs[0][0],pairs[0][1]); return true; }
    const sup=mine().filter(u=>!(UNITS[u.type].atk>0)).sort((a,b)=>a.tier-b.tier);
    if (!cfg.noReroll && sup.length>3 && G.power>=CFG.coopRerollCost*sup[0].tier && D.coopReroll(sup[0])) return true;
    const cnt={}; for (const u of mine()) cnt[u.type]=(cnt[u.type]||0)+u.tier;
    const t=Object.keys(cnt).sort((a,b)=>cnt[b]-cnt[a]).find(t=>D.coopUpCost(false,t)!=null);
    if (t && G.power>=D.coopUpCost(false,t) && (!room() || D.coopUpCost(false,t)<=D.coopSumCost(false)*1.5)) return D.coopUpgrade(false,t);
    return false;
  }
  const dps=b=>Math.round(G.units.filter(u=>!!u.owner===b).reduce((a,u)=>a+(u.def.atk||0)*(u.atkMul||1)*(u.tier>=1?1:1)/(u.def.itv||1),0)/100);
  const snap=[]; let lastW=0;
  const dt=1/30; let next=0;
  while (!G.over && G.t<1500){
    if (G.coop.cleared!==lastW){ lastW=G.coop.cleared; if (lastW%4===0) snap.push(`${lastW}:${dps(false)}/${dps(true)}`); }
    if (G.t>=next){ next=G.t+.25; for (let k=0;k<3;k++) if (!act()) break; }
    D.update(dt); G.fx.length=0; G.parts.length=0; G.texts.length=0;
  }
  const st=us=>us.map(u=>u.type+u.tier).sort().join(' ');
  return { snap:snap.join(' '), waves:G.coop.cleared, t:Math.round(G.t), peak:G.coop.peak, n:G.coop.n, bn:G.coop.bn, me:st(mine()), bot:st(G.units.filter(u=>u.owner)),
           up:JSON.stringify(G.coop.up), bup:JSON.stringify(G.coop.bup) };
  } finally { Math.random=realRandom; window.__DZ_GEAR=null; }
};
})();
