// 바깥 맵 탐험 한 번의 규칙과 상태. 화면과 상관없는 순수 C# 이다.
// 보드게임처럼 길(MapData.links)로 이어진 이웃 노드로 한 번에 하나씩 옮겨 간다. 타일은 배경과 전투 칸 수에만 쓴다.
// 맵에 길이 하나도 없으면 MapLinks.AutoLink 로 자동으로 잇는다.
// 확률은 쓰지 않고, 탐험을 시작할 때 섞기(셔플)만 무작위다.
//
// 임시 수치는 ExplorationConfig 에 모아 두었다. 규칙이 정해지면 여기만 고친다.
using System;
using System.Collections.Generic;

namespace CardGame.Core.Map
{
    [Serializable]
    public class ExplorationConfig
    {
        public int visionHops = 1;        // 시야: 길로 몇 번 너머 노드까지 종류가 보이는지 (그 다음 한 겹은 '?' 로 보임)
        public int visionRadius = 2;      // 보이는 노드 둘레 몇 칸의 지형을 밝힐지 (지금 있는 노드 기준, 이웃 노드는 1칸 덜)
        public int maxHp = 50;
        public int maxWeight = 100;       // 인간(보통)
        public int respawnEvery = 3;      // 몇 번 이동할 때마다 지나온 전투 노드 1곳에 적이 다시 생기는지
        public int openExits = 2;         // 탐험마다 열리는 탈출 지점 수
        public float restHealRate = 0.3f; // 상인 야영지 휴식 회복량 (최대 체력 비율)
        public int maxTotems = 3;

        // 임시 전투 결과: 난이도별 (쉬움, 보통, 어려움)
        public int[] battleDamage = { 5, 10, 15 };
        public int[] battleMoney = { 5, 10, 20 };
        public int dropWeaponWeight = 5;  // 이기면 떨어뜨리는 무기 1개 (중형 무기 무게)
        public int farmMaterials = 3;     // 파밍 지역에서 줍는 재료 수 (무게 1씩)
        public int farmMoney = 5;
    }

    public enum CellView { Unknown, Remembered, Hint, Visible }
    public enum ExplorationEnd { None, Escaped, Dead }

    [Serializable]
    public class BagItem
    {
        public string name;
        public string kind;   // weapon, material
        public int weight;
        public BagItem(string name, string kind, int weight) { this.name = name; this.kind = kind; this.weight = weight; }
    }

    // 한 번 본 칸은 탐험이 끝나도 지도에 남는다. 화면 쪽에서 파일로 저장한다.
    public class MapMemory
    {
        public readonly int Width, Height;
        readonly bool[] seen;

        public MapMemory(int width, int height)
        {
            Width = width; Height = height;
            seen = new bool[width * height];
        }

        public bool IsSeen(int x, int y) { return x >= 0 && y >= 0 && x < Width && y < Height && seen[y * Width + x]; }
        public void See(int x, int y) { if (x >= 0 && y >= 0 && x < Width && y < Height) seen[y * Width + x] = true; }

        public string Save()
        {
            var c = new char[seen.Length];
            for (int i = 0; i < seen.Length; i++) c[i] = seen[i] ? '1' : '0';
            return new string(c);
        }

        public static MapMemory Load(int width, int height, string data)
        {
            var m = new MapMemory(width, height);
            if (data != null && data.Length == width * height)
                for (int i = 0; i < data.Length; i++) m.seen[i] = data[i] == '1';
            return m;
        }
    }

    public class Exploration
    {
        public readonly MapData Map;
        public readonly ExplorationConfig Config;
        public readonly MapMemory Memory;

        public int X { get; private set; }
        public int Y { get; private set; }
        public int Steps { get; private set; }     // 이동 횟수
        public int Hp { get; private set; }
        public int Money { get; private set; }
        public int Totems { get; private set; }
        public ExplorationEnd End { get; private set; }
        public readonly List<BagItem> Bag = new List<BagItem>();
        public readonly List<string> Log = new List<string>();

        readonly Dictionary<MapNode, int> depth;   // 시작에서 길로 몇 번 이동
        readonly int maxDepth;
        Dictionary<MapNode, int> hopsHere = new Dictionary<MapNode, int>();   // 지금 노드에서 길로 몇 번 (시야 + 1 까지만)
        readonly HashSet<int> visibleCells = new HashSet<int>();
        readonly HashSet<int> hintCells = new HashSet<int>();
        readonly HashSet<MapNode> cleared = new HashSet<MapNode>();
        readonly List<MapNode> clearOrder = new List<MapNode>();
        readonly HashSet<MapNode> openExits = new HashSet<MapNode>();
        readonly Dictionary<MapNode, string> enemyOf = new Dictionary<MapNode, string>();
        bool restUsed;

