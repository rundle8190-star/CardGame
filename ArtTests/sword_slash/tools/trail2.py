from blade import *
import random
RAMP=[(0x3c,0x43,0x4f),(0x5d,0x66,0x76),(0x88,0x92,0xa2),(0xb9,0xc2,0xd0),(0xe3,0xe9,0xf0),(0xff,0xff,0xff)]
SPARK=[(0xff,0xf3,0xa6),(0xff,0xe2,0x6b),(0xff,0xff,0xff)]
PAD=60

def swing_keys(a, start, end, d):
    bs={i:blade(a,i) for i in range(start,end+1)}
    Lm=max(b['len'] for b in (blade(a,i) for i in range(len(a['frames']))) if b)
    keys=[]
    for i in range(start,end+1):
        b=bs[i]
        if b is None or b['len']<0.4*Lm: continue   # blade pointing at camera: unreadable
        if not keys: keys.append((i,b['ang'],b)); continue
        step=((b['ang']-keys[-1][1])*d)%360
        if step<1 or step>300: continue              # small move the wrong way: skip
        keys.append((i,keys[-1][1]+d*step,b))
    return keys

def at(keys,tau):
    if tau<=keys[0][0]: k=keys[0]; return k[2]['grip'],k[1],k[2]['len']
    for (i0,a0,b0),(i1,a1,b1) in zip(keys,keys[1:]):
        if tau<=i1:
            f=(tau-i0)/(i1-i0); return b0['grip']*(1-f)+b1['grip']*f, a0*(1-f)+a1*f, b0['len']*(1-f)+b1['len']*f
    k=keys[-1]; return k[2]['grip'],k[1],k[2]['len']

def draw_swing(px_lvl, keys, t, tail, fade, Lmax, squash):
    head=min(t,keys[-1][0]); tl=max(keys[0][0],t-tail)
    if not (t>keys[0][0] and tl<head): return
    age=max(0,t-keys[-1][0])/(fade+1)
    dr=ImageDraw.Draw(px_lvl); n=40; S=[]
    for k in range(n+1):
        tau=tl+(head-tl)*k/n; g,ang,L=at(keys,tau); L=max(L,0.75*Lmax)
        u=k/n; r=np.radians(ang); v=np.array([np.cos(r),np.sin(r)*squash])
        rin=1-(0.04+0.26*u**1.5)
        S.append((u,g+v*L*rin+PAD,g+v*L+PAD))
    for (u0,i0,o0),(u1,i1,o1) in zip(S,S[1:]):
        lv=max(1,int(1+(len(RAMP)-1)*(u1*(1-age))))
        dr.polygon([tuple(i0),tuple(o0),tuple(o1),tuple(i1)],fill=lv)
    for (u0,i0,o0),(u1,i1,o1) in zip(S,S[1:]):
        if u1>0.45 and age<0.5: dr.line([tuple(o0),tuple(o1)],fill=len(RAMP),width=1)

def trail_frames(a, swings, tail=2.0, fade=None, seed=1):
    """swings: list of (start, end, dir, squash). Returns frames padded by PAD on every side."""
    fade = fade if fade is not None else max(3,int(tail+1))
    rnd=random.Random(seed); parts=[]
    S=[]
    for (s,e,d,sq) in swings:
        keys=swing_keys(a,s,e,d); Lmax=max(k[2]['len'] for k in keys)
        best=max(zip(keys,keys[1:]),key=lambda p:abs(p[1][1]-p[0][1])/(p[1][0]-p[0][0]))[1][0]
        S.append((keys,Lmax,sq,d,best))
    W,H=a['W']+2*PAD,a['H']+2*PAD; out=[]
    for t in range(len(a['frames'])+fade+2):
        lvl=Image.new('L',(W,H),0)
        for keys,Lmax,sq,d,best in S: draw_swing(lvl,keys,t,tail,fade,Lmax,sq)
        L_=np.array(lvl); px=np.zeros((H,W,4),np.uint8); yy,xx=np.indices(L_.shape)
        for li in range(1,len(RAMP)+1):
            m=(L_==li)
            if li<=2: m&=((xx+yy)%2==0)
            px[m]=RAMP[li-1]+(255,)
        img=Image.fromarray(px)
        for keys,Lmax,sq,d,best in S:
            if t==best:
                g,ang,L=at(keys,best); r=np.radians(ang); v=np.array([np.cos(r),np.sin(r)*sq]); tip=g+v*max(L,0.75*Lmax)+PAD
                tang=np.array([-np.sin(r),np.cos(r)])*d
                for _ in range(9):
                    sp=rnd.uniform(1.5,4); j=rnd.uniform(-0.6,0.6)
                    parts.append([tip.copy(),(tang*np.cos(j)+np.array([np.cos(r),np.sin(r)])*np.sin(j))*sp,rnd.randint(3,6),rnd.choice(SPARK)])
        dr=ImageDraw.Draw(img)
        for p in parts:
            if p[2]>0:
                q=p[0]+p[1]; dr.line([tuple(p[0]),tuple(q)],fill=p[3]+(255,)); p[0]=q; p[1]=p[1]*0.8+np.array([0,0.35]); p[2]-=1
        out.append(img)
    return out
