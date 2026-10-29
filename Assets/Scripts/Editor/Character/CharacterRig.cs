using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 굽기용으로 캐릭터를 조립하고(몸 + 옷 + 머리 + 무기 + 방패), 원하는 모션의 원하는 시간 자세를 만든 뒤
    /// 카메라 없이 직접 그려서 픽셀을 돌려준다.
    /// 화면에 보이지 않는 별도 공간(미리보기 씬)에서 만들기 때문에 열려 있는 씬은 건드리지 않는다.
    /// </summary>
    public class CharacterRig : IDisposable
    {
        const string ShaderName = "Hidden/CardGame/BakeToon";

        class DrawItem
        {
            public Renderer renderer;
            public SkinnedMeshRenderer skinned;
            public Mesh mesh;          // 스킨이면 매 프레임 구운 메시, 아니면 원본 메시
            public Material[] mats;
            public bool[] skipSub;     // 화면에서 너무 작아서 그리지 않을 부분 (버클, 징 등)
        }

        public readonly CharacterBakeSetup setup;
        Scene scene;
        GameObject root;
        Animator animator;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly List<DrawItem> items = new List<DrawItem>();
        readonly List<Material> createdMats = new List<Material>();
        readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();

        Transform weaponTf;
        Vector3 weaponTipLocal;
        Transform handTf;

        PlayableGraph graph;
        AnimationClipPlayable clipPlayable;
        AnimationClip currentClip;
        bool useDirect;        // 휴머노이드 변환 없이 뼈에 그대로 재생 중
        float clipOffsetY;     // 첫 프레임 기준 높이 맞춤 (모션 전체에 같은 값)

        RenderTexture rt;
        Texture2D readback;
        int autoFlip = -1; // -1: 아직 모름, 0: 그대로, 1: 뒤집기
        Color32[] paletteCache;
        /// <summary>전부 굽기 때 만든 고정 팔레트. 있으면 설정의 팔레트 PNG 대신 쓴다.</summary>
        public Color32[] AutoPalette;
        /// <summary>true면 팔레트·윤곽선 없이 색 그대로 돌려준다 (팔레트 만들 때 쓴다).</summary>
        public bool RawColors;

        public string Warnings { get; private set; } = "";
        /// <summary>점검용 정보 (무기, 방패 크기와 위치). 굽기 창 아래에 작게 표시된다.</summary>
        public string Info { get; private set; } = "";
        public bool IsValid => root != null && animator != null && humanAvatar != null && humanAvatar.isHuman;
        Avatar humanAvatar;   // 몸의 휴머노이드 아바타 (Generic 모션을 그대로 넣을 때는 잠시 뺀다)
        readonly Dictionary<HumanBodyBones, Transform> humanBones = new Dictionary<HumanBodyBones, Transform>();
        Transform HB(HumanBodyBones b) => humanBones.TryGetValue(b, out var t) ? t : null;
        // 현재 프레임 크기. 기본은 설정값이고, 크게 움직이는 모션은 굽기 직전에 자동으로 넓힌다.
        int frameW, frameH, pivX, ground;
        public int Width => frameW;
        public int Height => frameH;
        public int PivotX => pivX;
        public int Ground => ground;

        public void UseFrame(int w, int h, int pivotX, int groundY) { frameW = w; frameH = h; pivX = pivotX; ground = groundY; }
        public void ResetFrame() => UseFrame(setup.frameWidth, setup.frameHeight, Mathf.RoundToInt(setup.frameWidth * 0.375f), setup.groundPixel);

        public CharacterRig(CharacterBakeSetup setup)
        {
            this.setup = setup;
            ResetFrame();
            Build();
        }

        // ───────────────────────── 조립 ─────────────────────────

        void Build()
        {
            var s = setup;
            if (s.baseModel == null) { Warnings += "기본 몸(baseModel)이 비어 있습니다.\n"; return; }
            if (Shader.Find(ShaderName) == null) { Warnings += $"셰이더 {ShaderName} 를 찾을 수 없습니다. Assets/Art3D/Shaders 를 확인하세요.\n"; return; }

            scene = EditorSceneManager.NewPreviewScene();

            root = Spawn(s.baseModel, "bake_body");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = null;
            humanAvatar = animator.avatar;
            humanBones.Clear();
            if (humanAvatar != null && humanAvatar.isHuman)
                for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
                {
                    var t = animator.GetBoneTransform(b);
                    if (t != null) humanBones[b] = t;
                }
            if (animator.avatar == null || !animator.avatar.isHuman)
                Warnings += "기본 몸이 휴머노이드로 가져와지지 않았습니다. FBX 선택 > Rig > Animation Type = Humanoid 로 바꾸고 Apply 하세요.\n";

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;

            var pushRenderers = new HashSet<Renderer>(root.GetComponentsInChildren<Renderer>(true));

            // 옷을 입히면 기본 몸은 머리만 남긴다 (Quaternius 옷은 팔, 다리 맨살까지 들어 있다)
            if (s.hideBodyUnderClothes && s.parts.Exists(x => x != null))
                foreach (var r in pushRenderers)
                    if (r is SkinnedMeshRenderer bodySmr && !s.noPushRenderers.Exists(n => !string.IsNullOrEmpty(n) && r.name.Contains(n)))
                        KeepOnlyBones(bodySmr, s.keepBodyBones);

            // 옷, 머리카락: 뼈 이름으로 몸 뼈에 다시 연결한다
            foreach (var part in s.parts)
            {
                if (part == null) continue;
                var go = Spawn(part, part.name);
                foreach (var a in go.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(a);
                go.transform.SetParent(root.transform, false);
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var src = smr.bones;
                    var dst = new Transform[src.Length];
                    int missing = 0;
                    for (int i = 0; i < src.Length; i++)
                    {
                        if (src[i] != null && bones.TryGetValue(src[i].name, out var b)) dst[i] = b;
                        else { dst[i] = src[i]; missing++; }
                    }
                    smr.bones = dst;
                    if (smr.rootBone != null && bones.TryGetValue(smr.rootBone.name, out var rb)) smr.rootBone = rb;
                    if (missing > 0) Warnings += $"{part.name}/{smr.name}: 몸에 없는 뼈 {missing}개 (그 부분은 움직이지 않을 수 있음)\n";
                }
            }

            BuildWeapon();
            BuildShield();

            // 그릴 목록과 재질 만들기
            int tinyHidden = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                var item = new DrawItem { renderer = r };
                if (r is SkinnedMeshRenderer smr)
                {
                    if (smr.sharedMesh == null) continue;
                    item.skinned = smr;
                    item.mesh = new Mesh { name = smr.name + "_baked" };
                    createdObjects.Add(item.mesh);
                    if ((smr.transform.lossyScale - Vector3.one).sqrMagnitude > 0.0001f)
                        Warnings += $"{smr.name}: 크기 배율이 1이 아닙니다({smr.transform.lossyScale}). 크기가 어긋나 보이면 알려 주세요.\n";
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    item.mesh = mf.sharedMesh;
                }

                bool push = s.bodyDepthPush > 0f && pushRenderers.Contains(r) && !s.noPushRenderers.Exists(n => !string.IsNullOrEmpty(n) && r.name.Contains(n));
                var src = r.sharedMaterials;
                item.mats = new Material[Mathf.Max(1, src.Length)];
                for (int i = 0; i < item.mats.Length; i++)
                    item.mats[i] = r.gameObject.name == "bake_temp_shield"
                        ? MakeMat(null, s.shield.color, null, 0f)
                        : MakeMat(i < src.Length ? src[i] : null, Color.white, null, push ? s.bodyDepthPush : 0f);
                // 화면에서 너무 작은 조각(버클, 징 등)은 프레임마다 생겼다 사라지며 반짝이므로 아예 그리지 않는다
                var srcMesh = item.skinned != null ? item.skinned.sharedMesh : item.mesh;
                item.skipSub = new bool[srcMesh.subMeshCount];
                bool keep = r.gameObject.name.StartsWith("bake_temp") || (weaponTf != null && r.transform.IsChildOf(weaponTf))
                         || s.noPushRenderers.Exists(n => !string.IsNullOrEmpty(n) && r.name.Contains(n));
                if (s.hideTinyPartsPixels > 0f && !keep)
                {
                    float toPx = Mathf.Max(1f, s.pixelsPerMeter) * s.heightScale * r.transform.lossyScale.x;
                    for (int sub = 0; sub < srcMesh.subMeshCount; sub++)
                    {
                        var sz = srcMesh.GetSubMesh(sub).bounds.size;
                        if (Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z)) * toPx < s.hideTinyPartsPixels)
                        {
                            item.skipSub[sub] = true;
                            tinyHidden++;
                        }
                    }
                }
                items.Add(item);
            }
            if (tinyHidden > 0) Info += $"작은 부속 {tinyHidden}개는 화면에서 {s.hideTinyPartsPixels}픽셀보다 작아 그리지 않음\n";

            handTf = Bone("hand_r");
            InitFeet();
        }

        // ───────────────────────── 발 보정 ─────────────────────────

        class FootRef { public Transform foot, toe; public float restPitch, restHeight, restToeHeight, preSole; public Quaternion restToeLocal; public Transform upper, lower; public string name; }
        readonly List<FootRef> feet = new List<FootRef>();
        public string footInfo = "";

        /// <summary>처음 자세(바닥에 평평하게 선 자세)에서 발목→발가락 기울기와 발목 높이를 기억해 둔다.</summary>
        void InitFeet()
        {
            feet.Clear();
            if (!IsValid) return;
            foreach (var (fb, tb, nm) in new[] { (HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, "왼발"), (HumanBodyBones.RightFoot, HumanBodyBones.RightToes, "오른발") })
            {
                bool left = fb == HumanBodyBones.LeftFoot;
                var foot = HB(fb);
                if (foot == null) continue;
                var toe = HB(tb);
                if (toe == null && foot.childCount > 0) toe = foot.GetChild(0);
                if (toe == null) continue;
                Vector3 d = toe.position - foot.position;
                float horiz = new Vector2(d.x, d.z).magnitude;
                if (horiz < 1e-4f) continue;
                feet.Add(new FootRef
                {
                    foot = foot, toe = toe, name = nm, restToeLocal = toe.localRotation,
                    upper = HB(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg),
                    lower = HB(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg),
                    restToeHeight = Mathf.Max(0f, toe.position.y - root.transform.position.y),
                    restPitch = Mathf.Atan2(-d.y, horiz) * Mathf.Rad2Deg,
                    restHeight = Mathf.Max(0.02f, foot.position.y - root.transform.position.y),
                });
            }
            if (feet.Count == 0) Warnings += "발 뼈를 찾지 못해 발 평평하게 맞추기를 건너뜁니다.\n";
            else Info += $"발 기준: 발목 높이 {feet[0].restHeight * 100f:0.0}cm, 발등 기울기 {feet[0].restPitch:0}도\n";
        }

        /// <summary>
        /// 리타겟하면 발목이 펴져서 발끝이 아래로 꺾이고 뒤꿈치가 뜬다 (하이힐처럼 보임).
        /// 발이 바닥 근처에 있을 때만 발 기울기를 처음 자세로 돌려 놓고, 그래도 뜨는 만큼 몸 전체를 내린다.
        /// 점프나 쓰러지는 중처럼 발이 높이 있으면 건드리지 않는다.
        /// </summary>
        void FlattenFeet()
        {
            if (!setup.flattenFeet || setup.flattenFeetAmount <= 0f || feet.Count == 0) return;
            float baseY = root.transform.position.y;
            float maxFix = 0f;
            footDebug = "";
            foreach (var f in feet)
            {
                float h = f.foot.position.y - baseY;
                // 발목이 처음 높이의 1.3배 안이면 완전히, 1.8배 넘으면 안 맞춘다
                float g = Mathf.InverseLerp(f.restHeight * 1.8f, f.restHeight * 1.3f, h) * setup.flattenFeetAmount;
                Vector3 d = f.toe.position - f.foot.position;
                Vector3 hd = new Vector3(d.x, 0f, d.z);
                float pitch = Mathf.Atan2(-d.y, Mathf.Max(1e-5f, hd.magnitude)) * Mathf.Rad2Deg;
                float yaw = hd.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(Vector3.forward, hd, Vector3.up) : 0f;
                footDebug += $"{f.name} 발목 {h * 100f:0.0}cm 기울기 {pitch:0}도 바깥 {yaw:0}도 / ";
                if (g <= 0f) continue;
                // 옆모습에서 발이 카메라 쪽으로 돌아가 있으면 짧고 뾰족하게 보인다 (하이힐처럼).
                // 바닥에 있는 발은 발끝이 화면 앞쪽(+Z)을 보게 돌린다. 뒤를 보는 발은 그대로 둔다.
                if (Mathf.Abs(yaw) < 100f)
                {
                    f.foot.rotation = Quaternion.AngleAxis(-yaw * g, Vector3.up) * f.foot.rotation;
                    d = f.toe.position - f.foot.position;
                    hd = new Vector3(d.x, 0f, d.z);
                    pitch = Mathf.Atan2(-d.y, Mathf.Max(1e-5f, hd.magnitude)) * Mathf.Rad2Deg;
                    maxFix = Mathf.Max(maxFix, Mathf.Abs(yaw) * g);
                }
                // 바닥에 있는 발은 발가락 꺾임을 처음 자세로 편다
                f.toe.localRotation = Quaternion.Slerp(f.toe.localRotation, f.restToeLocal, g);
                if (hd.sqrMagnitude < 1e-8f) continue;
                float excess = pitch - f.restPitch;
                if (excess > 0f)
                {
                    float rp = f.restPitch * Mathf.Deg2Rad;
                    Vector3 target = hd.normalized * Mathf.Cos(rp) + Vector3.down * Mathf.Sin(rp);
                    var fix = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(d.normalized, target), g);
                    f.foot.rotation = fix * f.foot.rotation;
                    maxFix = Mathf.Max(maxFix, excess * g);
                }
            }
            lastFootFix = maxFix;
        }
        float lastFootFix;
        string footDebug = "";
        bool skipFixes;

        /// <summary>
        /// 바닥에서 조금(plantDistance 이내) 떠 있는 발을 바닥에 붙인다. 발만 내리면 다리와 떨어지므로
        /// 허벅지, 종아리를 다시 굽혀서(두 뼈 IK) 발목이 목표 위치에 오게 한다. 발 방향은 그대로 둔다.
        /// </summary>
        /// <summary>발바닥 가장 낮은 곳의 높이 (바닥 = 0). 뒤꿈치와 발가락 중 낮은 쪽.</summary>
        static float Sole(FootRef f) => Mathf.Min(f.foot.position.y - f.restHeight, f.toe.position.y - f.restToeHeight);

        /// <summary>
        /// 매 프레임 발 붙이기.
        /// 1) 원래 모션에서 두 발 중 낮은 발이 바닥 근처(groundLockHeight 안)면, 몸 전체를 올리거나 내려서 그 발을 바닥선에 딱 맞춘다.
        /// 2) 원래 모션에서 낮은 발과 높이 차이가 plantDistance 안인 발은 '디딘 발'로 보고, 다리를 굽혀(IK) 바닥에 붙인다.
        ///    리타겟이나 발 펴기 때문에 뒷발이 살짝 뜨는 것을 막는다. 걷거나 차는 중에 높이 든 발은 그대로 둔다.
        /// </summary>
        void PlantFeet()
        {
            if (!setup.plantFeet || feet.Count == 0) return;
            float minPre = float.MaxValue;
            foreach (var f in feet) minPre = Mathf.Min(minPre, f.preSole);
            if (minPre > setup.groundLockHeight) return; // 점프 등 공중

            float minNow = float.MaxValue;
            foreach (var f in feet) minNow = Mathf.Min(minNow, Sole(f));
            root.transform.position -= Vector3.up * minNow;

            foreach (var f in feet)
            {
                if (f.upper == null || f.lower == null) continue;
                if (f.preSole - minPre > setup.plantDistance) continue; // 든 발
                float sole = Sole(f);
                if (sole <= 0.002f) continue;
                SolveLeg(f, f.foot.position + Vector3.down * sole);
            }
        }

        static void SolveLeg(FootRef f, Vector3 target)
        {
            Quaternion footRot = f.foot.rotation;
            Vector3 h = f.upper.position, k = f.lower.position, a0 = f.foot.position;
            float a = (k - h).magnitude, b = (a0 - k).magnitude;
            if (a < 1e-4f || b < 1e-4f) return;
            float c = Mathf.Clamp((target - h).magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
            // 무릎 굽힘 각도를 목표 거리에 맞춘다 (코사인 법칙)
            Vector3 axis = Vector3.Cross(h - k, a0 - k);
            if (axis.sqrMagnitude < 1e-10f) axis = f.upper.rotation * Vector3.right;
            axis.Normalize();
            float cur = Vector3.Angle(h - k, a0 - k);
            float want = Mathf.Acos(Mathf.Clamp((a * a + b * b - c * c) / (2f * a * b), -1f, 1f)) * Mathf.Rad2Deg;
            f.lower.rotation = Quaternion.AngleAxis(want - cur, axis) * f.lower.rotation;
            // 허벅지를 돌려 발목이 목표 쪽을 향하게
            Vector3 now = f.foot.position - h;
            f.upper.rotation = Quaternion.FromToRotation(now, target - h) * f.upper.rotation;
            f.foot.rotation = footRot;
        } // 처음 방향을 잴 때는 보정 없이 원래 자세로 잰다

        /// <summary>
        /// 골반이 좌우로 돌아가 있거나 옆으로 기울면 3D에선 자연스러워도 도트 옆모습에선 어색하다.
        /// 골반의 좌우 축(왼쪽 허벅지→오른쪽 허벅지)이 화면 깊이 방향(X)과 나란하게 돌린다.
        /// 위아래 몸 전체가 같이 돌아가므로 모션 도중 뒤쪽을 보게 되는 것도 바로잡힌다. 앞뒤 숙임은 건드리지 않는다.
        /// </summary>
        void StraightenHips()
        {
            if (!setup.straightenHips || setup.straightenHipsAmount <= 0f) return;
            var hips = HB(HumanBodyBones.Hips);
            var l = HB(HumanBodyBones.LeftUpperLeg);
            var r = HB(HumanBodyBones.RightUpperLeg);
            if (hips == null || l == null || r == null) return;
            Vector3 side = r.position - l.position;
            if (side.sqrMagnitude < 1e-8f) return;
            side.Normalize();
            // 캐릭터는 +Z를 보므로 오른쪽은 +X. 뒤로 완전히 돈 자세(120도 넘게)는 일부러 도는 동작이라 두고 넘긴다.
            float yaw = Vector3.SignedAngle(Vector3.right, new Vector3(side.x, 0f, side.z), Vector3.up);
            if (Mathf.Abs(yaw) > 120f) { lastHipFix = 0f; return; }
            var fix = Quaternion.FromToRotation(side, Vector3.right);
            fix = Quaternion.Slerp(Quaternion.identity, fix, setup.straightenHipsAmount);
            // 골반 위치를 축으로 돌린다 (자식인 다리, 허리, 머리 모두 같이)
            hips.rotation = fix * hips.rotation;
            lastHipFix = Vector3.Angle(side, Vector3.right) * setup.straightenHipsAmount;
        }
        float lastHipFix;

        /// <summary>남길 뼈들에 무게가 절반 이상인 삼각형만 남긴 메시 복사본으로 바꾼다.</summary>
        void KeepOnlyBones(SkinnedMeshRenderer smr, List<string> keepNames)
        {
            var src = smr.sharedMesh;
            if (src == null) return;
            var smrBones = smr.bones;
            var keep = new bool[smrBones.Length];
            int keepCount = 0;
            for (int i = 0; i < smrBones.Length; i++)
                if (smrBones[i] != null && keepNames.Contains(smrBones[i].name)) { keep[i] = true; keepCount++; }
            if (keepCount == 0) { Warnings += $"{smr.name}: 남길 뼈({string.Join(", ", keepNames)})를 찾지 못해 몸을 지우지 않았습니다.\n"; return; }

            var weights = src.boneWeights;
            if (weights == null || weights.Length != src.vertexCount) return;
            var vKeep = new bool[weights.Length];
            for (int v = 0; v < weights.Length; v++)
            {
                var bw = weights[v];
                float k = 0f;
                if (bw.boneIndex0 < keep.Length && keep[bw.boneIndex0]) k += bw.weight0;
                if (bw.boneIndex1 < keep.Length && keep[bw.boneIndex1]) k += bw.weight1;
                if (bw.boneIndex2 < keep.Length && keep[bw.boneIndex2]) k += bw.weight2;
                if (bw.boneIndex3 < keep.Length && keep[bw.boneIndex3]) k += bw.weight3;
                vKeep[v] = k >= 0.5f;
            }

            var mesh = UnityEngine.Object.Instantiate(src);
            mesh.name = src.name + "_head_only";
            int removed = 0, total = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var tris = mesh.GetTriangles(sub);
                var kept = new List<int>(tris.Length);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    total++;
                    if (vKeep[tris[t]] && vKeep[tris[t + 1]] && vKeep[tris[t + 2]])
                    { kept.Add(tris[t]); kept.Add(tris[t + 1]); kept.Add(tris[t + 2]); }
                    else removed++;
                }
                mesh.SetTriangles(kept, sub);
            }
            createdObjects.Add(mesh);
            smr.sharedMesh = mesh;
            Info += $"몸 {smr.name}: 옷에 가려지는 삼각형 {removed}/{total}개 지움 (머리만 남김)\n";
        }

        GameObject Spawn(GameObject prefab, string name)
        {
            var go = (GameObject)UnityEngine.Object.Instantiate(prefab);
            go.name = name;
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        Transform Bone(string name) => !string.IsNullOrEmpty(name) && bones.TryGetValue(name, out var t) ? t : null;

        Material MakeMat(Material src, Color baseColor, Texture tex, float push)
        {
            Color color = baseColor;
            if (src != null)
            {
                // 1) 설정의 텍스처 연결 규칙 (가장 길게 일치하는 것)
                CharacterBakeSetup.TextureRule best = null;
                foreach (var rule in setup.textureRules)
                {
                    if (rule == null || string.IsNullOrEmpty(rule.materialNameContains)) continue;
                    if (src.name.IndexOf(rule.materialNameContains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (best == null || rule.materialNameContains.Length > best.materialNameContains.Length) best = rule;
                }
                if (best != null) { tex = best.texture; color = best.tint; }
                else
                {
                    // 2) 원래 재질의 텍스처와 색
                    if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
                    if (tex == null && src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");
                    if (src.HasProperty("_BaseColor")) color = src.GetColor("_BaseColor");
                    else if (src.HasProperty("_Color")) color = src.GetColor("_Color");
                }
            }
            var m = new Material(Shader.Find(ShaderName)) { hideFlags = HideFlags.HideAndDontSave };
            m.SetTexture("_MainTex", tex != null ? tex : Texture2D.whiteTexture);
            m.SetColor("_Color", color);
            m.SetFloat("_DepthPush", push);
            createdMats.Add(m);
            return m;
        }

        // ───────────────────────── 무기 ─────────────────────────

        void BuildWeapon()
        {
            var w = setup.weapon;
            if (w.prefab == null) return;
            var hand = Bone(w.boneName);
            if (hand == null) { Warnings += $"무기 뼈 '{w.boneName}' 를 찾을 수 없습니다.\n"; return; }

            var go = Spawn(w.prefab, "bake_weapon");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            // 무기 모양 분석: 가장 긴 축 = 자루 방향, 가장 넓은 곳 = 날(머리) 위치
            var pts = new List<Vector3>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices) pts.Add(go.transform.InverseTransformPoint(mf.transform.TransformPoint(v)));
            }
            if (pts.Count == 0) { Warnings += "무기 메시가 비어 있습니다.\n"; return; }

            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            int k = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : (b.size.y >= b.size.z ? 1 : 2);
            int o1 = (k + 1) % 3, o2 = (k + 2) % 3;
            int kb = b.size[o1] >= b.size[o2] ? o1 : o2; // 두 번째로 긴 축 (날 넓이 방향)

            const int Bins = 20;
            float lo = b.min[k], len = Mathf.Max(0.0001f, b.size[k]);
            var binMin = new float[Bins]; var binMax = new float[Bins];
            var binSumB = new float[Bins]; var binCount = new int[Bins];
            for (int i = 0; i < Bins; i++) { binMin[i] = float.MaxValue; binMax[i] = float.MinValue; }
            foreach (var p in pts)
            {
                int bi = Mathf.Clamp((int)((p[k] - lo) / len * Bins), 0, Bins - 1);
                binMin[bi] = Mathf.Min(binMin[bi], p[kb]); binMax[bi] = Mathf.Max(binMax[bi], p[kb]);
                binSumB[bi] += p[kb]; binCount[bi]++;
            }
            int widest = 0; float widestW = -1;
            for (int i = 0; i < Bins; i++)
            {
                if (binCount[i] == 0) continue;
                float wd = binMax[i] - binMin[i];
                if (wd > widestW) { widestW = wd; widest = i; }
            }
            float widestPos = lo + (widest + 0.5f) / Bins * len;

            string lname = w.prefab.name.ToLowerInvariant();
            bool bladeType = lname.Contains("sword") || lname.Contains("dagger") || lname.Contains("knife") || lname.Contains("blade") || lname.Contains("sabre") || lname.Contains("katana");
            float endLo = b.min[k], endHi = b.max[k];
            bool widestNearLo = Mathf.Abs(widestPos - endLo) < Mathf.Abs(widestPos - endHi);
            // 칼 종류: 가장 넓은 곳(코등이)에 가까운 끝이 손잡이. 도끼, 망치 종류: 머리에서 먼 끝이 손잡이.
            bool handleAtLo = bladeType ? widestNearLo : !widestNearLo;
            if (w.flipGrip) handleAtLo = !handleAtLo;
            float handleEnd = handleAtLo ? endLo : endHi;
            float headEnd = handleAtLo ? endHi : endLo;
            float dirSign = handleAtLo ? 1f : -1f;
            float gripAlong = bladeType ? (handleEnd + widestPos) * 0.5f : handleEnd + dirSign * 0.15f * len;

            Vector3 center = b.center;
            Vector3 gripPoint = center; gripPoint[k] = gripAlong;
            Vector3 tip = center; tip[k] = headEnd;
            Vector3 axisA = Vector3.zero; axisA[k] = dirSign;
            float bSign = binCount[widest] > 0 && (binSumB[widest] / binCount[widest] - center[kb]) < 0 ? -1f : 1f;
            Vector3 axisB = Vector3.zero; axisB[kb] = bSign;
            weaponTipLocal = tip;

            // 단위가 cm로 들어와 100배 작거나 큰 무기를 자동으로 맞춘다
            float unitFix = len < 0.1f ? 100f : (len > 10f ? 0.01f : 1f);
            float scale = w.scale * unitFix;
            go.transform.localScale = Vector3.one * scale;
            string side = w.boneName.EndsWith("_l") ? "_l" : "_r";
            var i1 = Bone("index_01" + side); var p1 = Bone("pinky_01" + side);
            var i2 = Bone("index_02" + side); var p2 = Bone("pinky_02" + side);

            if (w.autoGrip && i1 != null && p1 != null)
            {
                // 지금은 기본 자세(손가락 펴짐). 손가락 뿌리 두 개로 주먹 방향을 잡는다.
                Vector3 knuckle = (i1.position + p1.position) * 0.5f;
                Vector3 gripAxis = (i1.position - p1.position).normalized;              // 새끼 → 검지 = 칼날이 나가는 방향
                Vector3 forward = Vector3.ProjectOnPlane(knuckle - hand.position, gripAxis).normalized; // 손목 → 손가락
                Vector3 fingerDir = forward;
                if (i2 != null && p2 != null)
                {
                    Vector3 f = Vector3.ProjectOnPlane((i2.position + p2.position) * 0.5f - knuckle, gripAxis);
                    if (f.sqrMagnitude > 1e-8f) fingerDir = f.normalized;
                }
                Vector3 gripCenter = knuckle + fingerDir * 0.025f;

                Quaternion gripRot = Quaternion.LookRotation(gripAxis, forward);
                Quaternion weaponAxes = Quaternion.LookRotation(axisA, axisB);
                Quaternion rot = gripRot * Quaternion.Euler(w.extraRotation) * Quaternion.Inverse(weaponAxes);
                Vector3 pos = gripCenter + gripRot * w.extraPosition - rot * (gripPoint * scale);
                go.transform.SetPositionAndRotation(pos, rot);
                go.transform.SetParent(hand, true);
            }
            else
            {
                if (w.autoGrip) Warnings += "손가락 뼈를 찾지 못해 자동 쥐기를 못 했습니다. 직접 위치를 맞춰 주세요.\n";
                go.transform.SetParent(hand, false);
                go.transform.localPosition = w.extraPosition;
                go.transform.localRotation = Quaternion.Euler(w.extraRotation);
            }
            weaponTf = go.transform;
            var wb = WorldBounds(go);
            Info += $"무기 {w.prefab.name}: 길이 {len * scale:0.00}m, 손에서 {Vector3.Distance(wb.center, hand.position):0.00}m, 화면 크기 {wb.size.x:0.00}x{wb.size.y:0.00}x{wb.size.z:0.00}, 손 배율 {hand.lossyScale.x:0.###}\n";
        }

        // ───────────────────────── 방패 ─────────────────────────

        void BuildShield()
        {
            var sh = setup.shield;
            if (!sh.useTempShield && sh.prefab == null) return;
            var arm = Bone(sh.boneName);
            if (arm == null) { Warnings += $"방패 뼈 '{sh.boneName}' 를 찾을 수 없습니다.\n"; return; }
            Transform child = arm.childCount > 0 ? arm.GetChild(0) : null;
            foreach (Transform c in arm) if (c.name.StartsWith("hand")) child = c;
            if (child == null) { Warnings += "방패를 달 팔 방향을 알 수 없습니다.\n"; return; }

            // 기본 자세에서 손등 쪽(위쪽)이 방패 바깥면
            Vector3 armDir = (child.position - arm.position).normalized;
            Vector3 normal = Vector3.ProjectOnPlane(Vector3.up, armDir).normalized;
            if (normal.sqrMagnitude < 0.5f) normal = Vector3.ProjectOnPlane(Vector3.forward, armDir).normalized;
            Vector3 mid = (arm.position + child.position) * 0.5f;

            GameObject go;
            if (sh.prefab != null)
            {
                go = Spawn(sh.prefab, "bake_shield");
                go.transform.rotation = Quaternion.LookRotation(normal, armDir) * Quaternion.Euler(sh.extraRotation);
                go.transform.position = mid + normal * 0.06f + go.transform.rotation * sh.extraPosition;
            }
            else
            {
                go = new GameObject("bake_temp_shield");
                SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
                go.AddComponent<MeshRenderer>();
                // 기본 원기둥 메시: 지름 2, 높이 2, 축은 Y
                go.transform.localScale = new Vector3(sh.diameter * 0.5f, sh.thickness * 0.5f, sh.diameter * 0.5f);
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(sh.extraRotation);
                go.transform.position = mid + normal * (0.05f + sh.thickness * 0.5f) + sh.extraPosition;
            }
            go.transform.SetParent(arm, true);
            var sb = WorldBounds(go);
            Info += $"방패: 크기 {sb.size.x:0.00}x{sb.size.y:0.00}x{sb.size.z:0.00}, 팔 배율 {arm.lossyScale.x:0.###}\n";
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ───────────────────────── 모션 ─────────────────────────

        public void SetClip(AnimationClip clip)
        {
            if (clip == currentClip && graph.IsValid()) return;
            if (graph.IsValid()) graph.Destroy();
            currentClip = clip;
            useDirect = false;
            clipOffsetY = 0f;
            if (clip == null || !IsValid) return;

            // 같은 뼈대의 Generic 모션: 아바타를 빼고 뼈 경로 그대로 회전·위치를 넣는다 (리타깃 없음)
            useDirect = setup.directPlayback && !clip.humanMotion;
            animator.avatar = useDirect ? null : humanAvatar;
            animator.Rebind();

            graph = PlayableGraph.Create("CardGameCharacterBake");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "pose", animator);
            clipPlayable = AnimationClipPlayable.Create(graph, clip);
            clipPlayable.SetApplyFootIK(false);
            output.SetSourcePlayable(clipPlayable);
            graph.Play();
            FaceForward();
            GroundFromFirstFrame();
        }

        /// <summary>첫 프레임에서 가장 낮은 발바닥이 바닥(0)에 닿도록 모션 전체의 높이를 한 번 정한다.</summary>
        void GroundFromFirstFrame()
        {
            clipOffsetY = 0f;
            if (!setup.groundFromFirstFrame || feet.Count == 0) return;
            skipFixes = true;
            Sample(0f);
            skipFixes = false;
            float m = float.MaxValue;
            foreach (var f in feet) m = Mathf.Min(m, Sole(f));
            if (m != float.MaxValue) clipOffsetY = -m;
            Sample(0f);
        }

        /// <summary>
        /// 모션마다 처음 바라보는 방향이 달라서, 첫 프레임 자세에서 캐릭터 정면을 구해 +Z(화면 오른쪽 기준)로 돌려 놓는다.
        /// 모션 도중 몸을 돌리는 건 그대로 보인다.
        /// </summary>
        void FaceForward()
        {
            root.transform.rotation = Quaternion.identity;
            skipFixes = true;
            Sample(0f);
            skipFixes = false;
            var l = HB(HumanBodyBones.LeftUpperLeg);
            var r = HB(HumanBodyBones.RightUpperLeg);
            var ls = HB(HumanBodyBones.LeftUpperArm);
            var rs = HB(HumanBodyBones.RightUpperArm);
            if (l == null || r == null) return;
            Vector3 right = (r.position - l.position);
            if (ls != null && rs != null) right += (rs.position - ls.position);
            Vector3 fwd = Vector3.Cross(right, Vector3.up);
            fwd.y = 0;
            if (fwd.sqrMagnitude < 1e-6f) return;
            root.transform.rotation = Quaternion.FromToRotation(fwd.normalized, Vector3.forward);
            Sample(0f);
            facingInfo = $"처음 정면 방향 보정: {Vector3.SignedAngle(fwd, Vector3.forward, Vector3.up):0}도, 첫 프레임 골반 보정 {lastHipFix:0}도, 발 보정 {lastFootFix:0}도\n보정 전: {footDebug}";
        }
        public string facingInfo = "";

        /// <summary>현재 모션의 t초 자세를 만든다.</summary>
        public void Sample(float t)
        {
            if (!graph.IsValid()) return;
            root.transform.position = Vector3.zero;
            clipPlayable.SetTime(t);
            clipPlayable.SetTime(t); // 두 번 넣어야 이전 시간과의 차이(이동량)가 생기지 않는다
            graph.Evaluate(0f);
            root.transform.position = Vector3.up * clipOffsetY;
            if (skipFixes) return;
            StraightenHips();
            foreach (var f in feet) f.preSole = Sole(f);
            FlattenFeet();
            PlantFeet();
        }

        // ───────────────────────── 카메라 ─────────────────────────

        public void GetCamera(out Vector3 pos, out Quaternion rot, out Vector3 right, out Vector3 up, out Vector3 fwd)
        {
            var s = setup;
            // 캐릭터는 +Z를 바라본다. 카메라를 캐릭터 오른쪽(+X)에 두면 화면에서 오른쪽을 보고, 무기 든 손이 카메라 쪽.
            rot = Quaternion.Euler(s.pitch, -90f - s.yawToFront, 0f);
            right = rot * Vector3.right; up = rot * Vector3.up; fwd = rot * Vector3.forward;
            float ppm = Mathf.Max(1f, s.pixelsPerMeter);
            float hw = frameW * 0.5f / ppm, hh = frameH * 0.5f / ppm;
            Vector3 target = up * (hh - ground / ppm) + right * (hw - pivX / ppm);
            pos = target - fwd * 20f;
        }


        /// <summary>월드 위치를 화면 픽셀 좌표로 (프레임 왼쪽 아래가 0,0)</summary>
        public Vector2 WorldToPixel(Vector3 world)
        {
            GetCamera(out var pos, out _, out var right, out var up, out _);
            float ppm = setup.pixelsPerMeter;
            Vector3 d = world * setup.heightScale - pos;
            return new Vector2(Vector3.Dot(d, right) * ppm + frameW * 0.5f, Vector3.Dot(d, up) * ppm + frameH * 0.5f);
        }

        /// <summary>무기 끝(무기가 없으면 오른손) 월드 위치. 타격 프레임 찾기에 쓴다.</summary>
        public Vector3 StrikePoint()
        {
            if (weaponTf != null) return weaponTf.TransformPoint(weaponTipLocal);
            if (handTf != null) return handTf.position;
            return root != null ? root.transform.position : Vector3.zero;
        }

        // ───────────────────────── 그리기 ─────────────────────────

        /// <summary>현재 자세를 그려서 픽셀(왼쪽 아래부터)로 돌려준다. 윤곽선, 팔레트까지 적용.</summary>
        public Color32[] Render()
        {
            var s = setup;
            int w = frameW, h = frameH;
            // 몇 배 크게 그린 뒤 평균으로 줄인다 (작은 장식이 프레임마다 깜빡이는 것 방지)
            int ss = Mathf.Clamp(s.superSample, 1, 8);
            int bw = w * ss, bh = h * ss;
            if (rt == null || rt.width != bw || rt.height != bh)
            {
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                rt = new RenderTexture(bw, bh, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                { filterMode = FilterMode.Point, antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave };
                rt.Create();
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                readback = new Texture2D(bw, bh, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
            }

            GetCamera(out var camPos, out var camRot, out var right, out var up, out var fwd);
            float ppm = Mathf.Max(1f, s.pixelsPerMeter);
            float hw = w * 0.5f / ppm, hh = h * 0.5f / ppm;
            Matrix4x4 view = Matrix4x4.TRS(camPos, camRot, new Vector3(1, 1, -1)).inverse;
            Matrix4x4 proj = Matrix4x4.Ortho(-hw, hw, -hh, hh, 0.1f, 40f);
            Matrix4x4 scale = Matrix4x4.Scale(Vector3.one * s.heightScale);
            Vector3 l = s.lightFromScreen;
            Vector3 lightWorld = (right * l.x + up * l.y - fwd * l.z).normalized;

            var cb = new CommandBuffer { name = "CardGameCharacterBake" };
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
            cb.SetViewProjectionMatrices(view, proj);
            cb.SetGlobalVector("_CGBakeLightDir", lightWorld);
            cb.SetGlobalVector("_CGBakeShade", new Vector4(s.litThreshold, s.midThreshold, s.midBrightness, s.shadowBrightness));
            // 크게 그리면 그만큼 선명한 밉맵을 고르므로, 결과 해상도 기준으로 되돌린 뒤 설정만큼 더 흐리게
            cb.SetGlobalFloat("_CGBakeMipBias", Mathf.Log(ss, 2f) + s.textureMipBias);

            // 먼저 몸 메시를 구워서 가장 낮은 점을 잰다. 바닥(0) 아래로 내려갔으면 전부 그만큼 올려서 그린다.
            float lift = 0f;
            float meshMinY = float.MaxValue;
            foreach (var it in items)
            {
                if (it.skinned == null || it.renderer == null || !it.renderer.gameObject.activeInHierarchy) continue;
                it.skinned.BakeMesh(it.mesh);
                if (!s.keepAboveGround) continue;
                var t = it.skinned.transform;
                var mt = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                foreach (var v in it.mesh.vertices)
                {
                    float y = mt.MultiplyPoint3x4(v).y;
                    if (y < meshMinY) meshMinY = y;
                }
            }
            if (s.keepAboveGround && meshMinY < 0f && meshMinY != float.MaxValue) lift = -meshMinY;
            Vector3 shift = Vector3.up * lift;
            if (s.snapToPixel)
            {
                // 캐릭터 전체 위치를 결과 픽셀 칸에 맞춘다 (반 픽셀씩 흔들리며 윤곽이 반짝이는 것 방지)
                Vector3 off = (root.transform.position + shift) * s.heightScale;
                float dx = Vector3.Dot(off, right) * ppm, dy = Vector3.Dot(off, up) * ppm;
                Vector3 fix = right * ((Mathf.Round(dx) - dx) / ppm) + up * ((Mathf.Round(dy) - dy) / ppm);
                shift += fix / Mathf.Max(0.0001f, s.heightScale);
            }
            Matrix4x4 liftM = Matrix4x4.Translate(shift);

            foreach (var it in items)
            {
                if (it.renderer == null || !it.renderer.gameObject.activeInHierarchy) continue;
                Matrix4x4 m;
                if (it.skinned != null)
                {
                    var t = it.skinned.transform;
                    m = scale * liftM * Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                }
                else m = scale * liftM * it.renderer.localToWorldMatrix;

                for (int sub = 0; sub < it.mesh.subMeshCount; sub++)
                    if (it.skipSub == null || sub >= it.skipSub.Length || !it.skipSub[sub])
                        cb.DrawMesh(it.mesh, m, it.mats[Mathf.Min(sub, it.mats.Length - 1)], sub, 0);
            }
            Graphics.ExecuteCommandBuffer(cb);
            cb.Release();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, bw, bh), 0, 0, false);
            readback.Apply(false);
            RenderTexture.active = prev;

            Color32[] px = PixelArtUtil.BoxDownsample(readback.GetPixels32(), w, h, ss);
            for (int i = 0; i < px.Length; i++) px[i].a = px[i].a >= 128 ? (byte)255 : (byte)0;

            // 위아래가 뒤집혀 나오는 그래픽 장치가 있어서, 처음 그릴 때 발 위치로 한 번 판단한다
            if (autoFlip < 0 && PixelArtUtil.OpaqueRowRange(px, w, h, out int minY, out int maxY))
            {
                int g = ground;
                autoFlip = Mathf.Abs((h - 1 - maxY) - g) < Mathf.Abs(minY - g) ? 1 : 0;
                if (autoFlip == 1) Debug.Log("[캐릭터 굽기] 이 컴퓨터는 그림이 뒤집혀 나와서 자동으로 바로잡습니다.");
            }
            if ((autoFlip == 1) != s.flipY) PixelArtUtil.FlipRows(px, w, h);

            if (RawColors) return px;
            if (s.palette != null)
            {
                if (paletteCache == null) paletteCache = PixelArtUtil.LoadPalette(s.palette);
                PixelArtUtil.Quantize(px, paletteCache);
            }
            else if (AutoPalette != null) PixelArtUtil.Quantize(px, AutoPalette);
            if (s.outline) PixelArtUtil.AddOutline(px, w, h, s.outlineColor);
            return px;
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
            foreach (var m in createdMats) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            foreach (var o in createdObjects) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            createdMats.Clear(); createdObjects.Clear(); items.Clear();
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); rt = null; }
            if (readback != null) { UnityEngine.Object.DestroyImmediate(readback); readback = null; }
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            root = null;
        }
    }
}
