// 모션 편집 Timeline.
// Assets/Art/motion_edits/<세트>/timelines/<이름>.playable : 모션 클립을 놓고 자르기(clip in·길이), 속도, 블렌드를 Timeline 창에서 고친다.
// 굽기·미리보기는 Timeline의 클립 배치(시작, 길이, clip in, 속도, 블렌드 곡선)를 그대로 따라 굽기 리그에서 직접 섞어 재생한다
// (PlayableDirector 를 쓰지 않아서 루트 이동 처리가 모션 굽기와 같다 → 한 모션 Timeline은 원래 모션과 같은 결과).
// 음소거 안 된 Animation 트랙은 모두 층으로 겹친다: 아래 트랙이 위 트랙을 덮고, 마스크가 있으면 그 부위만 덮는다.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace CardGame.EditorTools
{
    public static class MotionEditTimeline
    {
        // 굽기 코드는 AnimationClip 단위로 돌아가므로, Timeline 마다 길이만 맞춘 빈 '대리 모션'을 만들어 넘긴다.
        static readonly Dictionary<AnimationClip, TimelineAsset> proxies = new Dictionary<AnimationClip, TimelineAsset>();

        public static bool TryGet(AnimationClip proxy, out TimelineAsset tl)
        {
            tl = null;
            return proxy != null && proxies.TryGetValue(proxy, out tl) && tl != null;
        }

        /// <summary>Timeline 길이만큼의 대리 모션. 반복 설정은 loopFrom(없으면 반복 안 함)을 따른다.</summary>
        public static AnimationClip MakeProxy(TimelineAsset tl, string name, AnimationClip loopFrom)
        {
            var c = new AnimationClip { name = name, hideFlags = HideFlags.HideAndDontSave };
            float len = Mathf.Max(1f / 60f, (float)tl.duration);
            AnimationUtility.SetEditorCurve(c, EditorCurveBinding.FloatCurve("__cg_timeline_length", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Linear(0f, 0f, len, 0f));
            var st = AnimationUtility.GetAnimationClipSettings(c);
            st.loopTime = loopFrom != null && loopFrom.isLooping;
            AnimationUtility.SetAnimationClipSettings(c, st);
            proxies[c] = tl;
            return c;
        }

        public static void ReleaseProxy(AnimationClip proxy)
        {
            if (proxy == null) return;
            proxies.Remove(proxy);
            UnityEngine.Object.DestroyImmediate(proxy);
        }

        /// <summary>굽기에 쓰는 Animation 트랙 하나 (= 섞기 층 하나)</summary>
        public class Layer
        {
            public AnimationTrack track;
            public List<TimelineClip> clips;
            /// <summary>Apply Avatar Mask 가 켜져 있을 때의 마스크 (없으면 null = 온몸)</summary>
            public AvatarMask mask;
            /// <summary>다른 Animation 트랙 안에 든 덮어쓰기 트랙(Override Track)인지</summary>
            public bool isOverride;
        }

        /// <summary>
        /// 굽는 층: 음소거 안 된 Animation 트랙 전부, Timeline 창 위에서 아래 순서 (덮어쓰기 트랙은 부모 바로 다음).
        /// 첫 층이 바탕, 아래 층일수록 위 층을 덮는다 (클립이 살아 있는 동안만, 마스크가 있으면 마스크 부위만).
        /// 부모 트랙이 음소거면 그 안의 덮어쓰기 트랙도 뺀다. 모션 클립이 없는 트랙은 뺀다.
        /// </summary>
        public static List<Layer> Layers(TimelineAsset tl)
        {
            var list = new List<Layer>();
            if (tl == null) return list;
            void Walk(TrackAsset t, bool parentMuted, bool insideAnim)
            {
                bool muted = parentMuted || t.muted;
                if (!muted && t is AnimationTrack at)
                {
                    var cl = new List<TimelineClip>();
                    foreach (var c in at.GetClips())
                        if (c.asset is AnimationPlayableAsset a && a.clip != null) cl.Add(c);
                    cl.Sort((a, b) => a.start.CompareTo(b.start));
                    if (cl.Count > 0)
                        list.Add(new Layer { track = at, clips = cl, mask = at.applyAvatarMask ? at.avatarMask : null, isOverride = insideAnim });
                }
                foreach (var ch in t.GetChildTracks()) Walk(ch, muted, insideAnim || t is AnimationTrack);
            }
            foreach (var r in tl.GetRootTracks()) Walk(r, false, false);
            return list;
        }

        /// <summary>굽기 전에 알려 줄 점 (결과가 Unity Timeline 미리보기와 다를 수 있는 것). 없으면 빈 목록.</summary>
        public static List<string> Warnings(TimelineAsset tl)
        {
            var w = new List<string>();
            var layers = Layers(tl);
            if (layers.Count == 0) { w.Add("음소거 안 된 Animation 트랙에 모션 클립이 없어요."); return w; }
            for (int i = 0; i < layers.Count; i++)
            {
                var L = layers[i];
                string tn = $"'{L.track.name}' 트랙";
                if (i == 0 && L.mask != null)
                    w.Add($"{tn}: 맨 위(바탕) 트랙의 마스크는 굽기에서 쓰지 않아요 (온몸으로 굽습니다).");
                if (i > 0 && L.mask != null && !L.isOverride)
                    w.Add($"{tn}: 따로 된 트랙에 마스크가 있어요. Unity Timeline 미리보기에선 마스크 밖 부위가 기본 자세(T자)로 보이지만, 굽기는 마스크 부위만 덮어요. 위 트랙 안의 덮어쓰기 트랙(Override Track)으로 넣으면 둘이 같아집니다.");
                if (L.track.trackOffset != TrackOffset.Auto && (L.track.position != Vector3.zero || L.track.eulerAngles != Vector3.zero))
                    w.Add($"{tn}: 트랙 위치·회전 보정값은 굽기에서 쓰지 않아요.");
                foreach (var c in L.clips)
                    if (c.asset is AnimationPlayableAsset a && (a.position != Vector3.zero || a.eulerAngles != Vector3.zero))
                    { w.Add($"{tn} '{c.displayName}': 클립 위치·회전 보정값은 굽기에서 쓰지 않아요."); break; }
            }
            return w;
        }

        /// <summary>굽는 바탕 클립: 첫 번째 층(음소거 안 된 맨 위 Animation 트랙)의 모션 클립들, 시작 순서</summary>
        public static List<TimelineClip> AnimClips(TimelineAsset tl)
        {
            var layers = Layers(tl);
            return layers.Count > 0 ? layers[0].clips : new List<TimelineClip>();
        }

        /// <summary>
        /// 덮는 층(두 번째 층부터)의 t초 무게. Timeline 과 같은 규칙: 클립 구간 안이면 섞기 곡선 무게,
        /// 클립 밖이어도 앞·뒤 늘이기(Hold, Loop 등)가 켜져 있는 구간이면 무게 1, 그 밖은 0.
        /// w 는 층 안 비율(합 1로 맞춤), 돌려주는 값 = 층 무게(0~1, 0이면 위 층이 그대로 보임).
        /// </summary>
        public static float OverrideWeights(List<TimelineClip> clips, double t, double duration, float[] w, double[] local)
        {
            if (t > duration) t = duration;
            float sum = 0f;
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                bool inside = t >= c.start && (t < c.end || (t <= c.end && c.end >= duration - 1e-6));
                if (inside) { w[i] = c.EvaluateMixIn(t) * c.EvaluateMixOut(t); local[i] = c.ToLocalTime(t); }
                else if ((c.hasPreExtrapolation || c.hasPostExtrapolation) && t >= c.extrapolatedStart && t < c.extrapolatedStart + c.extrapolatedDuration)
                { w[i] = 1f; local[i] = c.ToLocalTime(t); }
                else { w[i] = 0f; local[i] = c.ToLocalTimeUnbound(Math.Min(Math.Max(t, c.start), c.end)); }
                sum += w[i];
            }
            if (sum > 1e-6f) for (int i = 0; i < clips.Count; i++) w[i] /= sum;
            return Mathf.Clamp01(sum);
        }

        public static AnimationClip ClipOf(TimelineClip c) => (c.asset as AnimationPlayableAsset)?.clip;

        /// <summary>
        /// t초에서 각 클립의 무게와 클립 안 시간. Timeline 섞기와 같은 규칙:
        /// 클립 구간 [시작, 끝) 안이면 섞기 곡선(들어올 때 x 나갈 때) 무게. 아무 클립도 없으면 바로 앞 클립의 끝 자세를 유지.
        /// </summary>
        public static void Weights(List<TimelineClip> clips, double t, double duration, float[] w, double[] local)
        {
            if (t > duration) t = duration;
            bool any = false;
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                bool inside = t >= c.start && (t < c.end || (t <= c.end && c.end >= duration - 1e-6));
                w[i] = inside ? c.EvaluateMixIn(t) * c.EvaluateMixOut(t) : 0f;
                local[i] = c.ToLocalTimeUnbound(Math.Min(Math.Max(t, c.start), c.end));
                if (w[i] > 0f) any = true;
            }
            if (any) return;
            int hold = -1;
            for (int i = 0; i < clips.Count; i++) if (clips[i].end <= t + 1e-6) hold = i;
            if (hold < 0) hold = 0;
            if (clips.Count > 0)
            {
                w[hold] = 1f;
                local[hold] = clips[hold].ToLocalTimeUnbound(t < clips[hold].start ? clips[hold].start : clips[hold].end);
            }
        }

        // ───────── 만들기 ─────────

        /// <summary>
        /// Timeline 을 만든다. clips 를 차례로 놓고, blend 초만큼 겹쳐 섞는다. 이미 있으면 null.
        /// 각 모션은 편집본(.anim)이 있으면 그걸, 없으면 팩 모션을 쓴다.
        /// </summary>
        public static TimelineAsset Create(string set, string name, List<string> clipNames, CharacterBakeSetup setup, float blend)
        {
            string path = MotionEditData.TimelinePath(set, name);
            if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(path) != null) return null;
            MotionEditData.EnsureFolder(MotionEditData.TimelineFolder(set));
            var tl = ScriptableObject.CreateInstance<TimelineAsset>();
            tl.name = name;
            AssetDatabase.CreateAsset(tl, path);
            var track = tl.CreateTrack<AnimationTrack>(null, "캐릭터");
            double at = 0;
            TimelineClip prev = null;
            foreach (var cn in clipNames)
            {
                var e = MotionEditData.FindEntry(setup, cn);
                if (e == null) { Debug.LogWarning("[모션 편집] 모션이 없어요: " + cn); continue; }
                var clip = MotionEditData.LoadEditClip(set, cn) ?? e.clip;
                var tc = track.CreateClip(clip);
                tc.displayName = cn;
                double overlap = prev != null ? Math.Min(blend, Math.Min(prev.duration, tc.duration) * 0.5) : 0;
                tc.start = Math.Max(0, at - overlap);
                if (prev != null && overlap > 0) { prev.blendOutDuration = overlap; tc.blendInDuration = overlap; }
                // 루트 시작 위치를 지우지 않는다 (모션 굽기와 같게)
                if (tc.asset is AnimationPlayableAsset ap) ap.removeStartOffset = false;
                at = tc.end;
                prev = tc;
            }
            EditorUtility.SetDirty(tl);
            AssetDatabase.SaveAssets();
            MotionEditData.SaveTimelineSettings(set, name, new MotionEditData.Settings());
            return tl;
        }

        [MenuItem("CardGame/캐릭터/모션 편집/Timeline 만들기", false, 4)]
        static void CreateMenu() => TimelineCreateWindow.Open();
    }

    /// <summary>Timeline 만들기 창: 세트, 종류(한 모션 / 연속 공격), 모션, 이름</summary>
    public class TimelineCreateWindow : EditorWindow
    {
        int set, kind, clipIdx;
        string tlName = "";
        float blend = 0.1f;

        public static void Open()
        {
            var w = GetWindow<TimelineCreateWindow>(true, "Timeline 만들기", true);
            w.set = MotionEditScene.Set;
            w.minSize = w.maxSize = new Vector2(420, 190);
            w.ShowUtility();
        }

        void OnGUI()
        {
            string setId = MotionEditData.Sets[set];
            var setup = MotionEditData.LoadSetup(MotionEditScene.CharacterId, setId);
            set = EditorGUILayout.Popup("세트", set, MotionEditData.SetLabels);
            kind = EditorGUILayout.Popup("종류", kind, new[] { "한 모션 (자르기·속도)", "연속 공격 (공격 1→2→3)" });
            var names = new List<string>();
            foreach (var e in MotionEditData.BakeList(setup)) names.Add(MotionEditData.OutName(e));
            string prefix = MotionEditData.ClipPrefix(setId);
            if (kind == 0)
            {
                if (names.Count == 0) { EditorGUILayout.HelpBox("굽기 설정이 없어요.", MessageType.Warning); return; }
                clipIdx = Mathf.Clamp(clipIdx, 0, names.Count - 1);
                int ni = EditorGUILayout.Popup("모션", clipIdx, names.ToArray());
                if (ni != clipIdx || string.IsNullOrEmpty(tlName)) { clipIdx = ni; tlName = names[ni]; }
            }
            else
            {
                if (string.IsNullOrEmpty(tlName) || !tlName.EndsWith("combo")) tlName = prefix + "combo";
                blend = EditorGUILayout.Slider("섞는 시간(초)", blend, 0f, 0.3f);
            }
            tlName = PixelArtUtil.SafeName(EditorGUILayout.TextField("이름 (= 구운 파일 이름)", tlName));
            EditorGUILayout.LabelField(kind == 0 ? "이름이 원래 모션과 같으면, 구울 때 그 모션 결과를 이 Timeline으로 바꿉니다." : "새 모션으로 구워집니다 (예: " + prefix + "combo).", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("만들기", GUILayout.Height(26)))
            {
                var clips = kind == 0 ? new List<string> { names[clipIdx] } : new List<string> { prefix + "attack1", prefix + "attack2", prefix + "attack3" };
                var tl = MotionEditTimeline.Create(setId, tlName, clips, setup, kind == 0 ? 0f : blend);
                if (tl == null) { EditorUtility.DisplayDialog("Timeline 만들기", "같은 이름이 이미 있어요:\n" + MotionEditData.TimelinePath(setId, tlName), "확인"); return; }
                MotionEditScene.Set = set;
                MotionEditScene.Timeline = tlName;
                MotionEditScene.Rebuild();
                MotionEditPreviewWindow.ShowWindow();
                MotionEditPreviewWindow.RefreshAll();
                MotionEditScene.FocusTimelineWindow();
                Close();
            }
        }
    }
}
