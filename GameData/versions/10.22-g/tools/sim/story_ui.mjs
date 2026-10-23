// 가전 스토리 · 미니게임 확인(10.21-e): node tools/sim/story_ui.mjs <스크린샷 폴더/>
// 1) 미니게임 12종을 '손 안 댐' / '사람 흉내 봇(반응 지연)'으로 N판씩 돌려 성공률을 본다(손 안 대면 실패해야 함)
// 2) 스토리 탭 첫 방문 크리스탈, 1화 재생·보상, 2화 미니게임 진입·복귀, 잠금(호감도) 확인 + 스크린샷
import { chromium } from 'playwright'; import url from 'url';
const V=process.argv[2]||'/tmp/', N=+(process.argv[3]||20);
const b=await chromium.launch(); const p=await b.newPage({viewport:{width:430,height:930},deviceScaleFactor:2});
const errs=[]; p.on('pageerror',e=>errs.push(e.message+' '+(e.stack||'').split('\n')[1]));
await p.goto(url.pathToFileURL(new URL('../../index.html',import.meta.url).pathname).href+'#sim',{waitUntil:'domcontentloaded'}); await p.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0);
const res=await p.evaluate((N)=>{
  const MG=window.__DZ.MG, out={};
  const near=(st,arr,f)=>arr.filter(f||(()=>1)).sort((a,b)=>Math.hypot(a.x-st.px,a.y-st.py)-Math.hypot(b.x-st.px,b.y-st.py))[0];
  // 봇: 매 프레임 호출. me = {cd 반응 쿨다운}. 사람처럼 0.25~0.35초 반응 지연을 둔다
  const BOT={
    suck(G,st,me){ const d=near(st,st.d,d=>d.x>0&&d.x<360&&d.y>0&&d.y<414); st.hold=!!d; if (d){ st.px+=(d.x-st.px)*.12; st.py+=(d.y-st.py)*.12; } },
    patrol(G,st,me){ if (me.cd>0) return; me.cd=.3; const N=st.N, prev={}, q=[st.ry*N+st.rx], s0=q[0]; prev[s0]=-1; let goal=-1;
      while (q.length){ const i=q.shift(); if (st.dust.has(i)){ goal=i; break; } const x=i%N,y=Math.floor(i/N); for (const [dx,dy] of [[1,0],[-1,0],[0,1],[0,-1]]){ const nx=x+dx,ny=y+dy,j=ny*N+nx; if (nx<0||ny<0||nx>=N||ny>=N||j in prev||st.socks.has(j)) continue; prev[j]=i; q.push(j); } }
      if (goal<0) return; let c=goal; while (prev[c]!==s0) c=prev[c]; G.down(st,st.ox+(c%N)*st.cs+st.cs/2,st.oy+Math.floor(c/N)*st.cs+st.cs/2); },
    expiry(G,st,me){ if (me.cd>0) return; const i=st.slots.findIndex(v=>v&&v.bad&&v.life<1.8); if (i>=0){ me.cd=.3; G.down(st,30+(i%3)*100+50,60+Math.floor(i/3)*110+55); } },
    temp(G,st){ st.hold=st.T>24.2; },
    ding(G,st,me){ if (me.cd>0||st.wait>0) return; const m=(st.zone[0]+st.zone[1])/2; if (Math.abs(st.p-m)<(st.zone[1]-st.zone[0])*.35){ me.cd=.2; G.down(st); } },
    socks(G,st,me){ if (me.cd>0||st.wait>0) return; me.cd=.35; me.seen=me.seen||{}; st.c.forEach((c,i)=>{ if (c.open) me.seen[i]=c.v; });
      const cl=i=>G.down(st,24+(i%4)*80+40,60+Math.floor(i/4)*100+50);
      if (st.sel.length===1){ const a=st.sel[0], j=Object.keys(me.seen).map(Number).find(j=>j!==a&&me.seen[j]===st.c[a].v&&!st.c[j].done); if (j!=null) return cl(j); }
      if (!st.sel.length){ const ks=Object.keys(me.seen).map(Number).filter(j=>!st.c[j].done); for (const a of ks) for (const c2 of ks) if (a<c2&&me.seen[a]===me.seen[c2]) return cl(a); }
      const u=st.c.findIndex((c,i)=>!c.done&&!c.open&&!(i in me.seen)); if (u>=0) cl(u); else { const k=st.c.findIndex(c=>!c.done&&!c.open); if (k>=0) cl(k); } },
    bugs(G,st,me){ if (me.cd>0) return; const i=st.w.findIndex(v=>v&&!v.up&&v.life<.75); if (i>=0){ me.cd=.28; G.down(st,24+(i%3)*106+53,56+Math.floor(i/3)*106+53); } },
    breeze(G,st,me){ const tx=180,ty=414*.42; const d=st.d.slice().sort((a,b)=>Math.hypot(a.x-tx,a.y-ty)-Math.hypot(b.x-tx,b.y-ty))[0]; st.hold=!!d; if (d){ st.px+=(d.x-st.px)*.15; st.py+=(d.y-st.py)*.15; } },
    rhythm(G,st,me){ if (me.cd>0) return; const LY=414-90; const n=st.n.find(n=>!n.hit&&!n.miss&&Math.abs(n.y-LY)<10+Math.random()*8); if (n){ me.cd=.12; G.down(st); } },
    press(G,st,me){ const tgt=st.wr.flat().filter(q=>q.on).sort((a,b)=>Math.hypot(a.x-st.px,a.y-st.py)-Math.hypot(b.x-st.px,b.y-st.py))[0]; st.hold=!!tgt; if (!tgt) return; const dx=tgt.x-st.px, dy=tgt.y-st.py, r=Math.hypot(dx,dy), sp=Math.min(r,9); st.px+=dx/(r||1)*sp; st.py+=dy/(r||1)*sp; G.move(st,st.px,st.py); },
    flame(G,st){ st.hold=st.F<st.C; },
    valve(G,st,me){ if (me.cd>0) return; const i=st.g.findIndex(g=>g.p>=.76); if (i>=0){ me.cd=.3; G.down(st,20+i*80+40,200); } },
    // 10.21-f
    recipe(G,st,me){ if (me.cd>0) return; me.cd=.32; const f=st.rec[st.i]; G.down(st,30+(f%3)*100+50,230+Math.floor(f/3)*86+40); },
    sort(G,st,me){ const it=st.it; if (!it||it.go||it.y<70||me.cd>0) return; me.cd=.1; const bad=G.K[it.kd][1]===1; G.down(st,180,200); st.px=bad?60:300; G.up(st); },
    signal(G,st){ st.hold=true; const LY=414-50; const bads=st.o.filter(o=>o.bad&&o.y>LY-150&&o.y<LY+10);
      const goods=st.o.filter(o=>!o.bad&&o.y<LY+10).sort((a,b)=>b.y-a.y); let tx=goods.length?goods[0].x:st.rx;
      for (const b of bads) if (Math.abs(b.x-tx)<40) tx=b.x+(tx>=b.x?50:-50); st.px=Math.max(24,Math.min(336,tx)); },
    remote(G,st,me){ if (me.cd>0) return; me.cd=.3; const d=st.ch[st.in.length], i=d==='0'?10:+d-1; G.down(st,60+(i%3)*80+40,176+Math.floor(i/3)*56+26); },
  };
  for (const [id,G] of Object.entries(MG)){
    const r={none:0,bot:0,botS:[],len:[]};
    for (const mode of ['none','bot']) for (let k=0;k<N;k++){
      const st={t:0,score:0,end:false,hold:false,px:180,py:207}; G.init(st); const me={cd:0}; const dt=1/30;
      while (st.t<G.time && !st.end){ st.t+=dt; me.cd-=dt; if (mode==='bot') BOT[id](G,st,me); G.tick(st,dt); }
      const ok=st.score>=G.goal; if (ok) r[mode]++; if (mode==='bot'){ r.botS.push(Math.floor(st.score)); r.len.push(+st.t.toFixed(1)); }
    }
    r.botS=`${Math.min(...r.botS)}~${Math.max(...r.botS)}`; r.len=`${Math.min(...r.len)}~${Math.max(...r.len)}s`; r.goal=G.goal; out[id]=r;
  }
  return out;
},N);
console.log('minigame  손안댐/봇 성공(N='+N+')'); for (const [k,v] of Object.entries(res)) console.log(k.padEnd(7),`none ${v.none}/${N}`,`bot ${v.bot}/${N}`,`score ${v.botS} / goal ${v.goal}`,v.len);
// --- 스토리 흐름 ---
await p.evaluate(()=>{ const D=window.__DZ,S=D.S; S.guide=null; S.guideDone={first:1,levelup:1,gacha:1}; S.best=12; S.tutOff=1; S.att={n:1,last:'2099-1-1'}; S.items[1]=50000; S.items[2]=0;
  for (const t of ['vac','robot','fan','gen']) S.owned[t]={lv:12,star:1,eq:{}}; S.deck=['vac','robot','fan','gen']; document.getElementById('modal').hidden=true; D.persist(); D.renderLobby(); D.setTab('roster'); });
