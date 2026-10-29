// 길(노드끼리 잇는 선)을 다루는 계산: 길로 몇 번 이동해야 하는지, 자동 연결.
// 웹 맵 편집기의 autoLink 와 같은 규칙이다.
using System;
using System.Collections.Generic;

namespace CardGame.Core.Map
{
    public static class MapLinks
    {
        public static int AutoExtraMaxSteps = 8;   // 덧붙임 길은 걸어서 이 걸음 안쪽 노드끼리만

        // 시작 노드에서 길로 몇 번 이동하면 닿는지. 못 가는 노드는 들어 있지 않다.
        public static Dictionary<MapNode, int> Hops(MapData map, MapNode start)
        {
            var d = new Dictionary<MapNode, int>();
            if (start == null) return d;
            d[start] = 0;
            var q = new Queue<MapNode>();
            q.Enqueue(start);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                foreach (var nb in map.Neighbors(cur))
                {
                    if (d.ContainsKey(nb)) continue;
                    d[nb] = d[cur] + 1;
                    q.Enqueue(nb);
                }
            }
            return d;
        }

        // 걸어서 가까운 노드끼리 모두 이어지게(최소 신장 트리) 한 뒤, 노드마다 가까운 이웃을 하나 더 잇는다.
        // 강·산은 돌아가는 걸음 수로 따지고, 다른 길과 엇갈리는 덧붙임 길은 만들지 않는다. 이미 있는 길은 그대로 둔다.
        // 새로 이은 길 수를 돌려준다.
        public static int AutoLink(MapData map)
        {
            var ns = map.nodes.FindAll(n => map.IsWalkable(n.x, n.y));
            if (ns.Count < 2) return 0;
            int before = map.links.Count;
            var D = new int[ns.Count][,];
            for (int i = 0; i < ns.Count; i++) D[i] = MapValidator.Distances(map, ns[i].x, ns[i].y);
            Func<int, int, int> wd = (i, j) => { int v = D[i][ns[j].x, ns[j].y]; return v < 0 ? int.MaxValue : v; };

            var inTree = new List<int> { 0 };
            var inSet = new HashSet<int> { 0 };
            while (inSet.Count < ns.Count)
            {
                int bv = int.MaxValue, bi = -1, bj = -1;
                foreach (int i in inTree)
                    for (int j = 0; j < ns.Count; j++)
                    {
                        if (inSet.Contains(j)) continue;
                        int v = wd(i, j);
                        if (v == int.MaxValue) continue;
                        if (bi < 0 || v < bv || (v == bv && (i < bi || (i == bi && j < bj)))) { bv = v; bi = i; bj = j; }
                    }
                if (bi < 0)
                {
                    // 걸어서 못 가는 노드는 건너뛴다
                    for (int k = 0; k < ns.Count; k++) if (!inSet.Contains(k)) { inSet.Add(k); inTree.Add(k); break; }
                    continue;
                }
                if (map.FindLink(ns[bi], ns[bj]) == null) map.links.Add(new MapLink(ns[bi], ns[bj]));
                inSet.Add(bj); inTree.Add(bj);
            }

            for (int i = 0; i < ns.Count; i++)
            {
                int bv = int.MaxValue, bj = -1;
                for (int j = 0; j < ns.Count; j++)
                {
                    if (i == j || map.FindLink(ns[i], ns[j]) != null) continue;
                    int v = wd(i, j);
                    if (v > AutoExtraMaxSteps) continue;
                    if (CrossesAny(map, ns[i], ns[j])) continue;
                    if (bj < 0 || v < bv) { bv = v; bj = j; }
                }
                if (bj >= 0) map.links.Add(new MapLink(ns[i], ns[bj]));
            }
            return map.links.Count - before;
        }

        static bool CrossesAny(MapData map, MapNode a, MapNode b)
        {
            foreach (var l in map.links)
                if (SegCross(a.x, a.y, b.x, b.y, l.ax, l.ay, l.bx, l.by)) return true;
            return false;
        }

        static bool SegCross(int ax, int ay, int bx, int by, int cx, int cy, int dx, int dy)
        {
            if ((ax == cx && ay == cy) || (ax == dx && ay == dy) || (bx == cx && by == cy) || (bx == dx && by == dy)) return false;
            int o1 = Orient(ax, ay, bx, by, cx, cy), o2 = Orient(ax, ay, bx, by, dx, dy);
            int o3 = Orient(cx, cy, dx, dy, ax, ay), o4 = Orient(cx, cy, dx, dy, bx, by);
            return o1 * o2 < 0 && o3 * o4 < 0;
        }

        static int Orient(int px, int py, int qx, int qy, int rx, int ry)
        {
            return Math.Sign((qx - px) * (ry - py) - (qy - py) * (rx - px));
        }
    }
}
