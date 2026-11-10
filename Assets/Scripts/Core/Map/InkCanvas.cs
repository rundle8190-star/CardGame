// 잉크 지도 위에 매 프레임 덧그리는 것들 (B 스타일): 손으로 그은 듯 흔들리는 길 선, 외곽선·명암이 있는 노드 기호,
// 빨간 내 루트, 캐릭터 핀, 금색 고리, 펜촉. 웹 확정본 ink-map_keep_2026-11-10.html 의 drawSprite / drawLine / PIN 을 옮겼다.
// 모두 RGBA 바이트 버퍼(위쪽 줄부터)에 픽셀로 찍는다.
using System;
using System.Collections.Generic;

namespace CardGame.Core.Map
{
    public static class InkCanvas
    {
        // 노드 종류별 기호. 그림이 들어오기 전까지 쓰는 잉크 기호
        public static readonly Dictionary<string, string[]> Marks = new Dictionary<string, string[]>
        {
            { "battle", new[] { "..........#", ".........##", "........##.", ".......##..", "..#...##...", "...#.##....", "....##.....", "...##.#....", "..##...#...", ".##........", "##........." } },
            { "elite", new[] { "...#####...", "..#######..", ".#########.", ".##..#..##.", ".##..#..##.", ".####.####.", "..###.###..", "...#####...", "...#.#.#..." } },
            { "farm", new[] { ".....#.....", "...#.#.#...", "....###....", "..#.###.#..", "...#####...", ".....#.....", "....###....", "...#.#.#...", "..#..#..#..", ".....#....." } },
            { "event", new[] { "....###....", "....###....", "....###....", "....###....", "....###....", ".....#.....", "...........", "....###....", "....###...." } },
            { "totem", new[] { "...#####...", "..#.#.#.#..", "...#####...", "....#.#....", "...#####...", "..#..#..#..", "...#####...", "....#.#....", "..#######.." } },
            { "ruin_village", new[] { "...#.......", "..###......", ".#####.#...", "#######.#..", ".#...#####.", ".#.#.##..#.", ".#.#.##.##.", ".#####.###.", "###########" } },
            { "merchant_camp", new[] { ".....#.....", "....###....", "...#.#.#...", "...#.#.#...", "..#..#..#..", "..#..#..#..", ".#..###..#.", ".#..#.#..#.", "###########" } },
            { "small_dungeon", new[] { "...#####...", "..#.....#..", ".#..###..#.", ".#.#####.#.", "#..#####..#", "#..#####..#", "###########" } },
            { "big_dungeon", new[] { ".....#.....", "...#####...", ".#########.", "..#.#.#.#..", "..#.#.#.#..", "..#.#.#.#..", "..#.#.#.#..", ".#########.", "###########" } },
            { "exit", new[] { "##.........", "#######....", "#########..", "#######....", "##.........", "#..........", "#..........", "#..........", "###........" } },
            { "map_gate", new[] { ".....#.....", ".....##....", "#########..", "##########.", "#########..", ".....##....", ".....#....." } },
            { "start", new[] { "...#####...", "..#.....#..", ".#.......#.", ".#...#...#.", ".#..###..#.", ".#...#...#.", "..#.....#..", "...#####..." } },
            { "boss_spot", new[] { "..#.#.#....", "...........", ".#.#.#.#...", "..#####....", ".#######...", ".#######...", "..#####....", "...###....." } },
            { "boss", new[] { "#....#....#", "##..###..##", "###.###.###", "###########", "###########", ".#########.", ".#########." } },
        };
        public static readonly string[] Unknown = { "...####....", "..#....#...", "......##...", ".....##....", "....##.....", "....#......", "...........", "....#......" };
        public static readonly string[] Cross = { "#.....#", ".#...#.", "..#.#..", "...#...", "..#.#..", ".#...#.", "#.....#" };
        public static readonly string[] Check = { "......#", ".....##", "#...##.", "##.##..", ".###...", "..#...." };
        public static readonly string[] Nib = { "....##", "...###", "..###.", ".###..", "##....", "#....." };
        public static readonly string[] Pin = { "...oooooo...", "..o333333o..", ".o33443333o.", "o3344433332o", "o3344333332o", "o3333333322o", ".o33333322o.", "..o333322o..", "...o3322o...", "....o32o....", ".....oo....." };
        static readonly Dictionary<char, InkColor> PinColors = new Dictionary<char, InkColor>
        {
            { 'o', Ink.Out }, { '3', new InkColor(214, 52, 36) }, { '4', new InkColor(255, 226, 206) }, { '2', new InkColor(150, 30, 22) }
        };
        public static readonly string[][] Blots = { new[] { "##", "##" }, new[] { ".##", "###", "##." }, new[] { ".##.", "####", "###.", ".#.." }, new[] { "#" } };

