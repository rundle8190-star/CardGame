// 바깥 맵 플레이 화면. 맵 파일을 읽어 타일을 깔고, 보드게임처럼 길로 이어진 노드를 따라 말을 옮기며 탐험한다.
// 규칙은 Core 의 Exploration 이 맡고, 이 파일은 그리기와 입력만 한다.
// 쿼터뷰(2:1 마름모) 타일로 그린다. 타일 그림이 없으면 색 마름모로 대신한다.
// 타일 그림: Assets/Art/tiles/<지형 id>.png (변형은 <지형 id>_2.png ...),
// 노드 그림: Assets/Art/map_nodes/<노드 종류 id>.png. 플레이 버튼을 누를 때 자동으로 연결된다.
//
// 조작: 길로 이어진 이웃 노드 클릭, 또는 W/A/S/D·방향키로 화면에서 그쪽 방향에 있는 이웃 노드로.
//       휠로 확대·축소, B 키로 배낭
using System.Collections.Generic;
using System.IO;
using CardGame.Core.Map;
using UnityEngine;

namespace CardGame.View.Map
{
    public class MapPlayController : MonoBehaviour
    {
        [Tooltip("Assets/Data/Maps 의 맵 파일")]
        public TextAsset mapFile;
        public ExplorationConfig config = new ExplorationConfig();
        [Tooltip("한 번 본 칸을 다음 탐험에도 기억할지")]
        public bool keepMapMemory = true;

        [System.Serializable]
        public class ArtEntry
        {
            public string id;
            public Sprite sprite;
        }

        [Header("그림 (플레이 버튼이 자동으로 채움)")]
        public List<ArtEntry> tileArt = new List<ArtEntry>();
        public List<ArtEntry> nodeArt = new List<ArtEntry>();
        public Sprite playerSprite;
        [Tooltip("타일 그림 한 칸의 가로 픽셀 수. 세로는 절반(2:1)")]
        public int tilePixelWidth = 64;

        // 타일 한 칸: 가로 1, 세로 0.5 (월드 단위)
        const float TileW = 1f, TileH = 0.5f;
        readonly Dictionary<string, List<Sprite>> tileArtById = new Dictionary<string, List<Sprite>>();
        readonly Dictionary<string, Sprite> nodeArtById = new Dictionary<string, Sprite>();
        bool pixelPerfect;

        MapData map;
        Exploration ex;
        Camera cam;
        Sprite square;
        SpriteRenderer[,] tiles;
        MapData sourceMap;          // 파일에서 읽은 원본 (랜덤 칸 그대로)
        Transform nodeRoot;
        readonly Dictionary<MapNode, SpriteRenderer> nodeMarks = new Dictionary<MapNode, SpriteRenderer>();
        readonly Dictionary<MapLink, SpriteRenderer> linkMarks = new Dictionary<MapLink, SpriteRenderer>();
        Sprite pixel;
        SpriteRenderer player;
        Vector3 playerTarget;

        MapNode popup;        // 지금 열려 있는 노드 창
        bool showBag;
        bool pendingReset, pendingRestart;
        string loadError;
        string toast;
        float toastUntil;
        GUIStyle markStyle, boxStyle, titleStyle;

        static readonly Color UnknownColor = new Color(0.05f, 0.05f, 0.06f);

        // ───────── 시작 ─────────

        void Start()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            pixelPerfect = cam.GetComponent("PixelPerfectCamera") != null;
            if (!pixelPerfect) cam.orthographicSize = 5;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            if (mapFile == null) { loadError = "Map Player 오브젝트에 맵 파일(mapFile)을 넣어 주세요."; return; }
            try
            {
                sourceMap = JsonUtility.FromJson<MapData>(mapFile.text);
                sourceMap.FixRows();
                map = sourceMap;
                square = MakeDiamond(tilePixelWidth);
                IndexArt();
                BuildTiles();
                NewExploration();
            }
            catch (System.Exception e)
            {
                loadError = "맵을 읽지 못했어요: " + e.Message;
                Debug.LogError(loadError);
            }
        }

