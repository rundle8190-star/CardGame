using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 모션 하나를 프레임별로 그려서 스프라이트 시트(PNG)와 정보 파일(JSON)로 저장한다.
    /// 결과: {outputFolder}/{모션이름}.png, {모션이름}.json
    /// </summary>
    public static class CharacterBaker
    {
        [Serializable]
        public class ClipInfo
        {
            public string clip;
            public int fps;
            public int frames;
            public int frameWidth;
            public int frameHeight;
            [Tooltip("시트 한 줄에 든 프레임 수. 프레임이 많으면 여러 줄 (왼쪽 위부터 읽는다).")]
            public int columns;
            public int rows;
            public int pivotX;
            public int pivotY;
            public bool loop;
            [Tooltip("타격 프레임 번호 (0부터). 찾지 않았으면 -1.")]
            public int hitFrame = -1;
        }

        public static bool IsLoop(AnimationClip clip) => clip.isLooping || CharacterArtImport.LooksLooping(clip.name);

        public static int FrameCount(AnimationClip clip, CharacterBakeSetup s)
        {
            float len = Mathf.Max(0.0001f, clip.length);
            int n = IsLoop(clip) ? Mathf.Max(1, Mathf.RoundToInt(len * s.fps)) : Mathf.FloorToInt(len * s.fps + 0.0001f) + 1;
            return Mathf.Clamp(n, 1, Mathf.Max(1, s.maxFrames));
        }

        public static float FrameTime(AnimationClip clip, CharacterBakeSetup s, int i) => Mathf.Min(i / (float)s.fps, clip.length);

        /// <summary>무기 끝이 화면에서 가장 빠르게 움직이는 순간의 프레임 번호.</summary>
        public static int FindHitFrame(CharacterRig rig, AnimationClip clip, CharacterBakeSetup s)
        {
            rig.SetClip(clip);
            const float step = 1f / 60f;
            Vector2 prev = Vector2.zero;
            float bestSpeed = -1f, bestT = 0f;
            int count = Mathf.Max(2, Mathf.CeilToInt(clip.length / step) + 1);
            for (int i = 0; i < count; i++)
            {
                float t = Mathf.Min(i * step, clip.length);
                rig.Sample(t);
                Vector2 p = rig.WorldToPixel(rig.StrikePoint());
                if (i > 0)
                {
                    // 가장 빠른 구간의 가운데 시각을 기록한다 (구간 끝을 쓰면 무기가 지나간 뒤로 잡힌다)
                    float speed = (p - prev).magnitude;
                    if (speed > bestSpeed) { bestSpeed = speed; bestT = t - step * 0.5f; }
                }
                prev = p;
            }
            int frames = FrameCount(clip, s);
            // 가장 빠른 순간 직전에 보이는 프레임 (반올림하면 지나간 뒤 프레임이 될 수 있어 내림)
            return Mathf.Clamp(Mathf.FloorToInt(bestT * s.fps), 0, frames - 1);
        }

        /// <summary>
        /// 큰 캔버스에 모든 프레임을 한 번 그려 보고, 캐릭터가 기본 프레임 밖으로 나가면 그 모션만 프레임을 넓힌다.
        /// 발 위치(기준점)는 프레임 안에서 같은 자리를 유지하도록 왼쪽·아래 여백만큼 옮긴다.
        /// </summary>
        static void FitFrame(CharacterRig rig, AnimationClip clip, CharacterBakeSetup s, int n)
        {
            rig.ResetFrame();
            int baseW = rig.Width, baseH = rig.Height, basePiv = rig.PivotX, baseG = rig.Ground;
            // 사방으로 기본 크기만큼 여유를 둔 큰 캔버스
            int bigW = baseW * 3, bigH = baseH * 3;
            rig.UseFrame(bigW, bigH, basePiv + baseW, baseG + baseH);
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                rig.Sample(FrameTime(clip, s, i));
                var px = rig.Render();
                for (int y = 0; y < bigH; y++)
                    for (int x = 0; x < bigW; x++)
                        if (px[y * bigW + x].a != 0)
                        {
                            if (x < minX) minX = x; if (x > maxX) maxX = x;
                            if (y < minY) minY = y; if (y > maxY) maxY = y;
                        }
            }
            rig.ResetFrame();
            if (minX == int.MaxValue) return;

            const int pad = 2;
            int pivBig = basePiv + baseW, gBig = baseG + baseH;
            int needLeft = pivBig - minX + pad, needRight = maxX - pivBig + 1 + pad;
            int needDown = gBig - minY + pad, needUp = maxY - gBig + 1 + pad;
            int piv = Mathf.Max(basePiv, needLeft);
            int g = Mathf.Max(baseG, needDown);
            int w = Mathf.Max(baseW, piv + needRight);
            int h = Mathf.Max(baseH, g + needUp);
            // 8의 배수로 맞춰 둔다
            w = (w + 7) / 8 * 8; h = (h + 7) / 8 * 8;
            if (w != baseW || h != baseH)
                Debug.Log($"[캐릭터 굽기] {clip.name}: 프레임이 잘려서 {baseW}x{baseH} → {w}x{h} 로 넓혔습니다.");
            rig.UseFrame(w, h, piv, g);
        }

        /// <summary>모션 하나 굽기. 저장한 PNG 경로를 돌려준다.</summary>
        public static string BakeClip(CharacterRig rig, CharacterBakeSetup.ClipEntry entry)
        {
            var s = rig.setup;
            var clip = entry.clip;
            if (clip == null) return null;

            string name = PixelArtUtil.SafeName(string.IsNullOrEmpty(entry.outputName) ? clip.name : entry.outputName);
            string folder = s.outputFolder.TrimEnd('/');
            string png = $"{folder}/{name}.png";

            int n = FrameCount(clip, s);
            int hit = entry.hitFrameOverride >= 0 ? Mathf.Min(entry.hitFrameOverride, n - 1)
                    : entry.findHitFrame ? FindHitFrame(rig, clip, s) : -1;

            if (s.palette == null && s.autoPalette && rig.AutoPalette == null) rig.AutoPalette = LoadAutoPalette(s);

            rig.SetClip(clip);
            if (s.autoExpandFrame) FitFrame(rig, clip, s, n);
            var frames = new List<Color32[]>(n);
            var tip = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                rig.Sample(FrameTime(clip, s, i));
                tip[i] = rig.WorldToPixel(rig.StrikePoint());
                frames.Add(rig.Render());
            }

            int fw = rig.Width, fh = rig.Height, pivX = rig.PivotX, gy = rig.Ground;
            rig.ResetFrame();

            if (s.flickerFilter && n >= 3)
            {
                // 타격 프레임 앞뒤와 무기를 빠르게 휘두르는 프레임은 진짜 움직임이므로 건너뛴다
                bool loop = IsLoop(clip);
                var skip = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    if (hit >= 0 && Mathf.Abs(i - hit) <= 2) { skip[i] = true; continue; }
                    int pi = i > 0 ? i - 1 : (loop ? n - 1 : i), ni = i < n - 1 ? i + 1 : (loop ? 0 : i);
                    float speed = Mathf.Max((tip[i] - tip[pi]).magnitude, (tip[ni] - tip[i]).magnitude);
                    skip[i] = speed > s.flickerMaxSpeed;
                }
                // 두 번 돌려서, 첫 번째 정리로 새로 드러난 깜빡임도 지운다
                int fixedPx = PixelArtUtil.RemoveFlicker(frames, loop, skip) + PixelArtUtil.RemoveFlicker(frames, loop, skip);
                if (fixedPx > 0) Debug.Log($"[캐릭터 굽기] {clip.name}: 한 프레임짜리 깜빡임 {fixedPx}픽셀 정리");
            }

            var pivot = new Vector2(pivX / (float)fw, gy / (float)fh);
            int cols = PixelArtUtil.SaveStrip(png, frames, fw, fh, pivot, s.spritePixelsPerUnit);

            var info = new ClipInfo
            {
                clip = clip.name,
                fps = s.fps,
                frames = n,
                frameWidth = fw,
                frameHeight = fh,
                columns = cols,
                rows = (n + cols - 1) / cols,
                pivotX = pivX,
                pivotY = gy,
                loop = IsLoop(clip),
                hitFrame = hit,
            };
            string json = $"{folder}/{name}.json";
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), json), JsonUtility.ToJson(info, true));
            AssetDatabase.ImportAsset(json);
            return png;
        }

        static string PalettePath(CharacterBakeSetup s) => s.outputFolder.TrimEnd('/') + "/_palette.png";

        static Color32[] LoadAutoPalette(CharacterBakeSetup s)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), PalettePath(s));
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try { return tex.LoadImage(File.ReadAllBytes(path)) ? tex.GetPixels32() : null; }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>
        /// 체크된 모든 모션에서 몇 프레임씩 색을 모아 이 캐릭터 전용 고정 팔레트를 만들고 _palette.png로 저장한다.
        /// 모든 모션이 같은 팔레트를 쓰므로 프레임·모션마다 색이 바뀌지 않는다.
        /// </summary>
        static Color32[] MakeAutoPalette(CharacterRig rig, List<CharacterBakeSetup.ClipEntry> list)
        {
            var s = rig.setup;
            var hist = new Dictionary<int, int>();
            rig.RawColors = true;
            try
            {
                void Add(Color32[] px)
                {
                    foreach (var c in px)
                    {
                        if (c.a == 0) continue;
                        int key = (c.r << 16) | (c.g << 8) | c.b;
                        hist.TryGetValue(key, out int k); hist[key] = k + 1;
                    }
                }
                Add(rig.Render());
                foreach (var e in list)
                {
                    rig.SetClip(e.clip);
                    int n = FrameCount(e.clip, s);
                    for (int j = 0; j < 4; j++)
                    {
                        rig.Sample(FrameTime(e.clip, s, Mathf.Min(n - 1, j * n / 4)));
                        Add(rig.Render());
                    }
                }
            }
            finally { rig.RawColors = false; }
            var pal = PixelArtUtil.BuildPalette(hist, s.autoPaletteColors);
            if (pal == null) return null;
            var tex = new Texture2D(pal.Length, 1, TextureFormat.RGBA32, false);
            tex.SetPixels32(pal); tex.Apply(false);
            string rel = PalettePath(s);
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), s.outputFolder.TrimEnd('/')));
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), rel), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(rel);
            Debug.Log($"[캐릭터 굽기] 고정 팔레트 {pal.Length}색 저장: {rel}");
            return pal;
        }

        /// <summary>설정에서 체크된 모션을 전부 굽는다.</summary>
        public static void BakeAll(CharacterBakeSetup s)
        {
            var list = s.clips.FindAll(c => c != null && c.bake && c.clip != null);
            if (list.Count == 0) { EditorUtility.DisplayDialog("캐릭터 굽기", "구울 모션이 없습니다. 모션 목록에서 체크해 주세요.", "확인"); return; }

            var rig = new CharacterRig(s);
            try
            {
                if (!rig.IsValid) { EditorUtility.DisplayDialog("캐릭터 굽기", "조립에 실패했습니다.\n" + rig.Warnings, "확인"); return; }
                if (s.palette == null && s.autoPalette)
                {
                    EditorUtility.DisplayProgressBar("캐릭터 굽기", "고정 팔레트 만드는 중", 0f);
                    rig.AutoPalette = MakeAutoPalette(rig, list);
                }
                for (int i = 0; i < list.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("캐릭터 굽기", $"{list[i].clip.name} ({i + 1}/{list.Count})", i / (float)list.Count))
                        break;
                    string path = BakeClip(rig, list[i]);
                    Debug.Log($"[캐릭터 굽기] {path} 저장");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                rig.Dispose();
            }
            if (!string.IsNullOrEmpty(rig.Warnings)) Debug.LogWarning("[캐릭터 굽기] 참고:\n" + rig.Warnings);
            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(s.outputFolder.TrimEnd('/'));
            if (folder != null) EditorGUIUtility.PingObject(folder);
        }
    }
}
