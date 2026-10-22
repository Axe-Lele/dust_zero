// ================= SD 모에 캐릭터 (임시 아트, 코드로 그림) =================
// 모든 가전이 같은 몸체를 쓰고, 머리 모양·색·모자·소품·등 장식으로 구분한다.
const SKIN='#FFE6D8', LINE='#3B2A33';
function lineIt(c,w){ c.strokeStyle=LINE; c.lineWidth=w; c.lineJoin='round'; c.lineCap='round'; c.stroke(); }
function rrPath(c,x,y,w,h,r){ c.moveTo(x+r,y); c.arcTo(x+w,y,x+w,y+h,r); c.arcTo(x+w,y+h,x,y+h,r); c.arcTo(x,y+h,x,y,r); c.arcTo(x,y,x+w,y,r); c.closePath(); }
function fillLine(c,fill,lw){ c.fillStyle=fill; c.fill(); lineIt(c,lw); }

function chibiBackHair(c,S,hx,hy,R,lw,t){
  const st=S.style;
  c.beginPath();
  if (st==='long'){ rrPath(c,hx-R*1.1,hy-R*.95,R*2.2,R*2.15,R*.95); }
  else if (st==='bob'){ rrPath(c,hx-R*1.12,hy-R*1.0,R*2.24,R*1.8,R*.95); }
  else { c.arc(hx,hy,R*1.07,0,Math.PI*2); }
  fillLine(c,S.hair,lw);
  if (st==='twin'){
    for (const s of [-1,1]){
      if (S.drill){
        for (let i=0;i<3;i++){ c.beginPath(); c.arc(hx+s*R*(1.12+i*.04),hy+R*(.15+i*.42),R*(.36-i*.06),0,Math.PI*2); fillLine(c,S.hair,lw); }
      } else {
        c.beginPath(); c.ellipse(hx+s*R*1.12,hy+R*.75,R*.27,R*.95,s*.12,0,Math.PI*2); fillLine(c,S.hair,lw);
        if (S.tip){ c.beginPath(); rrPath(c,hx+s*R*1.2-R*.12,hy+R*1.6,R*.24,R*.2,R*.05); fillLine(c,S.tip,lw*.8); }
      }
    }
  } else if (st==='pony'){
    c.beginPath(); c.ellipse(hx+R*1.12,hy+R*.45,R*.28,R*.78,-.45,0,Math.PI*2); fillLine(c,S.hair,lw);
    circ(c,hx+R*.95,hy-R*.15,R*.12,S.accent||'#FF9A3C');
  } else if (st==='buns'){
    for (const s of [-1,1]){ c.beginPath(); c.arc(hx+s*R*.78,hy-R*.82,R*.34,0,Math.PI*2); fillLine(c,S.hair,lw); }
  }
}
function chibiBangs(c,S,hx,hy,R,lw){
  for (const s of [-1,1]){ c.beginPath(); c.ellipse(hx+s*R*.9,hy+R*.42,R*.17,R*.52,s*-.08,0,Math.PI*2); fillLine(c,S.hair,lw*.9); }
  c.beginPath();
  c.moveTo(hx-R*1.02,hy+R*.22);
  c.bezierCurveTo(hx-R*1.14,hy-R*1.45,hx+R*1.14,hy-R*1.45,hx+R*1.02,hy+R*.22);
  const pts=[[.84,-.08],[.62,-.34],[.4,0],[.14,-.38],[-.1,-.02],[-.36,-.36],[-.6,-.04],[-.84,-.32]];
  for (const [px,py] of pts) c.lineTo(hx+px*R,hy+py*R);
  c.closePath(); fillLine(c,S.hair,lw);
  c.beginPath(); c.ellipse(hx-R*.35,hy-R*.62,R*.32,R*.08,-.3,0,Math.PI*2); c.fillStyle='rgba(255,255,255,.35)'; c.fill();
}
function chibiFace(c,S,hx,hy,R,lw,u,t){
  const over=u&&u.over, sk=u&&u.skillT>0;
  const blink = u && !over && Math.sin(t*1.3+(u.id||0)*1.7)>.985;
  const ey=hy+R*.24, ex=R*.38;
  for (const s of [-1,1]){
    const cx=hx+s*ex;
    if (over){
      c.beginPath(); c.moveTo(cx-R*.13,ey-R*.12); c.lineTo(cx+R*.13*s*-1+ (s>0?R*.26:0) - (s>0?R*.13:0),ey); c.lineTo(cx-R*.13*(s>0?-1:1),ey+R*.12);
      c.beginPath(); c.moveTo(cx-s*R*.13,ey-R*.13); c.lineTo(cx+s*R*.12,ey); c.lineTo(cx-s*R*.13,ey+R*.13); lineIt(c,lw*1.4);
    } else if (blink){
      c.beginPath(); c.arc(cx,ey-R*.02,R*.17,.15*Math.PI,.85*Math.PI); lineIt(c,lw*1.4);
    } else {
      ell(c,cx,ey,R*.2,R*.26,'#FFFFFF');
      ell(c,cx,ey+R*.03,R*.17,R*.22,S.eye);
      ell(c,cx,ey+R*.06,R*.09,R*.12,'rgba(20,18,40,.55)');
      circ(c,cx-R*.06,ey-R*.06,R*.065,'#FFFFFF'); circ(c,cx+R*.06,ey+R*.12,R*.03,'rgba(255,255,255,.85)');
      if (S.sleepy && !sk){ c.beginPath(); c.ellipse(cx,ey,R*.21,R*.27,0,Math.PI,Math.PI*2); c.fillStyle=SKIN; c.fill(); c.beginPath(); c.moveTo(cx-R*.21,ey); c.lineTo(cx+R*.21,ey); lineIt(c,lw*1.3); }
      else { c.beginPath(); c.ellipse(cx,ey,R*.21,R*.27,0,Math.PI*1.08,Math.PI*1.92); lineIt(c,lw*1.7); }
      if (sk){ c.fillStyle='#FFFFFF'; c.beginPath(); const sx=cx+R*.02, sy=ey; c.moveTo(sx,sy-R*.08); c.lineTo(sx+R*.025,sy-R*.02); c.lineTo(sx+R*.08,sy); c.lineTo(sx+R*.025,sy+R*.02); c.lineTo(sx,sy+R*.08); c.lineTo(sx-R*.025,sy+R*.02); c.lineTo(sx-R*.08,sy); c.lineTo(sx-R*.025,sy-R*.02); c.closePath(); c.fill(); }
    }
    ell(c,hx+s*R*.6,hy+R*.5,R*.14,R*.065,over?'rgba(255,90,90,.6)':'rgba(255,120,140,.45)');
  }
  if (sk||over){ c.beginPath(); c.ellipse(hx,hy+R*.58,R*.08,R*.07,0,0,Math.PI*2); c.fillStyle='#B5485A'; c.fill(); lineIt(c,lw*.7); }
  else { c.beginPath(); c.arc(hx,hy+R*.48,R*.09,.2*Math.PI,.8*Math.PI); lineIt(c,lw*.9); }
  if (over){ c.beginPath(); const dx=hx+R*.95, dy=hy-R*.2; c.moveTo(dx,dy-R*.16); c.quadraticCurveTo(dx+R*.13,dy+R*.04,dx,dy+R*.08); c.quadraticCurveTo(dx-R*.13,dy+R*.04,dx,dy-R*.16); c.fillStyle='#8FD0F5'; c.fill(); lineIt(c,lw*.7); }
}
function drawChibi(c,x,y,k,S,u,t){
  t=t||0;
  const kick=u&&u.kick>0?u.kick:0, over=u&&u.over;
  const R=k*.2, lw=Math.max(1,k*.016);
  const bob=u&&!over?Math.sin(t*3+(u.id||0))*k*.008:0;
  const hx=x+(over?k*.02:0), hy=y-k*.19+bob+(over?k*.03:0);
  ell(c,x,y+k*.33,k*.24,k*.06,'rgba(0,0,0,.18)');
  if (S.back) S.back(c,x,y,k,R,hx,hy,lw,u,t);
  chibiBackHair(c,S,hx,hy,R,lw,t);
  const legTop=y+k*.2, legH=k*.1;
  for (const s of [-1,1]){
    c.beginPath(); rrPath(c,x+s*R*.28-R*.12,legTop,R*.24,legH,R*.08); fillLine(c,S.legs||SKIN,lw*.8);
    c.beginPath(); c.ellipse(x+s*R*.3,legTop+legH,R*.18,R*.1,0,0,Math.PI*2); fillLine(c,S.shoes||LINE,lw*.8);
  }
  c.beginPath(); c.moveTo(x-R*.42,hy+R*.9); c.lineTo(x+R*.42,hy+R*.9); c.lineTo(x+R*.8,y+k*.22); c.quadraticCurveTo(x,y+k*.27,x-R*.8,y+k*.22); c.closePath(); fillLine(c,S.dress,lw);
  if (S.body) S.body(c,x,y,k,R,hx,hy,lw,u,t);
  for (const s of [-1,1]){
    c.save(); c.translate(x+s*R*.5,hy+R*1.02); c.rotate(s*-.38+(s===1&&kick?-kick*5:0)+(over?s*.25:0));
    c.beginPath(); c.ellipse(0,R*.32,R*.13,R*.34,0,0,Math.PI*2); fillLine(c,S.sleeve||S.dress,lw*.8);
    c.beginPath(); c.arc(0,R*.66,R*.11,0,Math.PI*2); fillLine(c,SKIN,lw*.7);
    c.restore();
  }
  if (S.prop) S.prop(c,x,y,k,R,hx,hy,lw,u,t,kick);
  c.beginPath(); c.ellipse(hx,hy+R*.06,R*.93,R*.86,0,0,Math.PI*2); fillLine(c,SKIN,lw);
  chibiFace(c,S,hx,hy,R,lw,u,t);
  chibiBangs(c,S,hx,hy,R,lw);
  if (S.head) S.head(c,x,y,k,R,hx,hy,lw,u,t);
}

