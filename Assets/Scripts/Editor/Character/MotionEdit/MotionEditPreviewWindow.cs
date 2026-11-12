// 모션 편집 > 구운 모습 미리보기.
// 굽기와 같은 조립·카메라·팔레트·윤곽선으로 지금 프레임을 그려서 굽기 해상도 그대로(점 필터) 2~4배로 보여 준다.
// 프레임을 옮기면 Animation 창 시간도 같이 옮기고, Animation 창에서 시간을 옮기면 따라간다.
// 아래 칸에서 모션별 설정(타격 프레임, 반복, 굽기 표시)을 고친다 → motion_edits/세트/모션.json
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    public class MotionEditPreviewWindow : EditorWindow
    {
        [MenuItem("CardGame/캐릭터/모션 편집/구운 모습 미리보기", false, 3)]
        public static void ShowWindow()
        {
            var w = GetWindow<MotionEditPreviewWindow>();
            w.titleContent = new GUIContent("구운 모습 미리보기");
            w.minSize = new Vector2(620, 700);
            w.Show();
        }

        [System.NonSerialized] CharacterRig rig;
        [System.NonSerialized] string rigKey;
        [System.NonSerialized] AnimationClip clip;
        [System.NonSerialized] bool clipIsEdit;
        int frames = 1, frame;
        int zoom = 3;
        bool playing, syncAnimWindow = true;
        double playStart; int playStartFrame;
        bool dirty = true;          // 모션이 바뀌어 프레임 크기부터 다시 재야 함
        double dirtyAt;
        readonly Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();
        [System.NonSerialized] MotionEditData.Settings settings;
        string message = "";
        Vector2 scroll;

        void OnEnable()
        {
            // 스크립트를 다시 읽으면 조립·그림이 사라지므로 처음부터 다시 그린다
            dirty = true; dirtyAt = 0; playing = false;
            AnimationUtility.onCurveWasModified += OnCurveModified;
            Undo.undoRedoPerformed += MarkDirty;
            EditorApplication.update += Tick;
        }

        void OnDisable()
        {
            AnimationUtility.onCurveWasModified -= OnCurveModified;
            Undo.undoRedoPerformed -= MarkDirty;
            EditorApplication.update -= Tick;
            DisposeRig();
        }

        void OnCurveModified(AnimationClip c, EditorCurveBinding b, AnimationUtility.CurveModifiedType t)
        {
            if (c != null && c == clip) MarkDirty();
        }

        /// <summary>열린 미리보기 창을 다시 그리게 한다 (편집본을 만들거나 구운 뒤)</summary>
        public static void RefreshAll()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<MotionEditPreviewWindow>()) { w.MarkDirty(); w.dirtyAt = 0; }
        }

        void MarkDirty() { dirty = true; dirtyAt = EditorApplication.timeSinceStartup; Repaint(); }

        void DisposeRig()
        {
            ClearCache();
            if (rig != null) { rig.Dispose(); rig = null; }
            rigKey = null;
        }

        void ClearCache()
        {
            foreach (var t in cache.Values) if (t != null) DestroyImmediate(t);
            cache.Clear();
        }

        CharacterBakeSetup Setup => MotionEditData.LoadSetup(MotionEditScene.CharacterId, MotionEditScene.SetId);

        // ───────── 갱신 ─────────

        void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // 고치는 중에는 0.3초 쉬었다가 다시 그린다
            if (dirty && EditorApplication.timeSinceStartup - dirtyAt > 0.3) { Reload(); Repaint(); }

            if (playing && frames > 0)
            {
                var s = rig != null ? rig.setup : null;
                float fps = s != null ? s.fps : 24f;
                int f = playStartFrame + (int)((EditorApplication.timeSinceStartup - playStart) * fps);
                bool loop = settings != null && settings.loop.HasValue ? settings.loop.Value : (clip != null && CharacterBaker.IsLoop(clip));
                f = loop ? f % frames : Mathf.Min(f, frames - 1);
                if (!loop && f >= frames - 1) playing = false;
                if (f != frame) { frame = f; Repaint(); }
                return;
            }

            if (syncAnimWindow && clip != null && HasOpenInstances<AnimationWindow>())
            {
                var aw = GetWindow<AnimationWindow>(false, null, false);
                if (aw != null && aw.animationClip == clip && rig != null)
                {
                    int f = Mathf.Clamp(Mathf.FloorToInt(aw.time * rig.setup.fps + 0.0001f), 0, frames - 1);
                    if (f != frame) { frame = f; Repaint(); }
                }
            }
        }

        /// <summary>고른 것에 맞춰 조립(필요할 때만)하고, 모션을 넣어 프레임 크기를 다시 잰다.</summary>
        void Reload()
        {
            dirty = false;
            ClearCache();
            message = "";
            var s = Setup;
            if (s == null) { message = "굽기 설정이 없어요: " + MotionEditData.SetupPath(MotionEditScene.CharacterId, MotionEditScene.SetId); DisposeRig(); return; }
            string key = MotionEditScene.CharacterId + "|" + MotionEditScene.SetId;
            if (rig == null || rigKey != key)
            {
                DisposeRig();
                rig = new CharacterRig(s);
                rigKey = key;
                if (!rig.IsValid) { message = "조립 실패:\n" + rig.Warnings; DisposeRig(); return; }
            }
            clip = MotionEditScene.CurrentClip(s, out clipIsEdit);
            settings = MotionEditData.LoadSettings(MotionEditScene.SetId, MotionEditScene.Clip) ?? new MotionEditData.Settings();
            if (clip == null) { frames = 1; frame = 0; return; }
            if (!CharacterBaker.HasSavedPalettes(s)) message = "저장된 팔레트가 없어 색이 굽기와 다를 수 있어요 (먼저 이 세트를 한 번 구워 주세요).";
            frames = CharacterBaker.PreparePreview(rig, clip, MotionEditData.FirstBakeClip(s));
            frame = Mathf.Clamp(frame, 0, frames - 1);
        }

        Texture2D FrameTexture(int i)
        {
            if (rig == null || clip == null) return null;
            if (cache.TryGetValue(i, out var t) && t != null) return t;
            var px = CharacterBaker.RenderPreviewFrame(rig, clip, i, MotionEditScene.VisibleLayers(rig.setup));
            t = new Texture2D(rig.Width, rig.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(px); t.Apply(false);
            cache[i] = t;
            return t;
        }

        void SetFrame(int f, bool moveAnimWindow)
        {
            frame = Mathf.Clamp(f, 0, Mathf.Max(0, frames - 1));
            if (moveAnimWindow && syncAnimWindow && clip != null && rig != null && HasOpenInstances<AnimationWindow>())
            {
                var aw = GetWindow<AnimationWindow>(false, null, false);
                if (aw != null && aw.animationClip == clip)
                {
                    if (aw.canPreview && !aw.previewing) aw.previewing = true;
                    aw.time = CharacterBaker.FrameTime(clip, rig.setup, frame);
                }
            }
            Repaint();
        }

        // ───────── 화면 ─────────

        void OnGUI()
        {
            if (EditorApplication.isPlaying) { EditorGUILayout.HelpBox("플레이 중에는 쓰지 않아요.", MessageType.Info); return; }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawChoices();
            EditorGUILayout.Space(4);
            DrawPreview();
            EditorGUILayout.Space(4);
            DrawSettings();
            EditorGUILayout.EndScrollView();
        }

        void DrawChoices()
        {
            var s = Setup;
            EditorGUI.BeginChangeCheck();
            int ch = EditorGUILayout.Popup("캐릭터", MotionEditScene.Character, MotionEditData.CharacterLabels);
            int set = EditorGUILayout.Popup("세트", MotionEditScene.Set, MotionEditData.SetLabels);
            if (EditorGUI.EndChangeCheck())
            {
                bool setChanged = set != MotionEditScene.Set;
                MotionEditScene.Character = ch; MotionEditScene.Set = set;
                if (setChanged) { MotionEditScene.Weapon = 0; MotionEditScene.Shield = 0; }
                OnChoiceChanged(true);
                return;
            }
            if (s == null) { EditorGUILayout.HelpBox("굽기 설정이 없어요: " + MotionEditData.SetupPath(MotionEditScene.CharacterId, MotionEditScene.SetId), MessageType.Warning); return; }

            var list = MotionEditData.BakeList(s);
            var names = new string[list.Count];
            int cur = 0;
            for (int i = 0; i < list.Count; i++)
            {
                names[i] = MotionEditData.OutName(list[i]);
                if (MotionEditData.LoadEditClip(MotionEditScene.SetId, names[i]) != null) names[i] += "  (편집본)";
                if (MotionEditData.OutName(list[i]) == MotionEditScene.Clip) cur = i;
            }
            if (list.Count > 0 && MotionEditData.FindEntry(s, MotionEditScene.Clip) == null) { MotionEditScene.Clip = MotionEditData.OutName(list[0]); OnChoiceChanged(false); }

            EditorGUI.BeginChangeCheck();
            int pick = EditorGUILayout.Popup("모션", cur, names);
            var weapons = MotionEditData.WeaponChoices(s);
            var wNames = new string[weapons.Count];
            for (int i = 0; i < weapons.Count; i++) wNames[i] = weapons[i].Key;
            int w = weapons.Count > 0 ? EditorGUILayout.Popup("무기", Mathf.Clamp(MotionEditScene.Weapon, 0, weapons.Count - 1), wNames) : 0;
            var shields = MotionEditData.ShieldChoices(s);
            int sh = 0;
            if (shields.Count > 0)
            {
                var sNames = new string[shields.Count + 1];
                sNames[0] = "없음";
                for (int i = 0; i < shields.Count; i++) sNames[i + 1] = shields[i].Key;
                sh = EditorGUILayout.Popup("방패", Mathf.Clamp(MotionEditScene.Shield, 0, shields.Count), sNames);
            }
            if (EditorGUI.EndChangeCheck())
            {
                bool clipChanged = list.Count > 0 && MotionEditData.OutName(list[pick]) != MotionEditScene.Clip;
                if (list.Count > 0) MotionEditScene.Clip = MotionEditData.OutName(list[pick]);
                MotionEditScene.Weapon = w; MotionEditScene.Shield = sh;
                if (clipChanged) frame = 0;
                OnChoiceChanged(false);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(MotionEditScene.IsOpen ? "장면 다시 세우기" : "편집 장면 열기")) MotionEditScene.Open();
            if (GUILayout.Button(clipIsEdit ? "편집본 열기 (Animation 창)" : "편집용 복사본 만들기"))
            {
                var c = MotionEditScene.MakeCopy();
                if (c != null) { MarkDirty(); dirtyAt = 0; }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(clip == null ? "모션 없음" : clipIsEdit ? "편집본: " + MotionEditData.ClipPath(MotionEditScene.SetId, MotionEditScene.Clip) : "팩 원본 (읽기 전용) — 고치려면 편집용 복사본을 만드세요", EditorStyles.miniLabel);
        }

        void OnChoiceChanged(bool rebuildRig)
        {
            playing = false;
            if (rebuildRig) DisposeRig();
            MotionEditScene.Rebuild();
            MarkDirty(); dirtyAt = 0;
        }

        void DrawPreview()
        {
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Warning);
            if (dirty && rig == null) { EditorGUILayout.LabelField("준비 중…"); return; }
            var tex = FrameTexture(frame);

            EditorGUILayout.BeginHorizontal();
            zoom = EditorGUILayout.IntPopup("배율", zoom, new[] { "2배", "3배", "4배" }, new[] { 2, 3, 4 }, GUILayout.Width(220));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(rig != null ? $"{rig.Width}x{rig.Height}px" : "", EditorStyles.miniLabel, GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();

            if (tex != null)
            {
                var r = GUILayoutUtility.GetRect(tex.width * zoom, tex.height * zoom, GUILayout.ExpandWidth(false));
                r.x = Mathf.Floor((position.width - tex.width * zoom) * 0.5f); if (r.x < 4) r.x = 4;
                r.width = tex.width * zoom; r.height = tex.height * zoom;
                EditorGUI.DrawRect(r, new Color(0.35f, 0.37f, 0.40f));
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
                // 바닥선 (기준점 높이), 기준점 세로선
                float gy = r.yMax - rig.Ground * zoom;
                EditorGUI.DrawRect(new Rect(r.x, gy, r.width, 1), new Color(1f, 0.85f, 0.3f, 0.8f));
                EditorGUI.DrawRect(new Rect(r.x + rig.PivotX * zoom, gy - 6, 1, 12), new Color(1f, 0.85f, 0.3f, 0.8f));
                if (settings != null && HitFrame() == frame)
                    GUI.Label(new Rect(r.x + 4, r.y + 2, 120, 18), "타격", new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1f, 0.3f, 0.3f) } });
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(28))) { playing = false; SetFrame(frame - 1, true); }
            if (GUILayout.Button(playing ? "멈춤" : "재생", GUILayout.Width(48)))
            {
                playing = !playing;
                if (playing) { playStart = EditorApplication.timeSinceStartup; playStartFrame = frame >= frames - 1 ? 0 : frame; }
            }
            if (GUILayout.Button("▶", GUILayout.Width(28))) { playing = false; SetFrame(frame + 1, true); }
            EditorGUI.BeginChangeCheck();
            int nf = EditorGUILayout.IntSlider(frame, 0, Mathf.Max(0, frames - 1));
            if (EditorGUI.EndChangeCheck()) { playing = false; SetFrame(nf, true); }
            EditorGUILayout.EndHorizontal();
            var fps = rig != null ? rig.setup.fps : 24;
            EditorGUILayout.LabelField($"프레임 {frame} / {frames - 1}   ({frames}장, {frames / (float)fps:0.00}초, {fps}fps)", EditorStyles.miniLabel);
            syncAnimWindow = EditorGUILayout.ToggleLeft("Animation 창 시간과 맞추기", syncAnimWindow);
            EditorGUILayout.HelpBox("굽기와 같은 조립·카메라·팔레트·윤곽선으로 그립니다. 프레임 여러 장을 보고 하는 '깜빡임 정리'만 빠져 있어 실제 굽기와 1~2픽셀 다를 수 있어요.\n장면(3D)에는 망치·창의 왼손 붙이기(IK)와 바닥 아래 들어올리기가 안 보입니다. 이 창이 실제 결과에 가깝습니다.", MessageType.None);
        }

        int HitFrame()
        {
            if (settings != null && settings.hitFrame.HasValue) return settings.hitFrame.Value;
            return -2;
        }

        void DrawSettings()
        {
            if (clip == null || settings == null) return;
            EditorGUILayout.LabelField("모션 설정 (굽기에 쓰임)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            bool hitSet = settings.hitFrame.HasValue;
            EditorGUILayout.BeginHorizontal();
            hitSet = EditorGUILayout.ToggleLeft("타격 프레임 직접 정하기", hitSet, GUILayout.Width(170));
            int hit = settings.hitFrame ?? frame;
            using (new EditorGUI.DisabledScope(!hitSet))
            {
                hit = EditorGUILayout.IntField(hit, GUILayout.Width(50));
                if (GUILayout.Button("지금 프레임으로", GUILayout.Width(110))) hit = frame;
                if (GUILayout.Button("타격 없음", GUILayout.Width(70))) hit = -1;
            }
            EditorGUILayout.EndHorizontal();
            if (!hitSet) EditorGUILayout.LabelField("끄면 굽기가 정한 대로 (공격 모션은 자동 찾기)", EditorStyles.miniLabel);

            int loopIdx = settings.loop.HasValue ? (settings.loop.Value ? 1 : 2) : 0;
            loopIdx = EditorGUILayout.Popup("반복", loopIdx, new[] { "원래대로 (팩 설정)", "반복함", "한 번만" });

            settings.rebake = EditorGUILayout.ToggleLeft("굽기 표시 (다음 '표시된 모션만 굽기' 때 다시 굽기)", settings.rebake);
            if (EditorGUI.EndChangeCheck())
            {
                settings.hitFrame = hitSet ? Mathf.Clamp(hit, -1, Mathf.Max(0, frames - 1)) : (int?)null;
                settings.loop = loopIdx == 0 ? (bool?)null : loopIdx == 1;
                MotionEditData.SaveSettings(MotionEditScene.SetId, MotionEditScene.Clip, settings);
                Repaint();
            }
            EditorGUILayout.LabelField("저장 위치: " + MotionEditData.SettingsPath(MotionEditScene.SetId, MotionEditScene.Clip), EditorStyles.miniLabel);

            EditorGUILayout.Space(6);
            var marked = MotionEditData.Marked();
            EditorGUILayout.LabelField($"굽기 표시된 모션: {marked.Count}개", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(marked.Count == 0))
                if (GUILayout.Button("표시된 모션만 굽기 (레인저·나무꾼 둘 다)", GUILayout.Height(26)))
                {
                    MotionEditRebake.BakeMarked();
                    MarkDirty(); dirtyAt = 0;
                }
        }
    }
}
