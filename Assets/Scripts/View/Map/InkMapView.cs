// 잉크 지도 화면 (B 스타일). 캐릭터가 펼친 지도 위에, 지나간 길이 잉크로 그려지는 방식으로 탐험을 보여 준다.
// MapPlayController 의 "inkMap" 을 켜면 쿼터뷰 타일 대신 이 화면을 쓴다. 규칙·창(전투, 탈출 ...)은 그대로 컨트롤러가 맡는다.
// 웹 확정본 WebEditor/ink-map_keep_2026-11-10.html 의 frame() 을 옮겼다.
//
// 그리는 순서 (모두 한 장의 텍스처에 픽셀로 찍고, 점 필터로 키워 보여 준다)
//   1 종이: InkMapPainter 가 지형 글자로 그린 빈 종이 / 잉크 지도 / 연필 지도
//   2 안개: InkFog 가 픽셀마다 빈 종이·연필(지난 탐험)·잉크(이번 탐험) 중 하나를 고른다
//   3 밝기: 지도 전체를 살짝 어둡게 하고 캐릭터(또는 펜촉) 둘레만 등불처럼 밝힌다
//   4 길 선: 지난 탐험 길(연필), 알려진 길(가는 선), 지름길(점선), 이번 탐험에 지나온 길(빨간 굵은 선)
//   5 갈 수 있는 길: 띄엄띄엄 점선, 마우스를 올린 길은 빨강
//   6 노드 기호: 외곽선과 명암. 들른 곳은 초록 체크
//   7 지금 위치: 금색 고리 + 퍼지는 고리 + 빨간 핀. 이동 중에는 펜촉
// 지도 기억은 persistentDataPath/ink_memory_<맵 id>.txt 에 저장한다.
using System.Collections.Generic;
using System.IO;
using CardGame.Core.Map;
using UnityEngine;

namespace CardGame.View.Map
{
    public class InkMapView : MonoBehaviour
    {
        [Header("보기")]
        [Tooltip("텍스처 픽셀 몇 개가 월드 1단위인지")]
        public float pixelsPerUnit = 32f;
        [Tooltip("이름 글씨체 (예: 손글씨 폰트). 비우면 기본 글씨")]
        public Font labelFont;

        [Header("밝히는 넓이 (지형 칸 수)")]
        public float nodeRadius = 9f;
        public float pathRadius = 4f;

        [Header("캐릭터 둘레 밝기")]
        [Tooltip("지도 전체를 어둡게 하는 정도 (0~1)")]
        [Range(0f, 1f)] public float dimAmount = 0.22f;
        [Tooltip("밝히는 반지름 (픽셀)")]
        public float lightRadius = 110f;

        [Header("연출")]
        [Tooltip("길 하나를 그리는 최소·최대 시간 (초)")]
        public float minDrawTime = 0.3f;
        public float maxDrawTime = 0.9f;
        [Tooltip("쓰러지면 이번 탐험에 그린 잉크를 잃을지 (끄면 본 곳은 연필로 남는다)")]
        public bool loseInkOnDeath;

        // 컨트롤러가 매 프레임 채운다
        [System.NonSerialized] public List<MapNode> candidates = new List<MapNode>();
        [System.NonSerialized] public MapNode hover;

        MapData source;        // 랜덤 칸이 '?' 그대로인 원본
        Exploration ex;
        InkPaper paper;
        InkFog fog;
        InkMemory memory;
        Texture2D tex;
        SpriteRenderer sr;
        Camera cam;
        byte[] work;
        Color32[] pixels;
        GUIStyle labelStyle;
        System.Func<int, int, bool> visible;
        readonly System.Random rng = new System.Random();

