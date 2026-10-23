// 사용법: node tools/sim/run.mjs [시작스테이지번호] [끝] [옵션JSON]
//   예) node tools/sim/run.mjs 1 15          → 1-1 ~ 3-5 를 기준 계정으로 돌려 남은 청결도 표
//       node tools/sim/run.mjs 16 30 '{"tune":1}'  → 별 2개(청결도 목표 7)가 나오게 HP 보정을 찾아 hp_k.json 에 저장
// 필요: Node 18+, npm i playwright (크롬이 설치돼 있어야 함)
import { chromium } from 'playwright';
import fs from 'fs'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const [a=1,b=75,optS='{}']=process.argv.slice(2); const opt=JSON.parse(optS);
const br=await chromium.launch(); const pg=await br.newPage();
pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0);
await pg.addScriptTag({path:path.join(here,'bot.js')});
const decksFor=(ch)=>{
  if (opt.decks) return opt.decks;
  if (ch<=4) return [['vac','robot','ac','micro','fan','gen']];
  if (ch<=7) return [['vac','iron','ac','micro','fan','gen'],['vac','robot','ac','micro','fan','gen']];
  return [['vac','iron','ac','micro','stove','gen'],['vac','iron','ac','micro','fan','gen'],['vac','iron','ac','stove','micro','pc']];
};
const RAR={vac:'R',robot:'R',fan:'R',gen:'R',ac:'SR',micro:'SR',pc:'SR',iron:'SR',stove:'SR',fridge:'SSR',washer:'SSR',boiler:'SSR'};
function account(st,deck,bonus=0){ // 기준 계정: R은 기준 돌파, SR은 1단계, SSR은 2단계 낮음 → 레벨 상한에 걸림
  const lvOf={}, starOf={};
  for (const t of deck){ const dn=RAR[t]==='R'?0:RAR[t]==='SR'?1:2; const star=Math.max(0,st.refStar-dn); starOf[t]=star; lvOf[t]=Math.max(1,Math.min(Math.round(st.refLv+bonus),20+5*star)); }
  return {lvOf,starOf,gear:st.refGear};
}
const stages=await pg.evaluate(()=>window.__DZ.STAGES().map(s=>({no:s.no,order:s.order,chapter:s.chapter,refLv:s.refLv,refStar:s.refStar,refGear:s.refGear,hpMul:s.hpMul,boss:s.boss})));
const seeds=opt.seeds||[1,2,3];
async function evalStage(i,hpMul,bonus=0){
  const st=stages[i]; let best=null;
  for (const deck of decksFor(st.chapter)){
    const acc=account(st,deck,bonus); const res=[];
    for (const seed of seeds) res.push(await pg.evaluate(c=>window.simRun(c),{stage:i,deck,seed,hpMul,...acc}));
    const life=res.reduce((x,r)=>x+r.life,0)/res.length, wins=res.filter(r=>r.win).length;
    if (!best || life>best.life) best={life,wins,deck,res};
  }
  return best;
}
const out={};
const target=opt.target??7;
for (let n=+a;n<=+b;n++){
  const i=n-1, st=stages[i];
  if (opt.tune){
    // HP 배율을 이분 탐색: 남은 청결도 평균이 목표(일반 7 · 보스 6) 이상인 가장 큰 값. 덱은 지금 값에서 가장 잘 버틴 편성으로 고정해 빠르게
    const tg=st.boss?(opt.bossTarget??6):target;
    const one=async(x,bonus=0)=>(await evalStage(i,x,bonus)).life;   // 편성 후보 중 가장 잘 버틴 것
    let lo=st.hpMul*0.35, hi=st.hpMul*2.6, bestX=lo;
    for (let it=0;it<8;it++){ const mid=Math.sqrt(lo*hi); const life=await one(mid); if (life>=tg){ lo=mid; bestX=mid; } else hi=mid; }
    const r=await evalStage(i,bestX), lower=await one(bestX,-2), upper=await one(bestX,+2);
    out[st.no]={hpMul:+bestX.toFixed(3),k:+(bestX/(1+0.05*(st.refLv-1)+0.12*st.refStar)).toFixed(3),life:+r.life.toFixed(1),lifeLvMinus2:+lower.toFixed(1),lifeLvPlus2:+upper.toFixed(1),deck:r.deck.join(',')};
    console.log(st.no,JSON.stringify(out[st.no]));
  } else {
    const r=await evalStage(i,null,opt.bonus||0);
    console.log(st.no,'Lv',st.refLv,'★',st.refStar,'hp',st.hpMul,'life',r.life.toFixed(1),'win',r.wins+'/'+seeds.length,r.deck.join(','),'|',r.res.map(x=>`${x.life}@${x.t}s[${x.comp}]`).join(' / '));
  }
}
if (opt.tune){ const f=path.join(here,'hp_k.json'); const prev=fs.existsSync(f)?JSON.parse(fs.readFileSync(f,'utf8')):{}; fs.writeFileSync(f,JSON.stringify(Object.assign(prev,out),null,1)); }
await br.close();