const CHIBI = {
  vac:{ style:'twin', drill:true, hair:'#D9DEE6', eye:'#4C6FB8', dress:'#2563EB', sleeve:'#2563EB', legs:'#2563EB', shoes:'#1E2A44',
    back(c,x,y,k,R,hx,hy,lw){ c.beginPath(); rrPath(c,x-R*1.35,hy+R*1.05,R*.62,R*.95,R*.25); fillLine(c,'#AEB8C2',lw); c.beginPath(); c.ellipse(x-R*1.04,hy+R*1.15,R*.2,R*.07,0,0,Math.PI*2); fillLine(c,'#06B6D4',lw*.7); },
    body(c,x,y,k,R,hx,hy,lw){ c.beginPath(); c.moveTo(x-R*.28,hy+R*1.2); c.lineTo(x+R*.28,hy+R*1.2); c.lineTo(x+R*.5,y+k*.21); c.lineTo(x-R*.5,y+k*.21); c.closePath(); fillLine(c,'#FFFFFF',lw*.8);
      c.beginPath(); c.moveTo(x-R*.42,hy+R*.92); c.lineTo(x,hy+R*1.2); c.lineTo(x+R*.42,hy+R*.92); lineIt(c,lw*1.2); },
    prop(c,x,y,k,R,hx,hy,lw,u,t,kick){ const a=kick?-kick*3:0; c.save(); c.translate(x+R*.82,hy+R*1.62); c.rotate(.35+a);
      c.beginPath(); c.moveTo(0,0); c.lineTo(0,R*1.25); c.strokeStyle='#5D6871'; c.lineWidth=R*.1; c.lineCap='round'; c.stroke();
      c.beginPath(); rrPath(c,-R*.35,R*1.2,R*.7,R*.18,R*.06); fillLine(c,'#3A4046',lw*.8); c.restore();
      c.beginPath(); c.moveTo(x-R*1.04,hy+R*1.9); c.bezierCurveTo(x-R*1.2,y+k*.34,x+R*.2,y+k*.34,x+R*.8,hy+R*1.75); c.strokeStyle='#3A4046'; c.lineWidth=R*.09; c.stroke(); },
    head(c,x,y,k,R,hx,hy,lw){ c.beginPath(); c.ellipse(hx-R*.08,hy-R*.95,R*.78,R*.3,-.08,0,Math.PI*2); fillLine(c,'#2B3550',lw); c.beginPath(); c.ellipse(hx-R*.08,hy-R*.8,R*.66,R*.08,-.08,0,Math.PI); c.strokeStyle='#06B6D4'; c.lineWidth=lw*1.6; c.stroke(); } },
  robot:{ style:'bob', hair:'#F1F1EE', eye:'#6B7785', sleepy:true, dress:'#8A949C', shoes:'#3E454B',
    head(c,x,y,k,R,hx,hy,lw,u){ c.beginPath(); c.ellipse(hx,hy-R*.86,R*1.0,R*.3,0,0,Math.PI*2); fillLine(c,'#3E454B',lw); c.beginPath(); c.ellipse(hx,hy-R*.96,R*.94,R*.26,0,0,Math.PI*2); fillLine(c,'#ECEFF2',lw); circ(c,hx+R*.48,hy-R*.98,R*.08,u&&u.skillT>0?'#FF9A3C':'#3FAE7A');
      for (const s of [-1,1]){ c.beginPath(); for (let i=0;i<3;i++){ const a=i*2.09+(u?Date.now()/150:0)*s; c.moveTo(hx+s*R*1.02,hy-R*.75); c.lineTo(hx+s*R*1.02+Math.cos(a)*R*.22,hy-R*.75+Math.sin(a)*R*.22); } c.strokeStyle='#5D6871'; c.lineWidth=lw; c.stroke(); } },
    body(c,x,y,k,R,hx,hy,lw){ for (const s of [-1,1]){ circ(c,x+s*R*.2,y+k*.33,R*.06,'#3E454B'); circ(c,x+s*R*.4,y+k*.33,R*.06,'#3E454B'); } } },
  fridge:{ style:'long', hair:'#BFE3F5', eye:'#3D8BD9', dress:'#E9EFF3', sleeve:'#E9EFF3', legs:'#C9D6DF', shoes:'#7F96A6',
    body(c,x,y,k,R,hx,hy,lw,u,t){ c.beginPath(); c.moveTo(x-R*.6,hy+R*1.55); c.lineTo(x+R*.6,hy+R*1.55); c.strokeStyle='#9AAEBD'; c.lineWidth=lw*1.3; c.stroke();
      c.fillStyle='#9AAEBD'; c.fillRect(x+R*.22,hy+R*1.15,R*.07,R*.3); c.fillRect(x+R*.22,hy+R*1.7,R*.07,R*.4);
      if (u&&u.skillT>0) for (let i=0;i<6;i++) circ(c,x+Math.sin(t*2+i*1.3)*R*1.4,hy-R*.5+((t*.5+i/6)%1)*R*3,R*.06,'#FFFFFF'); },
    head(c,x,y,k,R,hx,hy,lw){ const cx=hx-R*.72, cy=hy-R*.5; c.beginPath(); for (let i=0;i<3;i++){ const a=i*Math.PI/3; c.moveTo(cx-Math.cos(a)*R*.17,cy-Math.sin(a)*R*.17); c.lineTo(cx+Math.cos(a)*R*.17,cy+Math.sin(a)*R*.17); } c.strokeStyle='#FFFFFF'; c.lineWidth=lw*1.5; c.stroke(); c.strokeStyle='#3D8BD9'; c.lineWidth=lw*.6; c.stroke(); } },
  ac:{ style:'long', hair:'#F7F9FB', eye:'#3D8BD9', dress:'#F4F7F9', sleeve:'#F4F7F9', shoes:'#3D8BD9',
    back(c,x,y,k,R,hx,hy,lw,u,t){ const on=u&&u.skillT>0; for (const s of [-1,1]) for (let i=0;i<3;i++){ c.save(); c.translate(x+s*R*.55,hy+R*(1.0+i*.28)); c.rotate(s*(on?-.35:-.12)); c.beginPath(); rrPath(c,s>0?0:-R*.95,-R*.09,R*.95,R*.18,R*.08); fillLine(c,'#E3EAF0',lw*.8); c.restore(); }
      if (on){ c.strokeStyle='rgba(61,139,217,.7)'; c.lineWidth=lw*1.4; for (let i=0;i<3;i++){ const ph=(t*2+i/3)%1; c.beginPath(); c.arc(x,hy+R*1.2,R*(1.4+ph*.8),-.5,.5); c.stroke(); c.beginPath(); c.arc(x,hy+R*1.2,R*(1.4+ph*.8),Math.PI-.5,Math.PI+.5); c.stroke(); } } },
    body(c,x,y,k,R,hx,hy,lw){ c.beginPath(); c.moveTo(x-R*.72,y+k*.17); c.lineTo(x+R*.72,y+k*.17); c.strokeStyle='#3D8BD9'; c.lineWidth=R*.12; c.stroke(); },
    prop(c,x,y,k,R,hx,hy,lw){ c.beginPath(); rrPath(c,x+R*.68,hy+R*1.45,R*.2,R*.42,R*.06); fillLine(c,'#F4F7F9',lw*.8); circ(c,x+R*.78,hy+R*1.56,R*.05,'#3D8BD9'); },
    head(c,x,y,k,R,hx,hy,lw){ c.beginPath(); c.ellipse(hx+R*.82,hy-R*.45,R*.16,R*.1,.6,0,Math.PI*2); fillLine(c,'#3D8BD9',lw*.7); } },
  micro:{ style:'pony', hair:'#F08A3E', accent:'#FFD08A', eye:'#C0561E', dress:'#6B7680', sleeve:'#6B7680', shoes:'#3A4046',
    body(c,x,y,k,R,hx,hy,lw){ c.beginPath(); c.moveTo(x-R*.78,y+k*.2); c.quadraticCurveTo(x,y+k*.25,x+R*.78,y+k*.2); c.strokeStyle='#FF9A3C'; c.lineWidth=R*.1; c.stroke(); circ(c,x,hy+R*1.25,R*.09,'#FF9A3C'); },
    head(c,x,y,k,R,hx,hy,lw,u,t){ const on=u&&u.skillT>0; c.beginPath(); rrPath(c,hx-R*.82,hy-R*.72,R*1.64,R*.3,R*.12); fillLine(c,on?`rgb(255,${Math.round(150+60*Math.sin(t*20))},60)`:'#2E3A40',lw); c.fillStyle='#FF9A3C'; c.fillRect(hx+R*.4,hy-R*.64,R*.28,R*.12); } },
  washer:{ style:'buns', hair:'#7CC8E8', eye:'#2F7FC0', dress:'#F4F7F9', sleeve:'#F4F7F9', shoes:'#3D8BD9',
    body(c,x,y,k,R,hx,hy,lw,u,t){ c.strokeStyle='#3D8BD9'; c.lineWidth=lw*1.3; c.beginPath(); c.arc(x,y+k*.1,R*.5,.2,2.4); c.stroke(); c.beginPath(); c.arc(x,y+k*.1,R*.3,3.3,5.6); c.stroke();
      if (u) for (let i=0;i<4;i++){ const ph=(t*.4+i/4)%1; c.beginPath(); c.arc(x+Math.sin(i*2+t)*R*1.2,hy+R*1.6-ph*R*2.6,R*(.06+.04*(i%2)),0,Math.PI*2); c.strokeStyle=`rgba(61,139,217,${.8*(1-ph)})`; c.lineWidth=lw*.8; c.stroke(); } },
    head(c,x,y,k,R,hx,hy,lw,u,t){ const cy=hy-R*1.02; c.beginPath(); c.arc(hx,cy,R*.42,0,Math.PI*2); fillLine(c,'#9AAEBD',lw); circ(c,hx,cy,R*.31,'#BFE0F2');
      const a=t*(u&&u.skillT>0?14:3); c.strokeStyle='#3D8BD9'; c.lineWidth=lw*1.4; c.beginPath(); c.arc(hx,cy,R*.2,a,a+1.9); c.stroke(); c.beginPath(); c.arc(hx,cy,R*.2,a+3.1,a+5); c.stroke(); } },
  pc:{ style:'twin', hair:'#2B3136', tip:'#8EE3C8', eye:'#3FAE9A', dress:'#3A4046', sleeve:'#3A4046', legs:'#2B3136', shoes:'#8EE3C8',
    body(c,x,y,k,R,hx,hy,lw,u,t){ c.beginPath(); c.moveTo(x,hy+R*.95); c.lineTo(x,y+k*.2); c.strokeStyle='#8EE3C8'; c.lineWidth=lw*1.2; c.stroke();
      if (u&&u.skillT>0){ c.fillStyle=`hsla(${150+Math.sin(t*12)*20},75%,62%,.55)`; c.beginPath(); rrPath(c,x-R*.4,hy+R*1.3,R*.8,R*.45,R*.08); c.fill(); } },
    head(c,x,y,k,R,hx,hy,lw){ c.beginPath(); c.arc(hx,hy-R*.05,R*1.08,Math.PI*1.08,Math.PI*1.92); c.strokeStyle='#1D2226'; c.lineWidth=R*.12; c.stroke();
      for (const s of [-1,1]){ c.beginPath(); c.ellipse(hx+s*R*1.02,hy+R*.12,R*.18,R*.26,0,0,Math.PI*2); fillLine(c,'#3A4046',lw); circ(c,hx+s*R*1.02,hy+R*.12,R*.09,'#8EE3C8'); }
      c.beginPath(); c.moveTo(hx-R*1.0,hy+R*.32); c.quadraticCurveTo(hx-R*.85,hy+R*.75,hx-R*.4,hy+R*.72); c.strokeStyle='#1D2226'; c.lineWidth=lw*1.3; c.stroke(); } },
  fan:{ style:'bob', hair:'#7FD3C3', eye:'#2E8B7A', dress:'#FFFFFF', sleeve:'#FFFFFF', shoes:'#7FD3C3',
    body(c,x,y,k,R,hx,hy,lw){ c.strokeStyle='#7FD3C3'; c.lineWidth=lw*1.2; for (let i=-2;i<=2;i++){ c.beginPath(); c.moveTo(x+i*R*.15,hy+R*1.5); c.lineTo(x+i*R*.3,y+k*.21); c.stroke(); } },
    head(c,x,y,k,R,hx,hy,lw,u,t){ const cx=hx+R*.42, cy=hy-R*1.0, a0=u?t*(u.skillT>0?30:8):.4;
      for (let i=0;i<3;i++){ const a=a0+i*2.094; c.beginPath(); c.ellipse(cx+Math.cos(a)*R*.2,cy+Math.sin(a)*R*.2,R*.2,R*.09,a,0,Math.PI*2); fillLine(c,'#7FD3C3',lw*.7); }
      c.beginPath(); c.arc(cx,cy,R*.07,0,Math.PI*2); fillLine(c,'#FFFFFF',lw*.7); } },
};
const DRAW = Object.fromEntries(Object.keys(CHIBI).map(id=>[id,(c,x,y,k,u,t)=>drawChibi(c,x,y,k,CHIBI[id],u,t)]));
