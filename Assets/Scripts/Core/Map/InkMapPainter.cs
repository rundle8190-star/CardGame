// 잉크 지도의 "종이"를 그린다 (B 스타일: 8픽셀 칸, 전투 팔레트, 외곽선과 명암이 있는 기호).
// 웹 확정본 WebEditor/ink-map_keep_2026-11-10.html 의 paintBlank / paintTerrain 을 그대로 옮겼다.
// 결과는 세 장: 빈 종이(Blank), 잉크로 다 그린 지도(InkLayer), 연필로 흐리게 그린 지도(Pencil).
// 화면에서는 픽셀마다 이 셋 중 하나를 골라 보여 준다 (InkFog 참고).
// 엔진과 상관없는 순수 C# 이라 편집기 도구(PNG 굽기)와 게임 화면이 같이 쓴다.
// 픽셀 버퍼는 RGBA 바이트, 위쪽 줄부터(y 가 아래로 커짐). Unity 텍스처에 올릴 때 위아래를 뒤집는다.
using System;
using System.Collections.Generic;

namespace CardGame.Core.Map
{
    public struct InkColor
    {
        public byte r, g, b;
        public InkColor(int r, int g, int b) { this.r = (byte)r; this.g = (byte)g; this.b = (byte)b; }

        public InkColor Lighter(float t) { return new InkColor(r + (int)((255 - r) * t), g + (int)((255 - g) * t), b + (int)((255 - b) * t)); }
        public InkColor Darker(float t) { return new InkColor((int)(r * t), (int)(g * t), (int)(b * t)); }
        public static InkColor Mix(InkColor a, InkColor b, float t) { return new InkColor((int)(a.r + (b.r - a.r) * t), (int)(a.g + (b.g - a.g) * t), (int)(a.b + (b.b - a.b) * t)); }
    }

    // 잉크 지도에서 쓰는 색과 작은 계산. 색은 전투 팔레트(_palette_swap)에서 뽑았다
    public static class Ink
    {
        public static readonly InkColor Out = new InkColor(26, 18, 30);   // 외곽선
        public static readonly InkColor[] G = { new InkColor(25, 46, 20), new InkColor(37, 65, 22), new InkColor(48, 75, 27), new InkColor(58, 83, 32), new InkColor(73, 98, 44), new InkColor(96, 120, 60) };
        public static readonly InkColor[] BR = { new InkColor(57, 35, 18), new InkColor(78, 48, 27), new InkColor(93, 61, 37), new InkColor(112, 75, 44), new InkColor(151, 102, 70), new InkColor(173, 133, 91) };
        public static readonly InkColor[] ST = { new InkColor(100, 98, 84), new InkColor(114, 118, 105), new InkColor(131, 140, 128), new InkColor(152, 161, 147) };
        public static readonly InkColor[] WA = { new InkColor(52, 70, 88), new InkColor(74, 98, 116), new InkColor(104, 130, 140), new InkColor(150, 170, 168) };
        public static readonly InkColor[] PaperTones = { new InkColor(214, 196, 158), new InkColor(226, 210, 172), new InkColor(236, 222, 186) };

        public static readonly InkColor Paper = new InkColor(233, 220, 184);
        public static readonly InkColor Sepia = new InkColor(59, 42, 26);
        public static readonly InkColor Brown = new InkColor(110, 78, 46);
        public static readonly InkColor Red = new InkColor(138, 42, 30);
        public static readonly InkColor Graphite = new InkColor(120, 116, 108);   // 지난 탐험 길 (연필)
        public static readonly InkColor Route = new InkColor(128, 20, 16);       // 이번 탐험에 지나온 길
        public static readonly InkColor BrightRed = new InkColor(186, 64, 46);
        public static readonly InkColor SoftBlue = new InkColor(74, 98, 140);
        public static readonly InkColor Gold = new InkColor(240, 196, 70);

