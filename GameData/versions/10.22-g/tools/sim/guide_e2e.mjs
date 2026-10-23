// 화면 안내 끝까지 따라가기(10.18-e): 새 계정 1-1 → 레벨 업 안내 → 1-2 → 소환 안내(무료 SSR) → 편성 → 홈.
//   node tools/sim/guide_e2e.mjs [full]   (full = 편성 6칸을 미리 채워 '빼기' 단계까지 확인). 전투는 바로 이긴 것으로 처리.
//   안내가 밝힌 곳(#gRing 가운데)을 눌러 따라가고, 단계 기록을 출력한다. 소환 안내 화면은 tools/sim/out/g_*.png
import { chromium } from 'playwright'; import path from 'path'; import url from 'url'; import fs from 'fs';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const V=path.join(here,'out')+'/'; fs.mkdirSync(V,{recursive:true});
const fullDeck=process.argv[2]==='full';
const b=await chromium.launch(); const p=await b.newPage({viewport:{width:430,height:930},deviceScaleFactor:2});
const errs=[]; p.on('pageerror',e=>errs.push('PAGEERR '+e.message+' @'+(e.stack||'').split('\n')[1]));
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
await p.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await p.evaluate(()=>localStorage.clear()); await p.reload({waitUntil:'domcontentloaded'});
await p.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await sleep(800);
if (fullDeck) await p.evaluate(()=>{ const S=window.__DZ.S; for (const t of ['robot','micro']) { S.owned[t]={lv:1,star:0}; if (S.deck.length<6) S.deck.push(t); } window.__DZ.persist(); });
let last='', shots=0, log=[];
for (let i=0;i<220;i++){
  const st=await p.evaluate(()=>{ const D=window.__DZ, S=D.S, g=D.guideStep(), G=D.G, bt=document.getElementById('battle'), mo=document.getElementById('modal'), ad=document.getElementById('attendOv');
    return {flow:S.guide&&S.guide.flow, step:S.guide&&S.guide.step, target:g&&g.target, text:document.getElementById('gText').textContent, gvis:!document.getElementById('guide').hidden,
      battle:!bt.hidden, running:!!(G&&G.running), over:!!(G&&G.over), best:S.best, tutPull:!!S.tutPull, deck:S.deck.join(','), modal:!mo.hidden, done:S.guideDone}; });
  const key=st.flow+':'+st.step+':'+st.gvis;
  if (key!==last){ log.push(`${st.flow}#${st.step} ${st.target} vis=${st.gvis} | ${st.text.slice(0,40)} | best=${st.best} pull=${st.tutPull} deck=${st.deck}`); last=key;
    if (st.flow==='gacha' && st.gvis){ await sleep(250); await p.screenshot({path:V+`g_${fullDeck?'full_':''}${st.step}.png`}); } }
  if (st.battle && st.running && !st.over){ await p.evaluate(()=>{ const G=window.__DZ.G; G.si=1e9; for (const e of G.enemies) e.dead=true; G.tutHold=false; document.getElementById('tut').hidden=true; G.tut=null; }); await sleep(300); continue; }
  if (st.battle && !st.running && !st.over && !st.gvis){ await p.click('#btnStart').catch(()=>{}); await sleep(300); continue; }
  if (st.gvis){ const r=await p.evaluate(()=>{ const e=document.getElementById('gRing').getBoundingClientRect(); return {x:e.left+e.width/2,y:e.top+e.height/2}; }); await p.mouse.click(r.x,r.y); await sleep(450); continue; }
  if (st.modal){ await p.evaluate(()=>{ const m=document.getElementById('modal'); const c=m.querySelector('#btnClose,.secondary,.primary'); if (c) c.click(); else m.hidden=true; }); await sleep(250); continue; }
  if (!st.flow && st.done && st.done.gacha) break;
  if (!st.flow && st.best>=2 && !st.tutPull && i>20){ log.push('!! 소환 안내가 시작되지 않음'); break; }
  await sleep(300);
}
console.log(log.join('\n'));
await p.screenshot({path:V+'g_end.png'});
console.log(await p.evaluate(()=>{ const el=document.querySelector('#btnCta'); const r=el.getBoundingClientRect(); return {cta:!!el, off:el.offsetParent&&el.offsetParent.id, rect:[r.top,r.bottom], curTabVis:[...document.querySelectorAll('.tabsec')].filter(x=>!x.hidden).map(x=>x.id), lobby:!document.getElementById('lobby').hidden, battle:!document.getElementById('battle').hidden, overlays:[...document.querySelectorAll('[id$=Ov],#modal')].filter(x=>!x.hidden).map(x=>x.id)}; }));
console.log(await p.evaluate(()=>({deck:window.__DZ.S.deck, owned:Object.keys(window.__DZ.S.owned), done:window.__DZ.S.guideDone, crystal:window.__DZ.S.items[2]})));
console.log(errs.join('\n')||'no errors'); await b.close();