        int W, H, T;
        readonly HashSet<string> visited = new HashSet<string>();
        readonly HashSet<string> glimpsed = new HashSet<string>();   // 다음 칸으로 종류를 본 랜덤 칸 (이번 탐험)
        readonly List<KeyValuePair<MapLink, string>> runLines = new List<KeyValuePair<MapLink, string>>();   // 지나간 길, 출발 노드 키
        readonly Dictionary<string, string> runLineKeys = new Dictionary<string, string>();
        readonly List<int[]> runCircles = new List<int[]>();
        readonly List<float[]> stains = new List<float[]>();   // x, y, 모양, 진하기 (도착할 때 튄 잉크 얼룩)
        readonly Dictionary<string, MapLink> linkByKey = new Dictionary<string, MapLink>();

        // 이동 그리기
        MapLink moveLink;
        bool moveReverse;
        float moveT, moveDur;
        float lastCx, lastCy;
        // 도착해서 둘레가 번져 나가는 중
        bool growing;
        float growX, growY, growR, growT;
        const float GrowTime = 0.25f;   // 도착한 노드 둘레가 번지는 시간

        public bool Moving { get { return moveLink != null; } }

        // 이번 탐험의 길. 원본 맵에 길이 없으면 탐험(Exploration)이 시작할 때 자동으로 이어 둔 길을 쓴다
        List<MapLink> Links { get { return ex != null ? ex.Map.links : source.links; } }

        static string KeyOf(int x, int y) { return x + "," + y; }

        // 길 이름표. 이 PC의 MapLink 에는 Key 가 없어서 양 끝 좌표로 만든다 (방향과 상관없이 같은 값)
        // TODO 지름길: 맵 코어 반영 후 복구 (MapLink.Key 로 바꾸기)
        static string LinkKey(MapLink l)
        {
            string a = KeyOf(l.ax, l.ay), b = KeyOf(l.bx, l.by);
            return string.CompareOrdinal(a, b) <= 0 ? a + "-" + b : b + "-" + a;
        }

        // ───────── 준비 ─────────

        public void Init(MapData sourceMap, Camera camera)
        {
            source = sourceMap;
            cam = camera;
            paper = InkMapPainter.Paint(source);
            fog = new InkFog(paper);
            W = paper.Width; H = paper.Height; T = paper.TilePx;
            work = new byte[W * H * 4];
            pixels = new Color32[W * H];
            memory = LoadMemory();
            visible = (x, y) => fog.StateAt(x, y) != 0;

            tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var go = new GameObject("Ink Map");
            go.transform.SetParent(transform, false);
            sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), pixelsPerUnit);

