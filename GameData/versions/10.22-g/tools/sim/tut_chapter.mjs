// 1챕터 튜토리얼 분포(10.21-a): 새 계정으로 1-1~1-5를 차례로 하며 판마다 어떤 안내가 언제(게임 시각) 뜨는지 본다.
// 안내는 바로 따르고(배치 안내 = 빛나는 칸 또는 놓을 수 있는 첫 칸), 그 밖에는 단순 봇이 전력이 되면 가전을 놓는다.
//   node tools/sim/tut_chapter.mjs
import { chromium } from 'playwright'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const b=await chromium.launch(); const p=await b.newPage({viewport:{width:430,height:930}});
const errs=[]; p.on('pageerror',e=>errs.push(e.message));
await p.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await p.evaluate(()=>localStorage.clear()); await p.reload({waitUntil:'domcontentloaded'});
await p.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0);
await p.evaluate(()=>{ const S=window.__DZ.S; S.guideDone={first:1,levelup:1,gacha:1}; S.guide=null; });
for (let k=0;k<5;k++){
  await p.evaluate(k=>{ document.getElementById('modal').hidden=true; window.__DZ.startStage(k); },k);
  await new Promise(r=>setTimeout(r,300)); await p.evaluate(()=>document.getElementById('btnStart').click());
  const log=await p.evaluate(()=>{ const D=window.__DZ, G=D.G, U=D.UNITS(), out=[]; let n=0, lastId=null;
    while (!G.over && n<30*200){
      if (G.tut){ const st=G.tut.step; if (st.id!==lastId){ lastId=st.id; out.push(`${G.t.toFixed(1)}s ${document.getElementById('tutName').textContent}: ${st.text.slice(0,26)}`); }
        if (!document.getElementById('tutOk').hidden) document.getElementById('tutOk').click();
        else { const t=G.tut.target; let cell=G.tutTile; if (!cell){ for (let r=0;r<10&&!cell;r++) for (let c=0;c<7;c++) if (D.canPlace(t,c,r)){ cell=[c,r]; break; } }
          if (cell && D.affordable(t)) D.deploy(t,cell[0],cell[1]); }
      } else if (n%15===0 && !D.noDeploy){ for (const t of D.S.deck){ if (!D.affordable(t)) continue; let done=false; for (let r=0;r<10&&!done;r++) for (let c=0;c<7&&!done;c++) if (D.canPlace(t,c,r)){ D.deploy(t,c,r); done=true; } if (done) break; } }
      D.tutTick(); if (!G.tutHold) D.update(1/30); n++;
    }
    return {t:G.t.toFixed(0), life:G.life, out}; });
  console.log(`== 전투 ${k+1} (${['1-1','1-2','1-3','1-4','1-5'][k]}) 안내 ${log.out.length}개 · 끝 ${log.t}s 청결도 ${log.life}`); for (const l of log.out) console.log('   '+l);
  await p.evaluate(()=>{ const D=window.__DZ; D.S.tutLastT=D.G.t; });
}
console.log(errs.slice(0,5).join('\n')||'no errors'); await b.close();
