// Runtime art checks in a fresh browser profile; never touches the user's save.
const { chromium }=require('playwright');
const fs=require('fs'),path=require('path'),{pathToFileURL}=require('url'),sharp=require('sharp');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'notes/art_10.22-f');
const manifest=JSON.parse(fs.readFileSync(path.join(root,'art/261022/manifest.json'),'utf8'));
(async()=>{
  fs.mkdirSync(out,{recursive:true});
  if(manifest.assets.length!==16) throw Error('Expected 16 SD assets');
  for(const a of manifest.assets){
    const im=sharp(path.join(root,'art/261022',a.id+'_sd.webp'));
    const {data,info}=await im.ensureAlpha().raw().toBuffer({resolveWithObject:true});
    let clear=0,solid=0;
    for(let i=3;i<data.length;i+=info.channels){ if(data[i]===0)clear++;if(data[i]>200)solid++; }
    if(clear<100||solid<100)throw Error(a.id+': invalid alpha silhouette');
  }
  const b=await chromium.launch({channel:'chrome',args:['--allow-file-access-from-files']});
  const p=await b.newPage({viewport:{width:430,height:930},deviceScaleFactor:2});
  const errors=[],failed=[];
  p.on('pageerror',e=>errors.push(e.message));
  p.on('requestfailed',r=>{if(r.url().includes('/art/'))failed.push(r.url());});
  await p.goto(pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
  await p.waitForFunction(()=>window.__DZ&&window.__DZ.S&&!document.getElementById('lobby').hidden);
  await p.evaluate(()=>{
    const D=window.__DZ,S=D.S; S.guide=null; S.guideDone={first:1,levelup:1,gacha:1}; S.tutOff=1;S.attAuto='2099-1-1';
    S.best=75; for(const t of Object.keys(D.UNITS()))S.owned[t]={lv:20,star:1,eq:{}};
    S.deck=['vac','robot','ac','micro','fan','gen'];document.getElementById('modal').hidden=true;
    D.renderLobby();D.setTab('home');
  });
  await p.waitForTimeout(800);
  await p.evaluate(()=>document.getElementById('modal').hidden=true);
  await p.screenshot({path:path.join(out,'home.png')});
  const ids=manifest.assets.map(a=>a.id);
  for(const id of ids){
    await p.evaluate(id=>window.__DZ.openArt(id),id);
    await p.waitForFunction(()=>[...document.querySelectorAll('#artOv img')].every(i=>i.complete&&i.naturalWidth>0));
    for(const tab of ['face','design']){
      await p.evaluate(tab=>{const buttons=[...document.querySelectorAll('#artTabs button')];(buttons.find(b=>b.dataset.k===tab)||buttons[2]).click();},tab);
      await p.waitForFunction(()=>[...document.querySelectorAll('#artOv img')].every(i=>i.complete&&i.naturalWidth>0));
    }
  }
  await p.evaluate(()=>window.__DZ.openArt('tv'));
  await p.waitForTimeout(150);await p.screenshot({path:path.join(out,'tv-detail.png')});
  await p.evaluate(()=>{document.getElementById('artOv').hidden=true;window.__DZ.setTab('roster');});
  await p.screenshot({path:path.join(out,'roster.png'),fullPage:true});
  await p.addScriptTag({path:path.join(root,'tools/sim/bot.js')});
  const battle=await p.evaluate(()=>window.simRun({stage:0,seed:1,deck:['vac','robot','ac','micro','fan','gen'],lv:3}));
  await p.evaluate(()=>{
    const D=window.__DZ;D.S.guide=null;D.S.guideDone={first:1,levelup:1,gacha:1};D.S.tutOff=1;
    document.getElementById('modal').hidden=true;document.getElementById('artOv').hidden=true;
    D.startStage(0);document.getElementById('btnStart').click();D.G.power=100;
    for(const [t,c,r] of [['vac',2,8],['gen',4,0],['ac',2,7],['micro',4,7],['fan',4,6]])if(D.canPlace(t,c,r))D.deploy(t,c,r);
  });
  await p.waitForTimeout(300);await p.screenshot({path:path.join(out,'battle.png')});
  if(errors.length||failed.length)throw Error(JSON.stringify({errors,failed}));
  const report={build:'10.22-f',assets:ids.length,alpha:'pass',artTabs:'16 characters, full/face/design loaded',fileProtocol:'pass',pageErrors:errors,failedArtRequests:failed,stage1:battle};
  fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));
  console.log(JSON.stringify(report));await b.close();
})().catch(e=>{console.error(e);process.exitCode=1;});