        public static InkColor MarkColor(string type)
        {
            switch (type)
            {
                case "battle": return new InkColor(152, 161, 147);
                case "elite": case "big_dungeon": case "start": case "boss_spot": case "boss": return Ink.BrightRed;
                case "farm": return Ink.G[4];
                case "event": return new InkColor(210, 170, 60);
                case "totem": return new InkColor(131, 140, 128);
                case "ruin_village": return Ink.BR[3];
                case "merchant_camp": return Ink.BR[4];
                case "small_dungeon": return Ink.ST[1];
                case "exit": case "map_gate": return Ink.SoftBlue;
                default: return Ink.BR[3];
            }
        }

        public static string[] MarkOf(string type)
        {
            string[] s;
            return type != null && Marks.TryGetValue(type, out s) ? s : Unknown;
        }

        // 가운데 (cx, cy) 에 기호를 찍는다.
        // outline 이면 전투 그림처럼 어두운 외곽선을 두르고 위·왼쪽은 밝게, 아래·오른쪽은 어둡게 명암을 넣는다.
        public static void Mark(byte[] buf, int w, int h, string[] s, float cx, float cy, InkColor c, bool outline, float a = 1f)
        {
            Mark(buf, w, h, s, cx, cy, c, outline, Ink.Out, a);
        }

        public static void Mark(byte[] buf, int w, int h, string[] s, float cx, float cy, InkColor c, bool outline, InkColor outlineColor, float a = 1f)
        {
            int sw = s[0].Length, sh = s.Length;
            int x0 = (int)Math.Round(cx - sw / 2f), y0 = (int)Math.Round(cy - sh / 2f);
            Func<int, int, bool> on = (i, j) => j >= 0 && j < sh && i >= 0 && i < s[j].Length && s[j][i] == '#';
            if (!outline) { Ink.Sprite(buf, w, h, s, x0, y0, c, a); return; }
            for (int j = -1; j <= sh; j++)
                for (int i = -1; i <= sw; i++)
                {
                    if (on(i, j)) continue;
                    if (on(i - 1, j) || on(i + 1, j) || on(i, j - 1) || on(i, j + 1)) Ink.Set(buf, w, h, x0 + i, y0 + j, outlineColor);
                }
            InkColor lt = c.Lighter(0.35f), dk = c.Darker(0.7f);
            for (int j = 0; j < sh; j++)
                for (int i = 0; i < sw; i++)
                {
                    if (!on(i, j)) continue;
                    InkColor col = (!on(i, j - 1) || !on(i - 1, j)) ? lt : ((!on(i, j + 1) || !on(i + 1, j)) ? dk : c);
                    Ink.Set(buf, w, h, x0 + i, y0 + j, col);
                }
        }

        // 고리 (size 픽셀 점으로)
        public static void Ring(byte[] buf, int w, int h, float cx, float cy, float r, int dots, InkColor c, float a, int size = 1)
        {
            for (int t = 0; t < dots; t++)
            {
                double an = t / (double)dots * Math.PI * 2;
                Ink.Fill(buf, w, h, (int)Math.Round(cx + Math.Cos(an) * r), (int)Math.Round(cy + Math.Sin(an) * r), size, size, c, a);
            }
        }

        // 지금 위치: 금색 고리 하나 + 바깥으로 퍼지는 고리. phase 0~1 이 계속 돈다
        public static void GoldRings(byte[] buf, int w, int h, float cx, float cy, float phase)
        {
            float rr = 13f;
            Ring(buf, w, h, cx, cy, rr, Math.Max(24, (int)(rr * 6)), Ink.Gold, 0.95f, 2);
            rr = 13f + phase * 14f;
            Ring(buf, w, h, cx, cy, rr, Math.Max(24, (int)(rr * 6)), Ink.Gold, (1f - phase) * 0.8f, 2);
        }

        // 캐릭터 핀 (노드 위에 떠서 위아래로 흔들린다) + 밑의 작은 그림자
        public static void DrawPin(byte[] buf, int w, int h, float cx, float cy, int bob)
        {
            int x = (int)Math.Round(cx), y = (int)Math.Round(cy);
            Ink.Multi(buf, w, h, Pin, x - 6, y - 24 + bob, PinColors);
            Ink.Fill(buf, w, h, x - 3, y + 1, 7, 2, Ink.Out, 0.35f);
        }

        // 손으로 그은 선: 가운데가 조금 휘고, 끝은 노드에 정확히 닿는다. t 0~1
        public struct Stroke
        {
            public float ax, ay, bx, by;
            public int seed;

