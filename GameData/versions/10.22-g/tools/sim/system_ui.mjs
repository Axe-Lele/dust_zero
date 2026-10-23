// 우편함·호감도·수집 보너스·적 구분 화면 확인(10.21-d): node tools/sim/system_ui.mjs <스크린샷 폴더/>
import { chromium } from 'playwright'; import url from 'url';
const V=process.argv[2]||'/tmp/';
const b=await chromium.launch(); const p=await b.newPage({viewport:{width:430,height:930},deviceScaleFactor:2});
const errs=[]; p.on('pageerror',e=>errs.push(e.message+' '+(e.stack||'').split('\n')[1]));
await p.goto(url.pathToFileURL(new URL('../../index.html',import.meta.url).pathname).href+'#sim',{waitUntil:'domcontentloaded'}); await p.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0);
await p.evaluate(()=>{ const D=window.__DZ,S=D.S; S.guide=null; S.guideDone={first:1,levelup:1,gacha:1}; S.best=12; S.tutOff=1; S.att={n:1,last:'2099-1-1'}; S.items[1]=50000;
  for (const t of ['vac','robot','fan','gen','ac','micro','iron','boiler','washer']) S.owned[t]={lv:12,star:1,eq:{}}; S.deck=['vac','iron','ac','micro','gen','fan']; document.getElementById('modal').hidden=true; D.persist(); D.renderLobby(); D.setTab('home'); });
const r0=await p.evaluate(()=>{ const D=window.__DZ; const before=JSON.stringify(D.idleRate()); return {mails:D.S.mail?D.S.mail.list.map(m=>m.title):[], idle:before}; });
console.log('mails', JSON.stringify(r0));
await new Promise(r=>setTimeout(r,300)); await p.screenshot({path:V+'s_home.png'});
await p.evaluate(()=>window.__DZ.openMail()); await new Promise(r=>setTimeout(r,300)); await p.screenshot({path:V+'s_mail.png'});
await p.evaluate(()=>document.getElementById('mlAll').click()); await new Promise(r=>setTimeout(r,200));
// 스테이지 1-1 한 판 → 편성 가전 호감도
const r1=await p.evaluate(()=>{ const D=window.__DZ; document.getElementById('modal').hidden=true; D.startStage(0); document.getElementById('btnStart').click(); const G=D.G; G.power=50; for (const [t,c,r] of [['vac',1,3],['ac',3,4]]) if (D.canPlace(t,c,r)) D.deploy(t,c,r);
  let n=0; while(!G.over && n<30*120){ D.update(1/30); n++; } return {win:G.life>0, aff:JSON.stringify(D.S.aff)}; });
console.log('after 1-1', JSON.stringify(r1));
await p.evaluate(()=>{ const D=window.__DZ; document.getElementById('battle').hidden=true; document.getElementById('lobby').hidden=false; document.getElementById('overlay').hidden=true; D.affAdd('vac',300); D.setTab('roster'); });
await p.evaluate(()=>{ const el=document.querySelector('.ut[data-unit="vac"]'); el.click(); }); await new Promise(r=>setTimeout(r,300));
await p.evaluate(()=>{ const b=[...document.querySelectorAll('.dtabs button')].find(b=>b.dataset.k==='aff'); b.click(); }); await new Promise(r=>setTimeout(r,300)); await p.screenshot({path:V+'s_aff.png'});
await p.evaluate(()=>{ document.getElementById('afTalk').click(); document.getElementById('afGift').click(); }); await new Promise(r=>setTimeout(r,300));
console.log('vac aff', await p.evaluate(()=>JSON.stringify(window.__DZ.S.aff.vac)));
await p.evaluate(()=>{ document.getElementById('modal').hidden=true; window.__DZ.openCollect(); }); await new Promise(r=>setTimeout(r,300)); await p.screenshot({path:V+'s_collect.png'});
console.log('bonus', await p.evaluate(()=>JSON.stringify(window.__DZ.colBonus())), 'idle', await p.evaluate(()=>JSON.stringify(window.__DZ.idleRate())));
// 적 구분: 2-2 시작 창 + 예고 말풍선
await p.evaluate(()=>{ document.getElementById('modal').hidden=true; window.__DZ.startStage(6); }); await new Promise(r=>setTimeout(r,400)); await p.screenshot({path:V+'s_intro.png'});
await p.evaluate(()=>{ document.getElementById('btnStart').click(); const D=window.__DZ; for (let i=0;i<30*9;i++) D.update(1/30); }); await new Promise(r=>setTimeout(r,400)); await p.screenshot({path:V+'s_warn.png'});
console.log(errs.join('\n')||'no errors'); await b.close();
