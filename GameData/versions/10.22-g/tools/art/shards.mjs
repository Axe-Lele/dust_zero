// 가전 조각 아이콘: 등급 색 결정 모양 안에 게임 SD를 그려 art/icon/shard_<가전ID>.png 로 저장 (그다음 gen_icons.py --shards 로 webp)
//   node tools/art/shards.mjs
import { chromium } from 'playwright'; import fs from 'fs'; import path from 'path'; import url from 'url';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..','..');
const br=await chromium.launch(); const pg=await br.newPage();
await pg.goto(url.pathToFileURL(path.join(root,'index.html')).href+'#sim',{waitUntil:'domcontentloaded'});
await pg.waitForFunction(()=>window.__DZ && window.__DZ.STAGES().length>0);
const out=await pg.evaluate(()=>{ const D=window.__DZ, U=D.UNITS(), res={};
  const COL={R:['#C9D3DA','#7D8B96'],SR:['#BFDDF7','#3D8BD9'],SSR:['#FFD9A8','#E8842A']};
  for (const t of Object.keys(U)){ const c=document.createElement('canvas'); c.width=c.height=96; const x=c.getContext('2d'), [f,e]=COL[U[t].rar];
    const shard=()=>{ x.beginPath(); x.moveTo(48,4); x.lineTo(84,28); x.lineTo(78,78); x.lineTo(48,92); x.lineTo(14,74); x.lineTo(10,26); x.closePath(); };
    shard(); x.fillStyle=f; x.fill(); x.save(); shard(); x.clip(); x.fillStyle='rgba(255,255,255,.45)'; x.beginPath(); x.moveTo(48,4); x.lineTo(10,26); x.lineTo(40,40); x.closePath(); x.fill();
    x.drawImage(D.sdIcon(t,74),11,14,74,74); x.restore();
    shard(); x.lineWidth=5; x.strokeStyle=e; x.lineJoin='round'; x.stroke(); shard(); x.lineWidth=2; x.strokeStyle='#3B2A33'; x.stroke();
    x.fillStyle='#FFFFFF'; x.beginPath(); x.moveTo(78,6); x.lineTo(81,13); x.lineTo(88,16); x.lineTo(81,19); x.lineTo(78,26); x.lineTo(75,19); x.lineTo(68,16); x.lineTo(75,13); x.closePath(); x.fill(); x.lineWidth=1.5; x.strokeStyle='#3B2A33'; x.stroke();
    res[t]=c.toDataURL('image/png'); }
  return res; });
fs.mkdirSync(path.join(here,'out'),{recursive:true});
for (const [t,d] of Object.entries(out)) fs.writeFileSync(path.join(here,'out',`shard_${t}.png`),Buffer.from(d.split(',')[1],'base64'));
console.log('shards:',Object.keys(out).join(','));
await br.close();
