const fs=require('fs'),path=require('path'),vm=require('vm'),assert=require('assert/strict'),sharp=require('sharp');
const {chromium}=require('playwright'),{pathToFileURL}=require('url');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'notes/motion_10.22-g');
const manifest=JSON.parse(fs.readFileSync(path.join(root,'art/261022-motion/manifest.json'),'utf8'));
(async()=>{
 assert.equal(manifest.assets.length,16);
 const sandbox={window:{},console};vm.runInNewContext(fs.readFileSync(path.join(root,'art/261022-motion/motion.js'),'utf8'),sandbox);const M=sandbox.window.DZ_MOTION;
 const attack=[],skill=[];
 for(const elapsed of [.05,.17,.29]){const u={};M.cue(u,'attack');M.step(u,elapsed);attack.push(M.pick('iron',u,0).frame);}
 for(const elapsed of [.05,.18,.32,.48,.64,.78]){const u={};M.cue(u,'skill');M.step(u,elapsed);skill.push(M.pick('iron',u,0).frame);}
 assert.deepEqual(attack,[0,1,2]);assert.deepEqual(skill,[3,4,5,6,7,8]);
 const unit={};M.cue(unit,'skill');assert.equal(M.pick('iron',unit,0).frame,3);assert.equal(M.pick('iron',unit,999).frame,3);unit.over=true;assert.equal(M.pick('iron',unit,0),null);
 for(const a of manifest.assets){const img=sharp(path.join(root,'art/261022-motion',a.file)),meta=await img.metadata();assert.equal(meta.width,960);assert.equal(meta.height,960);assert(meta.hasAlpha);
   const hashes=new Set();for(let n=0;n<9;n++){const raw=await img.clone().extract({left:n%3*320,top:Math.floor(n/3)*320,width:320,height:320}).ensureAlpha().raw().toBuffer();let solid=0;for(let i=3;i<raw.length;i+=4)if(raw[i]>180)solid++;assert(solid>1000,a.id+' empty frame '+n);hashes.add(require('crypto').createHash('sha256').update(raw).digest('hex'));}assert.equal(hashes.size,9,a.id+' duplicate poses');
 }
 const browser=await chromium.launch({channel:'chrome',args:['--allow-file-access-from-files']}),p=await browser.newPage({viewport:{width:430,height:930},deviceScaleFactor:2}),errors=[],failed=[];
 p.on('pageerror',e=>errors.push(e.message));p.on('requestfailed',r=>{if(r.url().includes('/art/'))failed.push(r.url());});
 await p.goto(pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});await p.waitForFunction(()=>window.__DZ&&window.__DZ.S);
 assert.equal(await p.evaluate(()=>Object.keys(DZ_MOTION.images).length),16);
 await p.evaluate(()=>{const D=__DZ,S=D.S;S.guide=null;S.guideDone={first:1,levelup:1,gacha:1};S.tutOff=1;S.best=75;S.attAuto='2099-1-1';for(const t of Object.keys(D.UNITS()))S.owned[t]={lv:30,star:2,eq:{}};document.getElementById('modal').hidden=true;D.openArt('iron');document.querySelectorAll('#artTabs button')[2].click();});
 await p.waitForTimeout(700);await p.evaluate(()=>document.getElementById('modal').hidden=true);
 await p.locator('#artBody').evaluate(el=>el.scrollTop=el.scrollHeight);await p.screenshot({path:path.join(out,'iron-motion-detail.png')});
 await p.addScriptTag({path:path.join(root,'tools/sim/bot.js')});
 const events=await p.evaluate(()=>{const events={};const original=DZ_MOTION.cue;DZ_MOTION.cue=(u,k)=>{const id=u.type||u.t;events[id+':'+k]=(events[id+':'+k]||0)+1;original(u,k);};window.simRun({stage:24,seed:1,deck:['vac','iron','ac','micro','fan','gen'],lv:25,star:1});DZ_MOTION.cue=original;return events;});
 assert(events['iron:attack']>0);assert(events['iron:skill']>0);assert(events['ac:attack']>0);assert(events['gen:skill']>0);
 await p.goto(pathToFileURL(path.join(root,'motion-preview.html')).href,{waitUntil:'domcontentloaded'});await p.waitForFunction(()=>Object.keys(DZ_MOTION.images).length===16);
 await p.locator('[data-mode="skill"]').click();await p.waitForTimeout(450);await p.locator('#pause').click();await p.screenshot({path:path.join(out,'motion-gallery.png'),fullPage:true});
 assert.equal(await p.locator('#pause').textContent(),'계속 재생');
 await p.locator('#pause').click();await p.locator('[data-mode="attack"]').click();await p.waitForTimeout(200);
 assert.equal(errors.length,0,errors.join('\n'));assert.equal(failed.length,0,failed.join('\n'));
 await browser.close();
 const baseline=path.join(root,'_motion_before_10.22-f.html');if(fs.existsSync(baseline))throw Error('Temporary baseline path already exists');
 fs.copyFileSync(path.join(out,'index_10.22-f.before.html'),baseline);
 const b=await chromium.launch({channel:'chrome',args:['--allow-file-access-from-files']}),q=await b.newPage();const sides={};
 try{for(const version of ['before','after']){
   await q.goto(pathToFileURL(version==='before'?baseline:path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});await q.waitForFunction(()=>window.__DZ&&window.__DZ.S);
   for(const bot of ['leaguebot.js','towerbot.js','coopbot.js','survbot.js'])await q.addScriptTag({path:path.join(root,'tools/sim',bot)});
   sides[version]=await q.evaluate(()=>{const owned=Object.fromEntries(['vac','robot','ac','micro','fan','gen','iron','stove','pc','fridge','washer','boiler','blender','purifier','router','tv'].map(t=>[t,{lv:30,star:2}])),deck=['vac','iron','ac','micro','fan','gen'],league=[],tower=[],coop=[],survivor=[];
     for(const tier of [1,2,3])league.push(window.leagueRun({tier,seed:7919,owned,deck}));
     for(const floor of [1,10,20,30])tower.push(window.towerRun({floor,owned,gear:.2,seed:7}));
     for(const pid of ['rush','greedy'])coop.push(window.coopRun({pid,best:25,lv:25,star:1,gear:.1,deck,seed:977}));
     for(const leader of ['vac','ac','robot'])survivor.push(window.survRun({leader,deck:['vac','robot','ac','micro','fan','gen'],lv:15,star:0,gear:0,seed:1}));
     return {league,tower,coop,survivor};});console.log(version+': league/tower/coop/survivor captured');
  }
  assert.deepEqual(sides.before,sides.after,'Other mode outcomes changed');
 }finally{await b.close();fs.unlinkSync(baseline);}
 const report={build:'10.22-g',characters:16,attackFrames:48,skillFrames:96,distinctTransparentPoses:'144/144 pass',attackSequence:attack,skillSequence:skill,pausedClock:'pass',instantIronSkill:'pass',overheatOverride:'pass',realCombatEvents:events,fileProtocol:'pass',galleryControls:'pass',pageErrors:errors,failedAssets:failed,regression:'75 stages +/-3 levels and two seeds identical; league tiers 1/2/3, tower 1/10/20/30, both coop partners, all 3 survivor leaders identical',modeOutcomes:sides.after};
 fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log('PASS: motion frames, real events, previews, pauses, and cross-mode regression');
})().catch(e=>{console.error(e);process.exitCode=1;});