        public Exploration(MapData map, ExplorationConfig config, MapMemory memory, int seed)
        {
            Map = map;
            Config = config ?? new ExplorationConfig();
            Memory = memory != null && memory.Width == map.width && memory.Height == map.height
                ? memory : new MapMemory(map.width, map.height);
            Hp = Config.maxHp;

            MapNode start = null;
            foreach (var n in map.nodes) if (n.type == "start") start = n;
            if (start == null) throw new InvalidOperationException("맵에 시작 지점이 없어요.");
            X = start.x; Y = start.y;

            if (map.links == null) map.links = new List<MapLink>();
            if (map.links.Count == 0) MapLinks.AutoLink(map);
            depth = MapLinks.Hops(map, start);
            maxDepth = 1;
            foreach (var d in depth.Values) if (d > maxDepth) maxDepth = d;

            var rng = new Random(seed);

            // 탈출 후보 중 몇 곳을 연다
            var exits = map.nodes.FindAll(n => n.type == "exit");
            Shuffle(exits, rng);
            for (int i = 0; i < exits.Count && i < Config.openExits; i++) openExits.Add(exits[i]);

            // 전투 노드마다 이번 탐험에 나올 적 계열을 정한다
            foreach (var n in map.nodes)
            {
                var def = MapCatalog.NodeType(n.type);
                if (def == null || !def.HasBattle) continue;
                var pool = MapData.SplitList(n.enemies);
                if (pool.Count == 0) pool = MapData.SplitList(map.enemies);
                if (pool.Count == 0) pool.Add(MapCatalog.EnemyFamilies[0].Id);
                enemyOf[n] = pool[rng.Next(pool.Count)];
            }

            // 시작 지점은 처음부터 지나간 곳
            cleared.Add(start);
            Reveal();
            AddLog(map.name + " 탐험을 시작했어요.");
        }

        // ───────── 보기 ─────────

        // 지형 칸이 얼마나 보이는지
        public CellView ViewOf(int x, int y)
        {
            int k = y * Map.width + x;
            if (visibleCells.Contains(k)) return CellView.Visible;
            if (hintCells.Contains(k)) return CellView.Hint;
            return Memory.IsSeen(x, y) ? CellView.Remembered : CellView.Unknown;
        }

        // 노드가 얼마나 보이는지: 시야 안이면 종류까지, 한 겹 바깥은 '?', 본 적 있으면 기억
        public CellView NodeView(MapNode n)
        {
            int h;
            if (hopsHere.TryGetValue(n, out h))
            {
                if (h <= Config.visionHops) return CellView.Visible;
                return Memory.IsSeen(n.x, n.y) ? CellView.Remembered : CellView.Hint;
            }
            return Memory.IsSeen(n.x, n.y) ? CellView.Remembered : CellView.Unknown;
        }

        // 길이 얼마나 보이는지 (양 끝 노드를 보고 정한다)
        public CellView LinkView(MapLink l)
        {
            var a = Map.NodeAt(l.ax, l.ay);
            var b = Map.NodeAt(l.bx, l.by);
            if (a == null || b == null) return CellView.Unknown;
            var va = NodeView(a);
            var vb = NodeView(b);
            if (va == CellView.Visible && vb == CellView.Visible) return CellView.Visible;
            if (va == CellView.Visible || vb == CellView.Visible) return CellView.Hint;
            if (Known(va) && Known(vb)) return CellView.Remembered;
            return CellView.Unknown;
        }

        static bool Known(CellView v) { return v == CellView.Visible || v == CellView.Remembered; }

        // 그 노드의 종류를 지도에 보여 줄 수 있는지 (시야 안이거나 본 적 있는 노드)
        public bool NodeKnown(MapNode n)
        {
            return Known(NodeView(n));
        }

        // 지금 노드에서 바로 갈 수 있는 이웃 노드
        public List<MapNode> Neighbors { get { var c = CurrentNode; return c != null ? Map.Neighbors(c) : new List<MapNode>(); } }

        public MapNode CurrentNode { get { return Map.NodeAt(X, Y); } }
        public bool IsCleared(MapNode n) { return cleared.Contains(n); }
        public bool IsExitOpen(MapNode n) { return openExits.Contains(n); }
        public string EnemyOf(MapNode n) { string e; return enemyOf.TryGetValue(n, out e) ? e : ""; }

