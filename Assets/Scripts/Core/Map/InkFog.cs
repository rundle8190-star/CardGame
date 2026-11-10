// 잉크 지도의 안개. 픽셀마다 세 가지 상태를 가진다.
//   0 빈 종이  : 아직 모르는 곳
//   1 연필     : 지난 탐험에서 본 곳 (지금은 바뀌었을 수 있다)
//   2 잉크     : 이번 탐험에서 직접 본 곳
// 밝힐 때는 원을 그리는데, 가장자리를 노이즈로 흔들어 잉크가 번진 것처럼 울퉁불퉁하게 만든다.
// 지난 탐험의 기억은 "밝힌 원 목록"과 "지나간 길 목록"으로 저장한다 (InkMemory). 픽셀을 통째로 저장하지 않는다.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CardGame.Core.Map
{
    public class InkFog
    {
        public readonly InkPaper Paper;
        public readonly int Width, Height;
        readonly byte[] run, mem;
        readonly float[] noise;
        public readonly byte[] Composite;   // 지금 보여 줄 종이 (RGBA, 위쪽 줄부터)

        public InkFog(InkPaper paper)
        {
            Paper = paper;
            Width = paper.Width; Height = paper.Height;
            run = new byte[Width * Height];
            mem = new byte[Width * Height];
            noise = new float[Width * Height];
            Composite = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float gx = x / 14f, gy = y / 14f;   // B 스타일(8픽셀 칸)에 맞춘 번짐 크기
                    int x0 = (int)gx, y0 = (int)gy;
                    float fx = gx - x0, fy = gy - y0;
                    float a = Ink.Hash(x0, y0, 5), b = Ink.Hash(x0 + 1, y0, 5), c = Ink.Hash(x0, y0 + 1, 5), d = Ink.Hash(x0 + 1, y0 + 1, 5);
                    noise[y * Width + x] = (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy + (Ink.Hash(x, y, 11) - 0.5f) * 0.25f;
                }
            Recomposite(0, 0, Width - 1, Height - 1);
        }

        public int StateAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return 0;
            int i = y * Width + x;
            return run[i] != 0 ? 2 : mem[i] != 0 ? 1 : 0;
        }

        int StateAt(int i) { return run[i] != 0 ? 2 : mem[i] != 0 ? 1 : 0; }

        // 이번 탐험에서 (cx, cy) 둘레 r 픽셀을 잉크로 밝힌다
        public void RevealRun(float cx, float cy, float r) { Reveal(run, cx, cy, r, true); }

        // 이번 탐험 잉크를 모두 지운다 (새 탐험, 또는 쓰러져서 잉크를 잃을 때)
        public void ClearRun()
        {
            Array.Clear(run, 0, run.Length);
            Recomposite(0, 0, Width - 1, Height - 1);
        }

        // 기억(연필)을 원 목록으로 다시 만든다
        public void LoadMemory(InkMemory memory)
        {
            Array.Clear(mem, 0, mem.Length);
            if (memory != null)
                foreach (var c in memory.Circles) Reveal(mem, c[0], c[1], c[2], false);
            Recomposite(0, 0, Width - 1, Height - 1);
        }

        void Reveal(byte[] arr, float cx, float cy, float r, bool redraw)
        {
            if (r <= 0) return;
            int x0 = Math.Max(0, (int)(cx - r - 6)), x1 = Math.Min(Width - 1, (int)(cx + r + 6));
            int y0 = Math.Max(0, (int)(cy - r - 6)), y1 = Math.Min(Height - 1, (int)(cy + r + 6));
            bool changed = false;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * Width + x;
                    if (arr[i] != 0) continue;
                    float dx = x - cx, dy = y - cy, e = r - 10 + noise[i] * 20;
                    if (e > 0 && dx * dx + dy * dy < e * e) { arr[i] = 1; changed = true; }
                }
            if (changed && redraw) Recomposite(x0 - 1, y0 - 1, x1 + 1, y1 + 1);
        }

        // 상태에 맞는 종이를 골라 Composite 에 옮긴다. 상태가 바뀌는 경계는 진하게 (잉크가 번진 테두리)
        void Recomposite(int x0, int y0, int x1, int y1)
        {
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(Width - 1, x1); y1 = Math.Min(Height - 1, y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * Width + x, s = StateAt(i), o = i * 4;
                    byte[] src = s == 2 ? Paper.InkLayer : s == 1 ? Paper.Pencil : Paper.Blank;
                    float k = 1f;
                    if (s > 0)
                    {
                        int nb = Math.Min(Math.Min(x > 0 ? StateAt(i - 1) : s, x < Width - 1 ? StateAt(i + 1) : s),
                                          Math.Min(y > 0 ? StateAt(i - Width) : s, y < Height - 1 ? StateAt(i + Width) : s));
                        if (nb < s) k = s == 2 ? 0.62f : 0.85f;
                    }
                    if (k < 1f)
                    {
                        float dr = s == 2 ? 6 : 0, dg = s == 2 ? 10 : 0, db = s == 2 ? 14 : 0;
                        Composite[o] = Ink.Clamp(src[o] * k - dr);
                        Composite[o + 1] = Ink.Clamp(src[o + 1] * k - dg);
                        Composite[o + 2] = Ink.Clamp(src[o + 2] * k - db);
                    }
                    else { Composite[o] = src[o]; Composite[o + 1] = src[o + 1]; Composite[o + 2] = src[o + 2]; }
                    Composite[o + 3] = 255;
                }
        }
    }

    // 지난 탐험들의 지도 기억 (연필로 남는 것). 파일 한 줄에 하나씩:
    //   c x y r          밝힌 원 (픽셀)
    //   l 링크키 출발키   지나간 길 (어느 쪽에서 그렸는지)
    //   k x,y 종류        그때 본 랜덤 칸의 종류
    public class InkMemory
    {
        public readonly List<int[]> Circles = new List<int[]>();
        public readonly Dictionary<string, string> Lines = new Dictionary<string, string>();   // 링크키 -> 출발 노드 "x,y"
        public readonly Dictionary<string, string> Known = new Dictionary<string, string>();   // 노드 "x,y" -> 종류

        public void AddRun(List<int[]> circles, Dictionary<string, string> lines, Dictionary<string, string> known)
        {
            Circles.AddRange(circles);
            foreach (var kv in lines) if (!Lines.ContainsKey(kv.Key)) Lines[kv.Key] = kv.Value;
            foreach (var kv in known) Known[kv.Key] = kv.Value;
            Compact();
        }

        // 너무 많이 쌓이면 다른 원 안에 완전히 들어간 원을 지운다
        void Compact()
        {
            if (Circles.Count < 600) return;
            var keep = new List<int[]>();
            for (int i = 0; i < Circles.Count; i++)
            {
                var a = Circles[i];
                bool inside = false;
                for (int j = 0; j < Circles.Count && !inside; j++)
                {
                    if (i == j) continue;
                    var b = Circles[j];
                    float d = (float)Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]));
                    if (d + a[2] <= b[2] && (b[2] > a[2] || j < i)) inside = true;
                }
                if (!inside) keep.Add(a);
            }
            Circles.Clear();
            Circles.AddRange(keep);
        }

        public string Save()
        {
            var sb = new StringBuilder();
            foreach (var c in Circles) sb.Append("c ").Append(c[0]).Append(' ').Append(c[1]).Append(' ').Append(c[2]).Append('\n');
            foreach (var kv in Lines) sb.Append("l ").Append(kv.Key).Append(' ').Append(kv.Value).Append('\n');
            foreach (var kv in Known) sb.Append("k ").Append(kv.Key).Append(' ').Append(kv.Value).Append('\n');
            return sb.ToString();
        }

        public static InkMemory Load(string data)
        {
            var m = new InkMemory();
            if (string.IsNullOrEmpty(data)) return m;
            foreach (var raw in data.Split('\n'))
            {
                var p = raw.Trim().Split(' ');
                if (p.Length < 3) continue;
                if (p[0] == "c" && p.Length >= 4)
                {
                    int x, y, r;
                    if (int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) &&
                        int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out y) &&
                        int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out r))
                        m.Circles.Add(new[] { x, y, r });
                }
                else if (p[0] == "l") m.Lines[p[1]] = p[2];
                else if (p[0] == "k") m.Known[p[1]] = p[2];
            }
            return m;
        }
    }
}
