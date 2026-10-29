using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 메뉴: CardGame > 캐릭터 굽기 > 굽기 창
    /// 위: 미리보기 (모션, 시간 선택) / 가운데: 굽기 버튼 / 아래: 설정 편집
    /// 설정을 바꾸면 미리보기가 바로 다시 그려진다. 무기 위치를 맞출 때 쓰면 편하다.
    /// </summary>
    public class CharacterBakerWindow : EditorWindow
    {
        CharacterBakeSetup setup;
        CharacterRig rig;
        int rigDirtyCount = -1;
        CharacterBakeSetup rigSetup;

        int clipIndex;
        float time;
        bool playing;
        double lastTick;
        int previewScale = 3;
        Texture2D previewTex;
        bool previewDirty = true;
        int hitFrameCache = -2;
        AnimationClip hitFrameClip;

        Editor setupEditor;
        Vector2 scroll;

        [MenuItem("CardGame/캐릭터 굽기/굽기 창")]
        static void Open() => GetWindow<CharacterBakerWindow>("캐릭터 굽기");

        [MenuItem("CardGame/캐릭터 굽기/테스트 설정 만들기")]
        static void CreateTestSetups()
        {
            var setups = CharacterBakeTestSetups.CreateAll();
            var w = GetWindow<CharacterBakerWindow>("캐릭터 굽기");
            w.setup = setups[0];
            w.ResetRig();
            Selection.activeObject = setups[0];
        }

        void OnEnable() { EditorApplication.update += Tick; }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            DisposeRig();
            if (previewTex != null) DestroyImmediate(previewTex);
            if (setupEditor != null) DestroyImmediate(setupEditor);
        }

        void DisposeRig()
        {
            rig?.Dispose();
            rig = null;
            rigSetup = null;
        }

        void ResetRig()
        {
            DisposeRig();
            previewDirty = true;
            hitFrameCache = -2;
        }

        void Tick()
        {
            if (!playing || setup == null) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - lastTick < 1.0 / Mathf.Max(1, setup.fps)) return;
            lastTick = now;
            var clip = CurrentClip();
            if (clip == null) return;
            int n = CharacterBaker.FrameCount(clip, setup);
            int f = (Frame(clip) + 1) % n;
            time = CharacterBaker.FrameTime(clip, setup, f);
            previewDirty = true;
            Repaint();
        }

        AnimationClip CurrentClip()
        {
            if (setup == null || setup.clips.Count == 0) return null;
            clipIndex = Mathf.Clamp(clipIndex, 0, setup.clips.Count - 1);
            return setup.clips[clipIndex]?.clip;
        }

        int Frame(AnimationClip clip) => Mathf.Clamp(Mathf.RoundToInt(time * setup.fps), 0, CharacterBaker.FrameCount(clip, setup) - 1);

        void EnsureRig()
        {
            if (setup == null) { DisposeRig(); return; }
            int dirty = EditorUtility.GetDirtyCount(setup);
            if (rig != null && rigSetup == setup && dirty == rigDirtyCount) return;
            DisposeRig();
            rig = new CharacterRig(setup);
            rigSetup = setup;
            rigDirtyCount = dirty;
            previewDirty = true;
            hitFrameCache = -2;
        }

        void OnGUI()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                var newSetup = (CharacterBakeSetup)EditorGUILayout.ObjectField("굽기 설정", setup, typeof(CharacterBakeSetup), false);
                if (newSetup != setup) { setup = newSetup; clipIndex = 0; time = 0; ResetRig(); }
                if (GUILayout.Button("테스트 설정 만들기", GUILayout.Width(130))) { CreateTestSetups(); GUIUtility.ExitGUI(); }
            }

            if (setup == null)
            {
                EditorGUILayout.HelpBox("1) '테스트 설정 만들기'를 누르면 받아 둔 에셋으로 설정 두 개가 생깁니다.\n2) 미리보기로 확인하고 '전부 굽기'를 누르면 Assets/Art/characters 아래에 PNG가 생깁니다.", MessageType.Info);
                return;
            }

            EnsureRig();
            if (rig != null && !string.IsNullOrEmpty(rig.Warnings))
                EditorGUILayout.HelpBox(rig.Warnings.TrimEnd(), rig.IsValid ? MessageType.Warning : MessageType.Error);

            if (rig != null && (!string.IsNullOrEmpty(rig.Info) || !string.IsNullOrEmpty(rig.facingInfo)))
                EditorGUILayout.LabelField((rig.Info + rig.facingInfo).TrimEnd(), EditorStyles.wordWrappedMiniLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawPreview();
            EditorGUILayout.Space(6);
            DrawBakeButtons();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("설정", EditorStyles.boldLabel);
            Editor.CreateCachedEditor(setup, null, ref setupEditor);
            setupEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
        }

        void DrawPreview()
        {
            if (setup.clips.Count == 0) { EditorGUILayout.HelpBox("모션 목록이 비어 있습니다.", MessageType.Info); return; }

            var names = new string[setup.clips.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = $"{i + 1}. " + (setup.clips[i]?.clip != null ? setup.clips[i].clip.name.Replace("|", "/") : "(비어 있음)");
            int newIndex = EditorGUILayout.Popup("모션", clipIndex, names);
            if (newIndex != clipIndex) { clipIndex = newIndex; time = 0; previewDirty = true; hitFrameCache = -2; }

            var clip = CurrentClip();
            if (clip == null || rig == null || !rig.IsValid) return;

            int n = CharacterBaker.FrameCount(clip, setup);
            using (new EditorGUILayout.HorizontalScope())
            {
                int f = Frame(clip);
                int nf = EditorGUILayout.IntSlider("프레임", f, 0, n - 1);
                if (nf != f) { time = CharacterBaker.FrameTime(clip, setup, nf); previewDirty = true; playing = false; }
                if (GUILayout.Button(playing ? "정지" : "재생", GUILayout.Width(50))) { playing = !playing; lastTick = 0; }
                previewScale = EditorGUILayout.IntPopup(previewScale, new[] { "x2", "x3", "x4" }, new[] { 2, 3, 4 }, GUILayout.Width(45));
            }

            var entry = setup.clips[clipIndex];
            if (entry.findHitFrame)
            {
                if (hitFrameCache == -2 || hitFrameClip != clip)
                {
                    hitFrameCache = CharacterBaker.FindHitFrame(rig, clip, setup);
                    hitFrameClip = clip;
                    previewDirty = true;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"자동 타격 프레임: {hitFrameCache}  (총 {n}장, {setup.fps}fps)");
                    if (GUILayout.Button("타격 프레임 보기", GUILayout.Width(110)))
                    { time = CharacterBaker.FrameTime(clip, setup, hitFrameCache); previewDirty = true; playing = false; }
                }
            }
            else EditorGUILayout.LabelField($"총 {n}장, {setup.fps}fps, {clip.length:0.00}초");

            if (previewDirty)
            {
                rig.SetClip(clip);
                rig.Sample(time);
                var px = rig.Render();
                if (previewTex == null || previewTex.width != setup.frameWidth || previewTex.height != setup.frameHeight)
                {
                    if (previewTex != null) DestroyImmediate(previewTex);
                    previewTex = new Texture2D(setup.frameWidth, setup.frameHeight, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                }
                previewTex.SetPixels32(px);
                previewTex.Apply(false);
                previewDirty = false;
            }

            Rect r = GUILayoutUtility.GetRect(setup.frameWidth * previewScale, setup.frameHeight * previewScale, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(r, new Color(0.32f, 0.36f, 0.34f));
            // 발 위치 표시선
            float gy = r.yMax - setup.groundPixel * previewScale;
            EditorGUI.DrawRect(new Rect(r.x, gy, r.width, 1), new Color(1, 1, 1, 0.25f));
            float px0 = r.x + rig.PivotX * previewScale;
            EditorGUI.DrawRect(new Rect(px0, r.y, 1, r.height), new Color(1, 1, 1, 0.15f));
            if (previewTex != null) GUI.DrawTexture(r, previewTex, ScaleMode.StretchToFill, true);
            bool isHit = entry.findHitFrame && Frame(clip) == hitFrameCache;
            if (isHit) EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 3), new Color(1f, 0.3f, 0.2f));
        }

        void DrawBakeButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = rig != null && rig.IsValid && CurrentClip() != null;
                if (GUILayout.Button("이 모션만 굽기", GUILayout.Height(28)))
                {
                    string path = CharacterBaker.BakeClip(rig, setup.clips[clipIndex]);
                    Debug.Log($"[캐릭터 굽기] {path} 저장");
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(path));
                    previewDirty = true;
                }
                GUI.enabled = true;
                if (GUILayout.Button("체크된 모션 전부 굽기", GUILayout.Height(28)))
                {
                    DisposeRig();
                    CharacterBaker.BakeAll(setup);
                    GUIUtility.ExitGUI();
                }
            }
            if (GUILayout.Button("기본 자세(모션 없이) PNG로 저장", GUILayout.Height(20)))
            {
                var r = new CharacterRig(setup);
                try
                {
                    var px = r.Render();
                    var tex = new Texture2D(r.Width, r.Height, TextureFormat.RGBA32, false);
                    tex.SetPixels32(px); tex.Apply(false);
                    string path = setup.outputFolder.TrimEnd('/') + "/_rest_pose.png";
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                    DestroyImmediate(tex);
                    AssetDatabase.ImportAsset(path);
                    Debug.Log($"[캐릭터 굽기] {path} 저장 (모션 없이 기본 자세)");
                }
                finally { r.Dispose(); }
            }
            EditorGUILayout.LabelField($"저장 위치: {setup.outputFolder}/모션이름.png (+ .json)", EditorStyles.miniLabel);
        }
    }
}
