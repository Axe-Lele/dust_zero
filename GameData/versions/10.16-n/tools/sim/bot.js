// 전투 시뮬레이터용 배치 봇 (브라우저 안에서 실행). index.html#sim 으로 연 페이지에 주입해서 window.simRun(cfg) 로 한 판을 돌린다.
// 사람처럼 '그럴듯하게' 두는 탐욕 봇: 열린 입구 길목마다 근접 1대 → 발전기 → 길 끝 합류 지점을 넓게 덮는 원거리 → 선풍기·컴퓨터 → 남는 전력은 멀티탭·강화
(function(){
const D=window.__DZ;
function rng(seed){ let s=seed>>>0||1; return ()=>{ s=(s*1664525+1013904223)>>>0; return s/4294967296; }; }
window.simRun=function(cfg){
  const UNITS=D.UNITS(), CFG=D.CFG(), STAGES=D.STAGES();
  const realRandom=Math.random; Math.random=rng(cfg.seed||1);
  try {
  const S={v:2,items:{},owned:{},deck:cfg.deck.slice(),best:cfg.stage,stars:{},starClaimed:{},tut:{},tutOff:1,speed:1,idleBank:{coin:0,part:0,crystal:0},idleSince:0,guide:null,guideDone:{first:1,levelup:1},gear:{next:1,inv:[]},excl:{},pass:{}};
  for (const t of cfg.deck) S.owned[t]={lv:(cfg.lvOf&&cfg.lvOf[t])||cfg.lv||1, star:(cfg.starOf&&cfg.starOf[t])||cfg.star||0};
  D.S=S;
  const g=cfg.gear||0;
  window.__DZ_GEAR=t=>UNITS[t].tile==='P'?{atk:g*.3,heat:g,sp:0}:{atk:g,heat:0,sp:0};
  if (cfg.hpMul!=null) STAGES[cfg.stage].hpMul=cfg.hpMul;
  D.loadStage(cfg.stage); D.newGame();
  const G=D.G, MAP=D.MAP, PATHS=D.PATHS, ST=D.ST;
  G.running=true; G.hand=S.deck.slice(); G.speed=1;
  if (cfg.noPs) G.ps={};   // 전력 스킬 없이(비교용)
  const ROWS=10, COLS=7;
  // ---- 지도 분석 ----
  const cellsOf=wp=>{ const out=[]; for (let j=0;j<wp.length-1;j++){ let [x,y]=wp[j]; const [x2,y2]=wp[j+1]; const dx=Math.sign(x2-x), dy=Math.sign(y2-y); if (!out.length) out.push([x,y]); while(x!==x2||y!==y2){ x+=dx; y+=dy; out.push([x,y]); } } return out; };
  const pcells=ST.paths.map(cellsOf);
  const goal=pcells[0][pcells[0].length-1];
  const onPath=(c,r)=>pcells.map((cs,pi)=>cs.findIndex(([x,y])=>x===c&&y===r));
  // 원거리 칸 점수: 사거리 안 경로 칸마다 (목표에 가까울수록 큰) 가중치, 여러 경로를 덮으면 보너스
  const H=[]; for (let r=0;r<ROWS;r++) for (let c=0;c<COLS;c++) if (MAP[r][c]==='H') H.push([c,r]);
  const covScore=(c,r,R,air)=>{ let s=0; const seen=new Set();
    pcells.forEach((cs,pi)=>cs.forEach(([x,y],i)=>{ if (Math.hypot(x+.5-c-.5,y+.5-r-.5)<=R){ const f=i/(cs.length-1); s+=0.6+1.4*f; seen.add(pi); } }));
    return s*(1+.25*(seen.size-1)); };
  const used=new Set(), key=(c,r)=>c+','+r;
  const rangedTypes=S.deck.filter(t=>UNITS[t].tile==='H' && UNITS[t].atk>0);
  const meleeTypes=S.deck.filter(t=>UNITS[t].tile==='P');
  const hasFly=ST.enemies.some(t=>D.ENEMY()[t].fly);
  const hasArmor=ST.enemies.some(t=>D.ENEMY()[t].armor>0);
  const EN=D.ENEMY(); const hpW=ST.sched.reduce((a,x)=>a+EN[x.type].hp*x.hm,0)||1;
  const airShare=ST.sched.reduce((a,x)=>a+(EN[x.type].fly?EN[x.type].hp*x.hm:0),0)/hpW;   // 비행 적 비중(HP 기준)
  const MELEE_PREF={fridge:4,vac:3,iron:hasArmor?3.5:2.5,robot:1.5};
  const RANGED_PREF={micro:3+airShare*2,ac:(hasFly?3.2:2.6)+airShare*2,washer:3.1+airShare*2,boiler:(hasArmor?3.6:2.8)+airShare*2,stove:2.7-airShare*4};
  const free=(c,r)=>!G.units.some(u=>u.c===c&&u.r===r);
  function bestH(type,scoreFn){ let b=null,bs=-1e9; for (const [c,r] of H){ if (!free(c,r)||!D.canPlace(type,c,r)) continue; const s=scoreFn(c,r); if (s>bs){ bs=s; b=[c,r]; } } return b; }
  function meleeCell(type,pi){ // 그 경로에서 목표 쪽에 가까운 배치 가능 칸. 물기·러그 칸 선호, 불·기름 칸 회피
    const cs=pcells[pi]; let b=null,bs=-1e9;
    cs.forEach(([c,r],i)=>{ if (!free(c,r)||!D.canPlace(type,c,r)) return; const ch=MAP[r][c]; let s=i/cs.length; if (ch==='W') s+=.15; if (ch==='F'||ch==='O') s-=.6;
      const near=pcells.reduce((a,cs2,pj)=>a+(pj!==pi&&cs2.some(([x,y])=>Math.abs(x-c)+Math.abs(y-r)<=1)?1:0),0); s+=near*.08;
      if (G.units.some(u=>u.maxHeat && Math.abs(u.c-c)+Math.abs(u.r-r)===1)) s+=.12;   // 서로 붙여 세우기
      if (s>bs){ bs=s; b=[c,r]; } });
    return b; }
  const sched=ST.sched;
  const activePaths=()=>{ const a=new Set(); for (const x of sched) if (x.t<=G.t+4) a.add(x.pi); for (const e of G.enemies) a.add(e.pi); return [...a]; };
  const guards=pi=>G.units.filter(u=>u.maxHeat && onPath(u.c,u.r)[pi]>=0).length;
  const cnt=t=>G.units.filter(u=>u.type===t).length;
  const pickMelee=()=>meleeTypes.filter(t=>G.cd[t]<=0).sort((a,b)=>(MELEE_PREF[b]||1)-(MELEE_PREF[a]||1)+(cnt(a)-cnt(b))*.9)[0];
  const pickRanged=()=>rangedTypes.filter(t=>G.cd[t]<=0).sort((a,b)=>((RANGED_PREF[b]||2)-cnt(b)*1.1)-((RANGED_PREF[a]||2)-cnt(a)*1.1))[0];
  function want(){ // 지금 하고 싶은 일 하나(우선순위 순)
    const act=activePaths(), nR=G.units.filter(u=>rangedTypes.includes(u.type)).length, nM=G.units.filter(u=>u.maxHeat).length;
    const slots=G.units.length<G.maxUnits;
    // 곧 비행 적이 오는데 공중을 칠 원거리가 없으면 그것부터
    const airSoon=sched.some(x=>x.t>G.t-1 && x.t<=G.t+6 && D.ENEMY()[x.type].fly) || G.enemies.some(e=>e.def.fly);
    const airUnits=G.units.filter(u=>u.def.atk>0 && (u.def.atkTarget&2) && u.def.tile==='H').length;
    if (slots && airSoon && airUnits<Math.max(1,Math.ceil(G.enemies.filter(e=>e.def.fly).length/3))){ const at=rangedTypes.filter(t=>(UNITS[t].atkTarget&2)&&G.cd[t]<=0).sort((a,b)=>(RANGED_PREF[b]||2)-(RANGED_PREF[a]||2))[0]; if (at){ const cell=bestH(at,(c,r)=>covScore(c,r,UNITS[at].range)); if (cell) return {kind:'deploy',type:at,cell}; } }
    if (slots && meleeTypes.length) for (const pi of act) if (guards(pi)===0){ const t=pickMelee(); if (t){ const cell=meleeCell(t,pi); if (cell) return {kind:'deploy',type:t,cell}; } }
    if (slots && S.deck.includes('gen') && cnt('gen')===0 && G.t>1){ const cell=bestH('gen',(c,r)=>-covScore(c,r,2.4)); if (cell) return {kind:'deploy',type:'gen',cell}; }
    if (slots && nR<1+act.length && rangedTypes.length){ const t=pickRanged(); if (t){ const cell=bestH(t,(c,r)=>covScore(c,r,UNITS[t].range)); if (cell) return {kind:'deploy',type:t,cell}; } }
    const hot=G.units.filter(u=>u.maxHeat).reduce((m,u)=>Math.max(m,u.heat/u.maxHeat+(u.over?1:0)),0);
    if (slots && S.deck.includes('fan') && cnt('fan')<Math.ceil(nM/2) && (hot>.35||nM>=2)){ const R=1.6; const cell=bestH('fan',(c,r)=>G.units.filter(u=>u.maxHeat&&Math.hypot(u.c-c,u.r-r)<=R).reduce((a,u)=>a+1+u.heat/u.maxHeat,0)-(G.units.some(u=>u.type==='fan'&&Math.hypot(u.c-c,u.r-r)<1.5)?2:0)); if (cell) return {kind:'deploy',type:'fan',cell}; }
    if (slots && S.deck.includes('pc') && cnt('pc')===0 && nR>=2){ const cell=bestH('pc',(c,r)=>G.units.filter(u=>u.def.atk>0&&Math.hypot(u.c-c,u.r-r)<=1.6).length); if (cell) return {kind:'deploy',type:'pc',cell}; }
    if (slots && meleeTypes.length && nR>=3+Math.round(airShare*4) && nM<act.length*(airShare>.3?1:2)){ const pi=act.slice().sort((a,b)=>guards(a)-guards(b))[0]; const t=pickMelee(); if (t){ const cell=meleeCell(t,pi); if (cell) return {kind:'deploy',type:t,cell}; } }
    if (slots && rangedTypes.length){ const t=pickRanged(); if (t){ const cell=bestH(t,(c,r)=>covScore(c,r,UNITS[t].range)); if (cell) return {kind:'deploy',type:t,cell}; } }
    if (!slots && D.tapCost()!=null) return {kind:'tap',cost:D.tapCost()};
    // 강화: 원거리(커버 넓은 것) · 과열 높은 근접 우선
    let bu=null, bv=-1;
    for (const u of G.units){ const c=D.boostCost(u); if (c==null) continue; let v=0;
      if (u.maxHeat) v=1.2+u.heat/u.maxHeat; else if (u.def.atk>0) v=1+covScore(u.c,u.r,u.def.range)/40; else if (u.type==='fan') v=.9; else if (u.type==='pc') v=1.1; else v=.6;
      v/=Math.sqrt(c); if (v>bv){ bv=v; bu=u; } }
    if (bu) return {kind:'boost',u:bu,cost:D.boostCost(bu)};
    return null;
  }
  const dt=1/30; let nextAct=0, acts=0; const leakBy={}, leakAt=[], psUse={storm:0,boost:0};
  const maxT=sched.length?sched[sched.length-1].t+120:200;
  while (!G.over && G.t<maxT){
    if (G.t>=nextAct){ nextAct=G.t+.2;
      // 전력 스킬: 배치가 어느 정도 끝난 뒤(6대 이상) 적이 몰렸거나 보스가 있을 때
      if (G.units.length>=6){
        const st=G.ps.storm; if (st && st.cd<=0 && G.power>=st.row.cost){ let best=null, bv=0;
          for (const e of G.enemies){ const q=D.epos(e); let v=0;
            for (const o of G.enemies){ const qo=D.epos(o); if (Math.hypot(qo.x-q.x,qo.y-q.y)<=st.row.radius) v+=(o.def.boss?4:1)*(o.hp/o.max); }
            if (v>bv){ bv=v; best=q; } }
          if (best && bv>=4){ D.castStorm(best.x,best.y); psUse.storm++; } }
        const bo=G.ps.boost; if (bo && bo.cd<=0 && G.power>=bo.row.cost && (G.enemies.some(e=>e.def.boss) || G.enemies.length>=12)){ D.castBoost(); psUse.boost++; }
      }
      for (let k=0;k<3;k++){ const w=want(); if (!w) break;
        if (w.kind==='deploy'){ if (G.power>=UNITS[w.type].cost && D.affordable(w.type)){ D.deploy(w.type,w.cell[0],w.cell[1]); acts++; } else break; }
        else if (w.kind==='tap'){ if (G.power>=w.cost){ D.buyTap(); acts++; } else break; }
        else if (w.kind==='boost'){ if (G.power>=w.cost){ D.boostUnit(w.u); acts++; } else break; } } }
    const before=G.enemies.slice(), lb=G.leaked;
    D.update(dt); G.fx.length=0; G.parts.length=0; G.texts.length=0;
    if (G.leaked>lb) for (const e of before) if (e.dead && e.hp>0){ leakBy[e.type]=(leakBy[e.type]||0)+1; leakAt.push(Math.round(G.t)+':'+e.type+'@'+e.pi); }
  }
  const win=G.over && G.life>0;
  return { win, life:Math.max(0,G.life), t:Math.round(G.t), killed:G.killed, leaked:G.leaked, n:G.units.length, taps:G.taps, comp:G.units.map(u=>u.type+(u.tier>1?u.tier:'')+'('+u.c+','+u.r+')').join(' '), leakBy, leakAt:leakAt.join(' '), psUse };
  } finally { Math.random=realRandom; }
};
})();
