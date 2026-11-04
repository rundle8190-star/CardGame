# Smooth version: one fixed centre per swing, angle by a monotone cubic, radius by a gentle fit,
# crescent that tapers to a rounded point at the head and a hair at the tail.
from trail2 import RAMP, SPARK, PAD, swing_keys
from blade import *
import random

def pchip(xs, ys):
    xs=np.asarray(xs,float); ys=np.asarray(ys,float); n=len(xs)
    if n==2:
        return lambda x: ys[0]+(ys[1]-ys[0])*np.clip((x-xs[0])/(xs[1]-xs[0]),0,1)
    h=np.diff(xs); dl=np.diff(ys)/h; m=np.zeros(n)
    m[0]=dl[0]; m[-1]=dl[-1]
    for k in range(1,n-1):
        if dl[k-1]*dl[k]<=0: m[k]=0
        else:
            w1=2*h[k]+h[k-1]; w2=h[k]+2*h[k-1]; m[k]=(w1+w2)/(w1/dl[k-1]+w2/dl[k])
    def f(x):
        x=min(max(x,xs[0]),xs[-1]); k=min(np.searchsorted(xs,x,side='right')-1,n-2)
        t=(x-xs[k])/h[k]; t2,t3=t*t,t*t*t
        return (2*t3-3*t2+1)*ys[k]+(t3-2*t2+t)*h[k]*m[k]+(-2*t3+3*t2)*ys[k+1]+(t3-t2)*h[k]*m[k+1]
    return f

def smooth_swing(a, s, e, d, sq, ell=(1,1), sharp=False):
    keys=swing_keys(a,s,e,d)
    tips=np.array([k[2]['tip'] for k in keys]); grips=np.array([k[2]['grip'] for k in keys])
    c=grips.mean(0)
    # angle of each tip around the fixed centre, unwrapped the same way the blade turns
    th=[]; 
    for (i,ang,b),tp in zip(keys,tips):
        v=tp-c; t=np.degrees(np.arctan2(v[1]/max(sq,1e-3) if sq<1 else v[1],v[0]))
        if not th: th.append(t); continue
        step=((t-th[-1])*d)%360
        th.append(th[-1]+d*step)
    r=np.array([np.hypot(*(tp-c)) for tp in tips])
    Lmax=max(b['len'] for b in (blade(a,i) for i in range(len(a['frames']))) if b)
    r=np.maximum(r,0.75*Lmax)
    fr=pchip([k[0] for k in keys],th)
    # radius: quadratic in angle when there are enough keys, else straight line; no kinks
    deg=2 if len(keys)>=4 else 1
    co=np.polyfit(th,r,deg); rr=lambda T: float(np.polyval(co,T))
    tm=fr((keys[0][0]+keys[-1][0])/2); mh=np.array([np.cos(np.radians(tm)),np.sin(np.radians(tm))])
    S=dict(keys=keys,c=c,th=fr,r=rr,sq=sq,d=d,t0=keys[0][0],t1=keys[-1][0],Lmax=Lmax,mh=mh,ell=ell,sharp=sharp,fix=None)
    if ell!=(1,1):
        res=[]
        for (i,_,b),tp in zip(keys,tips):
            T=fr(i); q=place(S,T,rr(T)); res.append(tp-q)
        res=np.array(res); ts=[k[0] for k in keys]
        fx_,fy_=pchip(ts,res[:,0]),pchip(ts,res[:,1])
        S['fix']=lambda tau:np.array([fx_(tau),fy_(tau)])
    return S

def place(S,T,R):
    rad=np.radians(T); v=np.array([np.cos(rad),np.sin(rad)])*R
    al=v@S['mh']; pe=v-al*S['mh']
    v=al*S['ell'][0]*S['mh']+pe*S['ell'][1]
    return S['c']+np.array([v[0],v[1]*S['sq']])

def profile(u, sharp=False):
    if sharp:   # thin tail, fullest at 70%, straight taper to a point at the head
        return np.sin(np.pi/2*min(u/0.7,1))**1.3 * (1 if u<0.7 else max(0.08,1-(u-0.7)/0.3*0.92))
    # width along the crescent: hair at the tail, fullest near the head, rounded point at the head
    w=np.sin(np.pi/2*min(u/0.8,1))**1.3
    if u>0.8: w*=np.sqrt(max(0,1-((u-0.8)/0.2)**2))*0.85+0.15
    return w

def draw(lvl, S, t, tail, fade):
    head=min(t,S['t1']); tl=max(S['t0'],t-tail)
    if not (t>S['t0'] and tl<head): return
    age=max(0,t-S['t1'])/(fade+1)
    n=60; P=[]
    for k in range(n+1):
        tau=tl+(head-tl)*k/n; T=S['th'](tau); R=S['r'](T); u=k/n
        W=0.30*R*profile(u,S['sharp'])
        off=S['fix'](tau) if S['fix'] else 0
        P.append((u,place(S,T,R-W)+off+PAD,place(S,T,R)+off+PAD))
    dr=ImageDraw.Draw(lvl)
    for (u0,i0,o0),(u1,i1,o1) in zip(P,P[1:]):
        lv=max(1,int(1+(len(RAMP)-1)*(u1*(1-age))))
        dr.polygon([tuple(i0),tuple(o0),tuple(o1),tuple(i1)],fill=lv)
    if age<0.5:
        rim=[tuple(o) for u,i,o in P if u>0.4]
        if len(rim)>1: dr.line(rim,fill=len(RAMP),width=1)

def trail_frames(a, swings, tail=3.0, fade=None, seed=1, ell=(1,1), sharp=False):
    fade=fade if fade is not None else max(3,int(tail+1))
    rnd=random.Random(seed); parts=[]
    SS=[smooth_swing(a,*sw,ell=ell,sharp=sharp) for sw in swings]
    for S in SS:
        ks=S['keys']; S['best']=max(zip(ks,ks[1:]),key=lambda p:abs(p[1][1]-p[0][1])/(p[1][0]-p[0][0]))[1][0]
    W,H=a['W']+2*PAD,a['H']+2*PAD; out=[]
    for t in range(len(a['frames'])+fade+2):
        lvl=Image.new('L',(W,H),0)
        for S in SS: draw(lvl,S,t,tail,fade)
        L_=np.array(lvl); px=np.zeros((H,W,4),np.uint8); yy,xx=np.indices(L_.shape)
        for li in range(1,len(RAMP)+1):
            m=(L_==li)
            if li<=2: m&=((xx+yy)%2==0)
            px[m]=RAMP[li-1]+(255,)
        img=Image.fromarray(px)
        for S in SS:
            if t==S['best']:
                T=S['th'](t); R=S['r'](T); rad=np.radians(T); v=np.array([np.cos(rad),np.sin(rad)*S['sq']]); tip=place(S,T,R)+(S['fix'](t) if S['fix'] else 0)+PAD
                tang=np.array([-np.sin(rad),np.cos(rad)])*S['d']
                for _ in range(9):
                    sp=rnd.uniform(1.5,4); j=rnd.uniform(-0.6,0.6)
                    parts.append([tip.copy(),(tang*np.cos(j)+v*np.sin(j))*sp,rnd.randint(3,6),rnd.choice(SPARK)])
        dr=ImageDraw.Draw(img)
        for p in parts:
            if p[2]>0:
                q=p[0]+p[1]; dr.line([tuple(p[0]),tuple(q)],fill=p[3]+(255,)); p[0]=q; p[1]=p[1]*0.8+np.array([0,0.35]); p[2]-=1
        out.append(img)
    return out
