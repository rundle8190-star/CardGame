// 바깥 맵 한 장의 데이터. 맵 편집기가 Assets/Data/Maps/<id>.json 으로 저장한다.
// 지형은 rows 에 한 줄에 한 글자씩(MapCatalog 의 지형 문자), 노드는 nodes 목록에 칸 좌표로 들어간다.
// 좌표: x 는 왼쪽에서 0부터, y 는 위에서 0부터.
using System;
using System.Collections.Generic;
using System.Text;

namespace CardGame.Core.Map
{
    [Serializable]
    public class MapNode
    {
        public int x;
        public int y;
        public string type = "battle";
        public string name = "";
        public int battleCells = 0;        // 0 이면 지형 기본값
        public string difficulty = "";     // "" 자동, easy, normal, hard
        public string enemies = "";        // 쉼표로 구분한 적 계열 id. 비우면 맵 기본값
        public string target = "";         // 맵 이동: 갈 맵 id, 던전: 던전 id
        public string note = "";
        public string pool = "";           // 랜덤 칸만: 이 칸의 후보 (예 "battle:3,farm:1"). 비우면 맵 기본값

        public MapNode Clone()
        {
            return (MapNode)MemberwiseClone();
        }
    }

    // 노드 두 개를 잇는 길. 끝점은 노드 좌표로 저장한다 (노드를 옮기면 MoveLinks 로 같이 옮긴다).
    [Serializable]
    public class MapLink
    {
        public int ax, ay, bx, by;

        public MapLink() { }
        public MapLink(MapNode a, MapNode b) { ax = a.x; ay = a.y; bx = b.x; by = b.y; }

        public bool Touches(int x, int y) { return (ax == x && ay == y) || (bx == x && by == y); }

        public bool Same(int x1, int y1, int x2, int y2)
        {
            return (ax == x1 && ay == y1 && bx == x2 && by == y2) || (ax == x2 && ay == y2 && bx == x1 && by == y1);
        }

        public MapLink Clone() { return (MapLink)MemberwiseClone(); }
    }

    [Serializable]
    public class MapData
    {
        public int version = 1;
        public string id = "plains";
        public string name = "평원";
        public int width = 30;
        public int height = 20;
        public int baseTier = 1;                 // 맵 단계 (1 평원, 2 숲·산지, 3 늪지·설원)
        public string enemies = "";              // 맵 기본 적 계열 (쉼표 구분)
        public string randomPool = "";           // 랜덤 칸 기본 후보 (예 "battle:40,elite:8:3"). 비우면 RandomNodeGenerator.DefaultPool
        public string[] rows = new string[0];
        public List<MapNode> nodes = new List<MapNode>();
        public List<MapLink> links = new List<MapLink>();   // 노드끼리 잇는 길 (보드게임식 이동)

        // 맵 전체를 깊은 복사한다 (탐험마다 랜덤 칸을 바꿔 끼울 때 원본을 지키려고)
        public MapData Clone()
        {
            var copy = (MapData)MemberwiseClone();
            copy.rows = rows != null ? (string[])rows.Clone() : new string[0];
            copy.nodes = new List<MapNode>();
            if (nodes != null) foreach (var n in nodes) copy.nodes.Add(n.Clone());
            copy.links = new List<MapLink>();
            if (links != null) foreach (var l in links) copy.links.Add(l.Clone());
            return copy;
        }

        public static MapData Create(string id, string name, int width, int height, char fill = MapCatalog.DefaultTerrain)
        {
            var map = new MapData { id = id, name = name, width = width, height = height };
            map.rows = new string[height];
            for (int y = 0; y < height; y++) map.rows[y] = new string(fill, width);
            return map;
        }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && y >= 0 && x < width && y < height;
        }

        public char GetTerrain(int x, int y)
        {
            if (!InBounds(x, y)) return '#';
            if (rows == null || y >= rows.Length || rows[y] == null || x >= rows[y].Length) return MapCatalog.DefaultTerrain;
            return rows[y][x];
        }

        public void SetTerrain(int x, int y, char code)
        {
            if (!InBounds(x, y)) return;
            FixRows();
            var sb = new StringBuilder(rows[y]);
            sb[x] = code;
            rows[y] = sb.ToString();
        }

        public bool IsWalkable(int x, int y)
        {
            return InBounds(x, y) && MapCatalog.Terrain(GetTerrain(x, y)).Walkable;
        }

