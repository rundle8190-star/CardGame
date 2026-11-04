from comp import *
import numpy as np
def blade(a,i):
    L=a['layer'][i]; al=np.array(L)[:,:,3]; ys,xs=np.nonzero(al>0)
    if len(xs)<5: return None
    cx,cy=a['px'],a['py']-55
    P=np.stack([xs,ys],1).astype(float); mu=P.mean(0); d=np.linalg.svd(P-mu,full_matrices=False)[2][0]
    t=(P-mu)@d; e1=mu+d*t.min(); e2=mu+d*t.max()
    tip,grip=(e1,e2) if np.hypot(*(e1-[cx,cy]))>np.hypot(*(e2-[cx,cy])) else (e2,e1)
    v=tip-grip
    return dict(grip=grip,tip=tip,ang=np.degrees(np.arctan2(v[1],v[0])),len=np.hypot(*v))  # image coords (y down)