        void NewExploration()
        {
            MapMemory memory = keepMapMemory ? LoadMemory() : null;
            // 랜덤 칸은 탐험마다 새로 굴린다. 지형은 같으니 노드 표시만 다시 만든다
            int seed = System.Environment.TickCount;
            map = RandomNodeGenerator.Resolve(sourceMap, seed);
            ex = new Exploration(map, config, memory, seed);   // 길이 없는 맵이면 여기서 자동으로 이어진다
            BuildNodes();
            popup = null;
            showBag = false;
            Refresh();
            SnapCamera();
        }

        void BuildTiles()
        {
            var root = new GameObject("Tiles").transform;
            root.SetParent(transform, false);
            tiles = new SpriteRenderer[map.width, map.height];
            for (int y = 0; y < map.height; y++)
                for (int x = 0; x < map.width; x++)
                {
                    var t = MapCatalog.Terrain(map.GetTerrain(x, y));
                    Sprite art = TileSprite(t.Id, x, y);
                    var sr = NewSprite("tile_" + x + "_" + y, root, CellBase(x, y), art ?? square, art != null ? 1f : 0.97f, Depth(x, y, 0));
                    tiles[x, y] = sr;
                }

            if (playerSprite != null)
                player = NewSprite("Player", transform, Vector3.zero, playerSprite, 1f, 0);
            else
            {
                // 임시 말: 흰 마름모 위에 검은 테두리
                player = NewSprite("Player", transform, Vector3.zero, square, 0.4f, 0);
                player.color = Color.white;
                var ring = NewSprite("PlayerRing", player.transform, new Vector3(0, -0.04f, 0), square, 1.35f, 0);
                ring.color = new Color(0, 0, 0, 0.85f);
            }
        }

        void BuildNodes()
        {
            if (nodeRoot != null) Destroy(nodeRoot.gameObject);
            nodeMarks.Clear();
            linkMarks.Clear();
            nodeRoot = new GameObject("Nodes").transform;
            nodeRoot.SetParent(transform, false);

            // 길: 흰 점 하나를 늘려서 선으로 쓴다. 타일보다 위, 노드보다 아래.
            if (pixel == null)
            {
                var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
            }
            foreach (var l in map.links)
            {
                Vector3 a = NodeMid(l.ax, l.ay), b = NodeMid(l.bx, l.by);
                var sr = NewSprite("link_" + l.ax + "_" + l.ay + "_" + l.bx + "_" + l.by, nodeRoot, (a + b) / 2f, pixel, 1f, LinkOrder);
                Vector3 d = b - a;
                sr.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                sr.transform.localScale = new Vector3(d.magnitude, 0.04f, 1);
                linkMarks[l] = sr;
            }

            foreach (var n in map.nodes)
            {
                Sprite art;
                nodeArtById.TryGetValue(n.type, out art);
                // 그림이 없으면 작은 마름모를 칸 가운데에 띄운다
                Vector3 pos = art != null ? CellBase(n.x, n.y) : CellBase(n.x, n.y) + new Vector3(0, TileH * 0.2f, 0);
                nodeMarks[n] = NewSprite("node_" + n.type + "_" + n.x + "_" + n.y, nodeRoot, pos, art ?? square, art != null ? 1f : 0.6f, Depth(n.x, n.y, 1));
            }
        }

        // ───────── 매 프레임 ─────────