        public int Weight
        {
            get { int w = 0; foreach (var b in Bag) w += b.weight; return w; }
        }

        // 무게 초과로 막히는 전투 행동 슬롯 수 (0~2), 125% 초과면 3 = 이동 불가
        public int BlockedSlots
        {
            get
            {
                float r = (float)Weight / Config.maxWeight;
                if (r > 1.25f) return 3;
                if (r > 1.10f) return 2;
                if (r > 1.00f) return 1;
                return 0;
            }
        }

        // 0 쉬움, 1 보통, 2 어려움
        public int DifficultyOf(MapNode n)
        {
            int idx = Array.IndexOf(MapCatalog.Difficulties, n.difficulty);
            if (idx > 0) return idx - 1;
            int d;
            if (!depth.TryGetValue(n, out d)) d = 0;
            int level = Map.baseTier - 1 + (d * 2 > maxDepth ? 1 : 0);
            if (n.type == "elite") level++;
            return Math.Max(0, Math.Min(2, level));
        }

        // ───────── 이동 ─────────

        // 갈 수 없으면 이유를, 갈 수 있으면 null 을 준다
        public string CannotMove(MapNode target)
        {
            if (End != ExplorationEnd.None) return "탐험이 끝났어요.";
            if (target == null) return "노드로만 옮겨 갈 수 있어요.";
            if (!Neighbors.Contains(target)) return "길로 이어진 노드로만 갈 수 있어요.";
            if (BlockedSlots >= 3) return "짐이 너무 무거워 움직일 수 없어요. 물품을 버리세요.";
            return null;
        }

        // 옮겨 갔으면 true. 도착한 노드는 CurrentNode 로 본다.
        public bool Move(MapNode target)
        {
            if (CannotMove(target) != null) return false;
            X = target.x; Y = target.y;
            Steps++;
            Reveal();
            if (Config.respawnEvery > 0 && Steps % Config.respawnEvery == 0) Respawn();
            return true;
        }

        void Reveal()
        {
            visibleCells.Clear();
            hintCells.Clear();
            var here = CurrentNode;

            // 지금 노드에서 길로 시야+1 까지
            hopsHere = new Dictionary<MapNode, int>();
            if (here != null)
            {
                hopsHere[here] = 0;
                var q = new Queue<MapNode>();
                q.Enqueue(here);
                while (q.Count > 0)
                {
                    var cur = q.Dequeue();
                    int h = hopsHere[cur];
                    if (h > Config.visionHops) continue;
                    foreach (var nb in Map.Neighbors(cur))
                        if (!hopsHere.ContainsKey(nb)) { hopsHere[nb] = h + 1; q.Enqueue(nb); }
                }
            }

            foreach (var kv in hopsHere)
            {
                var n = kv.Key;
                if (kv.Value <= Config.visionHops)
                {
                    Memory.See(n.x, n.y);
                    int r = kv.Value == 0 ? Config.visionRadius : Math.Max(0, Config.visionRadius - 1);
                    MarkArea(n.x, n.y, r, visibleCells);
                    MarkArea(n.x, n.y, r + 1, hintCells);
                }
                else MarkArea(n.x, n.y, 0, hintCells);
            }

            // 보이는 노드끼리 잇는 길 위의 칸도 밝힌다
            foreach (var l in Map.links)
            {
                var a = Map.NodeAt(l.ax, l.ay);
                var b = Map.NodeAt(l.bx, l.by);
                if (a == null || b == null) continue;
                int ha, hb;
                bool va = hopsHere.TryGetValue(a, out ha) && ha <= Config.visionHops;
                bool vb = hopsHere.TryGetValue(b, out hb) && hb <= Config.visionHops;
                if (va || vb) MarkLine(l.ax, l.ay, l.bx, l.by, va && vb ? visibleCells : hintCells);
            }

            hintCells.ExceptWith(visibleCells);
            foreach (int k in visibleCells) Memory.See(k % Map.width, k / Map.width);
        }

        void MarkArea(int cx, int cy, int r, HashSet<int> set)
        {
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                    if (Math.Abs(x - cx) + Math.Abs(y - cy) <= r && Map.InBounds(x, y)) set.Add(y * Map.width + x);
        }

