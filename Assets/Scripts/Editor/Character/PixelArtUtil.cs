using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 도트 마무리 공용 도구: 팔레트 색 맞추기, 윤곽선, 스프라이트 시트 저장.
    /// 색 거리 계산은 맵 타일 굽기(QuarterTileBaker)와 같은 방식(빨강 3, 초록 4, 파랑 2 가중치)이다.
    /// </summary>
    public static class PixelArtUtil
    {
        /// <summary>팔레트 PNG에서 색을 뽑는다. (반투명 이하 픽셀은 무시, 같은 색은 하나로)</summary>
        public static Color32[] LoadPalette(Texture2D tex)
        {
            if (tex == null) return null;
            string path = AssetDatabase.GetAssetPath(tex);
            var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (string.IsNullOrEmpty(path) || !copy.LoadImage(File.ReadAllBytes(path))) return null;
                var set = new HashSet<int>();
                var list = new List<Color32>();
                foreach (var c in copy.GetPixels32())
                {
                    if (c.a < 128) continue;
                    int key = (c.r << 16) | (c.g << 8) | c.b;
                    if (set.Add(key)) list.Add(new Color32(c.r, c.g, c.b, 255));
                }
                return list.ToArray();
            }
            finally { Object.DestroyImmediate(copy); }
        }

        public static Color32 Nearest(Color32 c, Color32[] pal)
        {
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < pal.Length; i++)
            {
                int dr = c.r - pal[i].r, dg = c.g - pal[i].g, db = c.b - pal[i].b;
                int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                if (d < bestD) { bestD = d; best = i; }
            }
            var p = pal[best];
            return new Color32(p.r, p.g, p.b, c.a);
        }

        public static void Quantize(Color32[] px, Color32[] pal)
        {
            if (pal == null || pal.Length == 0) return;
            var cache = new Dictionary<int, Color32>();
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a == 0) continue;
                int key = (px[i].r << 16) | (px[i].g << 8) | px[i].b;
                if (!cache.TryGetValue(key, out var q)) { q = Nearest(px[i], pal); cache[key] = q; }
                px[i] = new Color32(q.r, q.g, q.b, 255);
            }
        }

        /// <summary>
        /// ss배 크게 그린 그림을 ss x ss 칸 평균으로 줄인다. 색은 불투명도로 가중 평균해서 가장자리가 검게 번지지 않게 한다.
        /// </summary>
        public static Color32[] BoxDownsample(Color32[] src, int w, int h, int ss)
        {
            if (ss <= 1) return src;
            int bw = w * ss;
            var dst = new Color32[w * h];
            int n = ss * ss;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        int row = (y * ss + sy) * bw + x * ss;
                        for (int sx = 0; sx < ss; sx++)
                        {
                            var c = src[row + sx];
                            r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                        }
                    }
                    if (a == 0) continue;
                    dst[y * w + x] = new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)(a / n));
                }
            return dst;
        }

        /// <summary>
        /// 여러 프레임의 색을 모아 count색 팔레트를 만든다 (가장 많이 쓰인 색 위주, 중간 나누기 + 몇 번 다듬기). 디더링 없음.
        /// </summary>
        public static Color32[] BuildPalette(Dictionary<int, int> hist, int count)
        {
            var cols = new List<(int r, int g, int b, int n)>();
            foreach (var kv in hist) cols.Add(((kv.Key >> 16) & 255, (kv.Key >> 8) & 255, kv.Key & 255, kv.Value));
            if (cols.Count == 0) return null;
            // 1) 중간 나누기: 가장 넓게 퍼진 상자를 픽셀 수 기준 가운데에서 자른다
            var boxes = new List<List<(int r, int g, int b, int n)>> { cols };
            while (boxes.Count < count)
            {
                int bi = -1, bestRange = 0, axis = 0;
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].Count < 2) continue;
                    for (int ax = 0; ax < 3; ax++)
                    {
                        int lo = 255, hi = 0;
                        foreach (var c in boxes[i]) { int v = ax == 0 ? c.r : ax == 1 ? c.g : c.b; if (v < lo) lo = v; if (v > hi) hi = v; }
                        int range = (hi - lo) * (ax == 1 ? 4 : ax == 0 ? 3 : 2);
                        if (range > bestRange) { bestRange = range; bi = i; axis = ax; }
                    }
                }
                if (bi < 0) break;
                var box = boxes[bi];
                int a2 = axis;
                box.Sort((p, q) => (a2 == 0 ? p.r - q.r : a2 == 1 ? p.g - q.g : p.b - q.b));
                long total = 0; foreach (var c in box) total += c.n;
                long acc = 0; int cut = 1;
                for (int i = 0; i < box.Count - 1; i++) { acc += box[i].n; if (acc * 2 >= total) { cut = i + 1; break; } cut = i + 1; }
                boxes[bi] = box.GetRange(0, cut);
                boxes.Add(box.GetRange(cut, box.Count - cut));
            }
            var pal = new List<Color32>();
            foreach (var box in boxes)
            {
                long r = 0, g = 0, b = 0, n = 0;
                foreach (var c in box) { r += (long)c.r * c.n; g += (long)c.g * c.n; b += (long)c.b * c.n; n += c.n; }
                if (n > 0) pal.Add(new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255));
            }
            // 2) 몇 번 다듬기 (각 색을 가장 가까운 팔레트 색에 모아 평균)
            var arr = pal.ToArray();
            for (int iter = 0; iter < 6; iter++)
            {
                var sum = new long[arr.Length, 4];
                foreach (var c in cols)
                {
                    int best = 0, bestD = int.MaxValue;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        int dr = c.r - arr[i].r, dg = c.g - arr[i].g, db = c.b - arr[i].b;
                        int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    sum[best, 0] += (long)c.r * c.n; sum[best, 1] += (long)c.g * c.n; sum[best, 2] += (long)c.b * c.n; sum[best, 3] += c.n;
                }
                for (int i = 0; i < arr.Length; i++)
                    if (sum[i, 3] > 0) arr[i] = new Color32((byte)(sum[i, 0] / sum[i, 3]), (byte)(sum[i, 1] / sum[i, 3]), (byte)(sum[i, 2] / sum[i, 3]), 255);
            }
            return arr;
        }

        /// <summary>
        /// 한 프레임짜리 깜빡임 지우기: 앞뒤 프레임 색이 같고 이 프레임만 다르면 앞뒤 색으로 바꾼다.
        /// 세 프레임 모두 불투명한 픽셀만 건드려서 윤곽(실루엣)이나 지나가는 무기는 지우지 않는다.
        /// skip[i]가 true인 프레임은 건너뛴다. 반환값은 바꾼 픽셀 수.
        /// </summary>
        public static int RemoveFlicker(List<Color32[]> frames, bool loop, bool[] skip)
        {
            int n = frames.Count;
            if (n < 3) return 0;
            var src = new List<Color32[]>(n);
            foreach (var f in frames) src.Add((Color32[])f.Clone());
            int changed = 0;
            for (int i = 0; i < n; i++)
            {
                if (skip != null && skip[i]) continue;
                int pi = i - 1, ni = i + 1;
                if (pi < 0) { if (!loop) continue; pi = n - 1; }
                if (ni >= n) { if (!loop) continue; ni = 0; }
                var a = src[pi]; var c = src[i]; var b = src[ni]; var o = frames[i];
                if (a.Length != c.Length || b.Length != c.Length) continue;
                for (int k = 0; k < c.Length; k++)
                {
                    if (c[k].a == 0 || a[k].a == 0 || b[k].a == 0) continue;
                    if (!Same(a[k], b[k]) || Same(a[k], c[k])) continue;
                    o[k] = a[k];
                    changed++;
                }
            }
            return changed;
        }

        static bool Same(Color32 p, Color32 q) => p.r == q.r && p.g == q.g && p.b == q.b && p.a == q.a;

        /// <summary>캐릭터 바깥쪽 투명 픽셀 중 상하좌우로 닿은 곳에 1픽셀 윤곽선을 그린다.</summary>
        public static void AddOutline(Color32[] px, int w, int h, Color32 col)
        {
            var src = (Color32[])px.Clone();
            col.a = 255;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (src[i].a != 0) continue;
                    bool edge = (x > 0 && src[i - 1].a != 0) || (x < w - 1 && src[i + 1].a != 0)
                             || (y > 0 && src[i - w].a != 0) || (y < h - 1 && src[i + w].a != 0);
                    if (edge) px[i] = col;
                }
        }

        public static void FlipRows(Color32[] px, int w, int h)
        {
            var row = new Color32[w];
            for (int y = 0; y < h / 2; y++)
            {
                int a = y * w, b = (h - 1 - y) * w;
                System.Array.Copy(px, a, row, 0, w);
                System.Array.Copy(px, b, px, a, w);
                System.Array.Copy(row, 0, px, b, w);
            }
        }

        /// <summary>불투명 픽셀이 있는 가장 아래 줄과 위 줄. 하나도 없으면 false.</summary>
        public static bool OpaqueRowRange(Color32[] px, int w, int h, out int minY, out int maxY)
        {
            minY = -1; maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a != 0) { if (minY < 0) minY = y; maxY = y; break; }
            return minY >= 0;
        }

        /// <summary>
        /// 가로 한 줄짜리 스프라이트 시트 PNG로 저장하고, 도트용 가져오기 설정과 프레임 자르기까지 한다.
        /// 같은 이름으로 다시 구우면 덮어쓰고, 각 프레임의 스프라이트 ID는 유지한다.
        /// </summary>
        /// <summary>
        /// 프레임을 시트 PNG로 저장한다. 가로 8192px 안에 들어가면 한 줄, 넘치면 여러 줄 (왼쪽 위부터 오른쪽으로, 다음 줄).
        /// 돌려주는 값: 한 줄에 들어간 프레임 수.
        /// </summary>
        public static int SaveStrip(string assetPath, List<Color32[]> frames, int fw, int fh, Vector2 pivot01, int ppu)
        {
            int n = frames.Count;
            const int MaxSize = 8192;
            int cols = Mathf.Clamp(MaxSize / fw, 1, n);
            int rows = (n + cols - 1) / cols;
            var tex = new Texture2D(fw * cols, fh * rows, TextureFormat.RGBA32, false);
            try
            {
                var clear = new Color32[tex.width * tex.height];
                tex.SetPixels32(clear);
                for (int i = 0; i < n; i++) tex.SetPixels32((i % cols) * fw, (rows - 1 - i / cols) * fh, fw, fh, frames[i]);
                tex.Apply(false);
                string full = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllBytes(full, tex.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(tex); }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (ti == null) return cols;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Multiple;
            ti.spritePixelsPerUnit = ppu;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.isReadable = false;
            ti.maxTextureSize = 8192;

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dp = factory.GetSpriteEditorDataProviderFromObject(ti);
            dp.InitSpriteEditorDataProvider();

            var oldIds = new Dictionary<string, GUID>();
            foreach (var r in dp.GetSpriteRects()) oldIds[r.name] = r.spriteID;

            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            var rects = new SpriteRect[n];
            for (int i = 0; i < n; i++)
            {
                string name = $"{baseName}_{i:00}";
                rects[i] = new SpriteRect
                {
                    name = name,
                    rect = new Rect((i % cols) * fw, (rows - 1 - i / cols) * fh, fw, fh),
                    alignment = SpriteAlignment.Custom,
                    pivot = pivot01,
                    spriteID = oldIds.TryGetValue(name, out var id) ? id : GUID.Generate(),
                };
            }
            dp.SetSpriteRects(rects);
            dp.Apply();
            ti.SaveAndReimport();
            return cols;
        }

        /// <summary>파일 이름용: 영문 소문자와 밑줄만 남긴다.</summary>
        public static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "clip";
            int bar = s.LastIndexOf('|');
            if (bar >= 0) s = s.Substring(bar + 1);
            var sb = new System.Text.StringBuilder();
            bool lastUnderscore = false;
            foreach (char ch in s.ToLowerInvariant())
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) { sb.Append(ch); lastUnderscore = false; }
                else if (!lastUnderscore && sb.Length > 0) { sb.Append('_'); lastUnderscore = true; }
            }
            return sb.ToString().TrimEnd('_');
        }
    }
}
