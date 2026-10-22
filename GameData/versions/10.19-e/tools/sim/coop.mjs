// 협동 청소 밸런스(10.19-b 고리 판 · 랜덤 소환): 진행 단계별 기준 계정 × 파트너 성향 → 버틴 웨이브·판 길이(여러 씨앗 평균)
//   node tools/sim/coop.mjs [판 수=6] [진행 단계 목록, 예: 5,25,75] [설정 덮어쓰기 JSON, 예: '{"coopHpG":1.12}']
import { chromium } from 'playwright'; import path from 'path'; import url from 'url'; import fs from 'fs';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const N=+(process.argv[2]||6);
const br=await chromium.launch(); const pg=await br.newPage(); pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await pg.addScriptTag({path:path.join(here,'coopbot.js')});
if (process.argv[4]) await pg.evaluate(o=>Object.assign(window.__DZ.CFG(),o),JSON.parse(process.argv[4]));
const PIDS=(process.env.PIDS||'rush,greedy,solo').split(',');
const st=JSON.parse(fs.readFileSync(path.join(root,'data','stage.json'),'utf8')).sort((a,b)=>a.order-b.order);
const DECK={5:['vac','robot','ac','micro','gen','fan'],10:['vac','robot','ac','micro','gen','boiler'],15:['vac','robot','ac','micro','gen','boiler'],25:['vac','iron','ac','micro','gen','boiler'],45:['vac','iron','ac','micro','gen','boiler'],65:['vac','fridge','ac','washer','gen','boiler'],75:['vac','fridge','ac','washer','gen','boiler']};
for (const n of (process.argv[3]?process.argv[3].split(',').map(Number):[5,10,15,25,35,45,55,65,75])){
  const s=st[n-1], deck=DECK[n]||DECK[n<45?25:n<65?45:65];
  for (const pid of PIDS){
    const rs=await pg.evaluate(({c,N})=>{ const o=[]; for (let k=1;k<=N;k++) o.push(window.coopRun(Object.assign({},c,{seed:k*977}))); return o; },{c:{pid:pid==='solo'?'rush':pid,best:n,lv:s.refLv,star:s.refStar,gear:s.refGear,deck,solo:pid==='solo'},N});
    const ws=rs.map(r=>r.waves), avg=ws.reduce((a,b)=>a+b,0)/N;
    const ts=rs.map(r=>r.t), ta=ts.reduce((a,b)=>a+b,0)/N;
    console.log(`${String(n).padStart(2)}판(${s.no} Lv${s.refLv}★${s.refStar})  ${pid.padEnd(6)} 평균 ${avg.toFixed(1)}웨이브 [${ws.join(' ')}]  판 길이 ${Math.floor(ta/60)}분 ${Math.round(ta%60)}초  소환 나 ${rs[0].n}/파트너 ${rs[0].bn}  공격(나/파트너) ${rs[0].snap}`+(process.env.V?`\n   나: ${rs[0].me} | 파트너: ${rs[0].bot}`:''));
  }
}
await br.close();
