// 해금 알림 · 심화 모드 · 주간 시즌 · 이벤트 순환 확인(10.21-g·h·i): node tools/sim/endgame_ui.mjs <스크린샷 폴더/>
import { chromium } from 'playwright'; import url from 'url';
const V=process.argv[2]||'/tmp/';
const b=await chromium.launch();
const HREF=url.pathToFileURL(new URL('../../index.html',import.meta.url).pathname).href+'#sim';
async function page(date){ const p=await b.newPage({viewport:{width:430,height:930},deviceScaleFactor:2}); const errs=[]; p.on('pageerror',e=>errs.push(e.message+' '+(e.stack||'').split('\n')[1]));
  if (date) await p.clock.setFixedTime(new Date(date));
  await p.goto(HREF,{waitUntil:'domcontentloaded'}); await p.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); p.errs=errs; return p; }
const sh=async (p,n)=>{ await new Promise(r=>setTimeout(r,350)); await p.screenshot({path:V+n}); };
const setup=(p,best)=>p.evaluate(best=>{ const D=window.__DZ,S=D.S; S.guide=null; S.guideDone={first:1,levelup:1,gacha:1}; S.best=best; S.tutOff=1; S.att={n:1,last:'2099-1-1'}; S.items[1]=500000; S.items[2]=500;
  for (const t of ['vac','robot','iron','ac','micro','stove','gen','fan','pc']) S.owned[t]={lv:45,star:5,eq:{}}; S.deck=['vac','iron','ac','micro','stove','gen']; document.getElementById('modal').hidden=true; document.getElementById('guide').hidden=true; D.persist(); D.renderLobby(); },best);
// 1) 해금 알림: 3-4까지 깬 계정으로 3-5를 이김 → 협동 청소 열림
let p=await page();
await setup(p,14);
const u1=await p.evaluate(()=>{ const D=window.__DZ; D.startStage(14); document.getElementById('btnStart').click(); const G=D.G; G.power=99;
  for (const t of ['vac','iron','ac','micro','stove','gen']) for (let c=0;c<7;c++){ let ok=false; for (let r=0;r<10;r++) if (D.canPlace(t,c,r)){ D.deploy(t,c,r); ok=true; break; } if (ok) break; }
  for (let i=0;i<30*5;i++) D.update(1/30); G.life=10; D.end(true); return {win:G.life>0, best:D.S.best, unl:JSON.stringify(D.S.unlSeen), txt:[...document.querySelectorAll('.ov-unl b')].map(x=>x.textContent)}; });
console.log('unlock', JSON.stringify(u1)); await sh(p,'eg_unlock.png');
const dot1=await p.evaluate(()=>{ document.getElementById('overlay').hidden=true; document.getElementById('battle').hidden=true; document.getElementById('lobby').hidden=false; window.__DZ.renderLobby(); return document.querySelector('.tab[data-tab=content]').classList.contains('alert'); });
const dot2=await p.evaluate(()=>{ window.__DZ.setTab('content'); return [document.querySelector('.tab[data-tab=content]').classList.contains('alert'), JSON.stringify(window.__DZ.S.unlSeen)]; });
console.log('content dot before/after visit', dot1, JSON.stringify(dot2));
await p.close();
// 2) 심화 모드: 6-5까지 깬 계정 → 스테이지 탭 심화, 심화 1-1 이기기
p=await page();
await setup(p,30);
await p.evaluate(()=>{ document.getElementById('modal').hidden=true; window.__DZ.setTab('stage'); }); await sh(p,'eg_stage_normal.png');
await p.evaluate(()=>document.querySelector('#stMode button[data-m=hard]').click()); await sh(p,'eg_stage_hard.png');
const h1=await p.evaluate(()=>{ const D=window.__DZ; D.startStage(0,true); const intro=document.getElementById('ovCard').textContent.slice(0,160); document.getElementById('btnStart').click(); const G=D.G; G.power=99;
  for (const t of ['vac','iron','ac','micro','stove','gen']) for (let c=0;c<7;c++){ let ok=false; for (let r=0;r<10;r++) if (D.canPlace(t,c,r)){ D.deploy(t,c,r); ok=true; break; } if (ok) break; }
  let n=0; while(!G.over && n<30*300){ D.update(1/30); n++; } return {intro, st:D.ST.no, hard:!!D.ST.hard, spd:D.ST.spdMul, win:G.life>0, life:G.life, hbest:D.S.hbest, hstars:JSON.stringify(D.S.hstars), best:D.S.best, cry:D.S.items[2], ms:D.S.ms&&D.S.ms.st.hard_clear}; });
console.log('hard 1-1', JSON.stringify(h1)); await sh(p,'eg_hard_result.png');
await p.evaluate(()=>{ document.getElementById('btnNext')&&document.getElementById('btnNext').click(); }); await sh(p,'eg_hard_next_intro.png');
console.log('next is', await p.evaluate(()=>window.__DZ.ST.no));
// 정예 규칙(심화 5-1) 한 번 돌려 elite 수
const el=await p.evaluate(()=>{ const D=window.__DZ; D.S.hbest=20; D.S.best=50; D.startStage(20,true); document.getElementById('btnStart').click(); const G=D.G; for (let i=0;i<30*60;i++) D.update(1/30); return {no:D.ST.no, rule:D.ST.rule, elite:G.enemies.filter(e=>e.elite).length, all:G.enemies.length}; });
console.log('elite', JSON.stringify(el)); await sh(p,'eg_elite.png');
await p.close();
// 3) 주간 시즌 카드(리그·타워) + 이벤트 순환(2027-03-20 = 1번 이벤트 2회차)
p=await page('2027-03-20T10:00:00');
await setup(p,60);
await p.evaluate(()=>{ const S=window.__DZ.S; S.tower={best:12,clr:Object.fromEntries([...Array(12)].map((_,i)=>[i+1,1])),bon:{},who:{},form:[]}; S.league={tierMax:2,best:{1:8},cleared:{1:1}}; window.__DZ.persist(); window.__DZ.setTab('content'); });
const ev=await p.evaluate(()=>window.__DZ.evNow().map(e=>({id:e.id,name:e.name,start:e.start,end:e.end})));
console.log('event now (2027-03-20)', JSON.stringify(ev)); await sh(p,'eg_content.png');
await p.evaluate(()=>document.getElementById('mcEvent')?0:window.__DZ.openMissions(4)); await sh(p,'eg_event_ms.png');
await p.close();
p=await page('2026-12-20T10:00:00'); await setup(p,40);
console.log('event now (2026-12-20)', JSON.stringify(await p.evaluate(()=>window.__DZ.evNow().map(e=>e.name))));
await p.evaluate(()=>window.__DZ.openMail()); await sh(p,'eg_mail.png');
console.log(p.errs.join('\n')||'no errors (last page)'); await b.close();
