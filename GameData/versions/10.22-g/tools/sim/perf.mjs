// 10.18-b 성능 측정: 실제 게임 루프(rAF)로 전투·대청소를 돌려 프레임당 시간·캔버스 그리기 횟수·CPU 상위 함수를 본다
//   node tools/sim/perf.mjs battle|surv [측정 초]      (FF=360 → 대청소를 360초까지 미리 돌린 뒤 측정)
//   battle: 15-4 에 Lv.1 가전 8대를 깔고 40초 진행 후(적이 쌓인 상태) · surv: 청소기 리더 무적, 레벨 업은 첫 선택지
//   출력: per frame(layout·style·script·task ms) / canvas calls / frames(avg·p95·max) / 함수별 self 시간 %
//   task ms 는 헤드리스(소프트웨어 그리기) 기준이라 실제 기기와 절대값은 다름. 고치기 전·후 비교용
import { chromium } from 'playwright'; import path from 'path'; import url from 'url';
import fs from 'fs';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const [mode='battle', secs='8']=process.argv.slice(2);
const b=await chromium.launch({args:['--disable-gpu-vsync','--disable-frame-rate-limit']}); const p=await b.newPage({viewport:{width:430,height:930},deviceScaleFactor:2});
const errs=[]; p.on('pageerror',e=>errs.push(e.message));
await p.addInitScript(()=>{ const raf=window.requestAnimationFrame.bind(window); window.__FT=[];
  window.requestAnimationFrame=cb=>raf(ts=>{ const a=performance.now(); cb(ts); window.__FT.push(performance.now()-a); }); });
await p.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await p.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0);
await p.evaluate(s=>{ window.__SECS=s; },process.env.FF||'360');
await p.evaluate(mode=>{ const D=window.__DZ, S=D.S; S.guide=null; S.tutOff=1; S.best=75;
  const deck=['vac','iron','ac','micro','stove','gen']; for (const t of deck) S.owned[t]={lv:1,star:0,eq:{}}; S.deck=deck.slice(); D.persist();
  if (mode==='battle'){
    D.startStage(74); document.getElementById('btnStart').click();
    const G=D.G; G.power=99; G.life=1e9; G.speed=2;
    // 놓을 수 있는 칸에 가전 깔기(약하게: Lv1)
    let n=0; for (let r=0;r<10&&n<G.maxUnits;r++) for (let c=0;c<7&&n<G.maxUnits;c++){ const t=deck[n%deck.length]; if (D.canPlace(t,c,r)){ G.power=99; D.deploy(t,c,r); n++; } }
    // 적이 쌓이게 시간 당겨 두기
    for (let i=0;i<60*40;i++) D.update(1/60);
  } else {
    D.startSurv('vac'); const V=D.V; V.leader.hp=V.leader.maxHp=1e9;
    const pick=()=>{ while (V.pend>0){ D.applyOpt(D.survOptions()[0]); V.pend--; } V.picking=false; const bx=document.getElementById('svPick'); if (bx) bx.hidden=true; };
    for (let i=0;i<60*+window.__SECS;i++){ pick(); D.survUpdate(1/60); } pick(); V.auto=null;
    setInterval(pick,50);
  }
}, mode);
const info0=await p.evaluate(mode=>{ const D=window.__DZ; return mode==='battle'?{enemies:D.G.enemies.length,units:D.G.units.length,t:D.G.t|0}:{enemies:D.V.enemies.length,t:D.V.t|0,picking:D.V.picking,paused:D.V.paused}; },mode);
console.log(mode,'state',JSON.stringify(info0));
const cdp=await p.context().newCDPSession(p); await cdp.send('Profiler.enable'); await cdp.send('Profiler.setSamplingInterval',{interval:200});
await cdp.send('Performance.enable'); const pm0=Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map(m=>[m.name,m.value]));
await p.evaluate(()=>{ window.__FT.length=0; }); await cdp.send('Profiler.start');
await new Promise(r=>setTimeout(r,+secs*1000));
const {profile}=await cdp.send('Profiler.stop');
const pm1=Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map(m=>[m.name,m.value]));
const nf0=await p.evaluate(()=>window.__FT.length);
console.log('per frame: layouts',((pm1.LayoutCount-pm0.LayoutCount)/nf0).toFixed(2),'styleRecalcs',((pm1.RecalcStyleCount-pm0.RecalcStyleCount)/nf0).toFixed(2),'layout ms',((pm1.LayoutDuration-pm0.LayoutDuration)*1000/nf0).toFixed(3),'style ms',((pm1.RecalcStyleDuration-pm0.RecalcStyleDuration)*1000/nf0).toFixed(3),'script ms',((pm1.ScriptDuration-pm0.ScriptDuration)*1000/nf0).toFixed(3),'task ms',((pm1.TaskDuration-pm0.TaskDuration)*1000/nf0).toFixed(3));
const ft=await p.evaluate(()=>window.__FT.slice());
const calls=await p.evaluate(async()=>{ const P=CanvasRenderingContext2D.prototype, C={}; const names=['stroke','fill','fillText','strokeText','beginPath','drawImage','save','createRadialGradient','createLinearGradient','arc','ellipse','fillRect','setLineDash'];
  const orig={}; for (const n of names){ orig[n]=P[n]; P[n]=function(...a){ C[n]=(C[n]||0)+1; return orig[n].apply(this,a); }; }
  const d=Object.getOwnPropertyDescriptor(P,'shadowBlur'); let sb=0; Object.defineProperty(P,'shadowBlur',{set(v){ if (v) sb++; d.set.call(this,v); },get(){ return d.get.call(this); },configurable:true});
  const f0=window.__FT.length; await new Promise(r=>setTimeout(r,2000)); const nf=window.__FT.length-f0;
  for (const n of names) P[n]=orig[n]; Object.defineProperty(P,'shadowBlur',d);
  const o={frames:nf}; for (const n of names) if (C[n]) o[n]=Math.round(C[n]/nf); o.shadowBlur=Math.round(sb/nf); return o; });
