// 가전 하나를 바꿔 끼웠을 때 청결도 비교(10.21-f 신규 가전 밸런스용)
//   node tools/sim/unit_cmp.mjs "<스테이지 번호들>" "<기준 덱>" "<바꿀 가전=새 가전,...>" [판 수=3] [레벨 보정=0]
//   예) node tools/sim/unit_cmp.mjs "8,13,18,23" "vac,robot,ac,micro,fan,gen" "robot=blender,micro=purifier,micro=router" 3 -3
// 기준 계정(verify.mjs 와 같은 레벨·돌파 규칙)으로 덱 그대로 vs 한 칸만 바꾼 덱을 seed 1..N 평균으로 비교. 레벨 보정을 낮추면 차이가 잘 보임
import { chromium } from 'playwright'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const [stS,deckS,swS,nS='3',bS='0']=process.argv.slice(2);
const br=await chromium.launch(); const pg=await br.newPage(); pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await pg.addScriptTag({path:path.join(here,'bot.js')});
const RAR=await pg.evaluate(()=>Object.fromEntries(Object.entries(window.__DZ.UNITS()).map(([k,u])=>[k,u.rar])));
const stages=await pg.evaluate(()=>window.__DZ.STAGES().map(s=>({no:s.no,refLv:s.refLv,refStar:s.refStar,refGear:s.refGear})));
const base=deckS.split(','), swaps=swS.split(',').map(x=>x.split('=')), N=+nS, bonus=+bS;
async function life(i,deck){ const st=stages[i]; const lvOf={},starOf={};
  for (const t of deck){ const dn=RAR[t]==='R'?0:RAR[t]==='SR'?1:2; const s=Math.max(0,st.refStar-dn); starOf[t]=s; lvOf[t]=Math.max(1,Math.min(st.refLv+bonus,20+5*s)); }
  let tot=0; for (let seed=1;seed<=N;seed++) tot+=(await pg.evaluate(c=>window.simRun(c),{stage:i,deck,seed,lvOf,starOf,gear:st.refGear})).life; return tot/N; }
const head=['판   ','기준',...swaps.map(([a,b])=>`${a}→${b}`)]; console.log(head.join('\t'));
const sum=new Array(swaps.length+1).fill(0); const ns=stS.split(',').map(Number);
for (const n of ns){ const i=n-1, row=[stages[i].no.padEnd(5)]; const b0=await life(i,base); row.push(b0.toFixed(1)); sum[0]+=b0;
  for (let k=0;k<swaps.length;k++){ const [a,b]=swaps[k]; const d=base.map(t=>t===a?b:t); const v=await life(i,d); sum[k+1]+=v; row.push(v.toFixed(1)); }
  console.log(row.join('\t')); }
console.log(['평균 ',...sum.map(v=>(v/ns.length).toFixed(2))].join('\t'));
await br.close();