        // 두 칸 사이 직선이 지나는 칸 (브레젠험)
        void MarkLine(int x0, int y0, int x1, int y1, HashSet<int> set)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                if (Map.InBounds(x0, y0)) set.Add(y0 * Map.width + x0);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        // 가장 오래전에 이긴 전투 노드 1곳(지금 서 있는 곳 제외)에 적이 다시 생긴다
        void Respawn()
        {
            for (int i = 0; i < clearOrder.Count; i++)
            {
                var n = clearOrder[i];
                if (n.x == X && n.y == Y) continue;
                if (!MapCatalog.NodeType(n.type).HasBattle) continue;
                clearOrder.RemoveAt(i);
                cleared.Remove(n);
                AddLog(Label(n) + "에 적이 다시 나타났어요.");
                return;
            }
        }

        // ───────── 노드 ─────────

        // 임시 전투 결과. 이기면 체력이 깎이고 재화와 무기를 얻는다.
        public void ResolveBattle(MapNode n, bool won)
        {
            if (End != ExplorationEnd.None || IsCleared(n)) return;
            int lv = DifficultyOf(n);
            if (!won) { Die("전투에서 졌어요."); return; }

            Hp -= Config.battleDamage[lv];
            if (Hp <= 0) { Hp = 0; Die("이겼지만 쓰러졌어요."); return; }
            int money = Config.battleMoney[lv] * (n.type == "elite" ? 2 : 1);
            Money += money;
            string weapon = MapCatalog.FamilyName(EnemyOf(n)) + "의 무기";
            Bag.Add(new BagItem(weapon, "weapon", Config.dropWeaponWeight));
            MarkCleared(n);
            AddLog(Label(n) + " 승리: 체력 -" + Config.battleDamage[lv] + ", 재화 +" + money + ", " + weapon + " 획득");
        }

        public void Farm(MapNode n)
        {
            if (End != ExplorationEnd.None || IsCleared(n)) return;
            for (int i = 0; i < Config.farmMaterials; i++) Bag.Add(new BagItem("재료", "material", 1));
            Money += Config.farmMoney;
            MarkCleared(n);
            AddLog(Label(n) + "에서 재료 " + Config.farmMaterials + "개, 재화 " + Config.farmMoney + "을 주웠어요.");
        }

        public bool TakeTotem(MapNode n)
        {
            if (End != ExplorationEnd.None || IsCleared(n)) return false;
            if (Totems >= Config.maxTotems) { AddLog("토템은 " + Config.maxTotems + "개까지만 가질 수 있어요."); return false; }
            Totems++;
            MarkCleared(n);
            AddLog("토템을 얻었어요. (" + Totems + "/" + Config.maxTotems + ")");
            return true;
        }

        public bool CanRest { get { return !restUsed && Hp < Config.maxHp && End == ExplorationEnd.None; } }

        public void Rest()
        {
            if (!CanRest) return;
            int heal = (int)Math.Round(Config.maxHp * Config.restHealRate);
            Hp = Math.Min(Config.maxHp, Hp + heal);
            restUsed = true;
            AddLog("휴식: 체력 +" + heal);
        }

        // 이벤트·폐허 마을 같은 노드를 "봤음"으로 표시
        public void Visit(MapNode n)
        {
            if (!IsCleared(n)) MarkCleared(n);
        }

        public bool Escape(MapNode n)
        {
            if (End != ExplorationEnd.None || n.type != "exit" || !IsExitOpen(n)) return false;
            End = ExplorationEnd.Escaped;
            AddLog("탈출했어요. 재화 " + Money + ", 물품 " + Bag.Count + "개를 가지고 돌아가요.");
            return true;
        }

        public void DropItem(int index)
        {
            if (index < 0 || index >= Bag.Count) return;
            AddLog(Bag[index].name + "을 버렸어요.");
            Bag.RemoveAt(index);
        }

        void Die(string why)
        {
            End = ExplorationEnd.Dead;
            AddLog(why + " 들고 있던 물품과 재화를 모두 잃었어요.");
            Bag.Clear();
            Money = 0;
        }

        void MarkCleared(MapNode n)
        {
            if (cleared.Add(n)) clearOrder.Add(n);
        }

        void AddLog(string s)
        {
            Log.Add(s);
            if (Log.Count > 50) Log.RemoveAt(0);
        }

        public static string Label(MapNode n)
        {
            if (!string.IsNullOrEmpty(n.name)) return n.name;
            var def = MapCatalog.NodeType(n.type);
            return def != null ? def.Name : n.type;
        }

        static void Shuffle<T>(List<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }
    }
}
