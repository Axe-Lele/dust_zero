// 먼지 타워 시뮬레이터 공용: 진행 단계(클리어한 스테이지 수) → 기준 계정. stage.refLv·refStar·refGear 를 쓰고,
// 보유 가전은 2-5 시작 7종(초반 6종 + 소환 안내 보일러)에 다리미(20판)·컴퓨터(30)·가스레인지(40)·세탁기(50)·냉장고(60)를 차례로 더함. 60판부터 전용장비 4개
import fs from 'fs'; import path from 'path';
export function towerAccount(root,n){
  const st=JSON.parse(fs.readFileSync(path.join(root,'data','stage.json'),'utf8')).sort((a,b)=>a.order-b.order);
  const s=st[Math.max(1,Math.min(st.length,Math.round(n)))-1];
  const types=['vac','robot','fan','gen','ac','micro','boiler'].concat([[20,'iron'],[30,'pc'],[40,'stove'],[50,'washer'],[60,'fridge']].filter(([k])=>n>=k).map(([,t])=>t));
  const owned=Object.fromEntries(types.map(t=>[t,{lv:s.refLv,star:s.refStar}]));
  const excl=n>=60?Object.fromEntries(['vac','ac','micro','iron'].map(t=>[t,{star:n>=75?2:1}])):{};
  return {owned,excl,gear:s.refGear,label:`${s.no} Lv${s.refLv}★${s.refStar} 장비+${Math.round(s.refGear*100)}%`};
}
// 층 → 목표 계정(진행 단계). 1~5층 = 2-5 막 연 계정, 10층 = 4-5, 14층 = 6-5, 18층 = 8-5, 22층 = 10-5, 26층 = 12-5, 30층 = 15-5
export const TOWER_TARGET=[[1,10],[5,10],[10,20],[14,30],[18,40],[22,50],[26,60],[30,75]];
export function towerTarget(f){ const T=TOWER_TARGET; for (let i=1;i<T.length;i++) if (f<=T[i][0]){ const [a,x]=T[i-1],[b,y]=T[i]; return x+(y-x)*(f-a)/(b-a); } return T[T.length-1][1]; }
