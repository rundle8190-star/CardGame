import json, os, trail4
from trail2 import PAD
from comp import *
OUT='/mnt/project-files/graphics/fx_test/final'
SW={'sword_regular_a':[(4,7,-1,1)],'sword_regular_b':[(3,6,1,1)],'sword_regular_c':[(15,17,1,1)],
    'sword_dash':[(7,8,-1,0.3)],'sword_regular_combo':[(4,7,-1,1),(14,19,-1,1),(36,41,1,1)],
    'sword_heavy_combo':[(9,10,-1,0.3),(23,28,-1,1),(41,43,-1,0.3),(60,64,1,1)]}
for ch in ['ranger','woodcutter']:
    os.makedirs(f'{OUT}/{ch}',exist_ok=True)
    for m,sw in SW.items():
        a=load_anim(ch,m,'sword'); n=len(a['frames'])
        fr=trail4.trail_frames(a,sw,tail=3.0,ell=(0.85,1.2),sharp=True)[:n]
        W,H=fr[0].size; cols=min(n,12); rows=(n+cols-1)//cols
        sheet=Image.new('RGBA',(cols*W,rows*H),(0,0,0,0))
        for i,f in enumerate(fr): sheet.paste(f,((i%cols)*W,(i//cols)*H))
        sheet.save(f'{OUT}/{ch}/{m}_slash.png',optimize=True)
        json.dump(dict(motion=m,fps=24,frames=n,frameWidth=W,frameHeight=H,columns=cols,rows=rows,
             offsetX=-PAD,offsetY=-PAD,note='몸 칸의 왼쪽 위에서 (offsetX, offsetY) 만큼 옮겨 몸 위에 겹친다. 프레임 i = 몸 프레임 i.',
             swings=[dict(start=s,end=e,dir=('시계' if d>0 else '반시계'),flat=q) for s,e,d,q in sw],
             style=dict(shape='타원 약하게',ellipse=[0.85,1.2],tailFrames=3,palette=['#3c434f','#5d6676','#8892a2','#b9c2d0','#e3e9f0','#ffffff'])),
             open(f'{OUT}/{ch}/{m}_slash.json','w'),ensure_ascii=False,indent=1)
        print(ch,m,n,W,H,sheet.size)
