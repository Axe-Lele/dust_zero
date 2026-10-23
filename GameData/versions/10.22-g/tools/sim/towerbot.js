// 먼지 타워 밸런스 봇 (10.18-g). index.html#sim 페이지에 주입해서 window.towerRun(cfg) 로 한 층을 여러 편성으로 시험한다.
// 사람은 지면 편성을 바꿔 다시 하므로, 편성 몇 가지(기본 자동 · 원거리 위주 · 근접 위주 · 규칙 맞춤) 중 하나라도 이기면 '깬 층'으로 본다.
// cfg: { floor, owned:{가전:{lv,star}}, excl:{가전:{star}}, gear: 공격 장비 보너스(0~), seed, mul: 적 체력·공격 추가 배율(맞춤용) }
(function(){
const D=window.__DZ;
window.towerRun=function(cfg){
  const U=D.UNITS(); D.lgTables(); D.twTables();
  const S={v:2,items:{},owned:{},deck:[],best:75,stars:{},excl:cfg.excl||{},tut:{},tutOff:1,guide:null,guideDone:{first:1,levelup:1,gacha:1},tower:null};
  for (const t in cfg.owned) S.owned[t]=Object.assign({eq:{}},cfg.owned[t]);
  D.S=S; window.__DZ_GEAR=t=>({atk:cfg.gear||0,heat:0,sp:0});
  try {
    const F=D.TWF()[cfg.floor-1], pw=t=>{ const o=S.owned[t]; return o?1+.05*(o.lv-1)+.12*o.star:1; };
    const ros=D.twRoster(F), byPw=ros.slice().sort((a,b)=>pw(b)-pw(a)), mel=byPw.filter(t=>U[t].tile==='P'), rng=byPw.filter(t=>U[t].tile!=='P');
    const n=F.rule==='solo'?D.TWR().solo.p1:F.slots, guest=F.rule==='guest'&&!S.owned[F.extra]?F.extra:null;
    const mk=(...lists)=>{ const out=guest?[guest]:[]; for (const l of lists) for (const t of l) if (out.length<n && !out.includes(t)) out.push(t); return out; };
    const plans={
      auto:null,
      ranged:mk(mel.slice(0,1),rng,mel),
      melee:mk(mel.slice(0,3),rng,mel),
      rule:F.rule==='heat'?mk(byPw.filter(t=>U[t].family===4),mel.slice(0,2),byPw):F.rule==='blackout'?mk(['gen'].filter(t=>S.owned[t]),mel.slice(0,2),byPw)
          :F.rule==='guard'?mk(mel.slice(0,2),['fan'].filter(t=>S.owned[t]),rng,mel):mk(mel.slice(0,2),byPw.filter(t=>t!=='gen'&&t!=='pc'),byPw),
    };
    const res=[];
    for (const [name,pick] of Object.entries(plans)){
      const LG=D.twNew(cfg.floor,{sim:true,seed:cfg.seed||7,empty:!!pick});
      if (cfg.mul){ LG.T.hpMul*=cfg.mul; LG.T.atkMul*=cfg.mul; }   // tower_cal.mjs: 층 세기를 바꿔 가며 겨우 깨는 배율을 찾음
      if (pick) for (const t of pick) D.twToggle(t);
      if (!D.twStartBattle()) continue;
      D.lgSimToEnd(); const B=LG.B;
      const hp=B.units.filter(u=>u.side===0&&!u.obj).reduce((a,u)=>a+Math.max(0,u.hp)/u.maxHp,0)/Math.max(1,B.used);
      res.push({name, win:!!B.win, t:+B.t.toFixed(1), hp:+hp.toFixed(2), bonus:B.win&&D.twBonusOk(B), left:B.units.filter(u=>u.side===1&&u.alive&&!u.gold).length, board:LG.board.filter(Boolean).map(p=>p.t).join(' ')});
    }
    const win=res.filter(r=>r.win).sort((a,b)=>b.hp-a.hp);
    return {floor:cfg.floor, clear:win.length>0, bonus:res.some(r=>r.bonus), wins:win.length, best:win[0]||res.sort((a,b)=>a.left-b.left)[0], res};
  } finally { window.__DZ_GEAR=null; }
};
})();
