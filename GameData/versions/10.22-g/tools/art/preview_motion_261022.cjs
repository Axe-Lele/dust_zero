const fs=require('fs'),path=require('path'),sharp=require('sharp');
const dir=path.resolve(__dirname,'../../art/261022-motion');
const manifest=JSON.parse(fs.readFileSync(path.join(dir,'manifest.json'),'utf8'));
(async()=>{
  const ids=manifest.assets.map(a=>a.id),frames=[];
  for(const n of [0,1,2,2,8,3,4,5,6,7,8,8]){
    const layers=[];
    for(let i=0;i<ids.length;i++){
      const id=ids[i],pose=await sharp(path.join(dir,id+'_motion.webp')).extract({left:n%3*320,top:Math.floor(n/3)*320,width:320,height:320}).resize(200,200).png().toBuffer();
      const x=i%4*220,y=Math.floor(i/4)*240;layers.push({input:pose,left:x+10,top:y});
      layers.push({input:Buffer.from(`<svg width="220" height="25"><text x="110" y="18" fill="#253143" text-anchor="middle" font-family="Arial" font-size="14">${id}</text></svg>`),left:x,top:y+204});
    }
    frames.push(await sharp({create:{width:880,height:960,channels:4,background:'#edf1f7'}}).composite(layers).png().toBuffer());
  }
  await sharp(frames,{join:{animated:true}}).gif({delay:[140,120,180,160,300,140,140,140,160,160,180,400],loop:0,colours:256,dither:.4}).toFile(path.join(dir,'motion-preview.gif'));
  await sharp(frames[7]).png().toFile(path.join(dir,'motion-preview.png'));
  console.log('Generated 16-character animated preview');
})().catch(e=>{console.error(e);process.exitCode=1;});
