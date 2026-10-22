// 최종 보정: 기준 계정 결과가 청결도 5 미만이면 HP를 8%씩 낮추고(최대 6번), 10(완벽)이면 6%씩 올린다(6 아래로는 안 내려가게)
// 결과 배율은 adjust.json 에 { "4-1": 0.92, ... } 로 남김 → gen_tables.py 의 HP_K_LATE 에 곱해 반영
import { chromium } from 'playwright'; import fs from 'fs'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const [a=16,b=75]=process.argv.slice(2);
const br=await chromium.launch(); const pg=await br.newPage(); pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await pg.addScriptTag({path:path.join(here,'bot.js')});
const RAR={vac:'R',robot:'R',fan:'R',gen:'R',ac:'SR',micro:'SR',pc:'SR',iron:'SR',stove:'SR',fridge:'SSR',washer:'SSR',boiler:'SSR'};
const decksFor=ch=>ch<=4?[['vac','robot','ac','micro','fan','gen']]:ch<=7?[['vac','iron','ac','micro','fan','gen'],['vac','robot','ac','micro','fan','gen']]:[['vac','iron','ac','micro','stove','gen'],['vac','iron','ac','micro','fan','gen'],['vac','iron','ac','stove','micro','pc']];
const stages=await pg.evaluate(()=>window.__DZ.STAGES().map(s=>({no:s.no,chapter:s.chapter,refLv:s.refLv,refStar:s.refStar,refGear:s.refGear,hpMul:s.hpMul,boss:s.boss})));
async function life(i,hpMul){ const st=stages[i]; let best=0;
  for (const deck of decksFor(st.chapter)){ const lvOf={},starOf={}; for (const t of deck){ const dn=RAR[t]==='R'?0:RAR[t]==='SR'?1:2; const s=Math.max(0,st.refStar-dn); starOf[t]=s; lvOf[t]=Math.max(1,Math.min(st.refLv,20+5*s)); }
    let tot=0; for (const seed of [1,2,3]) tot+=(await pg.evaluate(c=>window.simRun(c),{stage:i,deck,seed,lvOf,starOf,gear:st.refGear,hpMul})).life; best=Math.max(best,tot/3); }
  return best; }
const f=path.join(here,'adjust.json'); const out=fs.existsSync(f)?JSON.parse(fs.readFileSync(f,'utf8')):{};
for (let n=+a;n<=+b;n++){ const i=n-1, st=stages[i]; let m=1, l=await life(i,st.hpMul); const l0=l;
  for (let k=0;k<6 && l<5;k++){ m*=0.92; l=await life(i,st.hpMul*m); }
  for (let k=0;k<3 && l>=9.9;k++){ const l2=await life(i,st.hpMul*m*1.06); if (l2<6) break; m*=1.06; l=l2; }
  out[st.no]=+((out[st.no]||1)*m).toFixed(3);
  console.log(st.no,'life',l0.toFixed(1),'->',l.toFixed(1),'x',m.toFixed(3));
}
fs.writeFileSync(f,JSON.stringify(out,null,1)); await br.close();
