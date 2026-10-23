// 심화 모드 HP 맞추기(10.21-h): node tools/sim/hard_cal.mjs <시작> <끝> [tune=1]
// 심화 n 을 그 판의 기준 계정(stage_hard.refLv·refStar·refGear, 등급별 돌파는 verify.mjs 규칙)으로 2판씩 돌려 청결도(남은 생명)를 본다.
// tune=1 이면 청결도 6~8.5 밖인 판의 HP 배율을 조금씩 바꿔 다시 돌리고, 찾은 배율을 tools/sim/hard_k.json 에 곱해 저장(gen_tables.py 가 읽음)
import { chromium } from 'playwright'; import path from 'path'; import url from 'url'; import fs from 'fs';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const [a=1,b=75,tune='0']=process.argv.slice(2);
const br=await chromium.launch(); const pg=await br.newPage(); pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.HSTAGES().length>0); await pg.addScriptTag({path:path.join(here,'bot.js')});
const RAR={vac:'R',robot:'R',fan:'R',gen:'R',ac:'SR',micro:'SR',pc:'SR',iron:'SR',stove:'SR'};
const decksFor=ch=>ch<=4?[['vac','robot','ac','micro','fan','gen']]:ch<=7?[['vac','iron','ac','micro','fan','gen'],['vac','robot','ac','micro','fan','gen']]:[['vac','iron','ac','micro','stove','gen'],['vac','iron','ac','micro','fan','gen'],['vac','iron','ac','stove','micro','pc']];
const hs=await pg.evaluate(()=>window.__DZ.HSTAGES().map(s=>({no:s.no,chapter:s.chapter,refLv:s.refLv,refStar:s.refStar,refGear:s.refGear,hpMul:s.hpMul,rule:s.rule})));
async function life(i,hp){ const st=hs[i]; let best=0;
  for (const deck of decksFor(Math.min(15,st.chapter+5))){ const lvOf={},starOf={}; for (const t of deck){ const dn=RAR[t]==='R'?0:RAR[t]==='SR'?1:2; const s=Math.max(0,st.refStar-dn); starOf[t]=s; lvOf[t]=Math.max(1,Math.min(st.refLv,20+5*s)); }
    let tot=0; for (const seed of [1,2]) tot+=(await pg.evaluate(c=>window.simRun(c),{stage:i,hard:true,deck,seed,lvOf,starOf,gear:st.refGear,hpMul:hp})).life; best=Math.max(best,tot/2); if (best>=10) break; }
  return best; }
const kf=path.join(here,'hard_k.json'); const K=fs.existsSync(kf)?JSON.parse(fs.readFileSync(kf,'utf8')):{};
let sum=0, cnt=0;
for (let n=+a;n<=+b;n++){ const i=n-1, st=hs[i]; let k=1, l=await life(i,st.hpMul*k); const log=[l];
  if (tune==='1') for (let r=0;r<4 && (l<6||l>8.5);r++){ k*= l<=0?.65 : l<6?.82 : l>=10?1.35 : 1.18; l=await life(i,st.hpMul*k); log.push(l); }
  if (tune==='1' && k!==1){ const cur=fs.existsSync(kf)?JSON.parse(fs.readFileSync(kf,'utf8')):{}; cur[String(n)]=+((K[String(n)]||1)*k).toFixed(3); fs.writeFileSync(kf,JSON.stringify(cur,null,1)); }
  sum+=l; cnt++;
  console.log(`${st.no.padEnd(9)} ${st.rule.padEnd(6)} Lv${st.refLv}★${st.refStar} g${st.refGear} hp ${(st.hpMul*k).toFixed(2).padStart(5)}  청결도 ${log.map(x=>x.toFixed(1)).join(' → ')}`);
}
console.log('평균', (sum/cnt).toFixed(2));
await br.close();
