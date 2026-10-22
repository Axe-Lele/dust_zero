import cv2, numpy as np
from textmask import TEXT
def clean(reg, t):
    """reg: 패널 왼쪽 위 기준 BGR uint8. 글자 블록 자리를 가장자리는 주변 질감(FSR)으로, 안쪽으로 갈수록 바탕색으로 부드럽게 채운다"""
    m=np.zeros(reg.shape[:2],np.uint8)
    for b in TEXT[t].values(): cv2.rectangle(m,(b[0]-2,b[1]-2),(b[2]+2,b[3]+2),255,-1)
    blob=(cv2.GaussianBlur(m.astype(np.float32)/255,(0,0),6)>0.35).astype(np.uint8)*255
    blob=np.maximum(blob,m)
    fsr=reg.copy(); cv2.xphoto.inpaint(reg,(blob==0).astype(np.uint8)*255,fsr,cv2.xphoto.INPAINT_FSR_BEST)
    R=reg.astype(np.float32)
    ring=(cv2.dilate(blob,np.ones((15,15),np.uint8))>0)&(blob==0)
    px=R[ring]; lum=px.mean(axis=1); keep=lum>=np.percentile(lum,55)
    w=np.zeros(m.shape,np.float32); w[ring]=keep.astype(np.float32); col=np.zeros_like(R); col[ring]=px
    num=cv2.GaussianBlur(col*w[...,None],(0,0),20); den=cv2.GaussianBlur(w,(0,0),20)[...,None]
    fill=np.where(den>1e-3,num/np.maximum(den,1e-3),np.median(px[keep],axis=0))
    dist=cv2.distanceTransform(blob,cv2.DIST_L2,5)
    a=np.clip((dist-3)/16,0,1)[...,None]
    inside=(blob>0)[...,None]
    out=np.where(inside, fsr.astype(np.float32)*(1-a)+fill*a, R)
    return np.clip(out,0,255).astype(np.uint8)
