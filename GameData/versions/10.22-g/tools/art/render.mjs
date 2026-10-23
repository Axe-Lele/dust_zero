// SVG → PNG (헤드리스 크롬). node render.mjs in.svg out.png [w h]
import { chromium } from 'playwright'; import fs from 'fs';
const jobs = JSON.parse(fs.readFileSync(process.argv[2],'utf8'));   // [{svg, out, w, h}]
const br = await chromium.launch(); const pg = await br.newPage();
for (const j of jobs){
  await pg.setViewportSize({width:j.w, height:j.h});
  await pg.setContent(`<!doctype html><html><body style="margin:0;background:transparent">${fs.readFileSync(j.svg,'utf8')}</body></html>`);
  await pg.screenshot({path:j.out, omitBackground:!!j.transparent, clip:{x:0,y:0,width:j.w,height:j.h}});
}
await br.close();
