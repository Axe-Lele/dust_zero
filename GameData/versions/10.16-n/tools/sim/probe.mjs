import { chromium } from 'playwright'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const [no,hp,deckS,bonus='0']=process.argv.slice(2);
const br=await chromium.launch(); const pg=await br.newPage(); pg.on('pageerror',e=>console.error('PAGEERR',e.message));
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await pg.addScriptTag({path:path.join(here,'bot.js')});
const st=await pg.evaluate(no=>{ const s=window.__DZ.STAGES().find(x=>x.no===no); return {i:s.order-1,refLv:s.refLv,refStar:s.refStar,refGear:s.refGear,hpMul:s.hpMul,map:s.map,en:s.enemies}; },no);
const RAR={vac:'R',robot:'R',fan:'R',gen:'R',ac:'SR',micro:'SR',pc:'SR',iron:'SR',stove:'SR',fridge:'SSR',washer:'SSR',boiler:'SSR'};
const deck=deckS.split(','); const lvOf={},starOf={}; for (const t of deck){ const dn=RAR[t]==='R'?0:RAR[t]==='SR'?1:2; const s=Math.max(0,st.refStar-dn); starOf[t]=s; lvOf[t]=Math.min(st.refLv+(+bonus),20+5*s); }
console.log(st.map.join('\n')); console.log(st.en.join(','));
const r=await pg.evaluate(c=>window.simRun(c),{stage:st.i,deck,seed:1,hpMul:+hp||st.hpMul,lvOf,starOf,gear:st.refGear});
console.log(JSON.stringify(r,null,1)); await br.close();
