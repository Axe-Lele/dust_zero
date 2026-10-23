// 기준 계정 / -3레벨 / +3레벨로 75스테이지를 돌려 남은 청결도(별)를 한 줄씩 출력
//   node tools/sim/verify.mjs [시작] [끝]
import { chromium } from 'playwright'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const [a=1,b=75]=process.argv.slice(2);
const br=await chromium.launch(); const pg=await br.newPage(); pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await pg.addScriptTag({path:path.join(here,'bot.js')});
const RAR={vac:'R',robot:'R',fan:'R',gen:'R',blender:'R',ac:'SR',micro:'SR',pc:'SR',iron:'SR',stove:'SR',purifier:'SR',router:'SR',fridge:'SSR',washer:'SSR',boiler:'SSR',tv:'SSR'};
const decksFor=ch=>ch<=4?[['vac','robot','ac','micro','fan','gen']]:ch<=7?[['vac','iron','ac','micro','fan','gen'],['vac','robot','ac','micro','fan','gen']]:[['vac','iron','ac','micro','stove','gen'],['vac','iron','ac','micro','fan','gen'],['vac','iron','ac','stove','micro','pc']];
const stages=await pg.evaluate(()=>window.__DZ.STAGES().map(s=>({no:s.no,chapter:s.chapter,refLv:s.refLv,refStar:s.refStar,refGear:s.refGear,hpMul:s.hpMul,boss:s.boss})));
async function life(i,bonus){ const st=stages[i]; let best=0;
  for (const deck of decksFor(st.chapter)){ const lvOf={},starOf={}; for (const t of deck){ const dn=RAR[t]==='R'?0:RAR[t]==='SR'?1:2; const s=Math.max(0,st.refStar-dn); starOf[t]=s; lvOf[t]=Math.max(1,Math.min(st.refLv+bonus,20+5*s)); }
    let tot=0; for (const seed of [1,2]) tot+=(await pg.evaluate(c=>window.simRun(c),{stage:i,deck,seed,lvOf,starOf,gear:st.refGear})).life; best=Math.max(best,tot/2); }
  return best; }
const stars=l=>l>=10?3:l>=6?2:l>=1?1:0;
const sum={};
for (let n=+a;n<=+b;n++){ const i=n-1, st=stages[i];
  const r0=await life(i,0), rm=await life(i,-3), rp=await life(i,3);
  console.log(`${st.no.padEnd(5)} Lv${String(st.refLv).padStart(2)}★${st.refStar} hp${st.hpMul.toFixed(2).padStart(5)}  기준 ${r0.toFixed(1).padStart(4)}(★${stars(r0)})  -3Lv ${rm.toFixed(1).padStart(4)}  +3Lv ${rp.toFixed(1).padStart(4)}${st.boss?'  보스':''}`);
}
await br.close();