        public MapNode NodeAt(int x, int y)
        {
            foreach (var n in nodes) if (n.x == x && n.y == y) return n;
            return null;
        }

        public void RemoveNodeAt(int x, int y)
        {
            nodes.RemoveAll(n => n.x == x && n.y == y);
            RemoveLinksAt(x, y);
        }

        // ───── 길 ─────

        public MapLink FindLink(MapNode a, MapNode b)
        {
            foreach (var l in links) if (l.Same(a.x, a.y, b.x, b.y)) return l;
            return null;
        }

        // 이어져 있으면 끊고 false, 아니면 잇고 true
        public bool ToggleLink(MapNode a, MapNode b)
        {
            if (a == b) return false;
            var l = FindLink(a, b);
            if (l != null) { links.Remove(l); return false; }
            links.Add(new MapLink(a, b));
            return true;
        }

        public void RemoveLinksAt(int x, int y)
        {
            links.RemoveAll(l => l.Touches(x, y));
        }

        public void MoveLinks(int oldX, int oldY, int newX, int newY)
        {
            foreach (var l in links)
            {
                if (l.ax == oldX && l.ay == oldY) { l.ax = newX; l.ay = newY; }
                if (l.bx == oldX && l.by == oldY) { l.bx = newX; l.by = newY; }
            }
        }

        // 이 노드와 길로 이어진 노드들
        public List<MapNode> Neighbors(MapNode node)
        {
            var result = new List<MapNode>();
            foreach (var l in links)
            {
                MapNode other = null;
                if (l.ax == node.x && l.ay == node.y) other = NodeAt(l.bx, l.by);
                else if (l.bx == node.x && l.by == node.y) other = NodeAt(l.ax, l.ay);
                if (other != null && !result.Contains(other)) result.Add(other);
            }
            return result;
        }

        // 노드의 실제 전투 칸 수 (노드 설정 > 지형 기본값)
        public int BattleCellsOf(MapNode node)
        {
            if (node.battleCells > 0) return node.battleCells;
            int cells = MapCatalog.Terrain(GetTerrain(node.x, node.y)).BattleCells;
            return cells > 0 ? cells : 7;
        }

        // 크기를 바꾼다. 늘어난 곳은 기본 지형, 밖으로 나간 노드는 지운다.
        public void Resize(int newWidth, int newHeight)
        {
            if (newWidth < 1 || newHeight < 1) return;
            var newRows = new string[newHeight];
            for (int y = 0; y < newHeight; y++)
            {
                var sb = new StringBuilder(newWidth);
                for (int x = 0; x < newWidth; x++)
                    sb.Append(x < width && y < height ? GetTerrain(x, y) : MapCatalog.DefaultTerrain);
                newRows[y] = sb.ToString();
            }
            width = newWidth;
            height = newHeight;
            rows = newRows;
            nodes.RemoveAll(n => !InBounds(n.x, n.y));
            FixLinks();
        }

        // 파일을 손으로 고쳐서 줄 길이가 어긋나도 깨지지 않게 맞춘다.
        public void FixRows()
        {
            if (rows == null || rows.Length != height)
            {
                var fixedRows = new string[height];
                for (int y = 0; y < height; y++)
                    fixedRows[y] = rows != null && y < rows.Length ? rows[y] : "";
                rows = fixedRows;
            }
            for (int y = 0; y < height; y++)
            {
                string r = rows[y] ?? "";
                if (r.Length < width) r = r + new string(MapCatalog.DefaultTerrain, width - r.Length);
                else if (r.Length > width) r = r.Substring(0, width);
                rows[y] = r;
            }
            if (nodes == null) nodes = new List<MapNode>();
            FixLinks();
        }

        // 끝점에 노드가 없는 길, 겹치는 길을 지운다
        public void FixLinks()
        {
            if (links == null) links = new List<MapLink>();
            var kept = new List<MapLink>();
            foreach (var l in links)
            {
                if (l == null || (l.ax == l.bx && l.ay == l.by)) continue;
                if (NodeAt(l.ax, l.ay) == null || NodeAt(l.bx, l.by) == null) continue;
                bool dup = false;
                foreach (var k in kept) if (k.Same(l.ax, l.ay, l.bx, l.by)) { dup = true; break; }
                if (!dup) kept.Add(l);
            }
            links = kept;
        }

        public static List<string> SplitList(string list)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(list)) return result;
            foreach (var part in list.Split(','))
            {
                var s = part.Trim();
                if (s.Length > 0) result.Add(s);
            }
            return result;
        }
    }
}