            // 길 목록은 BeginRun 에서 만든다 (길이 없는 맵은 탐험이 시작될 때 자동으로 이어지므로)
        }

        // 지도 전체가 화면에 들어오는 카메라 크기
        public float FitSize(float aspect)
        {
            float h = H / pixelsPerUnit / 2f, w = W / pixelsPerUnit / 2f / Mathf.Max(0.1f, aspect);
            return Mathf.Max(h, w) * 1.04f;
        }

        [Tooltip("처음 볼 때 화면 세로에 들어오는 지형 칸 수 (큰 맵은 이만큼 확대해서 시작)")]
        public int startTilesTall = 30;

        // 처음 카메라 크기: 작은 맵은 전체, 큰 맵은 캐릭터 둘레 startTilesTall 칸만
        public float StartSize(float aspect)
        {
            return Mathf.Min(FitSize(aspect), startTilesTall * T / pixelsPerUnit / 2f);
        }

        public void BeginRun(Exploration exploration)
        {
            ex = exploration;
            visited.Clear();
            glimpsed.Clear();
            runLines.Clear();
            runLineKeys.Clear();
            runCircles.Clear();
            stains.Clear();
            pendingReveals.Clear();
            moveLink = null;
            linkByKey.Clear();
            foreach (var l in Links) linkByKey[LinkKey(l)] = l;
            fog.ClearRun();
            fog.LoadMemory(memory);
            var n = ex.CurrentNode;
            if (n != null)
            {
                visited.Add(KeyOf(n.x, n.y));
                StartGrow(paper.PixelX(n.x), paper.PixelY(n.y));
                RevealNext(n);
            }
        }

        [Header("다음 루트 보이기")]
        [Tooltip("도착하면 갈 수 있는 이웃 노드 둘레를 이만큼(지형 칸 수) 밝힌다")]
        public float nextNodeRadius = 5f;
        [Tooltip("이웃 노드까지 가는 길을 이 폭(지형 칸 수)으로 밝힌다")]
        public float nextPathWidth = 3f;

        [Tooltip("도착하는 순간부터 길을 따라 다음 노드 둘레까지 밝혀지는 시간 (초)")]
        public float nextRevealTime = 0.22f;
        const float PieceGrowTime = 0.08f;   // 길 위 작은 원 하나가 번지는 시간

        // 아직 번지지 않은 원: x, y, 반지름, 번지기 시작할 시각
        readonly List<float[]> pendingReveals = new List<float[]>();

        // 지금 노드에서 갈 수 있는 이웃 노드와 거기까지 가는 길을 잉크로 밝힌다 (다음 이동 후보가 항상 끝까지 보이게)
        // 도착하는 순간 내 둘레와 함께 길을 따라 다음 노드 쪽으로 아주 짧게(약 0.2초) 번져 나간다
        void RevealNext(MapNode from)
        {
            if (from == null) return;
            float pr = nextPathWidth * T, step = Mathf.Max(6f, pr * 0.7f);
            float t0 = Time.time;   // 도착 둘레와 동시에 시작 (기다리지 않는다)
            foreach (var nb in ex.Map.Neighbors(from))
            {
                MapLink link = null;
                foreach (var l in Links) if (l.Same(from.x, from.y, nb.x, nb.y)) { link = l; break; }
                if (link != null)
                {
                    var s = StrokeOf(link);
                    bool rev = !(link.ax == from.x && link.ay == from.y);   // 내 쪽에서 시작하게
                    int pieces = Mathf.Max(1, Mathf.CeilToInt(s.Length / step));
                    for (int i = 1; i < pieces; i++)
                    {
                        float f = i / (float)pieces, x, y;
                        s.At(rev ? 1f - f : f, out x, out y);
                        pendingReveals.Add(new[] { x, y, pr, t0 + f * nextRevealTime * 0.8f });
                    }
                }
                pendingReveals.Add(new[] { (float)paper.PixelX(nb.x), paper.PixelY(nb.y), nextNodeRadius * T, t0 + nextRevealTime * 0.8f });
            }
        }

        // 때가 된 원을 조금씩 번지게 하고, 다 번진 원은 기억 목록으로 넘긴다
        void GrowPending()
        {
            float now = Time.time;
            for (int i = pendingReveals.Count - 1; i >= 0; i--)
            {
                var c = pendingReveals[i];
                if (now < c[3]) continue;
                float p = Mathf.Clamp01((now - c[3]) / PieceGrowTime), e = 1f - (1f - p) * (1f - p);
                fog.RevealRun(c[0], c[1], c[2] * e);
                if (p >= 1f) { runCircles.Add(new[] { Mathf.RoundToInt(c[0]), Mathf.RoundToInt(c[1]), Mathf.RoundToInt(c[2]) }); pendingReveals.RemoveAt(i); }
            }
        }

        // 다음 루트가 아직 번지는 중인지 (카메라가 같은 속도로 물러날 때 쓴다)
        public bool Revealing { get { return growing || pendingReveals.Count > 0; } }

        // 카메라가 이웃 노드까지 다 담으려면 필요한 크기 (지금 노드 기준, 월드 단위). 이웃이 없으면 0
        public float SizeToShowNext(float aspect)
        {
            var cur = ex != null ? ex.CurrentNode : null;
            if (cur == null) return 0f;
            Vector3 c = NodeWorld(cur.x, cur.y);
            float need = 0f, margin = (nextNodeRadius + 1f) * T / pixelsPerUnit;
            foreach (var nb in ex.Map.Neighbors(cur))
            {
                Vector3 p = NodeWorld(nb.x, nb.y);
                need = Mathf.Max(need, Mathf.Abs(p.y - c.y) + margin, (Mathf.Abs(p.x - c.x) + margin) / Mathf.Max(0.1f, aspect));
            }
            return need;
        }

        // 길을 따라 펜으로 그리기 시작한다. 규칙상 이동(ex.Move)은 이미 끝난 상태
        public void StartMove(MapNode from, MapNode to)
        {
            if (from == null || to == null) return;
            MapLink link = null;
            foreach (var l in Links)
                if (l.Same(from.x, from.y, to.x, to.y)) { link = l; break; }
            if (link == null) { Arrive(to); return; }
            moveLink = link;
            moveReverse = !(link.ax == from.x && link.ay == from.y);
            moveT = 0f;
            float len = Mathf.Sqrt((from.x - to.x) * (from.x - to.x) + (from.y - to.y) * (from.y - to.y));
            moveDur = Mathf.Clamp(0.3f + len * 0.014f, minDrawTime, maxDrawTime);
            lastCx = paper.PixelX(from.x);
            lastCy = paper.PixelY(from.y);
        }

        void Arrive(MapNode to)
        {
            string k = KeyOf(to.x, to.y);
            visited.Add(k);
            float x = paper.PixelX(to.x), y = paper.PixelY(to.y);
            // 펜을 떼며 튄 잉크 얼룩 1~3개
            int c = 1 + rng.Next(3);
            for (int i = 0; i < c; i++)
            {
                double an = rng.NextDouble() * 6.28, d = 16 + rng.NextDouble() * 12;
                stains.Add(new[] { (float)System.Math.Round(x + System.Math.Cos(an) * d), (float)System.Math.Round(y + System.Math.Sin(an) * d), rng.Next(4), 0.3f + (float)rng.NextDouble() * 0.3f });
            }
            StartGrow(x, y);
            RevealNext(to);
        }

        void StartGrow(float x, float y)
        {
            growing = true;
            growX = x; growY = y;
            growR = nodeRadius * T;
            growT = 0f;
        }

        void AddCircle(float x, float y, float r)
        {
            runCircles.Add(new[] { Mathf.RoundToInt(x), Mathf.RoundToInt(y), Mathf.RoundToInt(r) });
            fog.RevealRun(x, y, r);
        }

        // 탐험이 끝났을 때: 이번 잉크를 다음 탐험의 연필 기억으로 넘긴다
        public void EndRun(ExplorationEnd end)
        {
            if (end == ExplorationEnd.Dead && loseInkOnDeath) return;
            var lines = new Dictionary<string, string>();
            foreach (var kv in runLines) lines[LinkKey(kv.Key)] = kv.Value;
            var known = new Dictionary<string, string>();
            foreach (var n in ex.Map.nodes)
            {
                string k = KeyOf(n.x, n.y);
                var s = source.NodeAt(n.x, n.y);
                if (visited.Contains(k) && s != null && s.type == "random") known[k] = n.type;
            }
            memory.AddRun(runCircles, lines, known);
            SaveMemory();
        }

        public void ClearMemory()
        {
            memory = new InkMemory();
            try { if (File.Exists(MemoryPath)) File.Delete(MemoryPath); }
            catch (System.Exception e) { Debug.LogWarning(e.Message); }
        }

        // ───────── 좌표 ─────────

        // 카메라 가운데를 지도 안쪽으로 붙잡는다 (지도 밖 검은 곳이 보이지 않게). 지도보다 화면이 넓으면 가운데
        public Vector3 ClampToMap(Vector3 p, float camSize, float aspect)
        {
            float hw = W / pixelsPerUnit / 2f, hh = H / pixelsPerUnit / 2f, vw = camSize * aspect, vh = camSize;
            var c = transform.position;
            p.x = vw >= hw ? c.x : Mathf.Clamp(p.x, c.x - hw + vw, c.x + hw - vw);
            p.y = vh >= hh ? c.y : Mathf.Clamp(p.y, c.y - hh + vh, c.y + hh - vh);
            return p;
        }

        // 노드 (x, y) 가 찍힌 월드 위치 (컨트롤러의 방향키 이동에 쓴다)
        public Vector3 NodeWorld(int x, int y) { return PixelToWorld(paper.PixelX(x), paper.PixelY(y)); }

        public Vector3 PixelToWorld(float px, float py)
        {
            return transform.position + new Vector3((px - W / 2f) / pixelsPerUnit, (H / 2f - py) / pixelsPerUnit, 0);
        }

        public void WorldToPixel(Vector3 w, out float px, out float py)
        {
            var d = w - transform.position;
            px = d.x * pixelsPerUnit + W / 2f;
            py = H / 2f - d.y * pixelsPerUnit;
        }

        // 지금 그리는 펜촉, 아니면 지금 노드 (카메라가 따라간다)
        public Vector3 FocusWorld
        {
            get
            {
                float x, y;
                FocusPixel(out x, out y);
                return PixelToWorld(x, y);
            }
        }

        void FocusPixel(out float x, out float y)
        {
            if (moveLink != null) { InkCanvas.Tip(StrokeOf(moveLink), moveReverse, moveT, out x, out y); return; }
            x = paper.PixelX(ex.X); y = paper.PixelY(ex.Y);
        }

        // 화면 위치에 가장 가까운 노드 (가까운 게 없으면 null)
        public MapNode NodeNear(Vector3 world, float radiusPx = 20f)
        {
            float px, py;
            WorldToPixel(world, out px, out py);
            MapNode best = null;
            float bd = radiusPx * radiusPx;
            foreach (var n in ex.Map.nodes)
            {
                float dx = paper.PixelX(n.x) - px, dy = paper.PixelY(n.y) - py, d = dx * dx + dy * dy;
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        InkCanvas.Stroke StrokeOf(MapLink l)
        {
            return new InkCanvas.Stroke(paper.PixelX(l.ax), paper.PixelY(l.ay), paper.PixelX(l.bx), paper.PixelY(l.by), InkCanvas.SeedOf(l));
        }

        bool Seen(int x, int y) { return fog.StateAt(paper.PixelX(x), paper.PixelY(y)) != 0; }

        // ───────── 매 프레임 ─────────

        void LateUpdate()
        {
            if (ex == null) return;
            float dt = Time.deltaTime;

            if (moveLink != null)
            {
                moveT = Mathf.Clamp01(moveT + dt / Mathf.Max(0.05f, moveDur));
                float x, y;
                InkCanvas.Tip(StrokeOf(moveLink), moveReverse, moveT, out x, out y);
                float dx = x - lastCx, dy = y - lastCy;
                if (dx * dx + dy * dy >= 144f) { AddCircle(x, y, pathRadius * T); lastCx = x; lastCy = y; }
                if (moveT >= 1f)
                {
                    string from = moveReverse ? KeyOf(moveLink.bx, moveLink.by) : KeyOf(moveLink.ax, moveLink.ay);
                    if (!runLineKeys.ContainsKey(LinkKey(moveLink))) { runLineKeys[LinkKey(moveLink)] = from; runLines.Add(new KeyValuePair<MapLink, string>(moveLink, from)); }
                    var to = ex.CurrentNode;
                    moveLink = null;
                    if (to != null) Arrive(to);
                }
            }
            if (growing)
            {
                growT += dt / GrowTime;
                float p = Mathf.Clamp01(growT), e = 1f - (1f - p) * (1f - p) * (1f - p);
                fog.RevealRun(growX, growY, growR * e);
                if (p >= 1f) { runCircles.Add(new[] { Mathf.RoundToInt(growX), Mathf.RoundToInt(growY), Mathf.RoundToInt(growR) }); growing = false; }
            }
            GrowPending();

            Draw();
        }

        void Draw()
        {
            float fx, fy;
            FocusPixel(out fx, out fy);
            CopyLit(fx, fy);

            // 지난 탐험 길 (연필)
            foreach (var kv in memory.Lines)
            {
                MapLink l;
                if (runLineKeys.ContainsKey(kv.Key) || !linkByKey.TryGetValue(kv.Key, out l)) continue;
                bool rev = kv.Value != KeyOf(l.ax, l.ay);
                InkCanvas.Line(work, W, H, StrokeOf(l), rev, 1f, Ink.Graphite, 0.7f, InkCanvas.LineStyle.Ink, visible);
            }

            // 알려진 길: 한쪽 끝이라도 지도에 그려진 일반 길. 가늘고 옅게
            foreach (var l in Links)
            {
                if (l.IsGated || runLineKeys.ContainsKey(LinkKey(l))) continue;
                bool sa = Seen(l.ax, l.ay), sb = Seen(l.bx, l.by);
                if (!sa && !sb) continue;
                InkCanvas.Line(work, W, H, StrokeOf(l), false, 1f, Ink.BR[2], sa && sb ? 0.6f : 0.4f, InkCanvas.LineStyle.Thin, visible);
            }

            // 지름길: 양 끝이 보이면 빨간 점선과 X. 여는 규칙이 아직 없어서 지금은 항상 막혀 있다
            // TODO 지름길: 맵 코어(LinkGate) 반영 후 열린 길은 갈색 점선으로
            foreach (var l in Links)
            {
                if (!l.IsGated) continue;
                if (!Seen(l.ax, l.ay) || !Seen(l.bx, l.by)) continue;
                var s = StrokeOf(l);
                InkCanvas.Line(work, W, H, s, false, 1f, Ink.Red, 0.75f, InkCanvas.LineStyle.Dashed, visible);
                float mx, my;
                s.At(0.5f, out mx, out my);
                InkCanvas.Mark(work, W, H, InkCanvas.Cross, mx, my, Ink.Red, true);
            }

            // 잉크 얼룩
            foreach (var st in stains)
                InkCanvas.Mark(work, W, H, InkCanvas.Blots[(int)st[2] % InkCanvas.Blots.Length], st[0], st[1], Ink.Sepia, false, st[3]);

            // 이번 탐험에 지나온 길 (빨간 굵은 선)과 지금 그리는 길
            foreach (var kv in runLines)
                InkCanvas.Line(work, W, H, StrokeOf(kv.Key), kv.Value != KeyOf(kv.Key.ax, kv.Key.ay), 1f, Ink.Route, 1f, InkCanvas.LineStyle.Route, visible);
            if (moveLink != null) InkCanvas.Line(work, W, H, StrokeOf(moveLink), moveReverse, moveT, Ink.Route, 1f, InkCanvas.LineStyle.Route, visible);

            // 지금 갈 수 있는 길: 띄엄띄엄 점선. 마우스를 올린 길은 빨강, 나머지는 깜빡
            float blink = (Mathf.Sin(Time.time * 1000f / 260f) + 1f) / 2f;
            var cur = ex.CurrentNode;
            var curNbs = cur != null ? ex.Map.Neighbors(cur) : null;   // 바로 갈 수 있는 다음 칸 (랜덤 칸도 종류를 보여 준다)
            if (moveLink == null && candidates != null && cur != null)
                foreach (var n in candidates)
                {
                    MapLink l = null;
                    foreach (var q in Links) if (q.Same(cur.x, cur.y, n.x, n.y)) { l = q; break; }
                    if (l == null || runLineKeys.ContainsKey(LinkKey(l))) continue;
                    bool rev = !(l.ax == cur.x && l.ay == cur.y);
                    bool h = n == hover;
                    InkCanvas.Line(work, W, H, StrokeOf(l), rev, 1f, h ? Ink.Route : Ink.BR[0], h ? 1f : 0.85f + blink * 0.15f, InkCanvas.LineStyle.Dotted, visible);
                }
            if (moveLink == null && hover != null && candidates != null && candidates.Contains(hover))
                InkCanvas.Ring(work, W, H, paper.PixelX(hover.x), paper.PixelY(hover.y), 8, 22, Ink.Red, 1f);

            // 노드 기호
            var pencilOutline = new InkColor(96, 92, 86);
            foreach (var n in ex.Map.nodes)
            {
                int px = paper.PixelX(n.x), py = paper.PixelY(n.y);
                int st = fog.StateAt(px, py);
                bool isCand = candidates != null && candidates.Contains(n);
                if (st == 0)
                {
                    if (isCand) Ink.Fill(work, W, H, px - 1, py - 1, 3, 3, Ink.Sepia, 0.6f);
                    continue;
                }
                string k = KeyOf(n.x, n.y);
                var s = source.NodeAt(n.x, n.y);
                bool random = s != null && s.type == "random";
                bool wasHere = visited.Contains(k);
                bool isCur = cur != null && cur.x == n.x && cur.y == n.y;
                string type;
                if (wasHere) type = n.type;
                else if (!random) type = n.type;
                else if ((curNbs != null && curNbs.Contains(n)) || glimpsed.Contains(k)) { type = n.type; glimpsed.Add(k); }   // 다음 칸은 랜덤이어도 종류를 보여 주고, 이번 탐험 동안 기억한다
                else if (st == 1) { string kt; type = memory.Known.TryGetValue(k, out kt) ? kt : null; }
                else type = null;

                InkColor c = type == null ? Ink.PaperTones[1] : InkCanvas.MarkColor(type);
                if (st == 1 && !wasHere) c = new InkColor(150, 146, 136);
                bool faded = (wasHere && !isCur && random && type != "elite" && type != "big_dungeon")
                             || ex.IsCleared(n) || (n.type == "exit" && st == 2 && !ex.IsExitOpen(n));
                if (faded) c = InkColor.Mix(c, Ink.Paper, 0.5f);
                InkCanvas.Mark(work, W, H, InkCanvas.MarkOf(type), px, py, c, true, st == 1 && !wasHere ? pencilOutline : Ink.Out);
                // 체크는 다 쓴 노드에만 (지나가기·돌아가기로 남겨 둔 노드는 다시 쓸 수 있어서 체크하지 않는다)
                if (ex.IsCleared(n) && !isCur) InkCanvas.Mark(work, W, H, InkCanvas.Check, px + 8, py + 6, Ink.G[4], true);
            }

            // 지금 위치: 금색 고리 + 핀, 이동 중에는 펜촉
            float phase = (Time.time % 1.4f) / 1.4f;
            InkCanvas.GoldRings(work, W, H, fx, fy, phase);
            if (moveLink == null) InkCanvas.DrawPin(work, W, H, fx, fy, Mathf.RoundToInt(Mathf.Sin(Time.time * 1000f / 300f) * 1.5f));
            else InkCanvas.Mark(work, W, H, InkCanvas.Nib, fx + 3, fy - 3, new InkColor(30, 22, 14), false);

            Upload();
        }

        // 안개 결과를 작업 버퍼로 옮기며 지도 전체를 살짝 어둡게 하고, (fx, fy) 둘레만 따뜻한 빛으로 밝힌다 (screen 섞기)
        void CopyLit(float fx, float fy)
        {
            var src = fog.Composite;
            float k = 1f - dimAmount;
            float dr = 40 * dimAmount, dg = 30 * dimAmount, db = 30 * dimAmount;
            for (int i = 0; i < src.Length; i += 4)
            {
                work[i] = (byte)(src[i] * k + dr);
                work[i + 1] = (byte)(src[i + 1] * k + dg);
                work[i + 2] = (byte)(src[i + 2] * k + db);
                work[i + 3] = 255;
            }
            float R = lightRadius;
            int x0 = Mathf.Max(0, (int)(fx - R)), x1 = Mathf.Min(W - 1, (int)(fx + R));
            int y0 = Mathf.Max(0, (int)(fy - R)), y1 = Mathf.Min(H - 1, (int)(fy + R));
            const float lr = 1f, lg = 236 / 255f, lb = 190 / 255f;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x - fx) * (x - fx) + (y - fy) * (y - fy)) / R;
                    if (d >= 1f) continue;
                    float a = d < 0.5f ? Mathf.Lerp(0.55f, 0.25f, d / 0.5f) : Mathf.Lerp(0.25f, 0f, (d - 0.5f) / 0.5f);
                    int o = (y * W + x) * 4;
                    work[o] = ScreenMix(work[o], lr * a);
                    work[o + 1] = ScreenMix(work[o + 1], lg * a);
                    work[o + 2] = ScreenMix(work[o + 2], lb * a);
                }
        }

        static byte ScreenMix(byte c, float l) { float v = c / 255f; return (byte)(255f * (1f - (1f - v) * (1f - l))); }

        // 텍스처로 옮긴다. 버퍼는 위쪽 줄부터, 텍스처는 아래쪽 줄부터라 뒤집는다
        void Upload()
        {
            for (int y = 0; y < H; y++)
            {
                int dst = (H - 1 - y) * W, o = y * W * 4;
                for (int x = 0; x < W; x++, o += 4)
                    pixels[dst + x] = new Color32(work[o], work[o + 1], work[o + 2], 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
        }

        // ───────── 이름 글씨 ─────────

        void OnGUI()
        {
            if (ex == null || cam == null) return;
            GUI.depth = 10;   // 컨트롤러의 창보다 뒤에
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, clipping = TextClipping.Overflow };
                if (labelFont != null) labelStyle.font = labelFont;
            }
            float screenPerPixel = Screen.height / (2f * cam.orthographicSize) / pixelsPerUnit;
            labelStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(screenPerPixel * 5f), 9, 18);

            foreach (var n in source.nodes)
            {
                if (string.IsNullOrEmpty(n.name)) continue;
                int st = fog.StateAt(paper.PixelX(n.x), paper.PixelY(n.y));
                if (st == 0) continue;
                Vector3 sp = cam.WorldToScreenPoint(PixelToWorld(paper.PixelX(n.x), paper.PixelY(n.y) + 7));
                labelStyle.normal.textColor = st == 2 ? new Color(0.23f, 0.16f, 0.1f) : new Color(0.52f, 0.49f, 0.45f);
                GUI.Label(new Rect(sp.x - 100, Screen.height - sp.y, 200, 40), n.name, labelStyle);
            }
            // 막힌 지름길 위의 글씨
            foreach (var l in Links)
            {
                if (!l.IsGated) continue;
                if (!Seen(l.ax, l.ay) || !Seen(l.bx, l.by)) continue;
                float mx, my;
                StrokeOf(l).At(0.5f, out mx, out my);
                Vector3 sp = cam.WorldToScreenPoint(PixelToWorld(mx, my + 6));
                labelStyle.normal.textColor = new Color(0.54f, 0.16f, 0.12f);
                GUI.Label(new Rect(sp.x - 120, Screen.height - sp.y, 240, 40), ObstacleName(l.obstacle) + " (지금은 지나갈 수 없다)", labelStyle);
            }
        }

        // 장애물 이름. TODO 지름길: 맵 코어의 장애물 목록(MapCatalog.Obstacle)이 들어오면 그걸 쓴다
        static string ObstacleName(string id)
        {
            switch (id)
            {
                case "bridge": return "무너진 다리";
                case "rockfall": return "무너진 바위";
                case "gate": return "잠긴 문";
                default: return "막힌 길";
            }
        }

        // ───────── 기억 파일 ─────────

        string MemoryPath { get { return Path.Combine(Application.persistentDataPath, "ink_memory_" + source.id + ".txt"); } }

        InkMemory LoadMemory()
        {
            try { if (File.Exists(MemoryPath)) return InkMemory.Load(File.ReadAllText(MemoryPath)); }
            catch (System.Exception e) { Debug.LogWarning("잉크 지도 기억을 읽지 못했어요: " + e.Message); }
            return new InkMemory();
        }

        void SaveMemory()
        {
            try { File.WriteAllText(MemoryPath, memory.Save()); }
            catch (System.Exception e) { Debug.LogWarning("잉크 지도 기억을 저장하지 못했어요: " + e.Message); }
        }
    }
}
