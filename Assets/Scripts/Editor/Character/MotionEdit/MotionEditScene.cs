// 모션 편집 장면 (Assets/Scenes/Tools/MotionEdit.unity).
// 굽기와 같은 조립(CharacterRig)으로 캐릭터를 씬에 세우고, 굽기 카메라와 같은 위치·크기의 카메라를 둔다.
// 캐릭터·카메라는 씬 파일에 저장하지 않는다(DontSave). 장면을 열거나 고른 것이 바뀌면 다시 세운다.
// Animation 창은 이 캐릭터의 Animator에 연결한 편집본(.anim)을 고친다.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace CardGame.EditorTools
{
    [InitializeOnLoad]
    public static class MotionEditScene
    {
        public const string ScenePath = "Assets/Scenes/Tools/MotionEdit.unity";
        public const string ControllerPath = "Assets/Scenes/Tools/MotionEdit.controller";
        const string RigName = "편집_캐릭터";
        const string CamName = "굽기_카메라";

        // 지금 고른 것 (EditorPrefs에 남겨 둔다)
        public static int Character { get => EditorPrefs.GetInt("CG_ME_char", 0); set => EditorPrefs.SetInt("CG_ME_char", value); }
        public static int Set { get => EditorPrefs.GetInt("CG_ME_set", 0); set => EditorPrefs.SetInt("CG_ME_set", value); }
        public static string Clip { get => EditorPrefs.GetString("CG_ME_clip", ""); set => EditorPrefs.SetString("CG_ME_clip", value); }
        public static int Weapon { get => EditorPrefs.GetInt("CG_ME_weapon", 0); set => EditorPrefs.SetInt("CG_ME_weapon", value); }
        public static int Shield { get => EditorPrefs.GetInt("CG_ME_shield", 0); set => EditorPrefs.SetInt("CG_ME_shield", value); }   // 0 = 없음

        /// <summary>지금 고른 Timeline 이름 (비어 있으면 모션 하나)</summary>
        public static string Timeline { get => EditorPrefs.GetString("CG_ME_tl", ""); set => EditorPrefs.SetString("CG_ME_tl", value ?? ""); }

        public static TimelineAsset CurrentTimeline()
            => string.IsNullOrEmpty(Timeline) ? null : AssetDatabase.LoadAssetAtPath<TimelineAsset>(MotionEditData.TimelinePath(SetId, Timeline));

        /// <summary>장면 캐릭터의 PlayableDirector (Timeline 창이 이걸로 미리 보여 준다)</summary>
        public static PlayableDirector Director => sceneRig != null && sceneRig.Root != null ? sceneRig.Root.GetComponent<PlayableDirector>() : null;

        public static string CharacterId => MotionEditData.Characters[Mathf.Clamp(Character, 0, MotionEditData.Characters.Length - 1)];
        public static string SetId => MotionEditData.Sets[Mathf.Clamp(Set, 0, MotionEditData.Sets.Length - 1)];

        static CharacterRig sceneRig;
        static string builtKey;

        static MotionEditScene()
        {
            EditorSceneManager.sceneOpened += (scene, mode) => { if (scene.path == ScenePath) EditorApplication.delayCall += Rebuild; };
            EditorSceneManager.sceneClosing += (scene, removing) => { if (scene.path == ScenePath) Clear(); };
            // 스크립트를 다시 읽으면(도메인 다시 불러오기) 씬에 남은 캐릭터와 연결이 끊긴다 → 다시 세운다
            EditorApplication.delayCall += () => { if (IsOpen && !EditorApplication.isPlayingOrWillChangePlaymode) Rebuild(); };
            Camera.onPreCull += KeepInPlace;
        }

        /// <summary>
        /// 제자리 보기: 장면 캐릭터가 앞뒤·옆으로 이동하지 않게 한다 (굽기와 같게: 수평 이동만 빼고 높이·회전은 그대로).
        /// 모션·Timeline 파일은 건드리지 않고, 화면에 그리기 직전에 캐릭터 위치만 처음 자리로 돌려 놓는다.
        /// </summary>
        public static bool InPlace { get => EditorPrefs.GetBool("CG_ME_inplace", true); set => EditorPrefs.SetBool("CG_ME_inplace", value); }

        static void KeepInPlace(Camera cam)
        {
            if (!InPlace || EditorApplication.isPlaying || sceneRig == null || sceneRig.Root == null) return;
            var t = sceneRig.Root.transform;
            var p = t.position;
            if (p.x != 0f || p.z != 0f) t.position = new Vector3(0f, p.y, 0f);
        }

        public static bool IsOpen => SceneManager.GetActiveScene().path == ScenePath;

        [MenuItem("CardGame/캐릭터/모션 편집/편집 장면 열기", false, 1)]
        public static void Open()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("모션 편집", "플레이 중에는 열 수 없어요. 플레이를 멈춘 뒤 다시 눌러 주세요.", "확인"); return; }
            if (!IsOpen)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                if (!File.Exists(MotionEditData.Full(ScenePath))) CreateScene();
                else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            Rebuild();
            MotionEditPreviewWindow.ShowWindow();
        }

        /// <summary>빈 장면 + 조명만 (캐릭터·카메라는 열 때마다 새로 세운다)</summary>
        static void CreateScene()
        {
            MotionEditData.EnsureFolder("Assets/Scenes/Tools");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var light = new GameObject("조명");
            var l = light.AddComponent<Light>();
            l.type = LightType.Directional;
            light.transform.SetPositionAndRotation(new Vector3(0f, 4f, -3f), Quaternion.Euler(40f, -60f, 0f));   // 캐릭터를 가리지 않게 위로
            var note = new GameObject("안내 - 캐릭터는 CardGame > 캐릭터 > 모션 편집 메뉴가 세운다 (저장 안 됨)");
            note.transform.position = Vector3.zero;
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void Clear()
        {
            if (sceneRig != null) { sceneRig.Dispose(); sceneRig = null; }
            builtKey = null;
            // 도메인 다시 불러오기 뒤 남은 것
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;
            foreach (var go in scene.GetRootGameObjects())
                if ((go.name == RigName || go.name == CamName) && (go.hideFlags & HideFlags.DontSave) != 0) Object.DestroyImmediate(go);
        }

        /// <summary>지금 고른 캐릭터·세트·무기·방패·모션으로 장면 캐릭터를 다시 세운다. 장면이 열려 있지 않으면 아무것도 안 한다.</summary>
        public static void Rebuild()
        {
            if (!IsOpen || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var setup = MotionEditData.LoadSetup(CharacterId, SetId);
            if (setup == null) { Debug.LogWarning("[모션 편집] 굽기 설정이 없어요: " + MotionEditData.SetupPath(CharacterId, SetId)); Clear(); return; }

            string key = CharacterId + "|" + SetId;
            if (sceneRig == null || sceneRig.Root == null || builtKey != key)
            {
                Clear();
                sceneRig = new CharacterRig(setup, SceneManager.GetActiveScene());
                if (!sceneRig.IsValid) { Debug.LogWarning("[모션 편집] 조립 실패:\n" + sceneRig.Warnings); Clear(); return; }
                sceneRig.Root.name = RigName;
                builtKey = key;
                // 굽기처럼 첫 모션 0프레임으로 무기를 주먹 안에 맞춘다
                var first = MotionEditData.FirstBakeClip(setup);
                if (first != null) sceneRig.SetClip(first);
            }
            ApplyChoices(setup);
            PlaceCamera(setup);
        }

        /// <summary>무기·방패 보이기, 모션 연결, 발 높이</summary>
        public static void ApplyChoices(CharacterBakeSetup setup = null)
        {
            if (sceneRig == null || sceneRig.Root == null) return;
            setup = setup ?? sceneRig.setup;
            var visible = new HashSet<int>(VisibleLayers(setup));
            sceneRig.ShowLayers(visible);

            var clip = CurrentClip(setup, out _);
            sceneRig.ReleaseGraph();
            if (clip != null)
            {
                // 굽기와 같은 첫 프레임 발 높이를 구해 캐릭터 위치에 넣는다 (그 뒤 그래프는 치운다)
                sceneRig.SetClip(clip);
                float y = sceneRig.GroundOffset;
                sceneRig.ReleaseGraph();
                // 그래프를 치우면 기본 자세(T자)로 돌아가므로 0프레임 자세를 한 번 넣어 둔다 (Animation 창 미리보기가 켜지면 그쪽이 덮는다)
                clip.SampleAnimation(sceneRig.Root, 0f);
                sceneRig.Root.transform.SetPositionAndRotation(Vector3.up * y, Quaternion.identity);
            }
            var anim = sceneRig.Animator;
            if (anim != null)
            {
                anim.runtimeAnimatorController = clip != null ? Controller(clip) : null;
                anim.applyRootMotion = false;
            }
            // Timeline: 장면 캐릭터에 PlayableDirector 를 붙여 Timeline 창에서 고칠 수 있게
            var tl = CurrentTimeline();
            var dir = sceneRig.Root.GetComponent<PlayableDirector>();
            if (tl != null)
            {
                if (dir == null) { dir = sceneRig.Root.AddComponent<PlayableDirector>(); dir.hideFlags = HideFlags.DontSave; }
                dir.playOnAwake = false;
                dir.extrapolationMode = DirectorWrapMode.Hold;
                dir.playableAsset = tl;
                foreach (var track in tl.GetOutputTracks())
                    if (track is AnimationTrack) dir.SetGenericBinding(track, anim);
                dir.RebuildGraph();
            }
            else if (dir != null) dir.playableAsset = null;
        }

        /// <summary>미리보기·장면에서 보일 레이어 번호 (무기 묶음 + 방패)</summary>
        public static List<int> VisibleLayers(CharacterBakeSetup setup)
        {
            var list = new List<int>();
            var w = MotionEditData.WeaponChoices(setup);
            if (w.Count > 0) list.AddRange(w[Mathf.Clamp(Weapon, 0, w.Count - 1)].Value);
            var sh = MotionEditData.ShieldChoices(setup);
            if (sh.Count > 0 && Shield > 0 && Shield <= sh.Count) list.Add(sh[Shield - 1].Value);
            return list;
        }

        /// <summary>지금 고른 모션: 편집본이 있으면 편집본, 없으면 팩 모션. isEdit = 편집본인지</summary>
        public static AnimationClip CurrentClip(CharacterBakeSetup setup, out bool isEdit)
        {
            isEdit = false;
            var e = MotionEditData.FindEntry(setup, Clip);
            if (e == null) return null;
            var copy = MotionEditData.LoadEditClip(SetId, Clip);
            if (copy != null) { isEdit = true; CharacterBaker.RegisterEditCopy(copy, e.clip); return copy; }
            return e.clip;
        }

        static AnimatorController Controller(AnimationClip clip)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ac == null)
            {
                MotionEditData.EnsureFolder("Assets/Scenes/Tools");
                ac = AnimatorController.CreateAnimatorControllerAtPathWithClip(ControllerPath, clip);
                return ac;
            }
            var sm = ac.layers[0].stateMachine;
            AnimatorState st = sm.defaultState;
            if (st == null) { st = sm.AddState("모션"); sm.defaultState = st; }
            if (st.motion != clip) { st.motion = clip; EditorUtility.SetDirty(ac); AssetDatabase.SaveAssetIfDirty(ac); }
            return ac;
        }

        /// <summary>굽기 카메라와 같은 위치·각도·크기 (기본 프레임 크기 기준)</summary>
        static void PlaceCamera(CharacterBakeSetup s)
        {
            var scene = SceneManager.GetActiveScene();
            GameObject go = null;
            foreach (var r in scene.GetRootGameObjects()) if (r.name == CamName) go = r;
            if (go == null)
            {
                go = new GameObject(CamName) { hideFlags = HideFlags.DontSave };
                go.AddComponent<Camera>();
            }
            var cam = go.GetComponent<Camera>();
            sceneRig.ResetFrame();
            sceneRig.GetCamera(out var pos, out var rot, out _, out _, out _);
            float hs = Mathf.Max(0.0001f, s.heightScale);
            float ppm = Mathf.Max(1f, s.pixelsPerMeter);
            go.transform.SetPositionAndRotation(pos / hs, rot);
            cam.orthographic = true;
            cam.orthographicSize = s.frameHeight * 0.5f / ppm / hs;
            cam.nearClipPlane = 0.1f / hs;
            cam.farClipPlane = 40f / hs;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.35f, 0.37f, 0.40f, 1f);
        }

        /// <summary>장면 캐릭터를 선택하고 Animation 창을 이 모션으로 연다.</summary>
        public static void FocusAnimationWindow(AnimationClip clip)
        {
            if (sceneRig == null || sceneRig.Root == null || clip == null) return;
            Selection.activeGameObject = sceneRig.Root;
            var aw = EditorWindow.GetWindow<AnimationWindow>();
            aw.Show();
            EditorApplication.delayCall += () => { if (aw != null) aw.animationClip = clip; };
        }

        /// <summary>장면 캐릭터를 선택하고 Timeline 창을 연다.</summary>
        public static void FocusTimelineWindow()
        {
            if (sceneRig == null || sceneRig.Root == null) return;
            Selection.activeGameObject = sceneRig.Root;
            EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");
        }

        // ───────── 편집용 복사본 ─────────

        [MenuItem("CardGame/캐릭터/모션 편집/편집용 복사본 만들기", false, 2)]
        static void MakeCopyMenu()
        {
            MotionEditPreviewWindow.ShowWindow();
            MakeCopy();
        }

        /// <summary>
        /// 지금 고른 세트·모션의 팩 모션을 motion_edits/세트/모션.anim 으로 복사한다 (휴머노이드 곡선 그대로).
        /// 이미 있으면 그걸 연다. 덮어쓰기는 창에서 두 번 묻고 나서만.
        /// </summary>
        public static AnimationClip MakeCopy()
        {
            var setup = MotionEditData.LoadSetup(CharacterId, SetId);
            var e = MotionEditData.FindEntry(setup, Clip);
            if (e == null) { EditorUtility.DisplayDialog("모션 편집", "모션을 먼저 골라 주세요.", "확인"); return null; }
            string path = MotionEditData.ClipPath(SetId, Clip);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            AnimationClip result;
            if (existing != null)
            {
                int pick = EditorUtility.DisplayDialogComplex("모션 편집",
                    $"편집본이 이미 있어요.\n{path}\n\n그대로 열까요?", "열기", "취소", "원본으로 다시 만들기");
                if (pick == 1) return null;
                if (pick == 2)
                {
                    if (!EditorUtility.DisplayDialog("모션 편집", "지금까지 고친 내용이 모두 사라지고 팩 원본으로 돌아갑니다. 정말 덮어쓸까요?", "덮어쓰기", "취소"))
                        return null;
                    Undo.RegisterCompleteObjectUndo(existing, "편집본 원본으로 되돌리기");
                    string keepName = existing.name;
                    EditorUtility.CopySerialized(e.clip, existing);
                    existing.name = keepName;
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssetIfDirty(existing);
                }
                result = existing;
            }
            else
            {
                MotionEditData.EnsureFolder($"{MotionEditData.Root}/{SetId}");
                var copy = Object.Instantiate(e.clip);
                copy.name = Clip;
                AssetDatabase.CreateAsset(copy, path);
                AssetDatabase.SaveAssets();
                result = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                Debug.Log($"[모션 편집] 편집본 만듦: {path} (원본: {e.clip.name})");
            }
            CharacterBaker.RegisterEditCopy(result, e.clip);
            if (IsOpen) { ApplyChoices(setup); FocusAnimationWindow(result); }
            MotionEditPreviewWindow.RefreshAll();
            return result;
        }
    }
}
