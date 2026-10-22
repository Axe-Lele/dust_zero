// 가전 대청소 리그(오토배틀러) 밸런스 봇 (10.18-c). index.html#sim 페이지에 주입해서 window.leagueRun(cfg) 로 한 판을 끝까지 돌린다.
// 사람처럼 '그럴듯하게' 두는 탐욕 봇: 짝이 맞는 가전 우선 구매 → 판 빈자리 채우기 → 라운드별 목표 레벨까지 경험치 → 남으면 새로고침(라운드당 2번)
//   판 배치: 근접은 앞줄(먼지 쪽과 맞닿은 줄), 원거리는 뒷줄. 대기석이 차면 짝 없는 약한 가전부터 판다.
// cfg: { tier, seed, owned:{가전:{lv,star}}, deck:[편성 6종 = 상점 가전], excl:{가전:{star}}, growth:0이면 성장 끔 }
(function(){
const D=window.__DZ;
const RW=[1,1.7,2.8];
const score=(p,U)=>RW[{R:0,SR:1,SSR:2}[U[p.t].rar]]*[1,2.2,4.5][p.star-1];
window.leagueRun=function(cfg){
  const U=D.UNITS(), CFG=D.CFG(); D.lgTables(); let bt=0;
  const S={v:2,items:{},owned:{},deck:(cfg.deck||[]).slice(),best:75,stars:{},excl:cfg.excl||{},tut:{},tutOff:1,guide:null,guideDone:{first:1,levelup:1},league:null};
  for (const t in cfg.owned) S.owned[t]=Object.assign({eq:{}},cfg.owned[t]);
  D.S=S;
  const gm=CFG.leagueGrowthMax; if (cfg.growth===0) CFG.leagueGrowthMax=0;
  try {
  const LG=D.lgNew(cfg.tier||1,{seed:cfg.seed||1,sim:true});
  const all=()=>LG.bench.map((p,i)=>p&&{p,w:'bench',i}).filter(Boolean).concat(LG.board.map((p,i)=>p&&{p,w:'board',i}).filter(Boolean));
  const target=r=>r<=2?3:r===3?4:r<=5?5:r===6?6:7;
  let guard=0;
  while (LG.phase!=='over' && guard++<40){
    let rerolls=0;
    for (let step=0;step<40;step++){
      const have=new Set(all().map(x=>x.p.t+x.p.star));
      // 1) 짝이 맞는 가전
      let bi=LG.shop.findIndex(t=>t && have.has(t+'1') && LG.gold>=CFG.leagueCost[{R:0,SR:1,SSR:2}[U[t].rar]]);
      // 2) 판 빈자리 · 대기석 여유가 있으면 가장 좋은 등급
      if (bi<0 && (D.lgOnBoard()+LG.bench.filter(Boolean).length)<LG.lv+3){ let best=-1, bs=-1; LG.shop.forEach((t,i)=>{ if (!t) return; const c=CFG.leagueCost[{R:0,SR:1,SSR:2}[U[t].rar]]; if (c>LG.gold) return; const sc=RW[{R:0,SR:1,SSR:2}[U[t].rar]]; if (sc>bs){ bs=sc; best=i; } }); bi=best; }
      if (bi>=0 && D.lgBuy(bi)) continue;
      // 3) 목표 레벨까지
      if (LG.lv<target(LG.round) && LG.gold>=CFG.leagueXpCost && D.lgBuyXp()) continue;
      // 4) 새로고침
      if (LG.round>=2 && rerolls<2 && LG.gold>=CFG.leagueRerollCost+2){ D.lgRoll(false); rerolls++; continue; }
      break;
    }
    // 대기석 정리: 꽉 차면 짝 없는 약한 것부터 판매
    while (LG.bench.filter(Boolean).length>5){ const cnt={}; for (const x of all()) cnt[x.p.t+x.p.star]=(cnt[x.p.t+x.p.star]||0)+1;
      const c=LG.bench.map((p,i)=>p&&{p,i}).filter(Boolean).sort((a,b)=>(cnt[a.p.t+a.p.star]-cnt[b.p.t+b.p.star])||(score(a.p,U)-score(b.p,U)))[0]; if (!c) break; D.lgSell('bench',c.i); }
    // 배치: 전부 대기석으로 내린 뒤 점수 순으로 다시 놓기
    for (let i=0;i<21;i++) if (LG.board[i]){ const j=LG.bench.indexOf(null); if (j<0){ continue; } LG.bench[j]=LG.board[i]; LG.board[i]=null; }
    const pick=LG.bench.map((p,i)=>p&&{p,i}).filter(Boolean).sort((a,b)=>score(b.p,U)-score(a.p,U)).slice(0,LG.lv);
    const front=[3,2,4,1,5,0,6], back=[17,16,18,15,19,14,20,10,9,11,8,12];
    let fi=0, bk=0;
    for (const x of pick){ const melee=U[x.p.t].tile==='P'; const cell=melee&&fi<front.length?front[fi++]:back[bk++]; D.lgMove({where:'bench',i:x.i},{where:'board',i:cell}); }
    if (!D.lgStartBattle()) break;
    D.lgSimToEnd(); bt+=(D.LG.B&&D.LG.B.t)||0; D.lgRoundEnd();
  }
  const fin=all().filter(x=>x.w==='board').map(x=>x.p.t+x.p.star).join(' ');
  return { bt:Math.round(bt), wins:LG.wins, life:LG.life, clear:LG.life>0&&LG.round>=CFG.leagueRounds, lv:LG.lv, board:fin, round:LG.round };
  } finally { CFG.leagueGrowthMax=gm; }
};
})();
