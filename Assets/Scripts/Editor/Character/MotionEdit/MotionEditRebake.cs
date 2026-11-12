// 모션 편집 > 표시된 모션만 굽기.
// motion_edits/<세트>/<모션>.json 에서 rebake 가 true 인 모션만 레인저·나무꾼 둘 다 다시 굽는다.
// 결과 폴더·형식은 지금과 같다 (몸, _idx, json, 무기·방패 레이어). 팔레트는 저장된 것을 그대로 쓴다.
// 끝나면 _handoff/platform2/<캐릭터>_platform_<세트>.zip 안의 그 모션 파일만 바꾸고 (_prev.zip 은 건드리지 않음),
// 굽기 표시를 지우고, _handoff/platform2/rebake_report.txt 에 결과를 적는다.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    public static class MotionEditRebake
    {
        const string HandoffFolder = "_handoff/platform2";

        [MenuItem("CardGame/캐릭터/모션 편집/표시된 모션만 굽기", false, 20)]
        public static void BakeMarked()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("모션 편집", "플레이 중에는 구울 수 없어요.", "확인"); return; }
            var marked = MotionEditData.Marked();
            if (marked.Count == 0) { EditorUtility.DisplayDialog("모션 편집", "굽기 표시된 모션이 없어요.\n미리보기 창의 '굽기 표시'를 켜 주세요.", "확인"); return; }

            var sb = new StringBuilder();
            var t0 = DateTime.Now;
            sb.AppendLine($"표시된 모션만 굽기  {t0:yyyy-MM-dd HH:mm:ss}");
            var bySet = new Dictionary<string, List<string>>();
            foreach (var kv in marked)
            {
                if (!bySet.TryGetValue(kv.Key, out var l)) { l = new List<string>(); bySet[kv.Key] = l; }
                l.Add(kv.Value);
            }

            var done = new HashSet<string>();   // 세트/모션 (두 캐릭터 다 성공한 것)
            var failed = new List<string>();
            int total = marked.Count * MotionEditData.Characters.Length, step = 0;
            try
            {
                foreach (var kv in bySet)
                {
                    string set = kv.Key;
                    var ok = new Dictionary<string, int>();
                    foreach (var ch in MotionEditData.Characters)
                    {
                        var s = MotionEditData.LoadSetup(ch, set);
                        if (s == null) { failed.Add($"{ch} {set}: 굽기 설정 없음 ({MotionEditData.SetupPath(ch, set)})"); continue; }
                        var entries = new List<CharacterBakeSetup.ClipEntry>();
                        foreach (var clip in kv.Value)
                        {
                            var e = MotionEditData.FindEntry(s, clip);
                            if (e == null) failed.Add($"{ch} {set}/{clip}: 굽기 설정에 이 모션이 없음");
                            else entries.Add(e);
                        }
                        if (entries.Count == 0) continue;
                        if (!CharacterBaker.HasSavedPalettes(s)) { failed.Add($"{ch} {set}: 저장된 팔레트가 없어 건너뜀 (세트 전체를 먼저 구워야 함)"); continue; }

                        var rig = new CharacterRig(s);
                        var baked = new List<string>();
                        try
                        {
                            if (!rig.IsValid) { failed.Add($"{ch} {set}: 조립 실패 {rig.Warnings}"); continue; }
                            // 전부 굽기와 같은 순서: 첫 모션을 먼저 넣어 무기를 주먹에 맞춘다 (fistGrip)
                            var first = MotionEditData.FirstBakeClip(s);
                            if (first != null) rig.SetClip(first);
                            foreach (var e in entries)
                            {
                                string name = MotionEditData.OutName(e);
                                if (EditorUtility.DisplayCancelableProgressBar("표시된 모션만 굽기", $"{ch} {set}/{name}", step++ / (float)total))
                                    throw new OperationCanceledException();
                                string png = CharacterBaker.BakeClip(rig, e);
                                if (png == null) { failed.Add($"{ch} {set}/{name}: 굽기 실패"); continue; }
                                baked.Add(name);
                                ok.TryGetValue(name, out int c); ok[name] = c + 1;
                            }
                        }
                        finally { rig.Dispose(); }

                        string zipNote = UpdateZip(ch, set, s.outputFolder, baked);
                        foreach (var name in baked)
                        {
                            var ms = MotionEditData.LoadSettings(set, name);
                            bool edited = MotionEditData.LoadEditClip(set, name) != null;
                            sb.AppendLine($"  {ch} {set}/{name}: {(edited ? "편집본" : "팩 모션")}"
                                + (ms != null && ms.hitFrame.HasValue ? $", 타격 {ms.hitFrame.Value}" : "")
                                + (ms != null && ms.loop.HasValue ? $", 반복 {(ms.loop.Value ? "켬" : "끔")}" : ""));
                        }
                        if (!string.IsNullOrEmpty(zipNote)) sb.AppendLine("    " + zipNote);
                    }
                    foreach (var kv2 in ok) if (kv2.Value == MotionEditData.Characters.Length) done.Add(set + "/" + kv2.Key);
                }
            }
            catch (OperationCanceledException) { failed.Add("중간에 취소함"); }
            finally { EditorUtility.ClearProgressBar(); }

            // 두 캐릭터 다 구운 것만 굽기 표시를 지운다
            foreach (var key in done)
            {
                int slash = key.IndexOf('/');
                string set = key.Substring(0, slash), clip = key.Substring(slash + 1);
                var ms = MotionEditData.LoadSettings(set, clip);
                if (ms != null && ms.rebake) { ms.rebake = false; MotionEditData.SaveSettings(set, clip, ms); }
            }
            AssetDatabase.Refresh();

            var secs = (DateTime.Now - t0).TotalSeconds;
            sb.AppendLine($"끝: {done.Count}개 모션 x 캐릭터 {MotionEditData.Characters.Length}종, {secs:0}초");
            if (failed.Count > 0) { sb.AppendLine("문제:"); foreach (var f in failed) sb.AppendLine("  " + f); }
            string report = sb.ToString();
            try
            {
                Directory.CreateDirectory(MotionEditData.Full(HandoffFolder));
                File.WriteAllText(MotionEditData.Full(HandoffFolder + "/rebake_report.txt"), report, new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("[모션 편집] 보고서를 쓰지 못했어요: " + e.Message); }
            if (failed.Count > 0) Debug.LogWarning("[모션 편집] " + report); else Debug.Log("[모션 편집] " + report);
            MotionEditPreviewWindow.RefreshAll();
        }

        /// <summary>
        /// _handoff/platform2/<캐릭터>_platform_<세트>.zip 안에서 구운 모션의 파일만 바꾼다 (zip 안 폴더 = <캐릭터>_<세트>/).
        /// zip 이 없으면 건너뛴다. _prev.zip 은 건드리지 않는다.
        /// </summary>
        static string UpdateZip(string ch, string set, string outputFolder, List<string> clips)
        {
            if (clips.Count == 0) return null;
            string zip = MotionEditData.Full($"{HandoffFolder}/{ch}_platform_{set}.zip");
            if (!File.Exists(zip)) return $"zip 없음, 건너뜀: {ch}_platform_{set}.zip";
            string folder = MotionEditData.Full(outputFolder.TrimEnd('/'));
            string prefix = $"{ch}_{set}/";
            var files = new List<string>();
            foreach (var f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string file = Path.GetFileName(f);
                foreach (var clip in clips)
                    if (IsClipFile(file, clip)) { files.Add(f); break; }
            }
            int n = 0;
            using (var fs = new FileStream(zip, FileMode.Open, FileAccess.ReadWrite))
            using (var za = new ZipArchive(fs, ZipArchiveMode.Update))
            {
                foreach (var f in files)
                {
                    string rel = f.Substring(folder.Length).TrimStart('\\', '/').Replace('\\', '/');
                    string entryName = prefix + rel;
                    za.GetEntry(entryName)?.Delete();
                    var e = za.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
                    using (var es = e.Open())
                    using (var src = File.OpenRead(f)) src.CopyTo(es);
                    n++;
                }
            }
            return $"{ch}_platform_{set}.zip: 파일 {n}개 바꿈";
        }

        /// <summary>이 모션이 만든 파일인지: 모션.png/json, 모션_idx.png, 모션_back(.png|_idx.png), 모션_front(...)</summary>
        static bool IsClipFile(string file, string clip)
        {
            foreach (var tail in new[] { ".png", ".json", "_idx.png", "_back.png", "_front.png", "_back_idx.png", "_front_idx.png" })
                if (string.Equals(file, clip + tail, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
