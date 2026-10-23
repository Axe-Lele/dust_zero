const fs=require('fs'),path=require('path'),sharp=require('sharp');
const root=path.resolve(__dirname,'../..'),dir=path.join(root,'art/261022');
const m=JSON.parse(fs.readFileSync(path.join(dir,'manifest.json'),'utf8'));
(async()=>{
  const layers=[];
  for(let i=0;i<m.assets.length;i++){
    const a=m.assets[i],x=i%4*320,y=Math.floor(i/4)*340;
    const tile=await sharp(path.join(dir,a.id+'_sd.webp')).resize(280,290,{fit:'contain',background:'#00000000'}).png().toBuffer();
    layers.push({input:tile,left:x+20,top:y+10});
    const label=Buffer.from(`<svg width="320" height="36"><text x="160" y="26" text-anchor="middle" font-family="Arial" font-size="19" fill="#23313e">${a.id}</text></svg>`);
    layers.push({input:label,left:x,top:y+300});
  }
  await sharp({create:{width:1280,height:1360,channels:4,background:'#eef1f6'}}).composite(layers).png().toFile(path.join(dir,'sd-preview.png'));
  const full=m.assets.filter(a=>a.kind==='pair'),cards=[];
  for(let i=0;i<full.length;i++){
    const a=full[i],tile=await sharp(path.join(dir,a.id+'_full.webp')).resize(320,500,{fit:'contain',background:'#00000000'}).png().toBuffer();
    cards.push({input:tile,left:i%4*340+10,top:Math.floor(i/4)*540+10});
    cards.push({input:Buffer.from(`<svg width="340" height="30"><text x="170" y="22" text-anchor="middle" font-family="Arial" font-size="18" fill="#23313e">${a.id}</text></svg>`),left:i%4*340,top:Math.floor(i/4)*540+510});
  }
  await sharp({create:{width:1360,height:1080,channels:4,background:'#eef1f6'}}).composite(cards).png().toFile(path.join(dir,'illustration-preview.png'));
})().catch(e=>{console.error(e);process.exitCode=1;});
