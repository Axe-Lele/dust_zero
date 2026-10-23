// 배포용(플레이 전용) 빌드 만들기 (10.19-e)
//   node tools/release.mjs [출력 폴더=../release]   (esbuild 필요: 시뮬레이터처럼 node_modules 연결)
//   검사: KEEP_SIM=1 node tools/release.mjs /tmp/x → /tmp/x/dustzero/index.html 을 원본 폴더의 index.html 자리에 두고 verify·coop 시뮬레이터가 같은 결과인지 확인
// 결과: <출력>/dustzero/ 에 index.html(+ claude.ai 아티팩트용 artifact.html)(코드 압축·이름 바꿈, 테이블 데이터 내장) + art/ + audio/ + HOW_TO_PLAY.txt(플레이 방법, 파일 이름은 윈도우에서 깨지지 않게 영어)
// 빠지는 것: tables(엑셀) · tools · notes · data/*.json · README · HANDOFF · BALANCE · 시뮬레이터 훅(#sim)
// 받은 사람이 플레이만 하게 하는 용도예요. 웹 페이지라 완전히 막을 수는 없고, 이어서 개발하기 어렵게 만드는 정도예요.
import fs from 'fs'; import path from 'path'; import url from 'url'; import zlib from 'zlib';
import * as esbuild from 'esbuild';
const here=path.dirname(url.fileURLToPath(import.meta.url)), root=path.join(here,'..');
const outRoot=path.resolve(process.argv[2]||path.join(root,'..','release')), out=path.join(outRoot,'dustzero');
let html=fs.readFileSync(path.join(root,'index.html'),'utf8');
const must=(cond,msg)=>{ if (!cond) throw new Error('release: '+msg); };

// 1) 테이블 데이터: data/tables.js → gzip + base64 로 스크립트 안에 넣음
const tjs=fs.readFileSync(path.join(root,'data','tables.js'),'utf8');
const i0=tjs.indexOf('{'), i1=tjs.lastIndexOf('}'); const tables=JSON.parse(tjs.slice(i0,i1+1));
const names=JSON.parse(html.match(/const TABLE_NAMES=(\[[^\]]*\])/)[1].replace(/'/g,'"'));
for (const n of names) must(tables[n],'tables.js 에 없는 테이블 '+n+' (tools/pack_tables.py 먼저)');
const Z=zlib.gzipSync(Buffer.from(JSON.stringify(tables)),{level:9}).toString('base64');

// 2) 본 스크립트 꺼내기
const sOpen=html.indexOf('<script>\n(() => {'), sClose=html.indexOf('</script>',sOpen); must(sOpen>0&&sClose>0,'본 스크립트를 못 찾음');
let js=html.slice(sOpen+8,sClose);
const rep=(a,b)=>{ must(js.includes(a),'바꿀 곳을 못 찾음: '+a.slice(0,50)); js=js.replace(a,b); };
rep('const packed = window.DUSTZERO_TABLES;',
    "const packed = await (async()=>{ const b=Uint8Array.from(atob(__Z),c=>c.charCodeAt(0)); return JSON.parse(await new Response(new Blob([b]).stream().pipeThrough(new DecompressionStream('gzip'))).text()); })();");
const KEEP_SIM=!!process.env.KEEP_SIM;   // 검사용: 훅을 남겨 압축한 코드로 시뮬레이터를 돌려 볼 때만
if (!KEEP_SIM){ rep('if (/[#&]sim/.test(location.hash)) window.__DZ =','if (false) window.__DZ =');   // 시뮬레이터 훅 제거(아래 압축에서 통째로 빠짐)
  rep('if (window.__DZ_GEAR){','if (false){'); }
js=`const __Z="${Z}";\n`+js;
const mjs=(await esbuild.transform(js,{minify:true,target:'es2020',charset:'utf8',legalComments:'none'})).code;
if (!KEEP_SIM) must(!mjs.includes('__DZ'),'시뮬레이터 훅이 남음');

// 3) CSS 압축, HTML 주석·들여쓰기 정리, tables.js 태그 제거
html=html.slice(0,sOpen)+'<script>'+mjs+'</script>'+html.slice(sClose+9);
html=html.replace(/<script src="data\/tables\.js[^"]*"><\/script>\n?/,'');
must(!/<script src="data\/tables/.test(html),'tables.js 태그가 남음');
const css=[]; html=html.replace(/<style>([\s\S]*?)<\/style>/g,(m,c)=>{ css.push(c); return `<style>@@CSS${css.length-1}@@</style>`; });
for (let k=0;k<css.length;k++){ const c=(await esbuild.transform(css[k],{loader:'css',minify:true})).code.trim(); html=html.replace(`@@CSS${k}@@`,()=>c); }
const sc=html.indexOf('<script>'), head=html.slice(0,sc).replace(/<!--[\s\S]*?-->/g,'').replace(/\n\s+/g,'\n'); html=head+html.slice(sc);

// 4) 내보내기
fs.rmSync(out,{recursive:true,force:true}); fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'index.html'),html);
// claude.ai 아티팩트용(10.19-f): 게시할 때 doctype·head·body 뼈대가 씌워지므로 <title>부터 </body> 앞까지만. art·audio 는 files 로 같이 올림
{ const t0=html.indexOf('<title>'), t1=html.lastIndexOf('</body>'); must(t0>0&&t1>t0,'아티팩트용 본문을 못 자름');
  fs.writeFileSync(path.join(out,'artifact.html'),html.slice(t0,t1).trim()+'\n'); }
for (const d of ['art','audio']) fs.cpSync(path.join(root,d),path.join(out,d),{recursive:true});
const build=(html.match(/테스트 빌드 ([0-9.]+-[a-z])/)||[])[1]||'';
fs.writeFileSync(path.join(out,'HOW_TO_PLAY.txt'),
`오퍼레이션: 더스트 제로 (빌드 ${build})

1. 압축을 전부 풉니다(폴더째).
2. index.html 을 크롬(또는 엣지·사파리 최신)으로 엽니다.
3. 모바일 세로 화면용이에요. PC에서는 창을 좁게 하거나 개발자 도구의 모바일 보기로 보면 편해요.

진행 기록은 그 브라우저에 저장돼요. 처음부터 하려면 홈 오른쪽 위 "게임 초기화"를 3초 안에 두 번 누르세요.
`);
console.log(`release: ${out}  (index.html ${(fs.statSync(path.join(out,'index.html')).size/1024).toFixed(0)}KB, 빌드 ${build})`);
