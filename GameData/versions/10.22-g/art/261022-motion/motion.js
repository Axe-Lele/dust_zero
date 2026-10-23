// Cosmetic animation only. No random numbers, damage, cooldowns or game rules.
(()=>{
  'use strict';
  const images={},attackSeconds=.36,skillSeconds=.84,cell=320;
  const palette={vac:'#C9CDD1',robot:'#80D9A0',fridge:'#A7DCFF',ac:'#92D7FF',micro:'#FFAE55',washer:'#8BD8FF',pc:'#C3A0FF',fan:'#B4F2EE',gen:'#FFE475',iron:'#D4BEFF',stove:'#FF9A52',boiler:'#FFD2AC',blender:'#B8EB80',purifier:'#A0EBD7',router:'#9FBCFF',tv:'#FFB5D5'};
  const groups={vac:'wind',robot:'spin',fridge:'ice',ac:'ice',micro:'pulse',washer:'water',pc:'circuit',fan:'wind',gen:'power',iron:'press',stove:'fire',boiler:'steam',blender:'spin',purifier:'wind',router:'signal',tv:'signal'};
  function cue(u,kind){
    if(!u)return;
    if(kind==='skill'){u.motionSkill=skillSeconds;u.motionSkillAge=0;}
    else {u.motionAttack=attackSeconds;u.motionAttackAge=0;}
  }
  function step(u,dt,opt={}){
    if(!u)return;
    u.motionAttack=Math.max(0,(u.motionAttack||0)-dt);u.motionSkill=Math.max(0,(u.motionSkill||0)-dt);
    u.motionAttackAge=(u.motionAttackAge||0)+dt;u.motionSkillAge=(u.motionSkillAge||0)+dt;
    if(opt.observeKick && (u.kick||0)>(u.motionLastKick||0)+.025)cue(u,'attack');
    u.motionLastKick=u.kick||0;
    if(opt.skill && !u.motionWasSkill)cue(u,'skill');u.motionWasSkill=!!opt.skill;
    if(opt.pulse){u.motionPulse=(u.motionPulse||0)+dt;if(u.motionPulse>=2.4){u.motionPulse=0;cue(u,'attack');}}
    else u.motionPulse=0;
  }
  function pick(id,u,t){
    if(!u||u.over)return null;
    const state=u.motion||u;
    let kind='',frame=-1,progress=0;
    if(u.motionPreview){
      kind=u.motionPreview;const duration=kind==='skill'?skillSeconds:attackSeconds;
      progress=((t%duration)+duration)%duration/duration;
      frame=kind==='skill'?3+Math.min(5,Math.floor(progress*6)):Math.min(2,Math.floor(progress*3));
    } else if((state.motionSkill||0)>0||(u.skillT||0)>0){
      kind='skill';const age=state.motionSkillAge||0,remaining=Math.max(state.motionSkill||0,u.skillT||0);
      frame=age<.42?3+Math.min(2,Math.floor(age/.14)):remaining<=.18?8:6+(Math.floor((age-.42)/.16)%2);
      progress=Math.min(1,age/skillSeconds);
    } else if((state.motionAttack||0)>0){
      kind='attack';progress=Math.min(.999,(state.motionAttackAge||0)/attackSeconds);frame=Math.min(2,Math.floor(progress*3));
    }
    return frame<0?null:{image:images[id],frame,kind,progress,age:u.motionPreview?t:kind==='skill'?(state.motionSkillAge||0):(state.motionAttackAge||0),cell,scale:(window.DZ_MOTION_SCALE||{})[id]||1};
  }
  function effects(c,id,x,y,k,m,low){
    if(!m)return;
    const p=m.progress,sk=m.kind==='skill';if(!sk&&(p<.27||p>.8))return;
    c.save();c.translate(x,y);c.strokeStyle=palette[id]||'#FFF';c.fillStyle=c.strokeStyle;
    c.globalAlpha=sk?.60:.50;c.lineWidth=Math.max(1,k*(sk?.022:.016));c.lineCap='round';
    const group=groups[id],R=k*(sk?.44:.23),phase=m.age*8;
    if(group==='press'){
      c.beginPath();c.ellipse(k*.12,k*.29,R*(.5+p*.5),R*.18,0,0,Math.PI*2);c.stroke();
      if(sk&&!low)for(let i=0;i<3;i++){const xx=k*(i-1)*.12;c.beginPath();c.moveTo(xx,-k*.22);c.lineTo(xx,k*.15);c.stroke();}
    }else if(group==='circuit'){
      const n=low?1:3;for(let i=0;i<n;i++){const xx=k*(-.4+i*.27),yy=-k*(.25+(i%2)*.17);c.strokeRect(xx,yy,k*.24,k*.17);c.beginPath();c.moveTo(xx+k*.12,yy+k*.17);c.lineTo(xx+k*.12,yy+k*.25);c.lineTo(xx+k*.25,yy+k*.25);c.stroke();}
    }else if(group==='signal'||group==='pulse'){
      for(let i=0;i<(low?1:3);i++){c.beginPath();c.arc(k*.12,-k*.10,R*(.5+i*.25),-.7+phase*.06,.7+phase*.06);c.stroke();}
    }else if(group==='power'){
      for(let i=0;i<(low?2:4);i++){const a=phase+i*Math.PI/2,xx=Math.cos(a)*R,yy=-k*.15+Math.sin(a)*R*.65;c.beginPath();c.moveTo(xx,yy-k*.09);c.lineTo(xx-k*.04,yy);c.lineTo(xx+k*.04,yy);c.lineTo(xx,yy+k*.10);c.stroke();}
    }else if(group==='ice'){
      for(let i=0;i<(low?2:5);i++){const a=phase*.4+i*Math.PI*.4,xx=Math.cos(a)*R,yy=-k*.16+Math.sin(a)*R*.7,s=k*.045;c.beginPath();c.moveTo(xx-s,yy);c.lineTo(xx+s,yy);c.moveTo(xx,yy-s);c.lineTo(xx,yy+s);c.stroke();}
    }else if(group==='fire'){
      for(let i=0;i<(low?2:4);i++){const xx=k*(-.28+i*.18),yy=k*.26,hh=k*(.12+.07*Math.sin(phase+i));c.beginPath();c.moveTo(xx-k*.04,yy);c.quadraticCurveTo(xx-k*.06,yy-hh,xx+k*.02,yy-hh*1.35);c.quadraticCurveTo(xx+k*.09,yy-hh*.4,xx+k*.04,yy);c.fill();}
    }else{
      const n=low?1:3;for(let i=0;i<n;i++){c.beginPath();c.ellipse(0,k*(.15-i*.12),R*(.65+i*.13),R*(group==='steam'?.20:.28),phase*.1,phase+i*.8,phase+i*.8+Math.PI*1.4);c.stroke();}
      if(group==='water'&&!low)for(let i=0;i<3;i++){c.beginPath();c.arc(Math.cos(phase+i*2.1)*R,-k*.15+Math.sin(phase+i*2.1)*R*.65,k*.035,0,Math.PI*2);c.stroke();}
    }
    c.restore();
  }
  async function load(ids){await Promise.all(ids.map(id=>new Promise(resolve=>{const image=new Image();image.onload=()=>{images[id]=image;resolve();};image.onerror=()=>{console.warn('Motion sheet unavailable:',id);resolve();};image.src=`art/261022-motion/${id}_motion.webp?v=10.22-g`;})));}
  window.DZ_MOTION={load,cue,step,pick,effects,images,attackSeconds,skillSeconds};
})();
