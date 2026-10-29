// 맵을 검사한다: 시작 지점, 막힌 칸 위의 노드, 시작에서 갈 수 없는 노드, 걸음 수 등.
// 이동은 상하좌우 한 칸씩이다.
using System.Collections.Generic;

namespace CardGame.Core.Map
{
    public class MapReport
    {
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public Dictionary<string, int> Counts = new Dictionary<string, int>();
        public int[,] Distance;          // 시작 지점에서 걸음 수, 못 가면 -1
        public int FarthestSteps = -1;   // 시작에서 가장 먼 노드까지 걸음 수
        public int WalkableCells;
        public int Links;
        public int FarthestHops = -1;    // 시작에서 길로 가장 먼 노드까지 이동 횟수
    }

    public static class MapValidator
    {
        static readonly int[] DX = { 1, -1, 0, 0 };
        static readonly int[] DY = { 0, 0, 1, -1 };

        public static int[,] Distances(MapData map, int sx, int sy)
        {
            var dist = new int[map.width, map.height];
            for (int x = 0; x < map.width; x++)
                for (int y = 0; y < map.height; y++)
                    dist[x, y] = -1;
            if (!map.IsWalkable(sx, sy)) return dist;

            var queue = new Queue<int>();
            dist[sx, sy] = 0;
            queue.Enqueue(sy * map.width + sx);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int cx = cur % map.width, cy = cur / map.width;
                for (int i = 0; i < 4; i++)
                {
                    int nx = cx + DX[i], ny = cy + DY[i];
                    if (!map.IsWalkable(nx, ny) || dist[nx, ny] >= 0) continue;
                    dist[nx, ny] = dist[cx, cy] + 1;
                    queue.Enqueue(ny * map.width + nx);
                }
            }
            return dist;
        }

        public static MapReport Check(MapData map)
        {
            var report = new MapReport();

            for (int x = 0; x < map.width; x++)
                for (int y = 0; y < map.height; y++)
                    if (map.IsWalkable(x, y)) report.WalkableCells++;

            MapNode start = null;
            int startCount = 0;
            foreach (var n in map.nodes)
            {
                if (!report.Counts.ContainsKey(n.type)) report.Counts[n.type] = 0;
                report.Counts[n.type]++;
                if (n.type == "start") { start = n; startCount++; }
            }

            if (startCount == 0) report.Errors.Add("시작 지점이 없어요.");
            else if (startCount > 1) report.Errors.Add("시작 지점이 " + startCount + "개예요. 1개만 두세요.");

            foreach (var n in map.nodes)
            {
                var def = MapCatalog.NodeType(n.type);
                string where = Label(n);
                if (def == null) report.Errors.Add(where + ": 모르는 노드 종류 '" + n.type + "'");
                if (!map.IsWalkable(n.x, n.y)) report.Errors.Add(where + ": 지나갈 수 없는 지형 위에 있어요.");
                if (def != null && def.HasTarget && string.IsNullOrEmpty(n.target))
                    report.Warnings.Add(where + ": 이동할 맵이나 던전 id가 비어 있어요.");
                if (def != null && def.IsRandom && !string.IsNullOrEmpty(n.pool))
                {
                    var bad = new List<string>();
                    if (RandomNodeGenerator.ParsePool(n.pool, bad).Count == 0) bad.Add("(쓸 수 있는 후보 없음, 맵 기본값을 써요)");
                    if (bad.Count > 0) report.Warnings.Add(where + ": 랜덤 후보에 모르는 종류 " + string.Join(", ", bad.ToArray()));
                }
            }

            if (!string.IsNullOrEmpty(map.randomPool))
            {
                var bad = new List<string>();
                if (RandomNodeGenerator.ParsePool(map.randomPool, bad).Count == 0) bad.Add("(쓸 수 있는 후보 없음, 기본값을 써요)");
                if (bad.Count > 0) report.Warnings.Add("맵 랜덤 후보에 모르는 종류 " + string.Join(", ", bad.ToArray()));
            }

            int exits;
            report.Counts.TryGetValue("exit", out exits);
            if (exits < 2) report.Warnings.Add("탈출 후보가 " + exits + "개예요. 탐험마다 2곳을 열려면 3곳 이상을 추천해요.");

            report.Links = map.links != null ? map.links.Count : 0;
            if (report.Links == 0)
            {
                if (map.nodes.Count > 1) report.Warnings.Add("노드끼리 이은 길이 아직 없어요. 길 도구(5)로 잇거나 '자동 연결'을 눌러 보세요.");
            }
            else if (start != null)
            {
                var hops = MapLinks.Hops(map, start);
                foreach (var n in map.nodes)
                {
                    int h;
                    if (!hops.TryGetValue(n, out h)) report.Errors.Add(Label(n) + ": 시작 지점에서 길로 이어져 있지 않아요.");
                    else if (h > report.FarthestHops) report.FarthestHops = h;
                }
            }

            if (start != null)
            {
                report.Distance = Distances(map, start.x, start.y);
                foreach (var n in map.nodes)
                {
                    if (!map.InBounds(n.x, n.y)) continue;
                    int d = report.Distance[n.x, n.y];
                    if (d < 0) report.Errors.Add(Label(n) + ": 시작 지점에서 갈 수 없어요.");
                    else if (d > report.FarthestSteps) report.FarthestSteps = d;
                }
            }
            return report;
        }

        static string Label(MapNode n)
        {
            var def = MapCatalog.NodeType(n.type);
            string name = string.IsNullOrEmpty(n.name) ? (def != null ? def.Name : n.type) : n.name;
            return name + " (" + n.x + ", " + n.y + ")";
        }
    }
}
