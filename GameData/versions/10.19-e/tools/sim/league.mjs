// 가전 대청소 리그 밸런스: 단계 × 계정 수준별로 여러 판을 돌려 완주율·평균 승수를 본다 (10.18-c)
//   node tools/sim/league.mjs [판 수=40]
//   계정(편성 6종 = 상점 가전): 새내기(R·SR 6종 Lv.10★0) / 중간(8종 보유 Lv.25★1) / 고수(12종 보유 Lv.40★3 + 전용장비 ★1, SSR 3종 편성)
//   '성장 끔' 줄은 같은 계정에서 계정 성장(레벨·돌파)을 뺀 결과 → 성장이 얼마나 영향을 주는지
import { chromium } from 'playwright'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const N=+(process.argv[2]||40);
const br=await chromium.launch(); const pg=await br.newPage(); pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await pg.addScriptTag({path:path.join(here,'leaguebot.js')});
const mk=(types,lv,star)=>Object.fromEntries(types.map(t=>[t,{lv,star}]));
const ACC={
  '새내기': {owned:mk(['vac','robot','fan','gen','ac','micro'],10,0), deck:['vac','robot','fan','gen','ac','micro']},
  '중간':   {owned:mk(['vac','robot','fan','gen','ac','micro','iron','pc'],25,1), deck:['vac','robot','iron','ac','micro','gen']},
  '고수':   {owned:mk(['vac','robot','fan','gen','ac','micro','iron','pc','stove','fridge','washer','boiler'],40,3), excl:Object.fromEntries(['vac','ac','micro','iron'].map(t=>[t,{star:1}])), deck:['vac','iron','fridge','ac','washer','boiler']},
};
for (const tier of [1,2,3]) for (const [name,a] of Object.entries(ACC)) for (const growth of [1,0]){
  if (growth===0 && name==='새내기') continue;
  const rs=await pg.evaluate(({tier,a,growth,N})=>{ const out=[]; for (let s=1;s<=N;s++) out.push(window.leagueRun({tier,seed:s*7919,owned:a.owned,excl:a.excl,deck:a.deck,growth})); return out; },{tier,a,growth,N});
  const clear=rs.filter(r=>r.clear).length/N, wins=rs.reduce((x,r)=>x+r.wins,0)/N, life=rs.reduce((x,r)=>x+Math.max(0,r.life),0)/N;
  console.log(`단계${tier} ${name.padEnd(4)} ${growth?'      ':'성장끔'} 완주 ${(clear*100).toFixed(0).padStart(3)}%  평균 ${wins.toFixed(1)}승  남은 청결도 ${life.toFixed(1)}  예) ${rs[0].board}`);
}
await br.close();
