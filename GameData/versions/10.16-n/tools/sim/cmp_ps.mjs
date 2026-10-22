// 전력 스킬 있음/없음 비교: node tools/sim/cmp_ps.mjs [시작] [끝] [간격]
import { chromium } from 'playwright'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const [a=16,b=75,step=1]=process.argv.slice(2).map(Number);
const br=await chromium.launch(); const pg=await br.newPage(); pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await pg.addScriptTag({path:path.join(here,'bot.js')});
const RAR={vac:'R',robot:'R',fan:'R',gen:'R',ac:'SR',micro:'SR',pc:'SR',iron:'SR',stove:'SR',fridge:'SSR',washer:'SSR',boiler:'SSR'};
const decksFor=ch=>ch<=4?[['vac','robot','ac','micro','fan','gen']]:ch<=7?[['vac','iron','ac','micro','fan','gen'],['vac','robot','ac','micro','fan','gen']]:[['vac','iron','ac','micro','stove','gen'],['vac','iron','ac','micro','fan','gen'],['vac','iron','ac','stove','micro','pc']];
const stages=await pg.evaluate(()=>window.__DZ.STAGES().map(s=>({no:s.no,chapter:s.chapter,refLv:s.refLv,refStar:s.refStar,refGear:s.refGear})));
async function life(i,noPs,bonus=0){ const st=stages[i]; let best=0, use='';
  for (const deck of decksFor(st.chapter)){ const lvOf={},starOf={}; for (const t of deck){ const dn=RAR[t]==='R'?0:RAR[t]==='SR'?1:2; const s=Math.max(0,st.refStar-dn); starOf[t]=s; lvOf[t]=Math.max(1,Math.min(st.refLv+bonus,20+5*s)); }
    let tot=0, u=[]; for (const seed of [1,2]){ const r=await pg.evaluate(c=>window.simRun(c),{stage:i,deck,seed,lvOf,starOf,gear:st.refGear,noPs}); tot+=r.life; u.push(`${r.psUse.storm}/${r.psUse.boost}`); } if (tot/2>best){ best=tot/2; use=u.join(' '); } }
  return [best,use]; }
let sa=0,sb=0,n=0;
for (let k=a;k<=b;k+=step){ const i=k-1; const [x]=await life(i,true), [y,use]=await life(i,false); sa+=x; sb+=y; n++;
  console.log(stages[i].no.padEnd(5),'스킬 없음',x.toFixed(1).padStart(4),' 스킬 사용',y.toFixed(1).padStart(4),' (번개/증폭 횟수',use+')'); }
console.log('평균',(sa/n).toFixed(2),'→',(sb/n).toFixed(2));
await br.close();