        void Update()
        {
            if (ex == null) return;

            // 화면(OnGUI)을 그리는 도중에 탐험을 새로 만들면 배치가 어긋나서, 버튼은 표시만 하고 여기서 처리한다
            if (pendingReset)
            {
                pendingReset = false;
                pendingRestart = false;
                DeleteMemory();
                NewExploration();
                Toast("지도 기억을 지우고 다시 시작했어요.");
            }
            if (pendingRestart) { pendingRestart = false; NewExploration(); }

            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (wheel != 0 && !pixelPerfect) cam.orthographicSize = Mathf.Clamp(cam.orthographicSize - wheel * 5f, 2f, 12f);

            Vector3 target = CellCenter(ex.X, ex.Y) + new Vector3(0, 0, -10);
            cam.transform.position = Vector3.Lerp(cam.transform.position, target, 1f - Mathf.Exp(-10f * Time.deltaTime));
            // 말이 길을 따라 미끄러지듯 옮겨 간다
            player.transform.position = Vector3.Lerp(player.transform.position, playerTarget, 1f - Mathf.Exp(-12f * Time.deltaTime));

            if (Input.GetKeyDown(KeyCode.B)) showBag = !showBag;
            if (popup != null || ex.End != ExplorationEnd.None) return;

            // 화면 방향 (위·아래·왼쪽·오른쪽)으로 가장 잘 맞는 이웃 노드를 고른다
            Vector2 dir = Vector2.zero;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dir = Vector2.up;
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dir = Vector2.down;
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) dir = Vector2.left;
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) dir = Vector2.right;
            if (dir != Vector2.zero)
            {
                MapNode best = null;
                float bestDot = 0.5f;
                Vector3 here = NodeMid(ex.X, ex.Y);
                foreach (var nb in ex.Neighbors)
                {
                    Vector2 v = NodeMid(nb.x, nb.y) - here;
                    float dot = Vector2.Dot(v.normalized, dir);
                    if (dot > bestDot) { bestDot = dot; best = nb; }
                }
                if (best != null) TryMove(best);
                else Toast("그쪽으로 이어진 길이 없어요.");
            }
            else if (Input.GetMouseButtonDown(0) && !MouseOverUi())
            {
                Vector3 w = cam.ScreenToWorldPoint(Input.mousePosition);
                int cx, cy;
                ScreenCell(w, out cx, out cy);
                var clicked = map.NodeAt(cx, cy);
                if (clicked != null && (clicked.x != ex.X || clicked.y != ex.Y)) TryMove(clicked);
            }
        }

        void TryMove(MapNode target)
        {
            string why = ex.CannotMove(target);
            if (why != null) { Toast(why); return; }
            ex.Move(target);
            if (keepMapMemory) SaveMemory();

            var n = ex.CurrentNode;
            if (n != null && NeedsPopup(n)) popup = n;
            Refresh();
        }

        bool NeedsPopup(MapNode n)
        {
            switch (n.type)
            {
                case "start": return false;
                case "exit":
                case "merchant_camp":
                case "small_dungeon":
                case "big_dungeon":
                case "map_gate":
                    return true;
                default:
                    return !ex.IsCleared(n);
            }
        }

        // ───────── 색 갱신 ─────────

        void Refresh()
        {
            for (int y = 0; y < map.height; y++)
                for (int x = 0; x < map.width; x++)
                {
                    Color c = ColorOf(MapCatalog.Terrain(map.GetTerrain(x, y)).ColorHex);
                    switch (ex.ViewOf(x, y))
                    {
                        case CellView.Visible: break;
                        case CellView.Hint: c = Color.Lerp(c, Color.black, 0.6f); break;
                        case CellView.Remembered: c = Color.Lerp(Gray(c), Color.black, 0.35f); break;
                        default: c = UnknownColor; break;
                    }
                    if (TileSprite(MapCatalog.Terrain(map.GetTerrain(x, y)).Id, x, y) != null && c != UnknownColor)
                    {
                        // 그림이 있으면 그림 색을 그대로 쓰고 어둡기만 조절한다
                        var v = ex.ViewOf(x, y);
                        c = v == CellView.Visible ? Color.white : v == CellView.Hint ? new Color(0.4f, 0.4f, 0.4f) : new Color(0.55f, 0.55f, 0.6f);
                    }
                    tiles[x, y].color = c;
                }

            foreach (var kv in nodeMarks)
            {
                var n = kv.Key;
                var sr = kv.Value;
                var view = ex.NodeView(n);
                if (n.type == "start" || view == CellView.Unknown) { sr.enabled = false; continue; }
                sr.enabled = true;
                if (view == CellView.Hint) { sr.color = new Color(0.55f, 0.55f, 0.55f); continue; }

                var def = MapCatalog.NodeType(n.type);
                Color c = nodeArtById.ContainsKey(n.type) ? Color.white : def != null ? ColorOf(def.ColorHex) : Color.magenta;
                bool closedExit = n.type == "exit" && view == CellView.Visible && !ex.IsExitOpen(n);
                if (closedExit) c = new Color(0.35f, 0.4f, 0.35f);
                if (ex.IsCleared(n)) c = Color.Lerp(c, Color.black, 0.55f);
                if (view == CellView.Remembered) c = Color.Lerp(c, Color.black, 0.3f);
                sr.color = c;
            }

            var reachable = ex.Neighbors;
            foreach (var kv in linkMarks)
            {
                var l = kv.Key;
                var sr = kv.Value;
                var view = ex.LinkView(l);
                bool fromHere = l.Touches(ex.X, ex.Y);
                sr.enabled = view != CellView.Unknown;
                Color c = fromHere ? new Color(1f, 0.85f, 0.25f) : new Color(1f, 0.96f, 0.84f);
                if (view == CellView.Hint && !fromHere) c.a = 0.45f;
                if (view == CellView.Remembered) c = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                sr.color = c;
                sr.transform.localScale = new Vector3(sr.transform.localScale.x, fromHere ? 0.06f : 0.035f, 1);
            }
            // 갈 수 있는 이웃 노드는 조금 크게
            foreach (var kv in nodeMarks)
            {
                float baseSize = nodeArtById.ContainsKey(kv.Key.type) ? 1f : 0.6f;
                float k = reachable.Contains(kv.Key) && ex.End == ExplorationEnd.None ? 1.2f : 1f;
                kv.Value.transform.localScale = new Vector3(baseSize * k, baseSize * k, 1);
            }

            playerTarget = playerSprite != null ? CellBase(ex.X, ex.Y) : CellCenter(ex.X, ex.Y) + new Vector3(0, 0.05f, 0);
            if (ex.Steps == 0) player.transform.position = playerTarget;
            player.sortingOrder = Depth(ex.X, ex.Y, 2);
            foreach (var r in player.GetComponentsInChildren<SpriteRenderer>())
                if (r != player) r.sortingOrder = player.sortingOrder - 1;
        }

        // ───────── 화면 글자와 창 ─────────

        void OnGUI()
        {
            EnsureStyles();
            if (loadError != null)
            {
                GUI.Label(new Rect(20, 20, 600, 60), loadError, titleStyle);
                return;
            }
            if (ex == null) return;

            DrawNodeMarks();

            float scale = Mathf.Max(1f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float sw = Screen.width / scale, sh = Screen.height / scale;

            DrawHud();
            DrawLog(sh);
            if (showBag) DrawBag(sw);
            if (ex.End != ExplorationEnd.None) DrawEnd(sw, sh);
            else if (popup != null) DrawPopup(sw, sh);

            if (toast != null && Time.time < toastUntil)
                GUI.Label(new Rect(sw / 2 - 200, sh - 140, 400, 30), toast, boxStyle);

            GUI.matrix = Matrix4x4.identity;
        }

        void DrawNodeMarks()
        {
            float ppu = Screen.height / (2f * cam.orthographicSize);
            markStyle.fontSize = Mathf.Clamp((int)(ppu * 0.2f), 7, 32);
            foreach (var kv in nodeMarks)
            {
                if (!kv.Value.enabled) continue;
                var n = kv.Key;
                var view = ex.NodeView(n);
                if (nodeArtById.ContainsKey(n.type) && view != CellView.Hint) continue; // 그림이 있으면 글자는 안 쓴다
                string mark;
                if (view == CellView.Hint) mark = "?";
                else
                {
                    var def = MapCatalog.NodeType(n.type);
                    mark = def != null ? def.Mark : "!";
                }
                Vector3 sp = cam.WorldToScreenPoint(CellCenter(n.x, n.y) + new Vector3(0, TileH * 0.2f, 0));
                var r = new Rect(sp.x - ppu * TileW / 2, Screen.height - sp.y - ppu * TileH / 2, ppu * TileW, ppu * TileH);
                markStyle.normal.textColor = Brightness(kv.Value.color) > 0.6f ? Color.black : Color.white;
                GUI.Label(r, mark, markStyle);
            }
        }

        void DrawHud()
        {
            GUILayout.BeginArea(new Rect(10, 10, 280, 230), boxStyle);
            GUILayout.Label(map.name, titleStyle);
            GUILayout.Label("체력  " + ex.Hp + " / " + config.maxHp);
            GUILayout.Label("재화  " + ex.Money);
            string block = ex.BlockedSlots >= 3 ? "  (이동 불가)" : ex.BlockedSlots > 0 ? "  (전투 슬롯 " + ex.BlockedSlots + "개 막힘)" : "";
            GUILayout.Label("무게  " + ex.Weight + " / " + config.maxWeight + block);
            GUILayout.Label("토템  " + ex.Totems + " / " + config.maxTotems + "     이동  " + ex.Steps);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(showBag ? "배낭 닫기 (B)" : "배낭 열기 (B)")) showBag = !showBag;
            if (GUILayout.Button("지도 기억 지우기")) pendingReset = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawLog(float sh)
        {
            int count = Mathf.Min(5, ex.Log.Count);
            if (count == 0) return;
            GUILayout.BeginArea(new Rect(10, sh - 20 - count * 22, 520, count * 22 + 10), boxStyle);
            for (int i = ex.Log.Count - count; i < ex.Log.Count; i++) GUILayout.Label(ex.Log[i]);
            GUILayout.EndArea();
        }

        void DrawBag(float sw)
        {
            GUILayout.BeginArea(new Rect(sw - 290, 10, 280, 420), boxStyle);
            GUILayout.Label("배낭  (무게 " + ex.Weight + " / " + config.maxWeight + ")", titleStyle);
            if (ex.Bag.Count == 0) GUILayout.Label("비어 있어요.");
            int drop = -1;
            for (int i = 0; i < ex.Bag.Count && i < 15; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(ex.Bag[i].name + "  (무게 " + ex.Bag[i].weight + ")");
                if (GUILayout.Button("버리기", GUILayout.Width(60))) drop = i;
                GUILayout.EndHorizontal();
            }
            if (ex.Bag.Count > 15) GUILayout.Label("... 외 " + (ex.Bag.Count - 15) + "개");
            GUILayout.EndArea();
            if (drop >= 0) { ex.DropItem(drop); Refresh(); }
        }

        void DrawPopup(float sw, float sh)
        {
            var n = popup;
            var def = MapCatalog.NodeType(n.type);
            GUILayout.BeginArea(new Rect(sw / 2 - 210, sh / 2 - 150, 420, 300), boxStyle);
            GUILayout.Label(Exploration.Label(n), titleStyle);
            if (!string.IsNullOrEmpty(n.note)) GUILayout.Label(n.note);
            GUILayout.Space(6);

            bool close = false;
            switch (n.type)
            {
                case "battle":
                case "elite":
                {
                    int lv = ex.DifficultyOf(n);
                    GUILayout.Label("적: " + MapCatalog.FamilyName(ex.EnemyOf(n)) + " (" + MapCatalog.DifficultyNames[lv + 1] + ")");
                    GUILayout.Label("전투 칸: " + map.BattleCellsOf(n) + "칸");
                    GUILayout.Label("(전투 화면은 아직 없어서 결과를 직접 골라요)");
                    GUILayout.FlexibleSpace();
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("이겼다  (체력 -" + config.battleDamage[lv] + ")", GUILayout.Height(32))) { ex.ResolveBattle(n, true); close = true; }
                    if (GUILayout.Button("졌다", GUILayout.Height(32))) { ex.ResolveBattle(n, false); close = true; }
                    GUILayout.EndHorizontal();
                    break;
                }
                case "farm":
                    GUILayout.Label("쓸 만한 것을 찾아볼 수 있어요.");
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("뒤지기  (재료 " + config.farmMaterials + "개, 재화 " + config.farmMoney + ")", GUILayout.Height(32))) { ex.Farm(n); close = true; }
                    if (GUILayout.Button("그냥 지나가기")) close = true;
                    break;
                case "totem":
                    GUILayout.Label("토템이 놓여 있어요. (종류는 아직 미정)");
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("가져가기", GUILayout.Height(32))) { if (ex.TakeTotem(n)) close = true; }
                    if (GUILayout.Button("그냥 지나가기")) close = true;
                    break;
                case "merchant_camp":
                    GUILayout.Label("떠돌이 상인이 불을 피워 두었어요. 상점은 아직 없어요.");
                    GUILayout.FlexibleSpace();
                    GUI.enabled = ex.CanRest;
                    if (GUILayout.Button("휴식  (체력 " + Mathf.RoundToInt(config.maxHp * config.restHealRate) + " 회복, 탐험당 1번)", GUILayout.Height(32))) ex.Rest();
                    GUI.enabled = true;
                    if (GUILayout.Button("떠나기")) close = true;
                    break;
                case "exit":
                    if (ex.IsExitOpen(n))
                    {
                        GUILayout.Label("빠져나갈 수 있는 길이에요. 탈출하면 가진 것을 거점으로 가져가요.");
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("탈출하기", GUILayout.Height(32))) { ex.Escape(n); close = true; }
                        if (GUILayout.Button("계속 탐험하기")) close = true;
                    }
                    else
                    {
                        GUILayout.Label("이번에는 길이 막혀 있어요. 다른 탈출 지점을 찾아야 해요.");
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("떠나기")) close = true;
                    }
                    break;
                case "small_dungeon":
                case "big_dungeon":
                case "map_gate":
                {
                    string what = n.type == "map_gate" ? "다른 맵으로 가는 길" : "던전 입구";
                    GUILayout.Label(what + (string.IsNullOrEmpty(n.target) ? "" : "  (" + n.target + ")"));
                    GUILayout.Label("아직 들어갈 수 없어요. 다음 단계에서 만들어요.");
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("떠나기")) close = true;
                    break;
                }
                default:
                    GUILayout.Label((def != null ? def.Name : n.type) + " (내용은 아직 미정)");
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("확인", GUILayout.Height(32))) { ex.Visit(n); close = true; }
                    break;
            }
            GUILayout.EndArea();

            if (close) { popup = null; Refresh(); }
        }

        void DrawEnd(float sw, float sh)
        {
            GUILayout.BeginArea(new Rect(sw / 2 - 210, sh / 2 - 110, 420, 220), boxStyle);
            GUILayout.Label(ex.End == ExplorationEnd.Escaped ? "탈출 성공" : "쓰러졌다", titleStyle);
            GUILayout.Label(ex.Log.Count > 0 ? ex.Log[ex.Log.Count - 1] : "");
            GUILayout.Label("이동 " + ex.Steps + "번,  가져간 재화 " + ex.Money + ",  물품 " + ex.Bag.Count + "개");
            GUILayout.Label("(거점과 창고는 아직 없어요. 지도 기억은 남아요.)");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("다시 출발", GUILayout.Height(34))) pendingRestart = true;
            GUILayout.EndArea();
        }

        // ───────── 지도 기억 파일 ─────────

        string MemoryPath { get { return Path.Combine(Application.persistentDataPath, "map_memory_" + map.id + ".txt"); } }

        MapMemory LoadMemory()
        {
            try
            {
                if (File.Exists(MemoryPath)) return MapMemory.Load(map.width, map.height, File.ReadAllText(MemoryPath));
            }
            catch (System.Exception e) { Debug.LogWarning("지도 기억을 읽지 못했어요: " + e.Message); }
            return null;
        }

        void SaveMemory()
        {
            try { File.WriteAllText(MemoryPath, ex.Memory.Save()); }
            catch (System.Exception e) { Debug.LogWarning("지도 기억을 저장하지 못했어요: " + e.Message); }
        }

        void DeleteMemory()
        {
            try { if (File.Exists(MemoryPath)) File.Delete(MemoryPath); }
            catch (System.Exception e) { Debug.LogWarning(e.Message); }
        }

        // ───────── 도움 함수 ─────────

        // 칸 (x, y) 의 마름모 가운데. x 가 늘면 화면 오른쪽 아래, y 가 늘면 왼쪽 아래로 간다.
        static Vector3 CellCenter(int x, int y)
        {
            return new Vector3((x - y) * TileW / 2f, -(x + y) * TileH / 2f, 0);
        }

        // 마름모 아래 꼭짓점. 타일·노드 그림의 기준점(피벗: 아래 가운데)을 여기에 둔다.
        static Vector3 CellBase(int x, int y)
        {
            return CellCenter(x, y) - new Vector3(0, TileH / 2f, 0);
        }

        // 화면 위치 -> 칸
        static void ScreenCell(Vector3 world, out int x, out int y)
        {
            float a = world.x / (TileW / 2f);     // x - y
            float b = -world.y / (TileH / 2f);    // x + y
            x = Mathf.RoundToInt((a + b) / 2f);
            y = Mathf.RoundToInt((b - a) / 2f);
        }

        // 앞뒤 순서: 화면 아래쪽(x + y 가 큰 칸)이 앞. layer 0 타일, 1 노드, 2 말
        // 길 선이 타일에 가리지 않도록 노드·말은 모든 타일보다 위에 둔다 (보드게임 말처럼)
        const int LinkOrder = 20000;
        static int Depth(int x, int y, int layer)
        {
            return (x + y) * 4 + layer + (layer >= 1 ? LinkOrder + 1 : 0);
        }

        // 길 선의 끝점: 노드 표시가 놓이는 높이
        static Vector3 NodeMid(int x, int y)
        {
            return CellCenter(x, y) + new Vector3(0, TileH * 0.2f, 0);
        }

        void IndexArt()
        {
            tileArtById.Clear();
            nodeArtById.Clear();
            foreach (var a in tileArt)
            {
                if (a == null || a.sprite == null || string.IsNullOrEmpty(a.id)) continue;
                List<Sprite> list;
                if (!tileArtById.TryGetValue(a.id, out list)) tileArtById[a.id] = list = new List<Sprite>();
                list.Add(a.sprite);
            }
            foreach (var a in nodeArt)
                if (a != null && a.sprite != null && !string.IsNullOrEmpty(a.id)) nodeArtById[a.id] = a.sprite;
        }

        // 같은 지형에 그림이 여러 장이면 칸마다 정해진 하나를 고른다 (매번 같은 그림)
        Sprite TileSprite(string terrainId, int x, int y)
        {
            List<Sprite> list;
            if (!tileArtById.TryGetValue(terrainId, out list) || list.Count == 0) return null;
            int h = (x * 73856093) ^ (y * 19349663);
            if (h < 0) h = -h;
            return list[h % list.Count];
        }

        void SnapCamera()
        {
            cam.transform.position = CellCenter(ex.X, ex.Y) + new Vector3(0, 0, -10);
        }

        SpriteRenderer NewSprite(string name, Transform parent, Vector3 pos, Sprite sprite, float size, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size, size, 1);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        // 임시 타일: 2:1 마름모, 피벗은 아래 꼭짓점. 가로 1월드 단위 = tilePixelWidth 픽셀
        static Sprite MakeDiamond(int widthPx)
        {
            int w = Mathf.Max(8, widthPx), h = w / 2;
            var tex = new Texture2D(w, h) { filterMode = FilterMode.Point };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - w / 2f) / (w / 2f);
                    float dy = Mathf.Abs(y + 0.5f - h / 2f) / (h / 2f);
                    px[y * w + x] = dx + dy <= 1f ? Color.white : new Color(1, 1, 1, 0);
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), w);
        }

        bool MouseOverUi()
        {
            float scale = Mathf.Max(1f, Screen.height / 900f);
            Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y) / scale;
            if (new Rect(10, 10, 280, 230).Contains(m)) return true;
            if (showBag && new Rect(Screen.width / scale - 290, 10, 280, 420).Contains(m)) return true;
            return false;
        }

        void Toast(string s) { toast = s; toastUntil = Time.time + 2f; }

        readonly Dictionary<string, Color> colorCache = new Dictionary<string, Color>();
        Color ColorOf(string hex)
        {
            Color c;
            if (!colorCache.TryGetValue(hex, out c))
            {
                if (!ColorUtility.TryParseHtmlString(hex, out c)) c = Color.magenta;
                colorCache[hex] = c;
            }
            return c;
        }

        static Color Gray(Color c) { float g = Brightness(c); return Color.Lerp(c, new Color(g, g, g), 0.6f); }
        static float Brightness(Color c) { return 0.299f * c.r + 0.587f * c.g + 0.114f * c.b; }

        void EnsureStyles()
        {
            if (markStyle != null) return;
            markStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
            boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8), fontSize = 14, wordWrap = true };
            boxStyle.normal.textColor = Color.white;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = true };
            titleStyle.normal.textColor = Color.white;
        }
    }
}
