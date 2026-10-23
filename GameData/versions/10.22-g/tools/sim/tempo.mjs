// 전투 템포 측정(10.21-a): 스테이지마다 적 수·첫 등장·마지막 등장·판 길이(마지막 적이 목표까지 걸어가는 시간 포함)·
// 적 이동 속도(칸/초)·길 길이·첫 적이 목표에 닿기까지 걸리는 시간·초당 적 수.
//   node tools/sim/tempo.mjs [첫 판=1] [끝 판=10]
import { chromium } from 'playwright'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const [A=1,Z=10]=process.argv.slice(2).map(Number);
const br=await chromium.launch(); const pg=await br.newPage();
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0);
const rows=await pg.evaluate(({A,Z})=>{ const D=window.__DZ, E=D.ENEMY(), C=D.CFG(), out=[];
  for (let i=A-1;i<Z;i++){ D.loadStage(i); const ST=D.ST, sch=ST.sched, P=D.PATHS;
    const L=P.map(p=>p.L), sp=sch.map(s=>E[s.type].spd), trav=sch.map((s,k)=>(L[s.pi||0]||L[0])/E[s.type].spd);
    const first=Math.min(...sch.map(s=>s.t)), last=Math.max(...sch.map(s=>s.t)), end=Math.max(...sch.map((s,k)=>s.t+trav[k]));
    const reach=Math.min(...sch.map((s,k)=>s.t+trav[k]));
    out.push({no:ST.no, n:sch.length, first:+first.toFixed(1), last:+last.toFixed(1), end:+end.toFixed(0), reach:+reach.toFixed(1),
      spd:+(sp.reduce((a,b)=>a+b,0)/sp.length).toFixed(2), path:+(L.reduce((a,b)=>a+b,0)/L.length).toFixed(1), rate:+(sch.length/Math.max(1,last-first)).toFixed(2),
      kinds:[...new Set(sch.map(s=>s.type))].length, entr:P.length, startPow:ST.startPower||C.startPower, regen:C.regenPerSec}); }
  return out; },{A,Z});
console.log('판    적수 첫등장 마지막 판길이 첫도달 평균속도(칸/초) 길(칸) 초당적 종류 입구 시작전력 회복/초');
for (const r of rows) console.log(`${r.no.padEnd(5)} ${String(r.n).padStart(3)}  ${String(r.first).padStart(5)}  ${String(r.last).padStart(5)}  ${String(r.end).padStart(4)}초  ${String(r.reach).padStart(5)}  ${String(r.spd).padStart(5)}  ${String(r.path).padStart(5)}  ${String(r.rate).padStart(5)}  ${r.kinds}  ${r.entr}  ${r.startPow*100}W  ${r.regen*100}W`);
await br.close();
