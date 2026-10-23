// Import generated transparent PNGs; trim alpha padding and export runtime WebP.
// NODE_PATH must point to a node_modules directory containing sharp.
const fs = require('fs');
const path = require('path');
const sharp = require('sharp');
const root = path.resolve(__dirname, '../..');
const dir = path.join(root, 'art/261022');
const manifest = JSON.parse(fs.readFileSync(path.join(dir, 'manifest.json'), 'utf8'));
async function exportAsset(input, dest, max) {
  const trimmed = await sharp(input).trim({ threshold: 8 }).toBuffer();
  const image = sharp(trimmed).resize({ width: max, height: max, fit: 'inside', withoutEnlargement: true });
  await image.webp({ quality: 92, alphaQuality: 100 }).toFile(path.join(dir, dest));
}
(async () => {
  fs.mkdirSync(path.join(dir, 'source'), { recursive: true });
  for (const job of manifest.assets) {
    const src = path.join(dir, 'source', job.id + '.png');
    if (!fs.existsSync(src)) fs.copyFileSync(job.generatedPath, src);
    const meta = await sharp(src).metadata();
    if (!meta.hasAlpha) throw new Error(job.id + ': transparent alpha missing');
    if (job.kind === 'sd') await exportAsset(src, job.id + '_sd.webp', 640);
    else {
      // Generated sheets can place the gap slightly right of center. Split on
      // the actual transparent gutter so hair/props never leak into the SD.
      const raw = await sharp(src).ensureAlpha().raw().toBuffer();
      let min = Infinity, candidates = [];
      for (let x = Math.floor(meta.width * .38); x < meta.width * .72; x++) {
        let n = 0;
        for (let y = 0; y < meta.height; y++) if (raw[(y * meta.width + x) * 4 + 3] > 8) n++;
        if (n < min) { min = n; candidates = [x]; } else if (n === min) candidates.push(x);
      }
      const split = candidates[Math.floor(candidates.length / 2)];
      if (min > 0) throw new Error(job.id + ': no clear gutter; inspect source sheet');
      const left = await sharp(src).extract({ left: 0, top: 0, width: split, height: meta.height }).png().toBuffer();
      const right = await sharp(src).extract({ left: split, top: 0, width: meta.width - split, height: meta.height }).png().toBuffer();
      await exportAsset(left, job.id + '_full.webp', 1200);
      await exportAsset(right, job.id + '_sd.webp', 640);
      await sharp(src).resize({ width: 1600, withoutEnlargement: true }).webp({ quality: 92, alphaQuality: 100 }).toFile(path.join(dir, job.id + '_turn.webp'));
      const full = await sharp(left).trim({ threshold: 8 }).png().toBuffer();
      const fm = await sharp(full).metadata();
      await sharp(full).extract({ left: 0, top: 0, width: fm.width, height: Math.round(fm.height * .29) }).resize({ width: 360, height: 360, fit: 'contain', background: '#00000000' }).webp({ quality: 92, alphaQuality: 100 }).toFile(path.join(dir, job.id + '_face1.webp'));
    }
    console.log(job.id + ': imported');
  }
})().catch(e => { console.error(e); process.exitCode = 1; });
