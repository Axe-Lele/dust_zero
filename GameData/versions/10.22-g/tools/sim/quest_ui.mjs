// 메인 · 서브 퀘스트 확인(10.22-a): node tools/sim/quest_ui.mjs <스크린샷 폴더/>
// 홈 퀘스트 바(받기 → 다음 · 이동) · 퀘스트 창 [메인][서브] · 이동 대상 전부 열어 보기 · 모두 받기 · 결과 창 알림
import { chromium } from 'playwright'; import url from 'url';
const V=process.argv[2]||'/tmp/';
const b=await chromium.launch(); const p=await b.newPage({viewport:{width:430,height:930},deviceScaleFactor:2});
const errs=[]; p.on('pageerror',e=>errs.push(e.message+' '+(e.stack||'').split('\n')[1]));
await p.goto(url.pathToFileURL(new URL('../../index.html',import.meta.url).pathname).href+'#sim',{waitUntil:'domcontentloaded'}); await p.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0);
const sh=async n=>{ await new Promise(r=>setTimeout(r,350)); await p.screenshot({path:V+n}); };
const ev=f=>p.evaluate(f);
await ev(()=>{ const D=window.__DZ,S=D.S; S.guide=null; S.guideDone={first:1,levelup:1,gacha:1}; S.tutOff=1; S.att={n:1,last:'2099-1-1'}; document.getElementById('modal').hidden=true; D.persist(); D.renderLobby(); D.setTab('home'); });
const bar=()=>ev(()=>{ const b=document.getElementById('qBar'); return {hidden:b.hidden, done:b.classList.contains('done'), txt:b.textContent}; });
console.log('new account bar', JSON.stringify(await bar())); await sh('q_home0.png');
await ev(()=>{ window.__DZ.S.best=1; window.__DZ.renderLobby(); });
console.log('after 1-1', JSON.stringify(await bar())); await sh('q_home1.png');
await new Promise(r=>setTimeout(r,600)); await ev(()=>{ document.getElementById('modal').hidden=true; }); const c0=await ev(()=>window.__DZ.S.items[2]||0); await p.click('#qBar'); await new Promise(r=>setTimeout(r,300));
console.log('claimed → cry', c0, '→', await ev(()=>window.__DZ.S.items[2]), JSON.stringify(await bar()));
console.log('reward popup', await ev(()=>!document.getElementById('rwOv').hidden && document.getElementById('rwTitle').textContent)); await p.click('#rwOk'); await ev(()=>{ window.__DZ.S.ms.st.lvup=1; window.__DZ.S.ms.st.pull=1; window.__DZ.S.deck=['vac','ac','fan','gen','boiler']; window.__DZ.S.owned.boiler={lv:1,star:0}; window.__DZ.S.best=2; }); for (let k=0;k<4;k++){ await p.click('#qBar'); await new Promise(r=>setTimeout(r,250)); await p.click('#rwOk').catch(()=>{}); } await p.click('#qBar'); await new Promise(r=>setTimeout(r,300)); console.log('go idle → idleOv open', await ev(()=>!document.getElementById('idleOv').hidden));
await ev(()=>{ document.getElementById('idleOv').hidden=true; });
// 중간 계정: 6-5 · 서브 줄기 몇 개 열림
await ev(()=>{ const D=window.__DZ,S=D.S; const m=D.msSt(); Object.assign(m.st,{idle_claim:3,lvup:40,pull:12,stage_3star:8,surv_play:4,star_claim:2,gear_pull:2,gear_up:5,coop_play:2,starup:3,aff_talk:2,story:2,pool_pts:300,league_play:1});
  S.best=30; S.items[1]=100000; for (const t of ['vac','robot','ac','micro','fan','gen','iron']) S.owned[t]={lv:22,star:1,eq:{}}; S.deck=['vac','robot','ac','micro','fan','gen'];
  S.surv=Object.assign(S.surv||{},{bestEver:200}); S.tower={best:4,clr:{1:1,2:1,3:1,4:1},bon:{},who:{},form:[]}; D.persist(); D.renderLobby(); D.openMissions(5); });
