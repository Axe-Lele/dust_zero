// 대청소 모드 버틴 시간 측정: node tools/sim/surv.mjs [레벨] [돌파]
import { chromium } from 'playwright'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const [lv=15,star=0,gear=0]=process.argv.slice(2).map(Number);
const br=await chromium.launch(); const pg=await br.newPage(); pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await pg.addScriptTag({path:path.join(here,'survbot.js')});
const runs=[
  {leader:'vac',deck:['vac','fan','ac','micro','gen','robot']},
  {leader:'ac',deck:['ac','fridge','vac','micro','fan','gen']},
  {leader:'robot',deck:['robot','gen','vac','ac','micro','fan']},
  {leader:'vac',deck:['vac','iron','stove','boiler','fan','gen']},
];
for (const r of runs){ for (const ex of [false,true]){
  const excl={}; if (ex) for (const t of r.deck) excl[t]={lv:0,star:0};
  const res=[]; for (const seed of [1,2,3]) res.push(await pg.evaluate(c=>window.survRun(c),{...r,lv,star,gear,seed,excl}));
  console.log(r.leader.padEnd(6), r.deck.join(','), ex?'전용장비 O':'전용장비 X', '평균', Math.round(res.reduce((a,x)=>a+x.t,0)/res.length)+'초', '|', res.map(x=>`${x.t}s Lv${x.lv} [${x.comps}] ${x.combos}`).join(' / '));
} }
await br.close();