const sh=async n=>{ await new Promise(r=>setTimeout(r,350)); await p.screenshot({path:V+n}); };
const tab=k=>p.evaluate(k=>{ [...document.querySelectorAll('.dtabs button')].find(b=>b.dataset.k===k).click(); },k);
await p.evaluate(()=>document.querySelector('.ut[data-unit="vac"]').click()); await new Promise(r=>setTimeout(r,200));
const c0=await p.evaluate(()=>window.__DZ.S.items[2]||0); await tab('story'); const c1=await p.evaluate(()=>window.__DZ.S.items[2]||0);
await tab('info'); await tab('story'); const c2=await p.evaluate(()=>window.__DZ.S.items[2]||0);
console.log('crystal first visit', c0,'→',c1,'revisit →',c2, c1-c0===30&&c2===c1?'OK':'FAIL'); await sh('st_tab.png');
await p.evaluate(()=>document.querySelector('.st-ep button[data-ep="101"]').click()); await sh('st_play1.png');
for (let i=0;i<4;i++) await p.click('#stBox'); await sh('st_play1b.png');
for (let i=0;i<80 && await p.evaluate(()=>!document.getElementById('stOv').hidden); i++){ await p.click('#stBox'); }
console.log('ep1 done', await p.evaluate(()=>JSON.stringify({done:window.__DZ.S.story.done, aff:window.__DZ.S.aff.vac, coin:window.__DZ.S.items[1], cr:window.__DZ.S.items[2]})));
const lk=await p.evaluate(()=>[...document.querySelectorAll('.st-ep')].map(li=>li.className+' | '+li.querySelector('span').textContent));
console.log('list', JSON.stringify(lk));
await p.evaluate(()=>{ window.__DZ.affAdd('vac',300,true); }); await tab('info'); await tab('story');
await p.evaluate(()=>document.querySelector('.st-ep button[data-ep="102"]').click());
for (let i=0;i<40 && await p.evaluate(()=>document.getElementById('mgOv').hidden); i++){ await p.click('#stBox'); }
await sh('st_mg_intro.png'); await p.click('#mgStart');
// 흡입 대작전: 손가락을 누르고 먼지를 따라다님(실제 포인터 이벤트)
const bx=await p.locator('#mgCv').boundingBox(); await p.mouse.move(bx.x+bx.width/2,bx.y+bx.height/2); await p.mouse.down();
for (let i=0;i<70;i++){ await p.mouse.move(bx.x+bx.width*(.3+.4*Math.sin(i/6)),bx.y+bx.height*(.3+.4*Math.cos(i/7)),{steps:2}); await new Promise(r=>setTimeout(r,60)); if (i===30) await p.screenshot({path:V+'st_mg_play.png'}); }
await p.mouse.up(); await p.waitForFunction(()=>!document.getElementById('mgStart').hidden,{timeout:30000}); await sh('st_mg_end.png');
await p.click('#mgStart'); await new Promise(r=>setTimeout(r,200));
console.log('back to story', await p.evaluate(()=>!document.getElementById('stOv').hidden));
await p.click('#stSkip'); await new Promise(r=>setTimeout(r,200));
console.log('ep2', await p.evaluate(()=>JSON.stringify({done:window.__DZ.S.story.done, game:window.__DZ.S.story.game})));
await sh('st_tab2.png');
console.log(errs.join('\n')||'no errors'); await b.close();