console.log('canvas calls / frame', JSON.stringify(calls));
const info1=await p.evaluate(mode=>{ const D=window.__DZ; return mode==='battle'?{enemies:D.G.enemies.length,parts:D.G.parts.length,fx:D.G.fx.length,texts:D.G.texts.length}:{enemies:D.V.enemies.length,picking:D.V.picking}; },mode);
ft.sort((a,b)=>a-b); const avg=ft.reduce((a,b)=>a+b,0)/ft.length;
console.log('frames',ft.length,'avg ms',avg.toFixed(2),'p50',ft[ft.length>>1].toFixed(2),'p95',ft[Math.floor(ft.length*.95)].toFixed(2),'max',ft[ft.length-1].toFixed(2),JSON.stringify(info1));
// self time by function
const self={}; const byId={}; for (const n of profile.nodes) byId[n.id]=n;
const dts=profile.timeDeltas; const cnt={}; profile.samples.forEach((id,i)=>{ cnt[id]=(cnt[id]||0)+(dts[i]||0); });
for (const [id,us] of Object.entries(cnt)){ const n=byId[id]; const k=(n.callFrame.functionName||'(anon)')+':'+n.callFrame.lineNumber; self[k]=(self[k]||0)+us; }
const total=Object.values(self).reduce((a,b)=>a+b,0);
console.log(Object.entries(self).sort((a,b)=>b[1]-a[1]).slice(0,22).map(([k,v])=>`${(v/total*100).toFixed(1)}% ${k}`).join('\n'));
fs.writeFileSync(path.join(here,`perf_${mode}.cpuprofile`),JSON.stringify(profile));   // 크롬 개발자 도구 Performance 탭에 끌어다 놓으면 볼 수 있음(압축에 넣지 않음)
console.log(errs.join('\n')||'no errors'); await b.close();
