// 먼지 타워 층 세기 맞추기(10.18-g): 층마다 '목표 계정'(towerAcc.mjs)이 겨우 깨는 추가 배율 m 을 찾아(이분 탐색) 보여 준다.
//   node tools/sim/tower_cal.mjs [첫 층=1] [끝 층=30]
//   출력의 '권장 floorAdj' = 지금 floorAdj × m × 여유(1~4층 0.6~0.85, 그 뒤 0.92, 보스 0.97) → gen_tables.py TOWER_FLOOR 마지막 칸에 넣음
import { chromium } from 'playwright'; import path from 'path'; import url from 'url'; import fs from 'fs';
import { towerAccount, towerTarget } from './towerAcc.mjs';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const A=+(process.argv[2]||1), Z=+(process.argv[3]||30);
const br=await chromium.launch(); const pg=await br.newPage(); pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await pg.addScriptTag({path:path.join(here,'towerbot.js')});
const FL=JSON.parse(fs.readFileSync(path.join(root,'data','tower_floor.json'),'utf8'));
const margin=f=>f<=4?[.6,.7,.78,.85][f-1]:f%10===0?.97:.92;
const out=[];
for (let f=A;f<=Z;f++){
  const n=towerTarget(f), acc=towerAccount(root,n);
  const clear=async m=>(await pg.evaluate(c=>window.towerRun(c),{floor:f,...acc,seed:7,mul:m})).clear;
  let lo=.05, hi=20; if (!await clear(lo)) { console.log(f,'못 깸(배율 0.05)'); continue; }
  for (let i=0;i<14;i++){ const m=Math.sqrt(lo*hi); if (await clear(m)) lo=m; else hi=m; }
  const F=FL[f-1], adjNow=1;   // 지금 표의 floorAdj 는 hpMul/공식으로 역산하지 않고 1로 둔 상태 기준
  const rec=+(lo*margin(f)).toFixed(2);
  out.push([f,rec]); console.log(`${String(f).padStart(2)}층 ${F.rule.padEnd(8)} 목표 ${Math.round(n)}판(${acc.label})  겨우 깨는 배율 ×${lo.toFixed(2)}  → 권장 floorAdj ${rec}`);
}
console.log(JSON.stringify(out));
await br.close();
