import json
from comp import *
F='/mnt/project-files/graphics/fx_test/final'
MS=['sword_regular_a','sword_regular_b','sword_regular_c','sword_dash','sword_regular_combo','sword_heavy_combo']
cells=[];N=105;cw,chh,fy,ax=340,250,190,140
for ch in ['ranger','woodcutter']:
  for m in MS:
    a=load_anim(ch,m,'sword'); j=json.load(open(f'{F}/{ch}/{m}_slash.json')); sh=Image.open(f'{F}/{ch}/{m}_slash.png')
    W,H,c=j['frameWidth'],j['frameHeight'],j['columns']; fr=[]
    for t in range(N):
      i=t%a['frames'].__len__()
      im=Image.new('RGBA',(cw,chh),BG); ImageDraw.Draw(im).rectangle((0,fy,cw,chh),fill=GROUND)
      im.alpha_composite(a['frames'][i],(ax-a['px'],fy-a['py']))
      s=sh.crop(((i%c)*W,(i//c)*H,(i%c+1)*W,(i//c+1)*H)); im.alpha_composite(s,(ax-a['px']+j['offsetX'],fy-a['py']+j['offsetY']))
      ImageDraw.Draw(im).text((4,2),f'{ch} {m}',font=FONT,fill=(235,235,235,255)); fr.append(im)
    cells.append(fr)
g=grid(cells,6,cw,chh,N,'final_check.gif',scale=1)
for t in [6,16,40,63]: g[t].save(f'fc{t}.png')
