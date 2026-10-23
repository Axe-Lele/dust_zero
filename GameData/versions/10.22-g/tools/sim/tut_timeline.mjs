// 1-1 튜토리얼 시간표(10.18-f): 안내가 뜨는 게임 시각(G.t)·그때 전력. 안내대로 바로 따라 하는 플레이어.
//   node tools/sim/tut_timeline.mjs   — 안내 사이에 할 일 없이 전력만 쌓이는 구간이 없는지 볼 때
import { chromium } from '/opt/npm-tools/node_modules/playwright/index.mjs';
import { fileURLToPath } from 'url'; import path from 'path';
const root=process.argv[2]||path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const b=await chromium.launch(); const p=await b.newPage({viewport:{width:430,height:930}});
const errs=[]; p.on('pageerror',e=>errs.push(e.message));
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
await p.goto('file://'+root+'/index.html#sim',{waitUntil:'domcontentloaded'});
await p.evaluate(()=>localStorage.clear()); await p.reload({waitUntil:'domcontentloaded'});
await p.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await sleep(600);
await p.click('#btnCta'); await sleep(400); await p.click('#btnStart'); await sleep(200);
await p.evaluate(()=>{ window.__DZ.G.speed=2; });
let last='', out=[];
for (let i=0;i<600;i++){
  const s=await p.evaluate(()=>{ const D=window.__DZ, G=D.G, t=document.getElementById('tut'); return {t:+G.t.toFixed(1), W:Math.floor(G.power)*100, hidden:t.hidden, txt:document.getElementById('tutText').textContent.slice(0,22), ok:!document.getElementById('tutOk').hidden, over:G.over, units:G.units.map(u=>u.type).join(','), tile:G.tutTile}; });
  if (s.over){ out.push(`끝 t=${s.t}`); break; }
  const key=s.hidden?'':s.txt; if (key!==last){ if (!s.hidden) out.push(`t=${String(s.t).padStart(5)}  ${String(s.W).padStart(5)}W  ${s.txt}  [${s.units}]`); last=key; }
  if (!s.hidden){ if (s.ok){ await sleep(400); await p.click('#tutOk').catch(()=>{}); }
    else if (s.tile){ const card=await p.$('.card.tut-focus'); if (card){ await card.click(); await sleep(100); const box=await (await p.$('#cv')).boundingBox(); const ts=box.width/7; await p.mouse.click(box.x+(s.tile[0]+.5)*ts, box.y+(s.tile[1]+.5)*ts); } } }
  await sleep(100);
}
console.log(out.join('\n')); console.log(errs.join('\n')||'no errors'); await b.close();
