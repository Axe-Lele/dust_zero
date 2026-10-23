// 신규 가전 화면·동작 확인(10.21-f): node tools/sim/newunit_ui.mjs <스크린샷 폴더/>
// SD 16종 · 전투(신규 4종 배치 → 스킬 발동 횟수) · 대청소 동료 · 리그 전투 · 정보창/스토리 · 소환 화면
import { chromium } from 'playwright'; import url from 'url';
const V=process.argv[2]||'/tmp/', NEW=['blender','purifier','router','tv'];
const b=await chromium.launch(); const p=await b.newPage({viewport:{width:430,height:930},deviceScaleFactor:2});
const errs=[]; p.on('pageerror',e=>errs.push(e.message+' '+(e.stack||'').split('\n')[1]));
await p.goto(url.pathToFileURL(new URL('../../index.html',import.meta.url).pathname).href+'#sim',{waitUntil:'domcontentloaded'}); await p.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0);
const sh=async n=>{ await new Promise(r=>setTimeout(r,350)); await p.screenshot({path:V+n}); };
await p.evaluate(()=>{ const D=window.__DZ,S=D.S; S.guide=null; S.guideDone={first:1,levelup:1,gacha:1}; S.best=40; S.tutOff=1; S.att={n:1,last:'2099-1-1'}; S.items[1]=500000; S.items[2]=5000;
  for (const t of Object.keys(D.UNITS())) S.owned[t]={lv:30,star:3,eq:{}}; S.deck=['vac','blender','purifier','router','tv','gen']; document.getElementById('modal').hidden=true; document.getElementById('guide').hidden=true; D.persist(); D.renderLobby(); D.setTab('roster'); });
await sh('nu_roster.png');
// 전투: 4-3에 신규 4종 + 청소기 + 발전기
const r1=await p.evaluate((NEW)=>{ const D=window.__DZ; document.getElementById('modal').hidden=true; D.startStage(17); document.getElementById('btnStart').click(); const G=D.G; G.power=99;
  const spots=[]; for (let c=0;c<9;c++) for (let r=0;r<9;r++) spots.push([c,r]);
  const placed={}; for (const t of ['vac','blender',...NEW.slice(1),'gen']){ for (const [c,r] of spots){ if (D.canPlace(t,c,r)){ D.deploy(t,c,r); placed[t]=c+','+r; break; } } }
  const sk={}; let n=0; const seen=new Set();
  while(!G.over && n<30*200){ D.update(1/30); n++; for (const u of G.units) if (u.skillT>0 && !seen.has(u)){ seen.add(u); sk[u.type]=(sk[u.type]||0)+1; } for (const u of G.units) if (!(u.skillT>0)) seen.delete(u); if (n===30*40) window.__shotNow=1; }
  return {placed, skills:sk, life:G.life, over:G.over, t:(n/30).toFixed(0)}; },NEW);
console.log('battle 4-3', JSON.stringify(r1));
await p.evaluate(()=>{ const D=window.__DZ; document.getElementById('overlay').hidden=true; D.startStage(17); document.getElementById('btnStart').click(); const G=D.G; G.power=99;
  for (const t of ['vac','blender','purifier','router','tv','gen']) for (let c=0;c<9;c++){ let ok=false; for (let r=0;r<9;r++) if (D.canPlace(t,c,r)){ D.deploy(t,c,r); ok=true; break; } if (ok) break; }
  for (let i=0;i<30*35;i++) D.update(1/30); });
await sh('nu_battle.png');
// 정보창 + 스토리 탭
await p.evaluate(()=>{ const D=window.__DZ; document.getElementById('battle').hidden=true; document.getElementById('lobby').hidden=false; document.getElementById('overlay').hidden=true; D.setTab('roster'); document.querySelector('.ut[data-unit="tv"]').click(); });
await sh('nu_detail_tv.png');
await p.evaluate(()=>{ [...document.querySelectorAll('.dtabs button')].find(b=>b.dataset.k==='story').click(); }); await sh('nu_story_tv.png');
// 미니게임 4종 봇 없이 열기만(오류 확인)
for (const id of ['recipe','sort','signal','remote']){ await p.evaluate(id=>window.__DZ.mgPlay(id,()=>{},{name:id}),id); await p.click('#mgStart'); await new Promise(r=>setTimeout(r,1200)); await p.locator('.mg-box').screenshot({path:V+'nu_mg_'+id+'.png'}); await p.click('#mgSkip'); }
// 소환 화면
await p.evaluate(()=>{ document.getElementById('modal').hidden=true; window.__DZ.setTab('gacha'); }); await sh('nu_gacha.png');
console.log(errs.join('\n')||'no errors'); await b.close();
