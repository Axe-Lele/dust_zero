// 첫 경험 따라가기(10.22-b): 새 계정으로 화면 안내를 누르며 1-1 → 2-1까지. 로비로 돌아올 때마다 떠 있는 창·토스트·퀘스트 바·탭 점을 기록하고 찍는다.
//   node tools/sim/ftue.mjs <스크린샷 폴더/> [끝 판=6]   전투는 바로 이긴 것으로 처리(배치 없이)
import { chromium } from 'playwright'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const V=process.argv[2]||'/tmp/', END=+(process.argv[3]||6);
const b=await chromium.launch(); const p=await b.newPage({viewport:{width:430,height:930},deviceScaleFactor:1});
const errs=[]; p.on('pageerror',e=>errs.push('PAGEERR '+e.message+' @'+(e.stack||'').split('\n')[1]));
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
await p.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await p.evaluate(()=>localStorage.clear()); await p.reload({waitUntil:'domcontentloaded'});
await p.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0); await sleep(900);
let n=0, last='';
const snap=async tag=>{ n++; const f=`f_${String(n).padStart(2,'0')}_${tag}.png`; await p.screenshot({path:V+f}); return f; };
const look=()=>p.evaluate(()=>{ const D=window.__DZ, S=D.S, G=D.G, $=id=>document.getElementById(id), vis=id=>$(id)&&!$(id).hidden;
  const mt=vis('modal')?($('mbox').querySelector('h2,h3')||{textContent:'?'}).textContent:'';
  return {best:S.best, flow:S.guide&&S.guide.flow, step:S.guide&&S.guide.step, gvis:vis('guide'), gtext:$('gText').textContent.slice(0,50), battle:vis('battle'), over:!!(G&&G.over), running:!!(G&&G.running), ov:vis('overlay'),
    ovh:vis('overlay')?($('ovCard').querySelector('h2')||{textContent:''}).textContent:'', modal:mt, toast:vis('toast')?$('toast').textContent:'', qbar:vis('qBar')?$('qBar').textContent:'', idleOv:vis('idleOv'), reveal:vis('revealOv'), rw:vis('rwOv'), rwt:vis('rwOv')?$('rwTitle').textContent:'',
    tutvis:vis('tut'), tuttext:vis('tut')?$('tutText').textContent.slice(0,40):'', alerts:[...document.querySelectorAll('.tab.alert')].map(x=>x.dataset.tab).join(','),
    homeAlerts:[...document.querySelectorAll('#homeUi .hbtn.alert')].map(x=>x.id).join(','), tab:([...document.querySelectorAll('.tab')].find(x=>x.getAttribute('aria-selected')==='true')||{dataset:{}}).dataset.tab}; });
const log=[];
for (let i=0;i<400;i++){
  const s=await look();
  const key=JSON.stringify([s.best,s.flow,s.step,s.gvis,s.battle,s.over,s.ov,s.modal,s.tutvis,s.tuttext,s.reveal,s.rw,s.rwt,s.qbar,s.tab]);
  if (key!==last){ last=key; const f=await snap(s.battle?'b':'l');
    log.push(`${f}  best=${s.best} ${s.battle?'[전투]':'[로비:'+s.tab+']'} ${s.flow?`안내 ${s.flow}#${s.step}${s.gvis?' «'+s.gtext+'»':''}`:''}${s.tutvis?' 튜토«'+s.tuttext+'»':''}${s.ov?' 창«'+s.ovh+'»':''}${s.modal?' 모달«'+s.modal+'»':''}${s.reveal?' 등장연출':''}${s.rw?' 보상창«'+s.rwt+'»':''}${s.toast?' 토스트«'+s.toast.slice(0,40)+'»':''}${!s.battle&&s.qbar?' 퀘스트바«'+s.qbar.slice(0,30)+'»':''}${s.alerts?' 탭점:'+s.alerts:''}${s.homeAlerts?' 홈점:'+s.homeAlerts:''}`); }
  if (s.best>=END && !s.battle && !s.flow && !s.modal) break;
  if (s.rw){ await p.click('#rwOk').catch(()=>{}); await sleep(300); continue; }
  if (s.reveal){ await p.click('#revealOv').catch(()=>{}); await sleep(400); continue; }
  if (s.gvis){ const r=await p.evaluate(()=>{ const e=document.getElementById('gRing').getBoundingClientRect(); return {x:e.left+e.width/2,y:e.top+e.height/2}; }); await p.mouse.click(r.x,r.y); await sleep(450); continue; }
  if (s.battle && s.running && !s.over){ await p.evaluate(()=>{ const D=window.__DZ, G=D.G; for (let k=0;k<30*8;k++){ if (G.tut){ document.getElementById('tutOk').click(); } D.update(1/30); if (G.over) break; } if (!G.over){ G.life=10; D.end(true); } }); await sleep(500); continue; }
  if (s.battle && s.tutvis){ await p.evaluate(()=>document.getElementById('tutOk').click()); await sleep(200); continue; }
  if (s.battle && !s.running && !s.over && s.ov){ await p.click('#btnStart').catch(()=>{}); await sleep(300); continue; }
  if (s.battle && s.over){ await p.click('#btnLobby').catch(()=>{}); await sleep(700); continue; }
  if (s.modal){ await p.evaluate(()=>{ const m=document.getElementById('mbox'); const c=m.querySelector('#attClaim,.primary:not([disabled]),#btnClose,.secondary'); if (c) c.click(); else document.getElementById('modal').hidden=true; }); await sleep(400); continue; }
  if (s.idleOv){ await p.evaluate(()=>{ const c=document.querySelector('#idleOv .primary:not([disabled]), #idleClaim'); if (c) c.click(); document.getElementById('idleOv').hidden=true; }); await sleep(300); continue; }
  if (s.qbar && /받기$/.test(s.qbar)){ await p.click('#qBar'); await sleep(500); continue; }
  if (s.tab!=='home'){ await p.evaluate(()=>window.__DZ.setTab('home')); await sleep(300); continue; }
  await p.click('#btnCta').catch(()=>{}); await sleep(700);
}
console.log(log.join('\n'));
console.log(errs.join('\n')||'no errors'); await b.close();
