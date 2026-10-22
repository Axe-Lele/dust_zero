// 먼지 타워 밸런스(10.18-g): 진행 단계별 기준 계정(stage.refLv·refStar·refGear)으로 1층부터 올라가며 어디까지 깨는지 본다
//   node tools/sim/tower.mjs [층 수=30]     · 줄마다 1 = 깸(편성 4가지 중 하나라도 이김), . = 못 깸, 숫자 아래 = 이긴 편성 수
import { chromium } from 'playwright'; import path from 'path'; import url from 'url';
import { towerAccount } from './towerAcc.mjs';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const NF=+(process.argv[2]||30);
const br=await chromium.launch(); const pg=await br.newPage(); pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await pg.addScriptTag({path:path.join(here,'towerbot.js')});
// 기준 계정은 towerAcc.mjs(tower_cal.mjs 와 같음)
const want={10:5,20:10,30:14,40:18,50:22,60:26,75:30};
for (const n of [10,20,30,40,50,60,75]){
  const {owned,excl,gear,label}=towerAccount(root,n);
  const row=[], wins=[]; let reach=0;
  for (let f=1;f<=NF;f++){ const r=await pg.evaluate(c=>window.towerRun(c),{floor:f,owned,excl,gear,seed:7});
    row.push(r.clear?(r.bonus?'B':'1'):'.'); wins.push(r.wins); if (r.clear && reach===f-1) reach=f; }
  console.log(`${String(n).padStart(2)}판(${label})  도달 ${String(reach).padStart(2)}층(목표 ${want[n]})  ${row.join('')}`);
  console.log(`${' '.repeat(39)}${wins.join('')}`);
}
await br.close();
