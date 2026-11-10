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
using CardGame.Core;
using CardGame.Core.Battle;
using CardGame.Core.Map;
using CardGame.View.Battle;
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

        [Header("전투 (비워 두면 Assets/Data 에서 바로 읽음)")]
        public TextAsset cardsFile;
        [Tooltip("임시 시험 몬스터·패턴·덱 (규칙집 아님)")]
        public TextAsset testBattleFile;
        public string testMonsterId = "swordsman";
        public string testDeckId = "sword_test";
        public BattleConfig battleConfig = new BattleConfig();

        [Header("이벤트 (비워 두면 Assets/Data/events.json 을 바로 읽음)")]
        public TextAsset eventsFile;

        [Header("장비 (비워 두면 Assets/Data/equipment.json 을 바로 읽음)")]
        public TextAsset equipmentFile;
        EquipmentFile equipData;
        Loadout gear;

        [Header("잉크 지도")]
        [Tooltip("켜면 쿼터뷰 타일 대신 잉크 지도(InkMapView)로 보여 준다")]
        public bool inkMap;
        InkMapView ink;
        MapNode inkPendingPopup;   // 펜이 길을 다 그린 뒤 열 노드 창
        MapNode inkLastNode;       // 펜 그림 없이 위치가 바뀌었는지(이벤트로 되돌아가기 등) 보려고
        bool inkRunEnded;
        float inkAspect;           // 지도 크기를 맞춘 화면 비율
        float inkZoomTarget = -1f; // 이웃까지 보이게 물러날 카메라 크기 (0 이면 할 일 없음, -1 이면 아직 계산 전)
        bool inkWasMoving;

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

        BattleDatabase battleDb;
        string battleDbError;
        BattleView battleView;
        System.Action pendingBattleEnd;   // 전투 화면이 닫힐 때 Update 에서 처리

        MapNode popup;        // 지금 열려 있는 노드 창
        EventOutcome eventResult;   // 이벤트 창에서 고른 결과 (창을 닫을 때까지 보여 줌)
        List<EventDef> eventDefs = new List<EventDef>();
        bool showBag;
        bool pendingReset, pendingRestart;
        string loadError;
        string toast;
        float toastUntil;
        GUIStyle markStyle, boxStyle, titleStyle, buttonStyle;

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

            if (mapFile == null) { loadError = "Map Player 오브젝트에 맵 파일(mapFile)이 없다."; return; }
            try
            {
                sourceMap = JsonUtility.FromJson<MapData>(mapFile.text);
                sourceMap.FixRows();
                map = sourceMap;
                square = MakeDiamond(tilePixelWidth);
                IndexArt();
                if (inkMap)
                {
                    ink = gameObject.AddComponent<InkMapView>();
                    ink.Init(sourceMap, cam);   // 랜덤 칸을 굴리기 전 원본
                    cam.orthographicSize = ink.StartSize(cam.aspect);   // 큰 맵은 캐릭터 둘레만 크게
                }
                else BuildTiles();
                LoadEventData();
                LoadEquipment();
                NewExploration();
                LoadBattleData();
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
            ex.SetEvents(eventDefs);
            gear = Loadout.Start(equipData);   // 탐험마다 시작 장비로
            eventResult = null;
            popup = null;
            showBag = false;
            if (ink != null)
            {
                ink.BeginRun(ex);
                inkPendingPopup = null;
                inkLastNode = ex.CurrentNode;
                inkRunEnded = false;
                inkZoomTarget = -1f;
            }
            else
            {
                BuildNodes();
                Refresh();
            }
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
                if (ink != null) ink.ClearMemory();
                NewExploration();
                Toast("지도 기억을 지우고 다시 시작했다.");
            }
            if (pendingRestart) { pendingRestart = false; NewExploration(); }
            if (pendingBattleEnd != null) { var a = pendingBattleEnd; pendingBattleEnd = null; a(); }
            bool inBattle = battleView != null && battleView.Active;
            if (ink != null) ink.enabled = !inBattle;   // 전투 중에는 지도를 그리지 않는다
            if (inBattle) return;   // 전투 중에는 맵 입력을 받지 않는다

            if (ink != null) { UpdateInk(); return; }

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
                else Toast("그쪽으로는 길이 없다.");
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

        // 잉크 지도일 때의 매 프레임: 카메라, 갈 수 있는 길 표시, 입력
        void UpdateInk()
        {
            float fit = ink.FitSize(cam.aspect);
            // 창 크기가 바뀌면(전체 화면으로 커질 때 등) 지도 전체가 보이게 다시 맞춘다
            if (!Mathf.Approximately(cam.aspect, inkAspect)) { inkAspect = cam.aspect; cam.orthographicSize = ink.StartSize(cam.aspect); }
            // 도착할 때마다(그리고 탐험 시작 때) 이웃 노드가 화면 밖이면 그만큼만 물러난다. 휠을 쓰면 그 크기를 따른다
            if (!ink.Moving && (inkWasMoving || inkZoomTarget < 0f))
                inkZoomTarget = Mathf.Min(fit, Mathf.Max(cam.orthographicSize, ink.SizeToShowNext(cam.aspect)));
            inkWasMoving = ink.Moving;
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (wheel != 0 && !pixelPerfect)
            {
                cam.orthographicSize = Mathf.Clamp(cam.orthographicSize * (1f - wheel * 2f), Mathf.Min(1.5f, fit), fit);   // 휠: 지금 크기의 비율로 확대·축소
                inkZoomTarget = 0f;
            }
            if (inkZoomTarget > 0f && !pixelPerfect)
            {
                cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, inkZoomTarget, 1f - Mathf.Exp(-4.5f * Time.deltaTime));
                if (Mathf.Abs(cam.orthographicSize - inkZoomTarget) < 0.01f) inkZoomTarget = 0f;
            }
            // 창이 떠 있으면 내 핀이 화면 위쪽 절반에 오게 카메라를 조금 아래로 둔다 (창은 화면 아래쪽)
            Vector3 focus = ink.FocusWorld;
            if (popup != null) focus.y -= cam.orthographicSize * 0.4f;
            Vector3 target = ink.ClampToMap(focus, cam.orthographicSize, cam.aspect) + new Vector3(0, 0, -10);

            // 지도를 누른 채 끌면 화면이 따라 움직인다 (그동안은 캐릭터를 따라가지 않는다).
            // 짧게 누르고 떼면 노드 클릭. 다음에 이동하거나 Space 를 누르면 다시 캐릭터 쪽으로 돌아온다
            if (Input.GetMouseButtonDown(0) && !MouseOverUi()) { inkPressed = true; inkDragging = false; inkPressPos = inkLastMouse = Input.mousePosition; }
            if (inkPressed && Input.GetMouseButton(0))
            {
                if (!inkDragging && (Input.mousePosition - inkPressPos).magnitude > 6f) inkDragging = true;
                if (inkDragging)
                {
                    Vector3 delta = cam.ScreenToWorldPoint(inkLastMouse) - cam.ScreenToWorldPoint(Input.mousePosition);
                    Vector3 p = ink.ClampToMap(cam.transform.position + delta, cam.orthographicSize, cam.aspect);
                    cam.transform.position = new Vector3(p.x, p.y, -10);
                    inkFreeCam = true;
                }
                inkLastMouse = Input.mousePosition;
            }
            bool clicked = false;
            if (inkPressed && Input.GetMouseButtonUp(0)) { clicked = !inkDragging; inkPressed = false; inkDragging = false; }
            if (Input.GetKeyDown(KeyCode.Space) || ink.Moving) inkFreeCam = false;
            if (!inkFreeCam) cam.transform.position = Vector3.Lerp(cam.transform.position, target, 1f - Mathf.Exp(-10f * Time.deltaTime));

            // 이벤트 등으로 펜 그림 없이 자리가 바뀌었으면 그쪽으로 그려 준다
            var cur = ex.CurrentNode;
            if (!ink.Moving && cur != inkLastNode)
            {
                ink.StartMove(inkLastNode, cur);
                inkLastNode = cur;
            }
            // 펜이 다 그린 뒤에 노드 창을 연다
            if (!ink.Moving && inkPendingPopup != null) { popup = inkPendingPopup; inkPendingPopup = null; eventResult = null; }
            // 탐험이 끝나면 한 번만 이번 잉크를 기억으로 넘긴다
            if (ex.End != ExplorationEnd.None && !inkRunEnded) { inkRunEnded = true; ink.EndRun(ex.End); }

            bool canAct = !ink.Moving && popup == null && ex.End == ExplorationEnd.None;
            var nbs = ex.Neighbors;
            ink.candidates = canAct ? nbs : null;
            MapNode hover = null;
            if (canAct && !MouseOverUi())
            {
                var h = ink.NodeNear(cam.ScreenToWorldPoint(Input.mousePosition), 20f);
                if (h != null && nbs.Contains(h)) hover = h;
            }
            ink.hover = hover;

            if (Input.GetKeyDown(KeyCode.B)) showBag = !showBag;
            if (!canAct) return;

            Vector2 dir = Vector2.zero;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dir = Vector2.up;
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dir = Vector2.down;
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) dir = Vector2.left;
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) dir = Vector2.right;
            if (dir != Vector2.zero)
            {
                MapNode best = null;
                float bestDot = 0.5f;
                Vector3 here = ink.NodeWorld(ex.X, ex.Y);
                foreach (var nb in nbs)
                {
                    Vector2 v = ink.NodeWorld(nb.x, nb.y) - here;
                    float dot = Vector2.Dot(v.normalized, dir);
                    if (dot > bestDot) { bestDot = dot; best = nb; }
                }
                if (best != null) TryMove(best);
                else Toast("그쪽으로는 길이 없다.");
            }
            else if (clicked && hover != null) TryMove(hover);
        }

        bool inkFreeCam, inkPressed, inkDragging;   // 드래그로 화면을 옮긴 상태 / 누르는 중 / 끄는 중
        Vector3 inkPressPos, inkLastMouse;

        void TryMove(MapNode target)
        {
            string why = ex.CannotMove(target);
            if (why != null) { Toast(why); return; }
            var from = ex.CurrentNode;
            ex.Move(target);
            if (keepMapMemory) SaveMemory();

            var n = ex.CurrentNode;
            if (ink != null)
            {
                ink.StartMove(from, n);
                inkLastNode = n;
                if (n != null && NeedsPopup(n)) inkPendingPopup = n;
                return;
            }
            if (n != null && NeedsPopup(n)) { popup = n; eventResult = null; }
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
            if (ink != null) return;   // 잉크 지도는 InkMapView 가 매 프레임 그린다
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
            if (battleView != null && battleView.Active) return;   // 전투 화면이 덮는다

            DrawNodeMarks();

            // 세로 900 기준으로 맞춘다. 작은 창에서는 같이 줄어들어 화면 밖으로 나가지 않는다
            uiScale = Mathf.Clamp(Screen.height / 900f, 0.6f, 2f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1));
            float sw = Screen.width / uiScale, sh = Screen.height / uiScale;

            DrawHud();
            DrawLog(sw, sh);
            bagRect = Rect.zero; popupRect = Rect.zero;
            if (showBag) DrawBag(sw, sh);
            if (ex.End != ExplorationEnd.None) DrawEnd(sw, sh);
            else if (popup != null) DrawPopup(sw, sh);

            if (toast != null && Time.time < toastUntil)
            {
                var tr = new Rect(sw / 2 - 220, UiMargin, 440, 36);
                GUI.Box(tr, GUIContent.none, panelStyle);
                GUI.Label(tr, toast, centerStyle);
            }

            GUI.matrix = Matrix4x4.identity;
        }

        // ───────── 종이 UI ─────────
        // 상자·버튼은 코드로 만든 임시 종이 모양. panelArt / buttonArt 에 그림을 넣으면 그 그림으로 바뀐다

        const float UiMargin = 12f;
        float uiScale = 1f;
        Rect hudRect, logRect, bagRect, popupRect;

        [Header("UI 그림 (비워 두면 코드로 만든 종이 모양)")]
        [Tooltip("상자 바탕 그림. 가장자리 8픽셀은 늘어나지 않는다 (9칸 자르기)")]
        public Texture2D panelArt;
        public Texture2D buttonArt;

        static readonly Color InkText = new Color32(59, 42, 26, 255);
        static readonly Color InkFaint = new Color32(110, 84, 58, 255);
        static readonly Color HpColor = new Color32(176, 52, 38, 255);
        static readonly Color WeightColor = new Color32(120, 90, 54, 255);
        Texture2D coinIcon, totemIcon, bar;

        // 막대 하나: 바탕(옅은 갈색) 위에 비율만큼 색을 채우고 글씨를 얹는다
        void Bar(Rect r, float t, Color fill, string text)
        {
            var old = GUI.color;
            GUI.color = new Color32(196, 176, 136, 255);
            GUI.DrawTexture(r, bar);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), bar);
            GUI.color = new Color32(57, 35, 18, 255);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), bar);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), bar);
            GUI.DrawTexture(new Rect(r.x, r.y, 1, r.height), bar);
            GUI.DrawTexture(new Rect(r.xMax - 1, r.y, 1, r.height), bar);
            GUI.color = old;
            // 글씨가 놓이는 가운데가 채워져 있으면 밝은 글씨, 비어 있으면 잉크색 글씨
            bool onFill = t >= 0.5f;
            Color light = new Color32(250, 244, 228, 255), dark = InkText;
            barShadowStyle.normal.textColor = onFill ? new Color(0.1f, 0.05f, 0.02f, 0.85f) : new Color(1f, 0.97f, 0.9f, 0.85f);
            barTextStyle.normal.textColor = onFill ? light : dark;
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, barShadowStyle);
            GUI.Label(r, text, barTextStyle);
        }

        void DrawHud()
        {
            hudRect = new Rect(UiMargin, UiMargin, 270, 164);
            GUI.Box(hudRect, GUIContent.none, panelStyle);
            float x = hudRect.x + 14, y = hudRect.y + 10, w = hudRect.width - 28;
            GUI.Label(new Rect(x, y, w, 26), map.name, titleStyle);
            y += 32;
            Bar(new Rect(x, y, w, 20), ex.Hp / (float)Mathf.Max(1, config.maxHp), HpColor, "체력  " + ex.Hp + " / " + config.maxHp);
            y += 26;
            string block = ex.BlockedSlots >= 3 ? "  · 이동 불가" : ex.BlockedSlots > 0 ? "  · 슬롯 " + ex.BlockedSlots + "개 막힘" : "";
            Bar(new Rect(x, y, w, 20), ex.Weight / (float)Mathf.Max(1, config.maxWeight), WeightColor, "무게  " + ex.Weight + " / " + config.maxWeight + block);
            y += 28;
            GUI.DrawTexture(new Rect(x, y + 3, 14, 14), coinIcon);
            GUI.Label(new Rect(x + 20, y, 100, 20), "재화 " + ex.Money, labelStyle);
            GUI.DrawTexture(new Rect(x + 110, y + 3, 14, 14), totemIcon);
            GUI.Label(new Rect(x + 130, y, 70, 20), "토템 " + ex.Totems + "/" + config.maxTotems, labelStyle);
            GUI.Label(new Rect(x + 190, y, 60, 20), "이동 " + ex.Steps, smallStyle);
            y += 28;
            if (GUI.Button(new Rect(x, y, w / 2 - 3, 28), showBag ? "배낭 닫기 (B)" : "배낭 (B)", buttonStyle)) showBag = !showBag;
            if (GUI.Button(new Rect(x + w / 2 + 3, y, w / 2 - 3, 28), "지도 기억 지우기", buttonStyle)) pendingReset = true;
        }

        void DrawLog(float sw, float sh)
        {
            int count = Mathf.Min(5, ex.Log.Count);
            logRect = Rect.zero;
            if (count == 0) return;
            // 긴 줄은 접어서 쓰고, 그만큼 상자를 키운다 (상자 밖으로 글씨가 나가지 않게)
            float w = Mathf.Min(560, sw - UiMargin * 2), tw = w - 28;
            var heights = new float[count];
            float h = 34;
            for (int i = 0; i < count; i++)
            {
                heights[i] = fadedStyle.CalcHeight(new GUIContent(ex.Log[ex.Log.Count - count + i]), tw);
                h += heights[i];
            }
            logRect = new Rect(UiMargin, sh - UiMargin - h, w, h);
            GUI.Box(logRect, GUIContent.none, panelStyle);
            GUI.Label(new Rect(logRect.x + 14, logRect.y + 6, tw, 20), "여정 기록", smallStyle);
            float ly = logRect.y + 26;
            for (int i = 0; i < count; i++)
            {
                var line = ex.Log[ex.Log.Count - count + i];
                GUI.Label(new Rect(logRect.x + 14, ly, tw, heights[i]), line, i == count - 1 ? labelStyle : fadedStyle);
                ly += heights[i];
            }
        }

        void DrawBag(float sw, float sh)
        {
            bagRect = new Rect(sw - UiMargin - 300, UiMargin, 300, Mathf.Min(600, sh - UiMargin * 2));
            GUILayout.BeginArea(bagRect, panelStyle);
            bagScroll = GUILayout.BeginScrollView(bagScroll);

            // 장비: 손에 든 무기 1칸 + 같은 모션 묶음의 예비 무기 (장비는 배낭 무게에 들어가지 않는다)
            GUILayout.Label("장비", titleStyle);
            GUILayout.BeginVertical(speechStyle);
            GUILayout.Label("무기", speakerStyle);
            GUILayout.Label(gear != null && gear.Equipped != null ? gear.Equipped.name : "(없음)", titleStyle);
            GUILayout.EndVertical();
            int equip = -1;
            if (gear != null && gear.Spare.Count > 0)
            {
                GUILayout.Label("바꿔 낄 수 있는 무기 (같은 모션)", smallStyle);
                for (int i = 0; i < gear.Spare.Count; i++)
                {
                    string why = gear.CannotEquip(i);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(gear.Spare[i].name + (why != null ? "  — " + why : ""), why != null ? fadedStyle : labelStyle);
                    GUI.enabled = why == null;
                    if (GUILayout.Button("장착", buttonStyle, GUILayout.Width(64), GUILayout.Height(24))) equip = i;
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.Label("그림만 바뀐다. 덱·수치는 그대로다.", smallStyle);
            GUILayout.Space(10);

            GUILayout.Label("배낭  (무게 " + ex.Weight + " / " + config.maxWeight + ")", titleStyle);
            if (ex.Bag.Count == 0) GUILayout.Label("비어 있다.", labelStyle);
            int drop = -1;
            for (int i = 0; i < ex.Bag.Count && i < 15; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(ex.Bag[i].name + "  (무게 " + ex.Bag[i].weight + ")", labelStyle);
                if (GUILayout.Button("버리기", buttonStyle, GUILayout.Width(64), GUILayout.Height(24))) drop = i;
                GUILayout.EndHorizontal();
            }
            if (ex.Bag.Count > 15) GUILayout.Label("... 외 " + (ex.Bag.Count - 15) + "개", smallStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (drop >= 0) { ex.DropItem(drop); Refresh(); }
            if (equip >= 0 && gear.Equip(equip)) Toast(Josa.Eul(gear.Equipped.name) + " 손에 들었다.");
        }

        Vector2 bagScroll;

        void LoadEquipment()
        {
            try
            {
                string text = equipmentFile != null ? equipmentFile.text : File.ReadAllText(Path.Combine(Application.dataPath, "Data/equipment.json"));
                equipData = JsonUtility.FromJson<EquipmentFile>(text);
            }
            catch (System.Exception e)
            {
                equipData = null;
                Debug.LogWarning("장비 데이터를 읽지 못했다: " + e.Message);
            }
        }

        // 말하는 사람과 대사 칸 (이벤트 창 위쪽)
        void Speech(string who, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            GUILayout.BeginVertical(speechStyle);
            if (!string.IsNullOrEmpty(who)) GUILayout.Label(who, speakerStyle);
            GUILayout.Label("“" + line + "”", quoteStyle);
            GUILayout.EndVertical();
            GUILayout.Space(4);
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

        void DrawPopup(float sw, float sh)
        {
            var n = popup;
            var def = MapCatalog.NodeType(n.type);
            var ev = n.type == "event" ? ex.EventAt(n) : null;
            bool talk = (ev != null && !string.IsNullOrEmpty(ev.line)) || n.type == "merchant_camp";
            float pw = Mathf.Min(500, sw - UiMargin * 2);
            float ph = Mathf.Min(sh - UiMargin * 2, (ev != null ? 380 : 300) + (talk ? 70 : 0));
            // 화면 아래쪽에 둔다 (위쪽 절반의 내 위치·지나온 길이 가리지 않게). 왼쪽 아래 기록 상자와 겹치면 그 오른쪽으로
            float px = sw / 2 - pw / 2;
            if (logRect.width > 0 && px < logRect.xMax + UiMargin && logRect.xMax + UiMargin + pw <= sw - UiMargin) px = logRect.xMax + UiMargin;
            float py = sh - UiMargin - ph;
            if (logRect.width > 0 && px < logRect.xMax + UiMargin) py = Mathf.Max(UiMargin, logRect.y - UiMargin - ph);
            popupRect = new Rect(px, py, pw, ph);
            GUILayout.BeginArea(popupRect, popupStyle);   // 반투명 종이: 뒤의 지도가 비친다
            GUILayout.Label(ev != null ? ev.title : (!string.IsNullOrEmpty(n.name) ? n.name : Exploration.Label(n)), titleStyle);
            GUILayout.Space(4);

            bool close = false;
            switch (n.type)
            {
                case "battle":
                case "elite":
                {
                    int lv = ex.DifficultyOf(n);
                    var mon = battleDb != null ? battleDb.Monster(testMonsterId) : null;
                    GUILayout.Label("적: " + (mon != null ? mon.name + " (임시 시험 몬스터)" : "?") + "   ·   원래 계열: " + MapCatalog.FamilyName(ex.EnemyOf(n)) + " (" + MapCatalog.DifficultyNames[lv + 1] + ")", labelStyle);
                    GUILayout.Label("전투 칸: " + map.BattleCellsOf(n) + "칸   ·   내 체력 " + ex.Hp + " / " + config.maxHp, labelStyle);
                    if (battleDbError != null) GUILayout.Label("전투 데이터를 읽지 못했다: " + battleDbError, labelStyle);
                    GUILayout.FlexibleSpace();
                    GUI.enabled = battleDbError == null;
                    if (GUILayout.Button("전투 시작", buttonStyle, GUILayout.Height(36))) StartBattle(n);
                    GUI.enabled = true;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("시험용 (결과만):", smallStyle, GUILayout.Width(150));
                    // 시험용: 결과만 내고 넘긴다. 체력은 그대로 (보상은 받는다)
                    if (GUILayout.Button("이겼다 (체력 그대로)", buttonStyle)) { ex.ResolveBattleResult(n, true, ex.Hp, 1, mon != null ? mon.name : null); close = true; }
                    if (GUILayout.Button("졌다", buttonStyle)) { ex.ResolveBattle(n, false); close = true; }
                    GUILayout.EndHorizontal();
                    break;
                }
                case "farm":
                    GUILayout.Label("쓸 만한 것이 남아 있는 듯하다.", labelStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("뒤지기  (재료 " + config.farmMaterials + "개, 재화 " + config.farmMoney + ")", buttonStyle, GUILayout.Height(32))) { ex.Farm(n); close = true; }
                    if (GUILayout.Button("그냥 지나가기", buttonStyle)) close = true;
                    break;
                case "totem":
                    GUILayout.Label("토템이 놓여 있다. (종류는 아직 미정)", labelStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("가져가기", buttonStyle, GUILayout.Height(32))) { if (ex.TakeTotem(n)) close = true; }
                    if (GUILayout.Button("그냥 지나가기", buttonStyle)) close = true;
                    break;
                case "merchant_camp":
                    Speech("떠돌이 상인", ex.CanRest ? "불 좀 쬐고 가게. 물건은… 아직 짐을 못 풀었어." : "푹 쉬었으면 슬슬 길을 나서게. 밤이 길어.");
                    GUILayout.Label("떠돌이 상인이 불을 피워 두었다. (상점은 아직 없다)", labelStyle);
                    GUILayout.FlexibleSpace();
                    GUI.enabled = ex.CanRest;
                    if (GUILayout.Button("휴식  (체력 " + Mathf.RoundToInt(config.maxHp * config.restHealRate) + " 회복, 탐험당 1번)", buttonStyle, GUILayout.Height(32))) ex.Rest();
                    GUI.enabled = true;
                    if (GUILayout.Button("떠나기", buttonStyle)) close = true;
                    break;
                case "exit":
                    if (ex.IsExitOpen(n))
                    {
                        GUILayout.Label("빠져나갈 수 있는 길이다. 탈출하면 가진 것을 거점으로 가져간다.", labelStyle);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("탈출하기", buttonStyle, GUILayout.Height(32))) { ex.Escape(n); close = true; }
                        if (GUILayout.Button("계속 탐험하기", buttonStyle)) close = true;
                    }
                    else
                    {
                        GUILayout.Label("이번에는 길이 막혀 있다. 다른 탈출 지점을 찾아야 할 듯하다.", labelStyle);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("떠나기", buttonStyle)) close = true;
                    }
                    break;
                case "small_dungeon":
                case "big_dungeon":
                case "map_gate":
                case "boss_spot":
                {
                    string what = n.type == "map_gate" ? "다른 맵으로 가는 길" : n.type == "boss_spot" ? "보스의 흔적이 짙게 남아 있다" : "던전 입구";
                    GUILayout.Label(what + (string.IsNullOrEmpty(n.target) ? "" : "  (" + n.target + ")"), labelStyle);
                    GUILayout.Label("아직 들어갈 수 없다. (다음 단계에서 만든다)", labelStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("떠나기", buttonStyle)) close = true;
                    break;
                }
                case "event" when ev != null:
                    close = DrawEvent(n, ev);
                    break;
                default:
                    GUILayout.Label((def != null ? def.Name : n.type) + " (내용은 아직 미정)", labelStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("확인", buttonStyle, GUILayout.Height(32))) { ex.Visit(n); close = true; }
                    break;
            }
            GUILayout.EndArea();

            if (close) { popup = null; eventResult = null; Refresh(); }
        }

        // 이벤트 창: 고르기 전에는 선택지, 고른 뒤에는 결과. 창을 닫아야 하면 true
        bool DrawEvent(MapNode n, EventDef ev)
        {
            if (eventResult == null)
            {
                GUILayout.Label(ev.text, labelStyle);
                GUILayout.Space(4);
                Speech(ev.speaker, ev.line);
                GUILayout.FlexibleSpace();
                for (int i = 0; i < ev.choices.Length; i++)
                {
                    var c = ev.choices[i];
                    string why = ex.CannotChoose(c);
                    GUI.enabled = why == null;
                    string text = c.label + (string.IsNullOrEmpty(c.hint) ? "" : "\n" + c.hint) + (why != null ? "  — " + why : "");
                    if (GUILayout.Button(text, buttonStyle, GUILayout.Height(44)))
                    {
                        eventResult = ex.ChooseEvent(n, i);
                        if (keepMapMemory) SaveMemory();
                        Refresh();
                    }
                    GUI.enabled = true;
                }
                return false;
            }

            Speech(eventResult.Speaker, eventResult.Reply);
            GUILayout.Label(eventResult.Text, labelStyle);
            GUILayout.FlexibleSpace();
            if (eventResult.Battle && ex.End == ExplorationEnd.None)
            {
                GUI.enabled = battleDbError == null;
                if (GUILayout.Button("전투 시작" + (eventResult.BattleMoneyMult > 1 ? "  (이기면 재화 " + eventResult.BattleMoneyMult + "배)" : ""), buttonStyle, GUILayout.Height(36)))
                    StartBattle(n, eventResult.BattleMoneyMult, eventResult.BattleEnemy);
                GUI.enabled = true;
                return false;
            }
            return GUILayout.Button("확인", buttonStyle, GUILayout.Height(32));
        }

        void LoadEventData()
        {
            try
            {
                string text = eventsFile != null ? eventsFile.text : File.ReadAllText(Path.Combine(Application.dataPath, "Data/events.json"));
                var file = JsonUtility.FromJson<EventsFile>(text);
                eventDefs = file != null && file.events != null ? new List<EventDef>(file.events) : new List<EventDef>();
            }
            catch (System.Exception e)
            {
                eventDefs = new List<EventDef>();
                Debug.LogWarning("이벤트 데이터를 읽지 못했어요: " + e.Message);
            }
        }

        void DrawEnd(float sw, float sh)
        {
            popupRect = new Rect(sw / 2 - 220, sh / 2 - 120, 440, 240);
            GUILayout.BeginArea(popupRect, panelStyle);
            GUILayout.Label(ex.End == ExplorationEnd.Escaped ? "탈출했다" : "쓰러졌다", titleStyle);
            GUILayout.Label(ex.Log.Count > 0 ? ex.Log[ex.Log.Count - 1] : "", labelStyle);
            GUILayout.Label("이동 " + ex.Steps + "번  ·  가져간 재화 " + ex.Money + "  ·  물품 " + ex.Bag.Count + "개", labelStyle);
            GUILayout.Label("(거점과 창고는 아직 없다. 지도 기억은 남는다.)", labelStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("다시 출발", buttonStyle, GUILayout.Height(34))) pendingRestart = true;
            GUILayout.EndArea();
        }

        // ───────── 전투 ─────────

        void LoadBattleData()
        {
            try
            {
                string cards = cardsFile != null ? cardsFile.text : File.ReadAllText(Path.Combine(Application.dataPath, "Data/cards.json"));
                string temp = testBattleFile != null ? testBattleFile.text : File.ReadAllText(Path.Combine(Application.dataPath, "Data/test_swordsman.json"));
                cards = BattleJson.Clean(cards);
                temp = BattleJson.Clean(temp);
                battleDb = new BattleDatabase(
                    JsonUtility.FromJson<CardsFile>(cards),
                    JsonUtility.FromJson<DecksFile>(temp),
                    JsonUtility.FromJson<MonstersFile>(temp),
                    JsonUtility.FromJson<PatternsFile>(temp));
                if (battleDb.Monster(testMonsterId) == null) battleDbError = "몬스터 " + Josa.I(testMonsterId) + " 없다.";
                else if (battleDb.Deck(testDeckId) == null) battleDbError = "덱 " + Josa.I(testDeckId) + " 없다.";
                foreach (var p in battleDb.Problems()) Debug.LogWarning("전투 데이터: " + p);
            }
            catch (System.Exception e)
            {
                battleDbError = e.Message;
                Debug.LogError("전투 데이터를 읽지 못했어요: " + e.Message);
            }
        }

        void StartBattle(MapNode n) { StartBattle(n, 1, null); }

        // moneyMult·enemyName: 이벤트 전투 (보상 배수, 무기 이름에 쓸 적 이름)
        void StartBattle(MapNode n, int moneyMult, string enemyName)
        {
            var deck = new List<string>();
            var d = battleDb.Deck(testDeckId);
            foreach (var e in d.cards) for (int i = 0; i < e.count; i++) deck.Add(e.card);
            var setup = new BattleSetup
            {
                Cells = map.BattleCellsOf(n),
                PlayerHp = ex.Hp,
                PlayerMaxHp = config.maxHp,
                PlayerDeck = deck,
                Monster = battleDb.Monster(testMonsterId),
                MonsterHp = ex.NextBattleEnemyHp > 0 ? battleDb.Monster(testMonsterId).hp + ex.NextBattleEnemyHp : 0,
                PlayerPoison = ex.NextBattlePoison,
                BlockedSlots = Mathf.Min(2, ex.BlockedSlots),
                Seed = System.Environment.TickCount,
            };
            ex.ClearNextBattleMods();   // 이벤트에서 받은 "다음 전투" 효과는 이번 전투에서 쓴다
            var engine = new BattleEngine(battleDb, battleConfig, setup);
            if (battleView == null) battleView = gameObject.AddComponent<BattleView>();
            battleView.PlayerWeaponLayer = gear != null ? gear.Layer : null;   // 장비창에서 든 무기 그림 (덱·수치는 그대로)
            battleView.Begin(engine, ex.NodeTitle(n), (won, hp) =>
            {
                pendingBattleEnd = () =>
                {
                    // 보상(무기 이름)은 실제로 싸운 몬스터 기준. 지금은 시험 몬스터 하나라 노드·이벤트의 적 이름 대신 이것을 쓴다
                    ex.ResolveBattleResult(n, won, hp, moneyMult, setup.Monster != null ? setup.Monster.name : enemyName);
                    if (keepMapMemory) SaveMemory();
                    popup = null;
                    eventResult = null;
                    Refresh();
                };
            });
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
            cam.transform.position = (ink != null ? ink.ClampToMap(ink.FocusWorld, cam.orthographicSize, cam.aspect) : CellCenter(ex.X, ex.Y)) + new Vector3(0, 0, -10);
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

        // 마우스가 UI 상자 위에 있는지 (지난 OnGUI 에서 그린 자리로 판단)
        bool MouseOverUi()
        {
            Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y) / Mathf.Max(0.01f, uiScale);
            return hudRect.Contains(m) || logRect.Contains(m) || bagRect.Contains(m) || popupRect.Contains(m);
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

            // 종이 상자: 바탕 종이색 + 갈색 테두리 두 줄. 그림(panelArt)이 있으면 그걸 쓴다
            var paper = new Color32(236, 222, 186, 182);   // 모든 UI 상자는 약 70% 진하기 (뒤의 지도가 비친다)
            var edge = new Color32(57, 35, 18, 255);
            var edge2 = new Color32(151, 102, 70, 255);
            panelStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft, padding = new RectOffset(16, 16, 12, 12), fontSize = 14, wordWrap = true,
                border = new RectOffset(8, 8, 8, 8),
            };
            panelStyle.normal.background = panelArt != null ? panelArt : PaperTexture(paper, edge, edge2, 7);
            panelStyle.normal.textColor = InkText;
            // 노드·이벤트 창: 같은 종이를 약 70% 진하기로 (뒤의 지도가 비쳐 보이게)
            popupStyle = new GUIStyle(panelStyle);
            popupStyle.normal.background = PaperTexture(new Color32(236, 222, 186, 182), edge, edge2, 9);

            buttonStyle = new GUIStyle(GUI.skin.button) { wordWrap = true, fontSize = 13, border = new RectOffset(6, 6, 6, 6), padding = new RectOffset(8, 8, 4, 4) };
            buttonStyle.normal.background = buttonArt != null ? buttonArt : PaperTexture(new Color32(222, 204, 164, 255), edge, edge2, 1);
            buttonStyle.hover.background = PaperTexture(new Color32(240, 228, 196, 255), edge, edge2, 2);
            buttonStyle.active.background = PaperTexture(new Color32(196, 172, 128, 255), edge, edge2, 3);
            buttonStyle.normal.textColor = buttonStyle.hover.textColor = buttonStyle.active.textColor = InkText;

            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            labelStyle.normal.textColor = InkText;
            fadedStyle = new GUIStyle(labelStyle);
            fadedStyle.normal.textColor = InkFaint;
            smallStyle = new GUIStyle(labelStyle) { fontSize = 12 };
            smallStyle.normal.textColor = InkFaint;
            centerStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter };
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, wordWrap = true };
            titleStyle.normal.textColor = InkText;
            boxStyle = panelStyle;

            // 대사 칸: 조금 더 짙은 종이 위에 말하는 사람 이름(빨간 잉크)과 대사(기울임)
            speechStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 6, 8), border = new RectOffset(6, 6, 6, 6), margin = new RectOffset(0, 0, 2, 2) };
            speechStyle.normal.background = PaperTexture(new Color32(222, 204, 164, 200), edge2, edge2, 4);
            speakerStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold, fontSize = 13 };
            speakerStyle.normal.textColor = new Color32(138, 42, 30, 255);
            quoteStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Italic };

            barTextStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false };
            // 막대 글씨: 잉크색 + 옅은 종이색 그림자 (채운 부분·빈 부분 어디서나 읽히게)
            barTextStyle.normal.textColor = InkText;
            barShadowStyle = new GUIStyle(barTextStyle);
            barShadowStyle.normal.textColor = new Color32(246, 236, 210, 220);

            bar = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            bar.SetPixel(0, 0, Color.white);
            bar.Apply();
            coinIcon = IconTexture(new Color32(222, 176, 60, 255), edge, true);
            totemIcon = IconTexture(new Color32(63, 160, 150, 255), edge, false);
        }

        GUIStyle popupStyle, panelStyle, labelStyle, fadedStyle, smallStyle, centerStyle, speechStyle, speakerStyle, quoteStyle, barTextStyle, barShadowStyle;

        // 임시 종이 그림: 고른 종이색, 바깥 갈색 테두리 2픽셀, 안쪽 옅은 테두리 1픽셀 (늘려도 얼룩이 뭉개지지 않게 바탕은 한 색)
        static Texture2D PaperTexture(Color32 fill, Color32 edge, Color32 inner, int seed)
        {
            const int S = 24;
            var t = new Texture2D(S, S) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(S - 1 - x, S - 1 - y));
                    Color32 c = fill;
                    if (d < 2) c = edge;
                    else if (d == 3) c = inner;
                    if (d == 0 && (x == 0 || x == S - 1) && (y == 0 || y == S - 1)) c.a = 0;   // 모서리를 살짝 둥글게
                    px[y * S + x] = c;
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // 작은 기호: 동전(원) 또는 토템(마름모), 테두리는 잉크색
        static Texture2D IconTexture(Color32 fill, Color32 edge, bool circle)
        {
            const int S = 14;
            var t = new Texture2D(S, S) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - S / 2f), dy = Mathf.Abs(y + 0.5f - S / 2f);
                    float d = circle ? Mathf.Sqrt(dx * dx + dy * dy) : dx + dy;
                    float r = S / 2f - 0.5f;
                    px[y * S + x] = d > r ? new Color32(0, 0, 0, 0) : d > r - 1.6f ? edge : (d < r * 0.45f && circle ? new Color32(246, 214, 110, 255) : fill);
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