        // 칸 좌표로 정해지는 0~1 난수 (웹 시험판과 같은 식이라 같은 그림이 나온다)
        public static float Hash(int x, int y, int s)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + s * 1442695041;
                n = (int)((uint)n ^ ((uint)n >> 13)) * 1274126177;
                n ^= (int)((uint)n >> 16);
                return (uint)n / 4294967296f;
            }
        }

        public static byte Clamp(float v) { return (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }

        // 불투명하게 찍기
        public static void Set(byte[] buf, int w, int h, int x, int y, InkColor c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int o = (y * w + x) * 4;
            buf[o] = c.r; buf[o + 1] = c.g; buf[o + 2] = c.b; buf[o + 3] = 255;
        }

        // 버퍼의 (x, y) 에 색을 알파로 섞어 찍는다. 밖이면 무시
        public static void Plot(byte[] buf, int w, int h, int x, int y, InkColor c, float a = 1f)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int o = (y * w + x) * 4;
            if (a >= 1f) { buf[o] = c.r; buf[o + 1] = c.g; buf[o + 2] = c.b; buf[o + 3] = 255; return; }
            buf[o] = Clamp(buf[o] + (c.r - buf[o]) * a);
            buf[o + 1] = Clamp(buf[o + 1] + (c.g - buf[o + 1]) * a);
            buf[o + 2] = Clamp(buf[o + 2] + (c.b - buf[o + 2]) * a);
            buf[o + 3] = 255;
        }

        public static void Fill(byte[] buf, int w, int h, int x, int y, int rw, int rh, InkColor c, float a = 1f)
        {
            for (int j = 0; j < rh; j++)
                for (int i = 0; i < rw; i++) Plot(buf, w, h, x + i, y + j, c, a);
        }

        // '#' 자리에 점을 찍는 작은 그림. (x, y) 는 왼쪽 위
        public static void Sprite(byte[] buf, int w, int h, string[] rows, int x, int y, InkColor c, float a = 1f)
        {
            for (int j = 0; j < rows.Length; j++)
                for (int i = 0; i < rows[j].Length; i++)
                    if (rows[j][i] == '#') Plot(buf, w, h, x + i, y + j, c, a);
        }

        // 글자마다 색이 다른 그림 (웹의 spr). 표에 없는 글자는 건너뛴다
        public static void Multi(byte[] buf, int w, int h, string[] rows, int x0, int y0, Dictionary<char, InkColor> cm)
        {
            for (int j = 0; j < rows.Length; j++)
            {
                string r = rows[j];
                for (int i = 0; i < r.Length; i++)
                {
                    InkColor c;
                    if (cm.TryGetValue(r[i], out c)) Set(buf, w, h, x0 + i, y0 + j, c);
                }
            }
        }
    }

    public class InkPaper
    {
        public const int DefaultTilePx = 8;   // 지형 한 칸 = 8x8 픽셀 (B 스타일)

        public readonly int Width, Height, TilePx;
        public readonly byte[] Blank, InkLayer, Pencil;

        internal InkPaper(int w, int h, int t)
        {
            Width = w; Height = h; TilePx = t;
            Blank = new byte[w * h * 4];
            InkLayer = new byte[w * h * 4];
            Pencil = new byte[w * h * 4];
        }

        // 노드 (x, y) 가 찍히는 픽셀. 웹과 같이 칸 왼쪽 위에서 2픽셀 안쪽
        public int PixelX(int x) { return x * TilePx + 2; }
        public int PixelY(int y) { return y * TilePx + 2; }
    }

    public static class InkMapPainter
    {
        static readonly string[] Pine = { "...o...", "..o2o..", "..o23o.", ".o223o.", ".o1223o", "o11223o", ".o1223o", "o111223o", "oooo3ooo", "...ob...", "...oo..." };
        static readonly string[] Pine2 = { "..o..", ".o2o.", ".o23o", "o1223o", "o1123o", "ooobo.", "..oo." };
        static readonly string[] Tree = { "..ooo...", ".o233o..", "o22334o.", "o12234o.", "o11223o.", ".o1122o.", "..oobo..", "...oo..." };
        static readonly string[] Hill = { "....oooo....", "..oo3344oo..", ".o22233344o.", "o1122223334o" };
        static readonly string[] Hill2 = { "..ooo...", ".o344o..", "o12334o." };
        static readonly string[] Tuft = { "o.o.o", ".o3o.", "..o.." };
        static readonly string[] Tuft2 = { ".o..o", "o3oo3", ".o..o" };
        static readonly string[] Ruin = { "oo.oo...", "o3oo3o..", "o23o23oo", "o12o12o3", "oooooooo" };
        static readonly string[] Ruin2 = { "o.oo", "o3o3", "oooo" };
        static readonly string[] Mount = { ".....oo.....", "....o44o....", "...o3444o...", "..o233344o..", ".o12233344o.", "o1122233334o", "oooooooooooo" };
        static readonly string[] Spark = { ".4.", "444", ".4." };
        static readonly string[] North = { "#...#", "##..#", "#.#.#", "#..##", "#...#" };

        // 지형별 옅은 칠 (0 없음). f 숲, F 깊은 숲, h 언덕, s 늪, r 폐허, n 눈밭, M 산
        static int WashClass(char c)
        {
            switch (c)
            {
                case 'f': return 1;
                case 'F': return 2;
                case 'h': return 3;
                case 's': return 4;
                case 'r': return 5;
                case 'n': return 6;
                case 'M': return 7;
                default: return 0;
            }
        }
        static readonly InkColor[] WashCol = { default(InkColor), new InkColor(150, 160, 110), new InkColor(118, 132, 88), new InkColor(204, 184, 138), new InkColor(150, 150, 110), new InkColor(180, 174, 154), new InkColor(226, 232, 234), new InkColor(176, 156, 122) };
        static readonly float[] WashAlpha = { 0f, 0.55f, 0.6f, 0.5f, 0.55f, 0.55f, 0.8f, 0.6f };

        public static InkPaper Paint(MapData map, int tilePx = InkPaper.DefaultTilePx)
        {
            var p = new InkPaper(map.width * tilePx, map.height * tilePx, tilePx);
            PaintBlank(p);
            Buffer.BlockCopy(p.Blank, 0, p.InkLayer, 0, p.Blank.Length);
            PaintTerrain(map, p);
            MakePencil(p);
            return p;
        }

        static void Mix(byte[] d, int W, int H, int x, int y, InkColor c, float a) { Ink.Plot(d, W, H, x, y, c, a); }

        // 종이 결, 얼룩, 가장자리 그늘, 테두리 두 줄, 나침반
        static void PaintBlank(InkPaper p)
        {
            int W = p.Width, H = p.Height;
            var d = p.Blank;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = Ink.Hash(x >> 3, y >> 3, 7) * 0.5f + Ink.Hash(x >> 1, y >> 1, 9) * 0.5f;
                    InkColor c = v > 0.82f ? Ink.PaperTones[2] : v > 0.3f ? Ink.PaperTones[1] : Ink.PaperTones[0];
                    if (Ink.Hash(x, y, 3) < 0.004f) c = Ink.BR[4];
                    Ink.Set(d, W, H, x, y, c);
                    if (x < 12 || y < 12 || x > W - 13 || y > H - 13) Mix(d, W, H, x, y, Ink.BR[1], 0.12f);
                }
            // 테두리 두 줄
            for (int x = 4; x < W - 4; x++) { Ink.Set(d, W, H, x, 4, Ink.BR[0]); Ink.Set(d, W, H, x, 5, Ink.BR[0]); Ink.Set(d, W, H, x, H - 5, Ink.BR[0]); Ink.Set(d, W, H, x, H - 6, Ink.BR[0]); }
            for (int y = 4; y < H - 4; y++) { Ink.Set(d, W, H, 4, y, Ink.BR[0]); Ink.Set(d, W, H, 5, y, Ink.BR[0]); Ink.Set(d, W, H, W - 5, y, Ink.BR[0]); Ink.Set(d, W, H, W - 6, y, Ink.BR[0]); }
            for (int x = 9; x < W - 9; x++) { Mix(d, W, H, x, 9, Ink.BR[2], 0.7f); Mix(d, W, H, x, H - 10, Ink.BR[2], 0.7f); }
            for (int y = 9; y < H - 9; y++) { Mix(d, W, H, 9, y, Ink.BR[2], 0.7f); Mix(d, W, H, W - 10, y, Ink.BR[2], 0.7f); }
            // 나침반: 오른쪽 아래 구석에 작게. 팔 길이 R 은 지도 크기에 맞춰 8~14 픽셀
            int R = Math.Max(8, Math.Min(14, Math.Min(W, H) / 16));
            int cx = W - 16 - R, cy = H - 16 - R;
            for (int k = -R; k <= R; k++) { Ink.Set(d, W, H, cx, cy + k, Ink.BR[0]); Mix(d, W, H, cx + k, cy, Ink.BR[0], 0.6f); }
            int head = Math.Max(5, R * 6 / 10);
            for (int k = 0; k < head; k++)
            {
                Ink.Set(d, W, H, cx - (k >> 1), cy - R + k, Ink.BR[0]);
                Ink.Set(d, W, H, cx + (k >> 1), cy - R + k, Ink.BR[0]);
                for (int i = 1; i < (k >> 1); i++) Ink.Set(d, W, H, cx + i, cy - R + k, Ink.BR[3]);
            }
            for (int j = 0; j < 5; j++)
                for (int i = 0; i < 5; i++)
                    if (North[j][i] == '#') Ink.Set(d, W, H, cx - 2 + i, cy - R - 7 + j, Ink.BR[0]);
        }

        static bool IsWater(char c) { return c == 'w' || c == '#'; }

        static char At(MapData m, int x, int y) { return m.InBounds(x, y) ? m.GetTerrain(x, y) : 'M'; }

        static char AtClamped(MapData m, int x, int y)
        {
            x = Math.Max(0, Math.Min(m.width - 1, x)); y = Math.Max(0, Math.Min(m.height - 1, y));
            return m.GetTerrain(x, y);
        }

        static Dictionary<char, InkColor> Cm(params object[] kv)
        {
            var d = new Dictionary<char, InkColor>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(char)kv[i]] = (InkColor)kv[i + 1];
            return d;
        }

        static void PaintTerrain(MapData m, InkPaper p)
        {
            int W = p.Width, H = p.Height, T = p.TilePx, MW = m.width, MH = m.height;
            var b = p.InkLayer;

            // 1) 옅은 칠. 칸을 4등분해서 모서리를 깎거나 채운다 (8픽셀 계단이 안 보이게)
            int Q = T / 2;
            var C = new byte[W * H];
            for (int y = 0; y < MH; y++)
                for (int x = 0; x < MW; x++)
                {
                    int own = WashClass(AtClamped(m, x, y));
                    for (int qy = 0; qy < 2; qy++)
                        for (int qx = 0; qx < 2; qx++)
                        {
                            int dx = qx == 1 ? 1 : -1, dy = qy == 1 ? 1 : -1;
                            int nh = WashClass(AtClamped(m, x + dx, y)), nv = WashClass(AtClamped(m, x, y + dy));
                            int k = (nh != own && nv != own && nh == nv) ? nh : own;
                            if (k == 0) continue;
                            for (int j = 0; j < Q; j++)
                                for (int i = 0; i < Q; i++) C[(y * T + qy * Q + j) * W + x * T + qx * Q + i] = (byte)k;
                        }
                }
            var edgeCol = new InkColor(60, 50, 40);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int k = C[y * W + x];
                    if (k == 0) continue;
                    Mix(b, W, H, x, y, WashCol[k], WashAlpha[k]);
                    bool e = (x > 0 && C[y * W + x - 1] != k) || (x < W - 1 && C[y * W + x + 1] != k) || (y > 0 && C[(y - 1) * W + x] != k) || (y < H - 1 && C[(y + 1) * W + x] != k);
                    if (e) Mix(b, W, H, x, y, edgeCol, 0.12f);
                }

            // 2) 기호
            var gm = Cm('o', Ink.Out, '1', Ink.G[0], '2', Ink.G[2], '3', Ink.G[4], '4', Ink.G[5], 'b', Ink.BR[1]);
            var dm = Cm('o', Ink.Out, '1', Ink.G[0], '2', Ink.G[1], '3', Ink.G[3], '4', Ink.G[4], 'b', Ink.BR[0]);
            var bm = Cm('o', Ink.BR[0], '1', Ink.BR[2], '2', Ink.BR[3], '3', Ink.BR[4], '4', Ink.BR[5]);
            var sm = Cm('o', Ink.Out, '1', Ink.ST[0], '2', Ink.ST[1], '3', Ink.ST[3]);
            var tm = Cm('o', Ink.G[1], '3', Ink.G[4]);
            var mm = Cm('o', Ink.Out, '1', Ink.BR[1], '2', Ink.BR[3], '3', Ink.BR[4], '4', new InkColor(236, 228, 214));
            var nm = Cm('4', new InkColor(248, 250, 252));
            var fieldTuft = Cm('o', Ink.G[3]);
            for (int y = 0; y < MH; y++)
                for (int x = 0; x < MW; x++)
                {
                    char c = m.GetTerrain(x, y);
                    int X = x * T, Y = y * T;
                    float r = Ink.Hash(x, y, 1), r2 = Ink.Hash(x, y, 2);
                    if (c == 'F') { if ((x + y) % 2 == 0) Ink.Multi(b, W, H, r2 < 0.75f ? Pine : Pine2, X - 1 + (int)(r * 2), Y - 4, dm); }
                    else if (c == 'f') { if (r < 0.35f) Ink.Multi(b, W, H, r2 < 0.55f ? Tree : (r2 < 0.8f ? Pine : Pine2), X + (int)(r * 3) - 1, Y - 2, gm); }
                    else if (c == 'h') { if (r < 0.2f) Ink.Multi(b, W, H, r2 < 0.6f ? Hill : Hill2, X - 2 + (int)(r2 * 3), Y + 3, bm); }
                    else if (c == 's') { if (r < 0.5f) Ink.Multi(b, W, H, r2 < 0.5f ? Tuft : Tuft2, X + 1 + (int)(r2 * 2), Y + 3, tm); }
                    else if (c == 'r') { if (r < 0.5f) Ink.Multi(b, W, H, r2 < 0.6f ? Ruin : Ruin2, X, Y + 2, sm); }
                    else if (c == 'M') { if ((x + y) % 2 == 0 && r < 0.6f) Ink.Multi(b, W, H, Mount, X - 4 + (int)(r2 * 5), Y - 2 - (int)(r * 3), mm); }
                    else if (c == 'n') { if (r < 0.04f) Ink.Multi(b, W, H, Spark, X + 2, Y + 2, nm); else if (r < 0.12f) Ink.Set(b, W, H, X + 3, Y + 4, Ink.WA[3]); }
                    else if (c == '.' || c == '=') { if (r < 0.06f) Ink.Set(b, W, H, X + 3, Y + 5, Ink.BR[4]); else if (r > 0.97f) Ink.Multi(b, W, H, Tuft, X + 1, Y + 3, fieldTuft); }
                    // 길 칸('=')은 따로 그리지 않는다. 길은 노드 사이 선으로만 보인다
                }

            // 3) 물: 칸 경계를 부드럽게 이은 곡선 물가
            var F = WaterMask(m, W, H, T);
            Func<int, int, bool> isw = (x, y) => x >= 0 && y >= 0 && x < W && y < H && F[y * W + x] == 1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (F[y * W + x] == 0) continue;
                    bool edge = !isw(x - 1, y) || !isw(x + 1, y) || !isw(x, y - 1) || !isw(x, y + 1);
                    bool edge2 = !isw(x - 2, y) || !isw(x + 2, y) || !isw(x, y - 2) || !isw(x, y + 2) || !isw(x + 3, y) || !isw(x, y + 3);
                    InkColor c = edge ? Ink.WA[0] : (edge2 ? Ink.WA[2] : Ink.WA[1]);
                    int yy = y + (int)Math.Round(Math.Sin(x / 3.0 + y) * 1);
                    if (!edge && Mod(yy, 9) == 4 && Ink.Hash(x >> 3, y, 23) < 0.6f && (x + y * 3) % 11 < 6) c = Ink.WA[3];
                    Ink.Set(b, W, H, x, y, c);
                }

            // 4) 다리
            for (int y = 0; y < MH; y++)
                for (int x = 0; x < MW; x++)
                {
                    if (m.GetTerrain(x, y) != 'b') continue;
                    int X = x * T, Y = y * T;
                    bool across = IsWater(At(m, x, y - 1)) || IsWater(At(m, x, y + 1));
                    for (int j = 0; j < T; j++)
                        for (int i = 0; i < T; i++) Ink.Set(b, W, H, X + i, Y + j, ((across ? i : j) % 3) != 0 ? Ink.BR[4] : Ink.BR[2]);
                    for (int i = 0; i < T; i++)
                    {
                        if (across) { Ink.Set(b, W, H, X + i, Y, Ink.Out); Ink.Set(b, W, H, X + i, Y + T - 1, Ink.Out); }
                        else { Ink.Set(b, W, H, X, Y + i, Ink.Out); Ink.Set(b, W, H, X + T - 1, Y + i, Ink.Out); }
                    }
                }
        }

        static int Mod(int a, int n) { int r = a % n; return r < 0 ? r + n : r; }

        // 물 칸(물·깊은 물·다리)을 픽셀 단위로 부드럽게 보간한 마스크
        static byte[] WaterMask(MapData m, int W, int H, int T)
        {
            var F = new byte[W * H];
            Func<int, int, float> tv = (x, y) =>
            {
                char c = AtClamped(m, x, y);
                return (c == 'w' || c == '#' || c == 'b') ? 1f : 0f;
            };
            for (int py = 0; py < H; py++)
                for (int px = 0; px < W; px++)
                {
                    float u = (px + 0.5f) / T - 0.5f, v = (py + 0.5f) / T - 0.5f;
                    int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
                    float fx = u - x0, fy = v - y0;
                    float val = (tv(x0, y0) * (1 - fx) + tv(x0 + 1, y0) * fx) * (1 - fy) + (tv(x0, y0 + 1) * (1 - fx) + tv(x0 + 1, y0 + 1) * fx) * fy;
                    F[py * W + px] = (byte)(val + (Ink.Hash(px >> 2, py >> 2, 21) - 0.5f) * 0.15f > 0.5f ? 1 : 0);
                }
            return F;
        }

        // 연필: 잉크 그림의 밝기 차이만 절반쯤 남기고 회색으로
        static void MakePencil(InkPaper p)
        {
            var ink = p.InkLayer; var bl = p.Blank; var pe = p.Pencil;
            for (int i = 0; i < ink.Length; i += 4)
            {
                float g = ink[i] * 0.3f + ink[i + 1] * 0.59f + ink[i + 2] * 0.11f;
                float g0 = bl[i] * 0.3f + bl[i + 1] * 0.59f + bl[i + 2] * 0.11f;
                float dv = (g - g0) * 0.55f;
                pe[i] = Ink.Clamp(bl[i] + dv - 6);
                pe[i + 1] = Ink.Clamp(bl[i + 1] + dv - 4);
                pe[i + 2] = Ink.Clamp(bl[i + 2] + dv + 4);
                pe[i + 3] = 255;
            }
        }
    }
}
