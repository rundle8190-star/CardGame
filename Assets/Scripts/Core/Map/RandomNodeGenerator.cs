// 랜덤 칸을 실제 노드로 바꾸는 생성기. 탐험을 시작할 때마다 한 번 돌린다.
//
// 후보 적는 법: "종류:가중치" 또는 "종류:가중치:최대 개수" 를 쉼표로 잇는다.
//   예) "battle:40, elite:8:3, farm:15"  → 전투 40, 엘리트 8(맵 전체 최대 3곳), 파밍 15 비율
//   최대 개수는 맵에 직접 찍은 같은 종류 노드까지 센다. (상인을 1곳 찍어 두고 merchant_camp:4:1 이면 랜덤으로는 안 나온다)
// 후보 우선순위: 칸의 pool > 맵의 randomPool > DefaultPool
//
// 규칙
//   1. 엘리트는 시작 지점에서 EliteMinSteps 걸음 안쪽에 나오지 않는다.
//   2. 같은 종류 노드가 SpreadRadius 칸(가로+세로) 안에 이미 있으면 그 종류는 1/4 확률로 줄어든다. 한쪽에 몰리지 않게.
//   3. 뽑을 게 없으면 전투가 된다.
//
// 웹 맵 편집기의 '랜덤 굴려 보기'도 같은 순서·같은 난수로 계산해서, 같은 시드면 결과가 같다.
using System.Collections.Generic;

namespace CardGame.Core.Map
{
    public class RandomPoolEntry
    {
        public string Type;
        public int Weight;
        public int Max;   // 0 이면 제한 없음
    }

    public static class RandomNodeGenerator
    {
        public const string DefaultPool =
            "battle:40, elite:8:3, farm:15, event:20, totem:6:3, ruin_village:5:2, merchant_camp:4:1";
        public static int EliteMinSteps = 6;
        public static int SpreadRadius = 2;
        public const string Fallback = "battle";

        public static List<RandomPoolEntry> ParsePool(string spec, List<string> problems = null)
        {
            var list = new List<RandomPoolEntry>();
            if (string.IsNullOrEmpty(spec)) return list;
            foreach (var raw in spec.Split(','))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                var bits = part.Split(':');
                var e = new RandomPoolEntry { Type = bits[0].Trim(), Weight = 1, Max = 0 };
                int v;
                if (bits.Length > 1 && int.TryParse(bits[1].Trim(), out v)) e.Weight = v < 0 ? 0 : v;
                if (bits.Length > 2 && int.TryParse(bits[2].Trim(), out v)) e.Max = v < 0 ? 0 : v;
                var def = MapCatalog.NodeType(e.Type);
                if (def == null || def.IsRandom || e.Type == "start")
                {
                    if (problems != null) problems.Add("'" + e.Type + "'");
                    continue;
                }
                list.Add(e);
            }
            return list;
        }

        public static List<RandomPoolEntry> PoolOf(MapData map, MapNode node)
        {
            var pool = ParsePool(node.pool);
            if (pool.Count == 0) pool = ParsePool(map.randomPool);
            if (pool.Count == 0) pool = ParsePool(DefaultPool);
            return pool;
        }

        // 원본은 그대로 두고, 랜덤 칸을 바꾼 복사본을 돌려준다.
        public static MapData Resolve(MapData source, int seed)
        {
            var map = source.Clone();
            var rng = new XorShift((uint)seed);

            MapNode start = null;
            foreach (var n in map.nodes) if (n.type == "start") start = n;
            int[,] dist = start != null ? MapValidator.Distances(map, start.x, start.y) : null;

            // 맵에 직접 찍은 노드 수부터 센다
            var counts = new Dictionary<string, int>();
            var randoms = new List<MapNode>();
            foreach (var n in map.nodes)
            {
                var def = MapCatalog.NodeType(n.type);
                if (def != null && def.IsRandom) randoms.Add(n);
                else Add(counts, n.type);
            }

            // 위에서 아래, 왼쪽에서 오른쪽 순서로 정한다 (편집 순서와 상관없이 같은 결과)
            randoms.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

            foreach (var n in randoms)
            {
                var pool = PoolOf(map, n);
                var weights = new int[pool.Count];
                int total = 0;
                for (int i = 0; i < pool.Count; i++)
                {
                    var e = pool[i];
                    int w = e.Weight * 4;
                    int have;
                    counts.TryGetValue(e.Type, out have);
                    if (e.Max > 0 && have >= e.Max) w = 0;
                    if (e.Type == "elite" && dist != null && map.InBounds(n.x, n.y)
                        && dist[n.x, n.y] >= 0 && dist[n.x, n.y] < EliteMinSteps) w = 0;
                    if (w > 0 && SameTypeNear(map, n, e.Type)) w = e.Weight;
                    weights[i] = w;
                    total += w;
                }

                string chosen = Fallback;
                if (total > 0)
                {
                    int r = (int)(rng.Next() % (uint)total);
                    for (int i = 0; i < pool.Count; i++)
                    {
                        if (r < weights[i]) { chosen = pool[i].Type; break; }
                        r -= weights[i];
                    }
                }
                n.type = chosen;
                n.pool = "";
                Add(counts, chosen);
            }
            return map;
        }

        static bool SameTypeNear(MapData map, MapNode self, string type)
        {
            foreach (var o in map.nodes)
            {
                if (o == self || o.type != type) continue;
                int d = System.Math.Abs(o.x - self.x) + System.Math.Abs(o.y - self.y);
                if (d <= SpreadRadius) return true;
            }
            return false;
        }

        static void Add(Dictionary<string, int> counts, string type)
        {
            int c;
            counts.TryGetValue(type, out c);
            counts[type] = c + 1;
        }

        // 웹 편집기와 똑같이 움직이는 간단한 난수 (xorshift32)
        public class XorShift
        {
            uint s;
            public XorShift(uint seed) { s = seed == 0 ? 0x9E3779B9u : seed; }
            public uint Next()
            {
                s ^= s << 13;
                s ^= s >> 17;
                s ^= s << 5;
                return s;
            }
        }
    }
}
