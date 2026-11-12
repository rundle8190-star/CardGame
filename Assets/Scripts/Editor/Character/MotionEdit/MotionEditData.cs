// 모션 편집 공통: 경로, 세트·캐릭터 이름, 모션별 편집 설정(json) 읽기·쓰기.
// 편집본: Assets/Art/motion_edits/<세트>/<모션>.anim  (팩 모션을 복사한 것, Animation 창으로 고친다)
// 설정:   Assets/Art/motion_edits/<세트>/<모션>.json  (타격 프레임, 반복, 다시 굽기 표시)
// <세트> = sword / dual / hammer / spear, <모션> = 구운 파일 이름 (예: sword_shield_attack1)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    public static class MotionEditData
    {
        public const string Root = "Assets/Art/motion_edits";
        public const string SetupFolder = "Assets/Art3D/BakeSetups";

        public static readonly string[] Characters = { "ranger", "woodcutter" };
        public static readonly string[] CharacterLabels = { "레인저", "나무꾼" };
        public static readonly string[] Sets = { "sword", "dual", "hammer", "spear" };
        public static readonly string[] SetLabels = { "검방패", "쌍검", "망치", "창" };

        public static string SetupPath(string character, string set) => $"{SetupFolder}/{character}_platform_{set}_x1_5.asset";
        public static CharacterBakeSetup LoadSetup(string character, string set) => AssetDatabase.LoadAssetAtPath<CharacterBakeSetup>(SetupPath(character, set));

        public static string ClipPath(string set, string clip) => $"{Root}/{set}/{clip}.anim";
        public static string SettingsPath(string set, string clip) => $"{Root}/{set}/{clip}.json";

        static readonly Regex SetRx = new Regex(@"_platform_(sword|dual|hammer|spear)_x1_5$", RegexOptions.IgnoreCase);

        /// <summary>Platform 무기별 세트 굽기 설정이면 세트 이름(sword 등)을 돌려준다.</summary>
        public static bool TrySetOf(CharacterBakeSetup s, out string set)
        {
            set = null;
            if (s == null || string.IsNullOrEmpty(s.outputFolder)) return false;
            var m = SetRx.Match(s.outputFolder.TrimEnd('/'));
            if (!m.Success) return false;
            set = m.Groups[1].Value.ToLowerInvariant();
            return true;
        }

        /// <summary>굽기 결과 파일 이름 (굽기와 같은 규칙)</summary>
        public static string OutName(CharacterBakeSetup.ClipEntry e)
            => e == null || e.clip == null ? "" : PixelArtUtil.SafeName(string.IsNullOrEmpty(e.outputName) ? e.clip.name : e.outputName);

        /// <summary>굽기 설정에서 굽는 모션 목록 (결과 파일 이름 순서 그대로)</summary>
        public static List<CharacterBakeSetup.ClipEntry> BakeList(CharacterBakeSetup s)
            => s == null ? new List<CharacterBakeSetup.ClipEntry>() : s.clips.FindAll(c => c != null && c.bake && c.clip != null);

        public static CharacterBakeSetup.ClipEntry FindEntry(CharacterBakeSetup s, string clipName)
            => BakeList(s).Find(c => OutName(c) == clipName);

        /// <summary>
        /// 전부 굽기 때 처음 재생하는 모션. 무기를 주먹 안으로 옮기는 값(fistGrip)이 이 모션 0프레임으로 정해지므로
        /// 일부만 굽거나 미리볼 때도 먼저 이 모션을 한 번 넣어야 결과가 같다.
        /// </summary>
        public static AnimationClip FirstBakeClip(CharacterBakeSetup s)
        {
            var list = BakeList(s);
            return list.Count > 0 ? list[0].clip : null;
        }

        public static AnimationClip LoadEditClip(string set, string clip)
            => string.IsNullOrEmpty(set) || string.IsNullOrEmpty(clip) ? null : AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(set, clip));

        // ───────── 모션별 설정 ─────────

        public class Settings
        {
            /// <summary>null = 굽기가 정한 대로 (자동 찾기 또는 없음). -1 = 타격 없음.</summary>
            public int? hitFrame;
            /// <summary>null = 원본 모션 설정대로</summary>
            public bool? loop;
            /// <summary>다음 '표시된 모션만 굽기' 때 다시 굽는다</summary>
            public bool rebake;
            /// <summary>Timeline 전용: 타격 프레임 여러 개 (연속 공격). 비어 있으면 굽기가 정한 대로.</summary>
            public List<int> hitFrames = new List<int>();
        }

        public static Settings LoadSettings(string set, string clip)
        {
            string full = Full(SettingsPath(set, clip));
            if (!File.Exists(full)) return null;
            try { return ParseSettings(File.ReadAllText(full)); }
            catch (Exception e) { Debug.LogWarning($"[모션 편집] 설정을 읽지 못했어요: {SettingsPath(set, clip)} ({e.Message})"); return null; }
        }

        public static Settings ParseSettings(string json)
        {
            var s = new Settings();
            var m = Regex.Match(json, "\"hitFrame\"\\s*:\\s*(null|-?\\d+)");
            if (m.Success && m.Groups[1].Value != "null") s.hitFrame = int.Parse(m.Groups[1].Value);
            m = Regex.Match(json, "\"loop\"\\s*:\\s*(null|true|false)");
            if (m.Success && m.Groups[1].Value != "null") s.loop = m.Groups[1].Value == "true";
            m = Regex.Match(json, "\"rebake\"\\s*:\\s*(true|false)");
            s.rebake = m.Success && m.Groups[1].Value == "true";
            m = Regex.Match(json, "\"hitFrames\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (m.Success)
                foreach (var part in m.Groups[1].Value.Split(','))
                    if (int.TryParse(part.Trim(), out int h)) s.hitFrames.Add(h);
            return s;
        }

        public static string ToJson(string set, string clip, Settings s, bool timeline = false)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"set\": \"{set}\",\n");
            sb.Append($"  \"{(timeline ? "timeline" : "clip")}\": \"{clip}\",\n");
            if (timeline) sb.Append($"  \"hitFrames\": [{string.Join(", ", s.hitFrames)}],\n");
            else sb.Append($"  \"hitFrame\": {(s.hitFrame.HasValue ? s.hitFrame.Value.ToString() : "null")},\n");
            sb.Append($"  \"loop\": {(s.loop.HasValue ? (s.loop.Value ? "true" : "false") : "null")},\n");
            sb.Append($"  \"rebake\": {(s.rebake ? "true" : "false")}\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        public static void SaveSettings(string set, string clip, Settings s)
        {
            string rel = SettingsPath(set, clip);
            EnsureFolder($"{Root}/{set}");
            File.WriteAllText(Full(rel), ToJson(set, clip, s));
            AssetDatabase.ImportAsset(rel);
        }

        /// <summary>'굽기 표시'된 모션 (세트, 모션) 목록</summary>
        public static List<KeyValuePair<string, string>> Marked()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var set in Sets)
            {
                string dir = Full($"{Root}/{set}");
                if (!Directory.Exists(dir)) continue;
                var files = Directory.GetFiles(dir, "*.json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                {
                    string clip = Path.GetFileNameWithoutExtension(f);
                    var st = LoadSettings(set, clip);
                    if (st != null && st.rebake) list.Add(new KeyValuePair<string, string>(set, clip));
                }
            }
            return list;
        }

        // ───────── Timeline (motion_edits/<세트>/timelines/<이름>.playable + .json) ─────────

        public static string TimelineFolder(string set) => $"{Root}/{set}/timelines";
        public static string TimelinePath(string set, string name) => $"{TimelineFolder(set)}/{name}.playable";
        public static string TimelineSettingsPath(string set, string name) => $"{TimelineFolder(set)}/{name}.json";

        /// <summary>세트별 모션 이름 앞부분 (연속 공격 이름 = 앞부분 + combo)</summary>
        public static string ClipPrefix(string set)
            => set == "sword" ? "sword_shield_" : set == "dual" ? "dualblades_" : set == "hammer" ? "hammer_" : set == "spear" ? "spear_" : set + "_";

        public static List<string> TimelineNames(string set)
        {
            var list = new List<string>();
            string dir = Full(TimelineFolder(set));
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*.playable")) list.Add(Path.GetFileNameWithoutExtension(f));
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public static Settings LoadTimelineSettings(string set, string name)
        {
            string full = Full(TimelineSettingsPath(set, name));
            if (!File.Exists(full)) return null;
            try { return ParseSettings(File.ReadAllText(full)); }
            catch (Exception e) { Debug.LogWarning($"[모션 편집] Timeline 설정을 읽지 못했어요: {TimelineSettingsPath(set, name)} ({e.Message})"); return null; }
        }

        public static void SaveTimelineSettings(string set, string name, Settings s)
        {
            string rel = TimelineSettingsPath(set, name);
            EnsureFolder(TimelineFolder(set));
            File.WriteAllText(Full(rel), ToJson(set, name, s, true));
            AssetDatabase.ImportAsset(rel);
        }

        /// <summary>'굽기 표시'된 Timeline (세트, 이름) 목록</summary>
        public static List<KeyValuePair<string, string>> MarkedTimelines()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var set in Sets)
                foreach (var n in TimelineNames(set))
                {
                    var st = LoadTimelineSettings(set, n);
                    if (st != null && st.rebake) list.Add(new KeyValuePair<string, string>(set, n));
                }
            return list;
        }

        public static string Full(string rel) => Path.Combine(Directory.GetCurrentDirectory(), rel);

        /// <summary>Assets 아래 폴더를 (없으면) 차례로 만든다.</summary>
        public static void EnsureFolder(string rel)
        {
            rel = rel.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(rel)) return;
            int slash = rel.LastIndexOf('/');
            string parent = rel.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, rel.Substring(slash + 1));
        }

        // ───────── 무기·방패 선택지 ─────────

        /// <summary>
        /// 무기 선택지: 이름 → 레이어 번호들. 쌍검은 오른손·왼손 두 개를 한 묶음으로 (dagger_a = dagger_a_r + dagger_a_l).
        /// </summary>
        public static List<KeyValuePair<string, int[]>> WeaponChoices(CharacterBakeSetup s)
        {
            var list = new List<KeyValuePair<string, int[]>>();
            if (s == null) return list;
            var groups = new Dictionary<string, List<int>>();
            var order = new List<string>();
            for (int i = 0; i < s.layers.Count; i++)
            {
                var L = s.layers[i];
                if (L == null || L.prefab == null || L.isShield || string.IsNullOrEmpty(L.name)) continue;
                string n = PixelArtUtil.SafeName(L.name);
                string key = n.EndsWith("_r") || n.EndsWith("_l") ? n.Substring(0, n.Length - 2) : n;
                if (!groups.TryGetValue(key, out var g)) { g = new List<int>(); groups[key] = g; order.Add(key); }
                g.Add(i);
            }
            foreach (var k in order) list.Add(new KeyValuePair<string, int[]>(k, groups[k].ToArray()));
            return list;
        }

        /// <summary>방패 선택지 (레이어 번호). 없으면 빈 목록.</summary>
        public static List<KeyValuePair<string, int>> ShieldChoices(CharacterBakeSetup s)
        {
            var list = new List<KeyValuePair<string, int>>();
            if (s == null) return list;
            for (int i = 0; i < s.layers.Count; i++)
            {
                var L = s.layers[i];
                if (L != null && L.prefab != null && L.isShield) list.Add(new KeyValuePair<string, int>(PixelArtUtil.SafeName(L.name), i));
            }
            return list;
        }
    }
}