            public Stroke(float ax, float ay, float bx, float by, int seed)
            {
                this.ax = ax; this.ay = ay; this.bx = bx; this.by = by; this.seed = seed;
            }

            public float Length { get { return (float)Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)); } }

            public void Normal(out float nx, out float ny)
            {
                float len = Math.Max(1f, Length);
                nx = -(by - ay) / len; ny = (bx - ax) / len;
            }

            public void At(float t, out float x, out float y)
            {
                float dx = bx - ax, dy = by - ay, nx, ny;
                Normal(out nx, out ny);
                float wob = (float)(Math.Sin(t * Math.PI) * (Math.Sin(t * 9 + seed) * 1.6 + Math.Sin(t * 23 + seed * 3) * 0.6));
                x = ax + dx * t + nx * wob;
                y = ay + dy * t + ny * wob;
            }
        }

        public enum LineStyle { Ink, Route, Dashed, Dotted, Thin }

        // 길 하나를 그린다. reverse 면 b 에서 a 쪽으로, progress 만큼만 (그려지는 중).
        // visible 이 있으면 지도에 그려진 곳(안개가 걷힌 곳)에서만 보인다.
        //   Ink    : 펜 압력이 있는 보통 선 (가운데 굵고 끝은 가늚)
        //   Route  : 이번 탐험에 지나온 길. 끝까지 굵게
        //   Dashed : 점선 (지름길)
        //   Dotted : 2x2 점이 띄엄띄엄 (지금 갈 수 있는 길)
        //   Thin   : 1픽셀 가는 선 (알려진 길)
        public static void Line(byte[] buf, int w, int h, Stroke s, bool reverse, float progress, InkColor c, float a, LineStyle style, Func<int, int, bool> visible = null)
        {
            bool dash = style == LineStyle.Dashed, dotted = style == LineStyle.Dotted, thin = style == LineStyle.Thin, route = style == LineStyle.Route;
            int steps = Math.Max(4, (int)(s.Length * 2));
            float p = Math.Max(0f, Math.Min(1f, progress));
            float nx, ny;
            s.Normal(out nx, out ny);
            int lastX = int.MinValue, lastY = int.MinValue;
            for (int i = 0; i <= steps * p; i++)
            {
                float t = i / (float)steps;
                if (reverse) t = 1 - t;
                float x, y;
                s.At(t, out x, out y);
                int qx = (int)Math.Round(x), qy = (int)Math.Round(y);
                if (qx == lastX && qy == lastY) continue;
                lastX = qx; lastY = qy;
                if (visible != null && !visible(qx, qy)) continue;
                if (dash && ((i >> 2) % 2) == 1) continue;
                if (dotted)
                {
                    if (i % 10 > 1 || i < 16 || i > steps * p - 16) continue;
                    Ink.Fill(buf, w, h, qx, qy, 2, 2, c, a);
                    continue;
                }
                Ink.Plot(buf, w, h, qx, qy, c, a);
                if (thin || dash) continue;
                Ink.Plot(buf, w, h, (int)Math.Round(x - nx * 0.6f), (int)Math.Round(y - ny * 0.6f), c, a);
                // 펜 압력: 선 가운데는 굵고 끝은 가늘게. 내 루트는 끝까지 굵게
                double pr = route ? 1 : Math.Sin(t * Math.PI) * (0.55 + 0.45 * Math.Sin(t * 7 + s.seed * 1.7));
                if (pr > 0.45) Ink.Plot(buf, w, h, (int)Math.Round(x + nx), (int)Math.Round(y + ny), c, a);
            }
            // 펜을 댄 곳과 (다 그렸으면) 뗀 곳에 잉크가 고인다
            if (!dash && !dotted && !thin && a >= 1f)
            {
                float sx, sy;
                s.At(reverse ? 1 : 0, out sx, out sy);
                Ink.Fill(buf, w, h, (int)Math.Round(sx) - 1, (int)Math.Round(sy) - 1, 2, 2, c);
                if (p >= 1f)
                {
                    float ex, ey;
                    s.At(reverse ? 0 : 1, out ex, out ey);
                    int ix = (int)Math.Round(ex), iy = (int)Math.Round(ey);
                    Ink.Fill(buf, w, h, ix - 1, iy, 3, 1, c);
                    Ink.Fill(buf, w, h, ix, iy - 1, 1, 3, c);
                }
            }
        }

        // 선 위 progress 지점 (펜촉 위치)
        public static void Tip(Stroke s, bool reverse, float progress, out float x, out float y)
        {
            s.At(reverse ? 1 - progress : progress, out x, out y);
        }

        public static int SeedOf(MapLink l)
        {
            return (int)(Ink.Hash(l.ax * 131 + l.bx, l.ay * 131 + l.by, 17) * 100);
        }
    }
}
