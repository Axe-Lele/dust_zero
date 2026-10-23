const {chromium}=require('playwright');
const fs=require('fs'),path=require('path'),{pathToFileURL}=require('url');
const root=path.resolve(__dirname,'../..'),dir=path.join(root,'notes/motion_10.22-g');
const capture=process.argv.includes('--capture');
(async()=>{
  const b=await chromium.launch({channel:'chrome',args:['--allow-file-access-from-files']});
  const p=await b.newPage();
  const errors=[];p.on('pageerror',e=>errors.push(e.message));
  await p.goto(pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
  await p.waitForFunction(()=>window.__DZ&&window.__DZ.S);
  await p.addScriptTag({path:path.join(root,'tools/sim/bot.js')});
  const rows=await p.evaluate(()=>window.__DZ.STAGES().map(s=>({no:s.no,chapter:s.chapter,refLv:s.refLv,refStar:s.refStar,refGear:s.refGear})));
  const rarity={vac:0,robot:0,fan:0,gen:0,ac:1,micro:1,iron:1,stove:1,pc:1};
  const decks=ch=>ch<=4?[['vac','robot','ac','micro','fan','gen']]:ch<=7?[['vac','iron','ac','micro','fan','gen'],['vac','robot','ac','micro','fan','gen']]:[['vac','iron','ac','micro','stove','gen'],['vac','iron','ac','micro','fan','gen'],['vac','iron','ac','stove','micro','pc']];
  const results=[];
  for(let i=0;i<rows.length;i++){
    const s=rows[i],runs=[];
    for(const bonus of [0,-3,3])for(const deck of decks(s.chapter))for(const seed of [1,2]){
      const lvOf={},starOf={};
      for(const t of deck){starOf[t]=Math.max(0,s.refStar-rarity[t]);lvOf[t]=Math.max(1,Math.min(s.refLv+bonus,20+5*starOf[t]));}
      runs.push(await p.evaluate(cfg=>window.simRun(cfg),{stage:i,deck,seed,lvOf,starOf,gear:s.refGear}));
    }
    results.push({stage:s.no,runs});if((i+1)%15===0)console.log((capture?'before':'after')+': '+(i+1)+'/75');
  }
  await b.close();if(errors.length)throw Error(errors.join('\n'));
  const filename=path.join(dir,capture?'regression-before.json':'regression-after.json');
  fs.writeFileSync(filename,JSON.stringify(results));
  if(!capture){const before=JSON.parse(fs.readFileSync(path.join(dir,'regression-before.json'),'utf8'));if(JSON.stringify(before)!==JSON.stringify(results))throw Error('Stage regression differs');console.log('PASS: all 75 stages, +/-3 levels, both seeds, all reference decks identical.');}
})().catch(e=>{console.error(e);process.exitCode=1;});
