// 스테이지별 HP 배율을 현재 값의 여러 배수로 바꿔 verify 방식(시드 1~3, 레벨 -3/0/+3)으로 청결도를 본다
//   node tools/sim/scan.mjs <시작> <끝> <배수 목록>   예) node tools/sim/scan.mjs 56 60 1,0.95,0.9,0.85,0.8
//   출력: ×배수(HP) 시드2개/시드3개 [−3Lv~+3Lv]. 경계(HP 5%에 청결도가 몇 칸씩 바뀌는 곳)를 피해 값을 고를 때 씀(10.16-l)
import { chromium } from 'playwright'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const [a,b,mulS='1,0.95,0.9,0.85,0.8']=process.argv.slice(2); const muls=mulS.split(',').map(Number);
const br=await chromium.launch(); const pg=await br.newPage(); pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await pg.addScriptTag({path:path.join(root,'tools/sim/bot.js')});
const RAR={vac:'R',robot:'R',fan:'R',gen:'R',ac:'SR',micro:'SR',pc:'SR',iron:'SR',stove:'SR',fridge:'SSR',washer:'SSR',boiler:'SSR'};
const decksFor=ch=>ch<=4?[['vac','robot','ac','micro','fan','gen']]:ch<=7?[['vac','iron','ac','micro','fan','gen'],['vac','robot','ac','micro','fan','gen']]:[['vac','iron','ac','micro','stove','gen'],['vac','iron','ac','micro','fan','gen'],['vac','iron','ac','stove','micro','pc']];
const stages=await pg.evaluate(()=>window.__DZ.STAGES().map(s=>({no:s.no,chapter:s.chapter,refLv:s.refLv,refStar:s.refStar,refGear:s.refGear,hpMul:s.hpMul,boss:s.boss})));
async function life(i,bonus,hpMul,seeds){ const st=stages[i]; let best=0;
  for (const deck of decksFor(st.chapter)){ const lvOf={},starOf={}; for (const t of deck){ const dn=RAR[t]==='R'?0:RAR[t]==='SR'?1:2; const s=Math.max(0,st.refStar-dn); starOf[t]=s; lvOf[t]=Math.max(1,Math.min(st.refLv+bonus,20+5*s)); }
    let tot=0; for (const seed of seeds) tot+=(await pg.evaluate(c=>window.simRun(c),{stage:i,deck,seed,hpMul,lvOf,starOf,gear:st.refGear})).life; best=Math.max(best,tot/seeds.length); }
  return best; }
for (let n=+a;n<=+b;n++){ const i=n-1, st=stages[i]; const row=[];
  for (const m of muls){ const hp=+(st.hpMul*m).toFixed(2);
    const v=await life(i,0,hp,[1,2]), v3=await life(i,0,hp,[1,2,3]), lo=await life(i,-3,hp,[1,2]), hi=await life(i,3,hp,[1,2]);
    row.push(`×${m}(${hp}) ${v.toFixed(1)}/${v3.toFixed(1)} [${lo.toFixed(1)}~${hi.toFixed(1)}]`); }
  console.log(st.no+(st.boss?'B':' '),row.join(' | '));
}
await br.close();