await sh('q_main.png');
console.log('main claimable', await ev(()=>window.__DZ.qClaimable(0)), 'sub', await ev(()=>window.__DZ.qClaimable(1)));
await ev(()=>document.getElementById('msAll').click()); await new Promise(r=>setTimeout(r,300));
console.log('after main 모두 받기 → current', await ev(()=>{ const q=window.__DZ.qMain(); return q?q.id+' '+q.text+' ch'+q.chapter:'none'; }));
await sh('q_main2.png');
await ev(()=>{ [...document.querySelectorAll('.ms-tabs button')].find(b=>b.dataset.t==='6').click(); }); await sh('q_sub.png');
// 이동 대상 전부
const gos=['stage','hard','stars','roster','deck','gacha','gear_gacha','gearbag','idle','surv','coop','tower','league','pool','aff','story'];
for (const g of gos){ await p.evaluate(g=>{ window.__DZ.qGo(g); },g); await new Promise(r=>setTimeout(r,250));
  const st=await ev(()=>({tab:[...document.querySelectorAll('.tab')].find(b=>b.getAttribute('aria-selected')==='true')?.dataset.tab, modal:!document.getElementById('modal').hidden, lg:!document.getElementById('league').hidden, surv:!document.getElementById('surv').hidden}));
  console.log('go', g.padEnd(10), JSON.stringify(st));
  await ev(()=>{ const D=window.__DZ; document.getElementById('modal').hidden=true; document.getElementById('idleOv').hidden=true; if (!document.getElementById('league').hidden||!document.getElementById('surv').hidden) D.goLobby('home'); }); }
// 결과 창 알림: 지금 메인이 '스테이지 클리어'일 때 그 판을 이김
const r=await ev(()=>{ const D=window.__DZ; D.S.quest={got:{1:1,2:1}}; const q=D.qMain(); if (!q || q.key!=='best') return 'skip '+(q&&q.key); D.S.best=q.goal-1; D.startStage(q.goal-1); document.getElementById('btnStart').click(); const G=D.G; for (let i=0;i<30*3;i++) D.update(1/30); G.life=10; D.end(true); return document.querySelector('.ov-q b')?.textContent||'no line'; });
console.log('result line:', r); await sh('q_result.png');
// 끝 계정: 메인 모두 받기 → 다 끝나면 바 숨김
const fin=await ev(()=>{ const D=window.__DZ,S=D.S; document.getElementById('overlay').hidden=true; S.best=75; S.hbest=75; for (const t of Object.keys(D.UNITS())) S.owned[t]={lv:45,star:5,eq:{}}; S.deck=Object.keys(D.UNITS()).slice(0,6);
  Object.assign(D.msSt().st,{idle_claim:30,lvup:400,pull:200,stage_3star:80,surv_play:40,star_claim:20,gear_pull:20,gear_up:50,coop_play:20,starup:30,aff_talk:20,story:20,pool_pts:3000,league_play:10});
  S.aff=Object.fromEntries(Object.keys(D.UNITS()).map(t=>[t,{xp:9999,lv:10,day:'',talk:0,gift:0}])); S.story={tab:{},done:{101:1},game:{}}; S.excl={vac:{lv:15,star:0}};
  S.tower={best:30,clr:{},bon:{},who:{},form:[]}; S.league={tierMax:3,best:{},cleared:{1:1,2:1,3:1}}; S.coop={best:{rush:30}}; S.surv=Object.assign(S.surv||{},{bestEver:400});
  for (let i=0;i<300;i++){ const q=D.qMain(); if (!q||!D.qClaim(q).length&&!S.quest.got[q.id]) break; } const q=D.qMain(); D.renderLobby(); D.setTab('home');
  return {left:q?q.id+' '+q.key+' '+D.qState(q.key)+'/'+q.goal:'all done', bar:document.getElementById('qBar').hidden}; });
console.log('end account', JSON.stringify(fin));
console.log(errs.join('\n')||'no errors'); await b.close();
