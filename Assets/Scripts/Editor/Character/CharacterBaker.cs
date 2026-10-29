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
            [Tooltip("같이 구운 무기 레이어 이름. 파일은 layers/이름/모션.png")]
            public string[] layers;
            [Tooltip("칸 번호 이미지 (빨강 = 팔레트 칸 번호). 색 교체를 안 하면 비어 있다.")]
            public string indexImage;
            [Tooltip("색 교체 팔레트 PNG (한 줄 = 색 세트 하나, 맨 위 줄이 기본 색)")]
            public string paletteImage;
        }

        /// <summary>
        /// 부위별로 칸을 나눈 팔레트. 칸 번호 = 부위 시작 번호 + 부위 안 번호. 마지막 칸은 윤곽선.
        /// 부위: 1 피부, 2 천, 3 가죽, 4 금속, 5 어두운 곳 (0은 안 씀)
        /// </summary>
        public class SwapPalette
        {
            public static readonly string[] PartNames = { "", "skin", "cloth", "leather", "metal", "dark" };
            public int[] start = new int[6];
            public int[] count = new int[6];
            public Color32[][] part = new Color32[6][];
            public int Total => start[5] + count[5];
            public int Outline => Total;

            public SwapPalette(CharacterBakeSetup s)
            {
                int[] c = { 0, s.skinColors, s.clothColors, s.leatherColors, s.metalColors, s.darkColors };
                int at = 0;
                for (int p = 1; p <= 5; p++) { start[p] = at; count[p] = Mathf.Max(1, c[p]); at += count[p]; }
            }

            /// <summary>기본 색 한 줄 (윤곽선 포함)</summary>
            public Color32[] Row(Color32 outline)
            {
                var row = new Color32[Total + 1];
                for (int p = 1; p <= 5; p++)
                    for (int k = 0; k < count[p]; k++) row[start[p] + k] = part[p][k];
                row[Outline] = outline;
                return row;
            }
        }

        static int PartOf(Color32 id) => id.a == 0 || id.r < 1 || id.r > 5 ? 3 : id.r;

        /// <summary>몸 색 + 부위 번호 → 칸 번호 이미지 (빨강 = 칸 번호)</summary>
        static Color32[] ToIndex(Color32[] raw, Color32[] ids, SwapPalette sp)
        {
            var o = new Color32[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i].a == 0) continue;
                int p = PartOf(ids[i]);
                o[i] = new Color32((byte)(sp.start[p] + PixelArtUtil.NearestIndex(raw[i], sp.part[p])), 0, 0, 255);
            }
            return o;
        }

        static Color32[] FromIndex(Color32[] idx, Color32[] row)
        {
            var o = new Color32[idx.Length];
            for (int i = 0; i < idx.Length; i++)
                if (idx[i].a != 0) { var c = row[Mathf.Min(idx[i].r, row.Length - 1)]; o[i] = new Color32(c.r, c.g, c.b, 255); }
            return o;
        }

        public static bool Layered(CharacterBakeSetup s) => s.paletteSwap || s.layers.Count > 0;

        static string ShortName(string n) { int bar = n.LastIndexOf('|'); return bar >= 0 ? n.Substring(bar + 1) : n; }

        /// <summary>이 모션에 무기 레이어 li를 굽는지</summary>
        public static bool LayerApplies(CharacterBakeSetup s, int li, AnimationClip clip)
        {
            var L = s.layers[li];
            if (L == null || L.prefab == null || string.IsNullOrEmpty(L.name)) return false;
            if (s.layerClipPrefixes.Count == 0) return true;
            string n = ShortName(clip.name);
            foreach (var pre in s.layerClipPrefixes)
                if (!string.IsNullOrEmpty(pre) && n.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string LayerFolder(CharacterBakeSetup s, int li) => s.outputFolder.TrimEnd('/') + "/layers/" + PixelArtUtil.SafeName(s.layers[li].name);

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
                var px = rig.RenderPass(CharacterRig.Pass.Fit, -1);
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

            if (Layered(s)) return BakeClipLayered(rig, entry, name, n, hit);
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

        static bool[] FlickerSkip(AnimationClip clip, CharacterBakeSetup s, Vector2[] tip, int hit, int n)
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
            return skip;
        }

        /// <summary>
        /// 몸과 무기를 따로 굽는다.
        /// 몸: {폴더}/{모션}.png (무기 없음). 색 교체를 켜면 {모션}_idx.png (칸 번호)도 같이.
        /// 무기: {폴더}/layers/{무기}/{모션}.png. 프레임 크기·기준점은 몸과 같고, 몸에 가려지는 부분은 비어 있다.
        /// </summary>
        static string BakeClipLayered(CharacterRig rig, CharacterBakeSetup.ClipEntry entry, string name, int n, int hit)
        {
            var s = rig.setup;
            var clip = entry.clip;
            string folder = s.outputFolder.TrimEnd('/');
            EnsurePalettes(rig, new List<CharacterBakeSetup.ClipEntry> { entry });
            var sp = rig.Swap;

            var lis = new List<int>();
            for (int li = 0; li < s.layers.Count; li++) if (LayerApplies(s, li, clip)) lis.Add(li);

            rig.SetClip(clip);
            if (s.autoExpandFrame) FitFrame(rig, clip, s, n);
            int fw = rig.Width, fh = rig.Height, pivX = rig.PivotX, gy = rig.Ground;

            var body = new List<Color32[]>(n);
            var lay = new Dictionary<int, List<Color32[]>>();
            foreach (int li in lis) lay[li] = new List<Color32[]>(n);
            var tip = new Vector2[n];
            var outlineIdx = sp != null ? new Color32((byte)sp.Outline, 0, 0, 255) : (Color32)s.outlineColor;
            for (int i = 0; i < n; i++)
            {
                rig.Sample(FrameTime(clip, s, i));
                tip[i] = rig.WorldToPixel(rig.StrikePoint());
                var raw = rig.RenderPass(CharacterRig.Pass.Body, -1);
                Color32[] f;
                if (sp != null) f = ToIndex(raw, rig.RenderPass(CharacterRig.Pass.BodyId, -1), sp);
                else { f = raw; if (rig.AutoPalette != null) PixelArtUtil.Quantize(f, rig.AutoPalette); }
                if (s.outline) PixelArtUtil.AddOutline(f, fw, fh, outlineIdx);
                body.Add(f);
                foreach (int li in lis)
                {
                    var px = rig.RenderPass(CharacterRig.Pass.Layer, li);
                    var alone = rig.RenderPass(CharacterRig.Pass.LayerAlone, li);
                    if (rig.LayerPalettes != null && rig.LayerPalettes[li] != null) PixelArtUtil.Quantize(px, rig.LayerPalettes[li]);
                    if (s.outline) PixelArtUtil.AddLayerOutline(px, alone, fw, fh, s.outlineColor);
                    lay[li].Add(px);
                }
            }
            rig.ResetFrame();

            if (s.flickerFilter && n >= 3)
            {
                bool loop = IsLoop(clip);
                var skip = FlickerSkip(clip, s, tip, hit, n);
                int fixedPx = PixelArtUtil.RemoveFlicker(body, loop, skip) + PixelArtUtil.RemoveFlicker(body, loop, skip);
                foreach (int li in lis) fixedPx += PixelArtUtil.RemoveFlicker(lay[li], loop, skip) + PixelArtUtil.RemoveFlicker(lay[li], loop, skip);
                if (fixedPx > 0) Debug.Log($"[캐릭터 굽기] {clip.name}: 한 프레임짜리 깜빡임 {fixedPx}픽셀 정리 (무기 레이어 포함)");
            }

            var pivot = new Vector2(pivX / (float)fw, gy / (float)fh);
            string png = $"{folder}/{name}.png";
            int cols;
            string idxPng = null;
            if (sp != null)
            {
                var row = sp.Row(s.outlineColor);
                var colorFrames = new List<Color32[]>(n);
                foreach (var f in body) colorFrames.Add(FromIndex(f, row));
                cols = PixelArtUtil.SaveStrip(png, colorFrames, fw, fh, pivot, s.spritePixelsPerUnit);
                idxPng = $"{folder}/{name}_idx.png";
                PixelArtUtil.SaveStrip(idxPng, body, fw, fh, pivot, s.spritePixelsPerUnit, true);
            }
            else cols = PixelArtUtil.SaveStrip(png, body, fw, fh, pivot, s.spritePixelsPerUnit);

            var names = new List<string>();
            foreach (int li in lis)
            {
                string lp = $"{LayerFolder(s, li)}/{name}.png";
                PixelArtUtil.SaveStrip(lp, lay[li], fw, fh, pivot, s.spritePixelsPerUnit);
                names.Add(PixelArtUtil.SafeName(s.layers[li].name));
            }

            var info = new ClipInfo
            {
                clip = clip.name, fps = s.fps, frames = n, frameWidth = fw, frameHeight = fh,
                columns = cols, rows = (n + cols - 1) / cols, pivotX = pivX, pivotY = gy,
                loop = IsLoop(clip), hitFrame = hit,
                layers = names.ToArray(),
                indexImage = idxPng != null ? System.IO.Path.GetFileName(idxPng) : "",
                paletteImage = sp != null ? "_palette_swap.png" : "",
            };
            string json = $"{folder}/{name}.json";
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), json), JsonUtility.ToJson(info, true));
            AssetDatabase.ImportAsset(json);
            return png;
        }

        static string SwapPalettePath(CharacterBakeSetup s) => s.outputFolder.TrimEnd('/') + "/_palette_swap.png";

        /// <summary>팔레트가 rig에 없으면 저장된 파일에서 읽고, 파일도 없으면 주어진 모션들로 만든다.</summary>
        static void EnsurePalettes(CharacterRig rig, List<CharacterBakeSetup.ClipEntry> list)
        {
            var s = rig.setup;
            if (s.paletteSwap && rig.Swap == null)
            {
                var px = LoadPng(SwapPalettePath(s), out int w, out int h);
                var sp = new SwapPalette(s);
                if (px != null && w == sp.Total + 1)
                {
                    // 맨 위 줄이 기본 색 (텍스처 좌표로는 마지막 줄)
                    int top = (h - 1) * w;
                    for (int p = 1; p <= 5; p++)
                    {
                        sp.part[p] = new Color32[sp.count[p]];
                        for (int k = 0; k < sp.count[p]; k++) sp.part[p][k] = px[top + sp.start[p] + k];
                    }
                    rig.Swap = sp;
                }
                else rig.Swap = MakeSwapPalette(rig, list);
            }
            if (s.layers.Count > 0 && rig.LayerPalettes == null)
            {
                var pals = new Color32[s.layers.Count][];
                bool all = true;
                for (int li = 0; li < s.layers.Count; li++)
                {
                    if (s.layers[li] == null || s.layers[li].prefab == null) continue;
                    pals[li] = LoadPng(LayerFolder(s, li) + "/_palette.png", out _, out _);
                    if (pals[li] == null) all = false;
                }
                rig.LayerPalettes = all ? pals : MakeLayerPalettes(rig, list);
            }
        }

        static Color32[] LoadPng(string rel, out int w, out int h)
        {
            w = h = 0;
            string path = Path.Combine(Directory.GetCurrentDirectory(), rel);
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
                w = tex.width; h = tex.height;
                return tex.GetPixels32();
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        static void SavePng(string rel, Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply(false);
            string full = Path.Combine(Directory.GetCurrentDirectory(), rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(rel) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default;
                ti.filterMode = FilterMode.Point;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
        }

        /// <summary>모션마다 몇 프레임씩 그려서 부위별 색을 모은다 (첫 프레임부터 4장)</summary>
        static void SampleFrames(CharacterRig rig, List<CharacterBakeSetup.ClipEntry> list, Func<CharacterBakeSetup.ClipEntry, bool> use, Action action)
        {
            var s = rig.setup;
            foreach (var e in list)
            {
                if (!use(e)) continue;
                rig.SetClip(e.clip);
                int n = FrameCount(e.clip, s);
                for (int j = 0; j < 4; j++)
                {
                    rig.Sample(FrameTime(e.clip, s, Mathf.Min(n - 1, j * n / 4)));
                    action();
                }
            }
        }

        static Color32[] SortByBrightness(Color32[] pal)
        {
            var l = new List<Color32>(pal);
            l.Sort((a, b) => (a.r * 3 + a.g * 4 + a.b * 2).CompareTo(b.r * 3 + b.g * 4 + b.b * 2));
            return l.ToArray();
        }

        static Color32[] Fit(Color32[] pal, int count)
        {
            var o = new Color32[count];
            for (int k = 0; k < count; k++)
                o[k] = pal == null || pal.Length == 0 ? new Color32(128, 128, 128, 255) : pal[Mathf.Min(k, pal.Length - 1)];
            return o;
        }

        /// <summary>
        /// 부위별 팔레트를 만들고 _palette_swap.png 로 저장한다.
        /// 줄: 0 기본, 1 천 파랑, 2 천 빨강, 3 피부 밝게, 4 피부 어둡게 (PNG 맨 위가 0번 줄)
        /// </summary>
        static SwapPalette MakeSwapPalette(CharacterRig rig, List<CharacterBakeSetup.ClipEntry> list)
        {
            var s = rig.setup;
            var sp = new SwapPalette(s);
            var hist = new Dictionary<int, int>[6];
            for (int p = 0; p < 6; p++) hist[p] = new Dictionary<int, int>();
            SampleFrames(rig, list, e => true, () =>
            {
                var raw = rig.RenderPass(CharacterRig.Pass.Body, -1);
                var ids = rig.RenderPass(CharacterRig.Pass.BodyId, -1);
                for (int i = 0; i < raw.Length; i++)
                {
                    if (raw[i].a == 0) continue;
                    int key = (raw[i].r << 16) | (raw[i].g << 8) | raw[i].b;
                    var hh = hist[PartOf(ids[i])];
                    hh.TryGetValue(key, out int k); hh[key] = k + 1;
                }
            });
            var log = new System.Text.StringBuilder("[캐릭터 굽기] 부위별 팔레트: ");
            for (int p = 1; p <= 5; p++)
            {
                sp.part[p] = Fit(SortByBrightness(PixelArtUtil.BuildPalette(hist[p], sp.count[p]) ?? new Color32[0]), sp.count[p]);
                int px = 0; foreach (var v in hist[p].Values) px += v;
                log.Append($"{SwapPalette.PartNames[p]} {sp.count[p]}칸({px}px) ");
            }
            Debug.Log(log.ToString());

            var rows = new List<Color32[]>();
            var baseRow = sp.Row(s.outlineColor);
            rows.Add(baseRow);
            rows.Add(Recolor(baseRow, sp, 2, 215f, 1.0f, 1.0f));   // 천 파랑
            rows.Add(Recolor(baseRow, sp, 2, 358f, 1.1f, 1.05f));  // 천 빨강
            rows.Add(Recolor(baseRow, sp, 1, -1f, 0.8f, 1.35f));   // 피부 밝게
            rows.Add(Recolor(baseRow, sp, 1, -1f, 1.1f, 0.7f));    // 피부 어둡게
            int w = baseRow.Length, h = rows.Count;
            var px2 = new Color32[w * h];
            for (int r = 0; r < h; r++) Array.Copy(rows[r], 0, px2, (h - 1 - r) * w, w);
            SavePng(SwapPalettePath(s), px2, w, h);

            var meta = new System.Text.StringBuilder();
            meta.Append("{\n  \"rows\": [\"default\", \"cloth_blue\", \"cloth_red\", \"skin_light\", \"skin_dark\"],\n  \"rowOrder\": \"PNG top = row 0\",\n  \"parts\": {");
            for (int p = 1; p <= 5; p++)
                meta.Append($"{(p > 1 ? ", " : "")}\"{SwapPalette.PartNames[p]}\": [{sp.start[p]}, {sp.count[p]}]");
            meta.Append($"}},\n  \"outlineIndex\": {sp.Outline}\n}}\n");
            string jrel = s.outputFolder.TrimEnd('/') + "/_palette_swap.json";
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), jrel), meta.ToString());
            AssetDatabase.ImportAsset(jrel);
            return sp;
        }

        /// <summary>한 부위의 색만 바꾼 줄. hue가 0 이상이면 색상을 그 값(도)으로, 채도·밝기는 배율.</summary>
        static Color32[] Recolor(Color32[] row, SwapPalette sp, int part, float hue, float sat, float val)
        {
            var o = (Color32[])row.Clone();
            for (int k = 0; k < sp.count[part]; k++)
            {
                int i = sp.start[part] + k;
                Color.RGBToHSV(row[i], out float h, out float sv, out float v);
                if (hue >= 0f) h = hue / 360f;
                var c = Color.HSVToRGB(h, Mathf.Clamp01(sv * sat), Mathf.Clamp01(v * val));
                o[i] = c;
            }
            return o;
        }

        static Color32[][] MakeLayerPalettes(CharacterRig rig, List<CharacterBakeSetup.ClipEntry> list)
        {
            var s = rig.setup;
            var pals = new Color32[s.layers.Count][];
            for (int li = 0; li < s.layers.Count; li++)
            {
                var L = s.layers[li];
                if (L == null || L.prefab == null) continue;
                var hist = new Dictionary<int, int>();
                int idx = li;
                SampleFrames(rig, list, e => LayerApplies(s, idx, e.clip), () =>
                {
                    foreach (var c in rig.RenderPass(CharacterRig.Pass.LayerAlone, idx))
                    {
                        if (c.a == 0) continue;
                        int key = (c.r << 16) | (c.g << 8) | c.b;
                        hist.TryGetValue(key, out int k); hist[key] = k + 1;
                    }
                });
                var pal = PixelArtUtil.BuildPalette(hist, s.layerPaletteColors);
                if (pal == null) continue;
                pals[li] = SortByBrightness(pal);
                SavePng(LayerFolder(s, li) + "/_palette.png", pals[li], pals[li].Length, 1);
            }
            return pals;
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
                if (Layered(s))
                {
                    EditorUtility.DisplayProgressBar("캐릭터 굽기", "부위별·무기 팔레트 만드는 중", 0f);
                    if (s.paletteSwap) rig.Swap = MakeSwapPalette(rig, list);
                    if (s.layers.Count > 0) rig.LayerPalettes = MakeLayerPalettes(rig, list);
                }
                else if (s.palette == null && s.autoPalette)
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
