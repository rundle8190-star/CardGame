# 의상 뼈대에 애니메이션 팩 동작 씌우기
# 사용법: 의상 FBX 부품들 + 애니메이션 팩 FBX를 새 장면에 불러온 뒤,
# 스크립팅 탭 > 텍스트 > 열기 로 이 파일을 열고 ▶ 실행
import bpy

if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')

arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
src = next((a for a in arms if a.animation_data and a.animation_data.action), None)
if src is None:
    raise Exception("동작이 들어 있는 뼈대를 못 찾았습니다. 애니메이션 팩 FBX를 불러왔는지 확인하세요.")
outfit_arms = [a for a in arms if a != src]
if not outfit_arms:
    raise Exception("의상 뼈대가 없습니다. 의상 FBX 부품을 불러왔는지 확인하세요.")
keep = outfit_arms[0]
print("동작 원본:", src.name, "/ 옷 뼈대:", keep.name)

for o in [o for o in bpy.data.objects if o.type == 'MESH']:
    if o.parent == src:
        continue
    for m in o.modifiers:
        if m.type == 'ARMATURE':
            m.object = keep
    mw = o.matrix_world.copy()
    o.parent = keep
    o.matrix_world = mw

for a in outfit_arms[1:]:
    bpy.data.objects.remove(a, do_unlink=True)

for act in bpy.data.actions:
    act.use_fake_user = True

act = src.animation_data.action
if not keep.animation_data:
    keep.animation_data_create()
keep.animation_data.action = act
if act.slots:
    keep.animation_data.action_slot = act.slots[0]

src_bones = set(b.name for b in src.data.bones)
miss = [b.name for b in keep.data.bones if b.name not in src_bones]
print("동작 없는 뼈:", len(miss), miss[:8])

for c in list(src.children):
    bpy.data.objects.remove(c, do_unlink=True)
bpy.data.objects.remove(src, do_unlink=True)

s, e = act.frame_range
bpy.context.scene.frame_start, bpy.context.scene.frame_end = int(s), int(e)
print("끝")
