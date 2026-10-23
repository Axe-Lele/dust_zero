const fs=require('fs'),path=require('path'),sharp=require('sharp');
const root=path.resolve(__dirname,'../..'),dir=path.join(root,'art/261022-motion'),size=320;
const ids=JSON.parse(fs.readFileSync(path.join(root,'art/261022/manifest.json'),'utf8')).assets.map(a=>a.id);
async function cleanCell(input){
  const {data,info}=await sharp(input).ensureAlpha().raw().toBuffer({resolveWithObject:true});
  const w=info.width,h=info.height,labels=new Int32Array(w*h),queue=new Int32Array(w*h),components=[];
  let next=0;
  for(let start=0;start<w*h;start++){
    if(labels[start]||data[start*4+3]<=16)continue;
    const label=++next;let head=0,tail=1,area=0,left=w,right=0,top=h,bottom=0;queue[0]=start;labels[start]=label;
    while(head<tail){const p=queue[head++],x=p%w,y=Math.floor(p/w);area++;left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);
      for(let yy=Math.max(0,y-1);yy<=Math.min(h-1,y+1);yy++)for(let xx=Math.max(0,x-1);xx<=Math.min(w-1,x+1);xx++){const q=yy*w+xx;if(!labels[q]&&data[q*4+3]>16){labels[q]=label;queue[tail++]=q;}}
    }
    components.push({label,area,left,right,top,bottom});
  }
  const main=components.reduce((a,b)=>!a||b.area>a.area?b:a,null);if(!main)throw Error('Empty sprite cell');
  // Remove isolated scraps cut from adjacent cells, while retaining internal FX.
  const keep=new Set(components.filter(c=>c===main||(c.area>main.area*.001&&c.left>2&&c.top>2&&c.right<w-3&&c.bottom<h-3)).map(c=>c.label));
  for(let p=0;p<w*h;p++)if(data[p*4+3]<=16||!keep.has(labels[p])){data[p*4]=data[p*4+1]=data[p*4+2]=data[p*4+3]=0;}
  return sharp(data,{raw:{width:w,height:h,channels:4}}).png().toBuffer();
}
(async()=>{
  const manifest={build:'10.22-g',tool:'Built-in image_gen.imagegen',frameSize:size,columns:3,rows:3,attack:[0,1,2],skill:[3,4,5,6,7,8],assets:[]};
  for(const id of ids){
    const source=path.join(dir,'source',id+'.png');if(!fs.existsSync(source)){console.log(id+': pending');continue;}
    const meta=await sharp(source).metadata();if(!meta.hasAlpha)throw Error(id+': alpha missing');
    const pixels=await sharp(source).ensureAlpha().raw().toBuffer();
    const gutter=(target,lo,hi,count)=>{let best=Infinity,candidates=[];for(let p=Math.round(lo);p<=Math.round(hi);p++){const n=count(p);if(n<best){best=n;candidates=[p];}else if(n===best)candidates.push(p);}return candidates.sort((a,b)=>Math.abs(a-target)-Math.abs(b-target))[0];};
    // The painter may shift a row slightly. Locate its real gutters before
    // extracting cells, rather than cutting hair at nominal thirds.
    const ys=[0,...[1,2].map(i=>gutter(meta.height*i/3,meta.height*(i/3-.055),meta.height*(i/3+.055),y=>{let n=0;for(let x=0;x<meta.width;x++)if(pixels[(y*meta.width+x)*4+3]>32)n++;return n;})),meta.height];
    const xs=[];
    for(let row=0;row<3;row++)xs[row]=[0,...[1,2].map(i=>gutter(meta.width*i/3,meta.width*(i/3-.085),meta.width*(i/3+.085),x=>{let n=0;for(let y=ys[row];y<ys[row+1];y++)if(pixels[(y*meta.width+x)*4+3]>32)n++;return n;})),meta.width];
    const frames=[];
    for(let n=0;n<9;n++){
      const row=Math.floor(n/3),col=n%3,x0=xs[row][col],x1=xs[row][col+1],y0=ys[row],y1=ys[row+1];
      const cell=await sharp(source).extract({left:x0,top:y0,width:x1-x0,height:y1-y0}).png().toBuffer();
      const cleaned=await cleanCell(cell);
      const trimmed=await sharp(cleaned).trim({threshold:12}).png().toBuffer(),fm=await sharp(trimmed).metadata();
      if(fm.width<24||fm.height<24)throw Error(id+': empty frame '+n);
      frames.push({buffer:trimmed,width:fm.width,height:fm.height});
    }
    // One scale for the sequence, anchored at the feet; never resize each pose independently.
    const scale=Math.min(size*.90/Math.max(...frames.map(f=>f.width)),size*.88/Math.max(...frames.map(f=>f.height)));
    const layers=[];
    for(let n=0;n<9;n++){
      const f=frames[n],w=Math.round(f.width*scale),h=Math.round(f.height*scale);
      layers.push({input:await sharp(f.buffer).resize(w,h).png().toBuffer(),left:n%3*size+Math.round((size-w)/2),top:Math.floor(n/3)*size+Math.round(size*.94-h)});
    }
    await sharp({create:{width:size*3,height:size*3,channels:4,background:'#00000000'}}).composite(layers).webp({quality:90,alphaQuality:100}).toFile(path.join(dir,id+'_motion.webp'));
    const drawScale=Math.max(1,Math.min(1.4,.88/(frames[0].height*scale/size*1.02)));
    manifest.assets.push({...JSON.parse(fs.readFileSync(path.join(dir,'source',id+'.json'),'utf8')),file:id+'_motion.webp',frames:9,drawScale});console.log(id+': 3 attack + 6 skill');
  }
  fs.writeFileSync(path.join(dir,'manifest.json'),JSON.stringify(manifest,null,2));
  fs.writeFileSync(path.join(dir,'metrics.js'),'window.DZ_MOTION_SCALE='+JSON.stringify(Object.fromEntries(manifest.assets.map(a=>[a.id,a.drawScale])))+';\n');
})().catch(e=>{console.error(e);process.exitCode=1;});
