// 퀘스트·업적·이벤트·먼지 도감 화면 확인(10.21-c): 1-1 한 판으로 카운터가 오르는지 + 각 화면 스크린샷. 스크린샷 폴더는 아래 V를 바꿔서 쓰세요
import { chromium } from 'playwright'; import url from 'url';
const V=process.argv[2]||'/tmp/';
const b=await chromium.launch(); const p=await b.newPage({viewport:{width:430,height:930},deviceScaleFactor:2});
const errs=[]; p.on('pageerror',e=>errs.push(e.message+' '+(e.stack||'').split('\n')[1]));
await p.goto(url.pathToFileURL(new URL('../../index.html',import.meta.url).pathname).href+'#sim',{waitUntil:'domcontentloaded'}); await p.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0);
await p.evaluate(()=>{ const D=window.__DZ,S=D.S; S.guide=null; S.guideDone={first:1,levelup:1,gacha:1}; S.best=12; S.tutOff=1; S.att={n:1,last:'2099-1-1'};
  for (const t of ['vac','robot','fan','gen','ac','micro','iron','boiler','washer']) S.owned[t]={lv:12,star:1,eq:{}}; S.deck=['vac','iron','ac','micro','gen','fan']; document.getElementById('modal').hidden=true; D.persist(); D.renderLobby(); D.setTab('home'); });
await new Promise(r=>setTimeout(r,400)); await p.screenshot({path:V+'q_home.png'});
// 실제 전투 한 판(1-1)을 시뮬레이터처럼 돌려서 카운터가 올라가는지
const res=await p.evaluate(()=>{ const D=window.__DZ; D.startStage(0); document.getElementById('btnStart').click(); const G=D.G; G.power=50;
  for (const [t,c,r] of [['vac',1,3],['ac',3,4]]) if (D.canPlace(t,c,r)) D.deploy(t,c,r);
  let n=0; while(!G.over && n<30*120){ D.update(1/30); n++; }
  const m=D.msSt(); return {over:G.over, life:G.life, d:m.d.c, seen:Object.keys(D.S.codex.seen)}; });
console.log('after 1-1:', JSON.stringify(res));
await p.evaluate(()=>{ const D=window.__DZ; D.track('lvup'); D.track('attend'); D.track('idle_claim'); D.track('pool_pts',40); D.track('kill',200); D.track('stage_play',3); document.getElementById('battle').hidden=true; document.getElementById('lobby').hidden=false; document.getElementById('overlay').hidden=true; D.setTab('home'); });
console.log('claimable', await p.evaluate(()=>window.__DZ.msClaimable()));
await p.evaluate(()=>window.__DZ.openMissions(1)); await new Promise(r=>setTimeout(r,300)); await p.screenshot({path:V+'q_daily.png'});
await p.evaluate(()=>document.getElementById('msAll').click()); await new Promise(r=>setTimeout(r,300)); await p.screenshot({path:V+'q_daily2.png'});
await p.evaluate(()=>window.__DZ.openMissions(3)); await new Promise(r=>setTimeout(r,300)); await p.screenshot({path:V+'q_ach.png'});
await p.evaluate(()=>window.__DZ.openMissions(4)); await new Promise(r=>setTimeout(r,300)); await p.screenshot({path:V+'q_event.png'});
await p.evaluate(()=>{ document.getElementById('modal').hidden=true; window.__DZ.setTab('content'); }); await new Promise(r=>setTimeout(r,300)); await p.screenshot({path:V+'q_content.png'});
await p.evaluate(()=>window.__DZ.openCodex()); await new Promise(r=>setTimeout(r,300)); await p.screenshot({path:V+'cx_list.png'});
await p.evaluate(()=>window.__DZ.openCodex('dust')); await new Promise(r=>setTimeout(r,300)); await p.screenshot({path:V+'cx_one.png'});
console.log('wallet', await p.evaluate(()=>JSON.stringify({coin:window.__DZ.S.items[1],cry:window.__DZ.S.items[2]})));
console.log(errs.join('\n')||'no errors'); await b.close();
