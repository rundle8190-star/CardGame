// 맵 편집기 창. 메뉴 CardGame > 맵 편집기 로 연다.
// 지형을 칠하고 노드를 찍어서 Assets/Data/Maps/<맵 id>.json 으로 저장한다.
//
// 조작
//   왼쪽 클릭·드래그 : 고른 도구 쓰기 (지형 칠하기, 노드 찍기, 선택·옮기기, 지우기)
//   오른쪽 클릭·드래그: 노드 지우기
//   가운데 버튼 드래그: 화면 옮기기
//   Ctrl + 휠       : 확대·축소
//   Ctrl+Z / Ctrl+Y  : 되돌리기 / 다시 하기,  Ctrl+S : 저장
//   1~4 키          : 지형 / 노드 / 선택 / 지우기 도구
//   ▶ 플레이        : 저장하고 이 맵으로 플레이 씬을 실행
using System.Collections.Generic;
using System.IO;
using CardGame.Core.Map;
using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    public class MapEditorWindow : EditorWindow
    {
        const string MapFolder = "Assets/Data/Maps";
        const string PrefLastPath = "CardGame.MapEditor.LastPath";
        const int PanelWidth = 270;
        const int MaxUndo = 100;

        enum Tool { Terrain, Node, Select, Erase, Link }
        static readonly string[] ToolNames = { "지형 (1)", "노드 (2)", "선택 (3)", "지우기 (4)", "길 (5)" };

        [SerializeField] MapData map;
        [SerializeField] string path;
        [SerializeField] bool dirty;
        [SerializeField] Tool tool = Tool.Terrain;
        [SerializeField] int terrainIndex;
        [SerializeField] int nodeIndex = 1;
        [SerializeField] int brush = 1;
        [SerializeField] int cellSize = 28;
        [SerializeField] bool showGrid = true;
        [SerializeField] bool showDistance;
        [SerializeField] bool showLinks = true;
        [SerializeField] int newWidth = 30;
        [SerializeField] int newHeight = 20;
        [SerializeField] List<string> undoStack = new List<string>();
        [SerializeField] List<string> redoStack = new List<string>();

        Vector2 canvasScroll;
        Vector2 panelScroll;
        MapNode selected;
        MapNode dragging;
        MapNode linkFrom;
        Vector2 hoverPos;          // 길 도구: 마지막으로 누른 노드 (다음 노드와 이어진다)
        bool dragUndoPushed;
        bool stroking;
        bool rightErasing;
        object inspectorUndoFor;
        Vector2Int hover = new Vector2Int(-1, -1);
        MapReport report;
        Dictionary<string, Color> colorCache;
        GUIStyle markStyle;

        // IMGUI 는 한 프레임 안의 Layout 단계와 다음 단계에서 그리는 칸 수가 같아야 한다.
        // 그래서 왼쪽 칸의 모양을 정하는 값은 Layout 단계에서 찍어 두고 그 값으로 그린다.
        Tool shownTool;
        MapNode shownSelected;
        string shownType;
        MapReport shownReport;
        bool shownHover;
        GUIStyle distStyle;

        [MenuItem("CardGame/맵 편집기")]
        public static void Open()
        {
            var w = GetWindow<MapEditorWindow>("맵 편집기");
            w.minSize = new Vector2(700, 450);
        }

        void OnEnable()
        {
            wantsMouseMove = true;
            selected = null;
            if (map == null || map.rows == null || map.rows.Length == 0)
            {
                string last = EditorPrefs.GetString(PrefLastPath, "");
                if (!string.IsNullOrEmpty(last) && File.Exists(last)) LoadFrom(last);
                else NewMap(newWidth, newHeight);
            }
            map.FixRows();
            RefreshReport();
        }

        void OnDestroy()
        {
            if (dirty && map != null &&
                EditorUtility.DisplayDialog("맵 편집기", "저장하지 않은 변경이 있어요. 저장할까요?", "저장", "저장 안 함"))
                Save();
        }

        // ───────────────────────── 화면 ─────────────────────────

        void OnGUI()
        {
            if (map == null) NewMap(newWidth, newHeight);
            EnsureStyles();
            if (report == null) RefreshReport();
            if (Event.current.type == EventType.Layout)
            {
                shownTool = tool;
                shownSelected = selected != null && map.nodes.Contains(selected) ? selected : null;
                shownType = shownSelected != null ? shownSelected.type : null;
                shownReport = report;
                shownHover = hover.x >= 0;
            }
            HandleShortcuts(Event.current);

            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawPanel();
            DrawCanvas();
            EditorGUILayout.EndHorizontal();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("새 맵", EditorStyles.toolbarButton, GUILayout.Width(50)))
                Defer(() => { if (ConfirmDiscard()) { NewMap(newWidth, newHeight); GUI.FocusControl(null); } });
            if (GUILayout.Button("열기", EditorStyles.toolbarDropDown, GUILayout.Width(50))) ShowOpenMenu();
            if (GUILayout.Button("저장", EditorStyles.toolbarButton, GUILayout.Width(45))) Defer(Save);
            if (GUILayout.Button("다른 이름으로 저장", EditorStyles.toolbarButton, GUILayout.Width(110))) Defer(SaveAs);
            if (GUILayout.Button("▶ 플레이", EditorStyles.toolbarButton, GUILayout.Width(60)))
                Defer(() =>
                {
                    if (dirty || string.IsNullOrEmpty(path)) Save();
                    if (!dirty && !string.IsNullOrEmpty(path)) MapPlaySceneTool.PlayMap(path);
                });
            GUILayout.Space(10);
            GUI.enabled = undoStack.Count > 0;
            if (GUILayout.Button("되돌리기", EditorStyles.toolbarButton, GUILayout.Width(60))) Defer(DoUndo);
            GUI.enabled = redoStack.Count > 0;
            if (GUILayout.Button("다시 하기", EditorStyles.toolbarButton, GUILayout.Width(60))) Defer(DoRedo);
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            string file = string.IsNullOrEmpty(path) ? "(저장 안 됨)" : path;
            GUILayout.Label(file + (dirty ? " *" : ""), EditorStyles.miniLabel);
            if (shownHover)
            {
                string where = map.InBounds(hover.x, hover.y)
                    ? "(" + hover.x + ", " + hover.y + ") " + MapCatalog.Terrain(map.GetTerrain(hover.x, hover.y)).Name
                    : "";
                GUILayout.Label(where, EditorStyles.miniLabel, GUILayout.Width(130));
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(PanelWidth));
            panelScroll = EditorGUILayout.BeginScrollView(panelScroll);

            DrawMapInfo();
            EditorGUILayout.Space(6);
            DrawTools();
            EditorGUILayout.Space(6);
            DrawSelectedNode();
            EditorGUILayout.Space(6);
            DrawView();
            EditorGUILayout.Space(6);
            DrawReport();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawMapInfo()
        {
            EditorGUILayout.LabelField("맵 정보", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string id = EditorGUILayout.TextField("맵 id (영문)", map.id);
            string name = EditorGUILayout.TextField("이름", map.name);
            int tier = EditorGUILayout.IntSlider("맵 단계", map.baseTier, 1, 3);
            if (EditorGUI.EndChangeCheck())
            {
                PushUndoOnce(map);
                map.id = id; map.name = name; map.baseTier = tier;
                MarkDirty();
            }

            EditorGUILayout.LabelField("맵 기본 적 계열", EditorStyles.miniBoldLabel);
            string families = FamilyToggles(map.enemies);
            if (families != map.enemies) { PushUndoOnce(map); map.enemies = families; MarkDirty(); }

            EditorGUILayout.LabelField("랜덤 칸 기본 후보 (비우면 기본값)", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            string randomPool = EditorGUILayout.TextField(map.randomPool);
            if (EditorGUI.EndChangeCheck()) { PushUndoOnce(map); map.randomPool = randomPool; MarkDirty(); }
            EditorGUILayout.LabelField("종류:가중치[:최대] 쉼표로 · 기본 " + RandomNodeGenerator.DefaultPool, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("크기 " + map.width + " × " + map.height, GUILayout.Width(90));
            newWidth = EditorGUILayout.IntField(newWidth, GUILayout.Width(40));
            newHeight = EditorGUILayout.IntField(newHeight, GUILayout.Width(40));
            newWidth = Mathf.Clamp(newWidth, 3, 200);
            newHeight = Mathf.Clamp(newHeight, 3, 200);
            if (GUILayout.Button("크기 바꾸기"))
            {
                int w = newWidth, h = newHeight;
                Defer(() =>
                {
                    PushUndo();
                    map.Resize(w, h);
                    if (selected != null && !map.nodes.Contains(selected)) selected = null;
                    MarkDirty();
                });
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawTools()
        {
            EditorGUILayout.LabelField("도구", EditorStyles.boldLabel);
            tool = (Tool)GUILayout.SelectionGrid((int)tool, ToolNames, 2);

            if (shownTool == Tool.Terrain)
            {
                brush = EditorGUILayout.IntSlider("붓 크기", brush, 1, 7);
                for (int i = 0; i < MapCatalog.Terrains.Length; i++)
                {
                    var t = MapCatalog.Terrains[i];
                    string label = t.Name + (t.Walkable ? "  · 전투 " + t.BattleCells + "칸" : "");
                    if (SwatchButton(ColorOf(t.ColorHex), label, i == terrainIndex)) terrainIndex = i;
                }
            }
            else if (shownTool == Tool.Node)
            {
                EditorGUILayout.HelpBox("클릭한 칸에 노드를 찍어요. 이미 노드가 있으면 종류를 바꿔요.", MessageType.None);
                for (int i = 0; i < MapCatalog.NodeTypes.Length; i++)
                {
                    var n = MapCatalog.NodeTypes[i];
                    if (SwatchButton(ColorOf(n.ColorHex), n.Mark + "  " + n.Name, i == nodeIndex)) nodeIndex = i;
                }
            }
            else if (shownTool == Tool.Select)
            {
                EditorGUILayout.HelpBox("노드를 클릭해 고르고, 끌어서 옮겨요. 이어진 길도 같이 따라와요.", MessageType.None);
            }
            else if (shownTool == Tool.Link)
            {
                EditorGUILayout.HelpBox("노드를 차례로 누르면 앞 노드와 이어져요. 이미 이어진 두 노드를 누르면 끊어요. 선을 누르면 그 길을 지워요. 빈 칸이나 Esc로 잇기를 멈춰요.", MessageType.None);
                if (GUILayout.Button("가까운 노드 자동 연결"))
                    Defer(() =>
                    {
                        PushUndo();
                        int added = MapLinks.AutoLink(map);
                        MarkDirty();
                        ShowNotification(new GUIContent(added > 0 ? "길 " + added + "개를 이었어요" : "더 이을 길이 없어요"));
                    });
                // 창은 그리기가 끝난 뒤에 띄운다 (그리는 도중에 띄우면 배치가 어긋난다)
                if (GUILayout.Button("길 모두 지우기") && map.links.Count > 0)
                    Defer(() =>
                    {
                        if (!EditorUtility.DisplayDialog("길 모두 지우기", "길 " + map.links.Count + "개를 지울까요? 되돌리기(Ctrl+Z)로 살릴 수 있어요.", "지우기", "취소")) return;
                        PushUndo(); map.links.Clear(); linkFrom = null; MarkDirty();
                    });
            }
            else
            {
                EditorGUILayout.HelpBox("클릭·드래그한 칸의 노드를 지워요. 다른 도구에서도 오른쪽 클릭으로 지울 수 있어요.", MessageType.None);
            }
        }

        void DrawSelectedNode()
        {
            EditorGUILayout.LabelField("고른 노드", EditorStyles.boldLabel);
            if (shownSelected == null)
            {
                EditorGUILayout.LabelField("(없음)  선택 도구로 노드를 클릭하세요.", EditorStyles.miniLabel);
                return;
            }

            var n = shownSelected;
            var shownDef = MapCatalog.NodeType(shownType) ?? MapCatalog.NodeTypes[1];
            EditorGUILayout.LabelField("위치", "(" + n.x + ", " + n.y + ")  지형: " + MapCatalog.Terrain(map.GetTerrain(n.x, n.y)).Name);

            EditorGUI.BeginChangeCheck();
            var typeNames = new string[MapCatalog.NodeTypes.Length];
            for (int i = 0; i < typeNames.Length; i++) typeNames[i] = MapCatalog.NodeTypes[i].Name;
            int typeIdx = EditorGUILayout.Popup("종류", Mathf.Max(0, MapCatalog.NodeTypeIndex(n.type)), typeNames);
            string name = EditorGUILayout.TextField("이름", n.name);
            var def = MapCatalog.NodeTypes[typeIdx];

            int cells = n.battleCells;
            string difficulty = n.difficulty;
            string enemies = n.enemies;
            string target = n.target;
            string pool = n.pool;
            if (shownDef.IsRandom)
            {
                EditorGUILayout.LabelField("이 칸의 후보 (비우면 맵 기본값)", EditorStyles.miniBoldLabel);
                pool = EditorGUILayout.TextField(n.pool);
                EditorGUILayout.LabelField("예) battle:3, farm:1  · 전투가 나오면 아래 설정을 써요", EditorStyles.wordWrappedMiniLabel);
            }
            if (shownDef.HasBattle || shownDef.IsRandom)
            {
                int terrainCells = MapCatalog.Terrain(map.GetTerrain(n.x, n.y)).BattleCells;
                string[] cellNames = { "지형 기본 (" + terrainCells + "칸)", "5칸", "7칸", "9칸" };
                int[] cellValues = { 0, 5, 7, 9 };
                int cellIdx = System.Array.IndexOf(cellValues, n.battleCells);
                cellIdx = EditorGUILayout.Popup("전투 칸 수", Mathf.Max(0, cellIdx), cellNames);
                cells = cellValues[cellIdx];

                int diffIdx = Mathf.Max(0, System.Array.IndexOf(MapCatalog.Difficulties, n.difficulty));
                diffIdx = EditorGUILayout.Popup("난이도", diffIdx, MapCatalog.DifficultyNames);
                difficulty = MapCatalog.Difficulties[diffIdx];

                EditorGUILayout.LabelField("적 계열 (모두 끄면 맵 기본값)", EditorStyles.miniBoldLabel);
                enemies = FamilyToggles(n.enemies);
            }
            if (shownDef.HasTarget)
            {
                string label = shownDef.Id == "map_gate" ? "갈 맵 id" : "던전 id";
                target = EditorGUILayout.TextField(label, n.target);
            }
            EditorGUILayout.LabelField("메모");
            string note = EditorGUILayout.TextArea(n.note, GUILayout.MinHeight(40));

            if (EditorGUI.EndChangeCheck())
            {
                PushUndoOnce(n);
                if (def.Id == "start" && n.type != "start") RemoveOtherStarts(n);
                n.type = def.Id; n.name = name; n.battleCells = cells; n.difficulty = difficulty;
                n.enemies = enemies; n.target = target; n.note = note; n.pool = pool;
                MarkDirty();
            }

            if (GUILayout.Button("이 노드 지우기"))
            {
                Defer(() =>
                {
                    PushUndo();
                    map.RemoveNodeAt(n.x, n.y);
                    if (selected == n) selected = null;
                    if (linkFrom == n) linkFrom = null;
                    MarkDirty();
                });
            }
        }

        void DrawView()
        {
            EditorGUILayout.LabelField("보기", EditorStyles.boldLabel);
            cellSize = EditorGUILayout.IntSlider("칸 크기", cellSize, 10, 64);
            showGrid = EditorGUILayout.Toggle("칸 구분선", showGrid);
            showDistance = EditorGUILayout.Toggle("시작에서 걸음 수", showDistance);
            showLinks = EditorGUILayout.Toggle("길 보이기", showLinks);
        }

        void DrawReport()
        {
            EditorGUILayout.LabelField("검사", EditorStyles.boldLabel);
            var report = shownReport ?? this.report;
            foreach (var e in report.Errors) EditorGUILayout.HelpBox(e, MessageType.Error);
            foreach (var w in report.Warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
            if (report.Errors.Count == 0 && report.Warnings.Count == 0)
                EditorGUILayout.HelpBox("문제 없음", MessageType.Info);

            EditorGUILayout.LabelField("지나갈 수 있는 칸", report.WalkableCells.ToString());
            EditorGUILayout.LabelField("가장 먼 노드까지", report.FarthestSteps >= 0 ? report.FarthestSteps + "걸음" : "-");
            int total = 0;
            foreach (var c in report.Counts.Values) total += c;
            EditorGUILayout.LabelField("노드 수", total.ToString());
            EditorGUILayout.LabelField("길 수", report.Links.ToString());
            if (report.Links > 0)
                EditorGUILayout.LabelField("가장 먼 노드까지 (길)", report.FarthestHops >= 0 ? report.FarthestHops + "번 이동" : "-");
            foreach (var def in MapCatalog.NodeTypes)
            {
                int c;
                if (report.Counts.TryGetValue(def.Id, out c) && c > 0)
                    EditorGUILayout.LabelField("   " + def.Name, c.ToString());
            }
        }

        void DrawCanvas()
        {
            Rect area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.13f));

            int cs = cellSize;
            Rect content = new Rect(0, 0, map.width * cs + 20, map.height * cs + 20);
            canvasScroll = GUI.BeginScrollView(area, canvasScroll, content);
            Rect visible = new Rect(canvasScroll.x, canvasScroll.y, area.width, area.height);

            Event e = Event.current;
            HandleCanvasInput(e, visible);

            if (e.type == EventType.Repaint)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(visible.xMin / cs));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(visible.yMin / cs));
                int x1 = Mathf.Min(map.width - 1, Mathf.CeilToInt(visible.xMax / cs));
                int y1 = Mathf.Min(map.height - 1, Mathf.CeilToInt(visible.yMax / cs));
                int gap = showGrid && cs >= 8 ? 1 : 0;

                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var t = MapCatalog.Terrain(map.GetTerrain(x, y));
                        EditorGUI.DrawRect(new Rect(x * cs, y * cs, cs - gap, cs - gap), ColorOf(t.ColorHex));
                    }

                if (showDistance && report != null && report.Distance != null)
                {
                    distStyle.fontSize = Mathf.Max(7, cs / 3);
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int d = report.Distance[x, y];
                            if (d > 0 && map.NodeAt(x, y) == null)
                                GUI.Label(new Rect(x * cs, y * cs, cs, cs), d.ToString(), distStyle);
                        }
                }

                if (showLinks || tool == Tool.Link) DrawLinks(cs);

                markStyle.fontSize = Mathf.Max(8, cs / 2);
                int pad = Mathf.Max(2, cs / 8);
                foreach (var n in map.nodes)
                {
                    if (n.x < x0 - 1 || n.x > x1 + 1 || n.y < y0 - 1 || n.y > y1 + 1) continue;
                    var def = MapCatalog.NodeType(n.type);
                    Color c = def != null ? ColorOf(def.ColorHex) : Color.magenta;
                    Rect r = new Rect(n.x * cs + pad, n.y * cs + pad, cs - pad * 2 - gap, cs - pad * 2 - gap);
                    EditorGUI.DrawRect(r, c);
                    markStyle.normal.textColor = Brightness(c) > 0.6f ? Color.black : Color.white;
                    GUI.Label(r, def != null ? def.Mark : "!", markStyle);
                    if (n == selected) Outline(new Rect(n.x * cs, n.y * cs, cs - gap, cs - gap), Color.yellow, 2);
                }

                if (map.InBounds(hover.x, hover.y))
                {
                    int r0 = tool == Tool.Terrain ? (brush - 1) / 2 : 0;
                    int r1 = tool == Tool.Terrain ? brush / 2 : 0;
                    Outline(new Rect((hover.x - r0) * cs, (hover.y - r0) * cs, (r0 + r1 + 1) * cs - gap, (r0 + r1 + 1) * cs - gap),
                        new Color(1, 1, 1, 0.8f), 1);
                }
            }

            GUI.EndScrollView();
        }

        Vector3 CellMid(int x, int y, int cs) { return new Vector3((x + 0.5f) * cs, (y + 0.5f) * cs, 0); }

        void DrawLinks(int cs)
        {
            float w = Mathf.Max(2f, cs / 7f);
            var hot = tool == Tool.Link && map.NodeAt(hover.x, hover.y) == null ? LinkNear(hoverPos, cs) : null;
            foreach (var l in map.links)
            {
                Vector3 a = CellMid(l.ax, l.ay, cs), b = CellMid(l.bx, l.by, cs);
                Handles.color = new Color(0, 0, 0, 0.55f);
                Handles.DrawAAPolyLine(w + 2f, a, b);
                Handles.color = l == hot ? new Color(1f, 0.42f, 0.42f) : new Color(1f, 0.96f, 0.84f);
                Handles.DrawAAPolyLine(w, a, b);
            }
            if (tool == Tool.Link && linkFrom != null && map.InBounds(hover.x, hover.y))
            {
                Handles.color = Color.yellow;
                Handles.DrawDottedLine(CellMid(linkFrom.x, linkFrom.y, cs), CellMid(hover.x, hover.y, cs), 4f);
            }
            Handles.color = Color.white;
        }

        // 화면 좌표에서 가까운 길 (선을 눌러 지울 때)
        MapLink LinkNear(Vector2 p, int cs)
        {
            MapLink best = null;
            float bestD = 8f;
            foreach (var l in map.links)
            {
                Vector2 a = CellMid(l.ax, l.ay, cs), b = CellMid(l.bx, l.by, cs);
                Vector2 ab = b - a;
                float len = Mathf.Max(0.0001f, ab.sqrMagnitude);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
                if (t < 0.08f || t > 0.92f) continue;
                float d = Vector2.Distance(a + ab * t, p);
                if (d < bestD) { bestD = d; best = l; }
            }
            return best;
        }

        void LinkClick(int cx, int cy, Vector2 mouse)
        {
            var node = map.NodeAt(cx, cy);
            if (node != null)
            {
                if (linkFrom != null && linkFrom != node && map.nodes.Contains(linkFrom))
                {
                    PushUndo();
                    bool added = map.ToggleLink(linkFrom, node);
                    ShowNotification(new GUIContent(added ? "길을 이었어요" : "길을 끊었어요"));
                    MarkDirty();
                }
                linkFrom = node;
                selected = node;
                return;
            }
            var l = LinkNear(mouse, cellSize);
            if (l != null)
            {
                PushUndo();
                map.links.Remove(l);
                MarkDirty();
            }
            else linkFrom = null;
        }

        // ───────────────────────── 입력 ─────────────────────────

        void HandleCanvasInput(Event e, Rect visible)
        {
            int cs = cellSize;
            int cx = Mathf.FloorToInt(e.mousePosition.x / cs);
            int cy = Mathf.FloorToInt(e.mousePosition.y / cs);
            bool inside = visible.Contains(e.mousePosition) && map.InBounds(cx, cy);

            switch (e.type)
            {
                case EventType.MouseMove:
                {
                    var h = inside ? new Vector2Int(cx, cy) : new Vector2Int(-1, -1);
                    hoverPos = e.mousePosition;
                    if (h != hover || tool == Tool.Link) { hover = h; Repaint(); }
                    break;
                }
                case EventType.ScrollWheel:
                    if (e.control || e.command)
                    {
                        int old = cellSize;
                        cellSize = Mathf.Clamp(cellSize + (e.delta.y < 0 ? 2 : -2), 10, 64);
                        // 마우스 아래 칸이 그대로 있게 스크롤을 맞춘다
                        Vector2 local = e.mousePosition - canvasScroll;
                        canvasScroll = (e.mousePosition / old) * cellSize - local;
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseDown:
                    if (!inside) break;
                    GUI.FocusControl(null);
                    if (e.button == 0)
                    {
                        if (tool == Tool.Link)
                        {
                            LinkClick(cx, cy, e.mousePosition);
                            RefreshReport();
                        }
                        else if (tool == Tool.Select)
                        {
                            selected = map.NodeAt(cx, cy);
                            dragging = selected;
                            dragUndoPushed = false;
                        }
                        else
                        {
                            PushUndo();
                            stroking = true;
                            ApplyTool(cx, cy, false);
                        }
                        e.Use();
                    }
                    else if (e.button == 1)
                    {
                        PushUndo();
                        stroking = true;
                        rightErasing = true;
                        EraseAt(cx, cy);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (e.button == 2)
                    {
                        canvasScroll -= e.delta;
                        e.Use();
                        Repaint();
                        break;
                    }
                    if (inside) hover = new Vector2Int(cx, cy);
                    if (dragging != null && inside && map.NodeAt(cx, cy) == null)
                    {
                        if (!dragUndoPushed) { PushUndo(); dragUndoPushed = true; }
                        map.MoveLinks(dragging.x, dragging.y, cx, cy);
                        dragging.x = cx; dragging.y = cy;
                        MarkDirty();
                    }
                    else if (stroking && inside)
                    {
                        if (rightErasing) EraseAt(cx, cy);
                        else ApplyTool(cx, cy, true);
                    }
                    if (stroking || dragging != null) { e.Use(); Repaint(); }
                    break;
                case EventType.MouseUp:
                    if (stroking || dragging != null)
                    {
                        stroking = false;
                        rightErasing = false;
                        dragging = null;
                        RefreshReport();
                        e.Use();
                        Repaint();
                    }
                    break;
            }
        }

        void ApplyTool(int cx, int cy, bool isDrag)
        {
            switch (tool)
            {
                case Tool.Terrain:
                {
                    char code = MapCatalog.Terrains[terrainIndex].Code;
                    int r0 = (brush - 1) / 2, r1 = brush / 2;
                    for (int y = cy - r0; y <= cy + r1; y++)
                        for (int x = cx - r0; x <= cx + r1; x++)
                            if (map.InBounds(x, y) && map.GetTerrain(x, y) != code) { map.SetTerrain(x, y, code); MarkDirty(); }
                    break;
                }
                case Tool.Node:
                {
                    if (isDrag) return; // 노드는 클릭한 칸에만 찍는다
                    var def = MapCatalog.NodeTypes[nodeIndex];
                    var node = map.NodeAt(cx, cy);
                    if (node == null)
                    {
                        node = new MapNode { x = cx, y = cy, type = def.Id };
                        map.nodes.Add(node);
                    }
                    else node.type = def.Id;
                    if (def.Id == "start") RemoveOtherStarts(node);
                    selected = node;
                    MarkDirty();
                    break;
                }
                case Tool.Erase:
                    EraseAt(cx, cy);
                    break;
            }
        }

        void EraseAt(int cx, int cy)
        {
            var node = map.NodeAt(cx, cy);
            if (node == null) return;
            if (node == selected) selected = null;
            if (node == linkFrom) linkFrom = null;
            map.RemoveNodeAt(cx, cy);
            MarkDirty();
        }

        void RemoveOtherStarts(MapNode keep)
        {
            map.nodes.RemoveAll(n => n != keep && n.type == "start");
            map.FixLinks();
        }

        void HandleShortcuts(Event e)
        {
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
            bool ctrl = e.control || e.command;
            if (ctrl && e.keyCode == KeyCode.Z) { Defer(DoUndo); e.Use(); }
            else if (ctrl && e.keyCode == KeyCode.Y) { Defer(DoRedo); e.Use(); }
            else if (ctrl && e.keyCode == KeyCode.S) { Defer(Save); e.Use(); }
            else if (!ctrl && e.keyCode == KeyCode.Alpha1) { tool = Tool.Terrain; e.Use(); }
            else if (!ctrl && e.keyCode == KeyCode.Alpha2) { tool = Tool.Node; e.Use(); }
            else if (!ctrl && e.keyCode == KeyCode.Alpha3) { tool = Tool.Select; e.Use(); }
            else if (!ctrl && e.keyCode == KeyCode.Alpha4) { tool = Tool.Erase; e.Use(); }
            else if (!ctrl && e.keyCode == KeyCode.Alpha5) { tool = Tool.Link; e.Use(); }
            else if (e.keyCode == KeyCode.Escape && linkFrom != null) { linkFrom = null; e.Use(); }
            else if (!ctrl && e.keyCode == KeyCode.Delete && selected != null)
            {
                Defer(() => { if (selected == null) return; PushUndo(); map.RemoveNodeAt(selected.x, selected.y); if (linkFrom == selected) linkFrom = null; selected = null; MarkDirty(); });
                e.Use();
            }
            if (e.type == EventType.Used) Repaint();
        }

        // ───────────────────────── 되돌리기 ─────────────────────────

        void PushUndo()
        {
            undoStack.Add(JsonUtility.ToJson(map));
            if (undoStack.Count > MaxUndo) undoStack.RemoveAt(0);
            redoStack.Clear();
            inspectorUndoFor = null;
        }

        // 왼쪽 칸의 글자를 고칠 때 한 글자마다 되돌리기가 쌓이지 않게, 같은 대상이면 한 번만 쌓는다.
        void PushUndoOnce(object target)
        {
            if (inspectorUndoFor != null && inspectorUndoFor == target) return;
            PushUndo();
            inspectorUndoFor = target;
        }

        void DoUndo()
        {
            if (undoStack.Count == 0) return;
            redoStack.Add(JsonUtility.ToJson(map));
            RestoreSnapshot(undoStack[undoStack.Count - 1]);
            undoStack.RemoveAt(undoStack.Count - 1);
        }

        void DoRedo()
        {
            if (redoStack.Count == 0) return;
            undoStack.Add(JsonUtility.ToJson(map));
            RestoreSnapshot(redoStack[redoStack.Count - 1]);
            redoStack.RemoveAt(redoStack.Count - 1);
        }

        void RestoreSnapshot(string json)
        {
            int sx = selected != null ? selected.x : -1, sy = selected != null ? selected.y : -1;
            map = JsonUtility.FromJson<MapData>(json);
            map.FixRows();
            selected = map.NodeAt(sx, sy);
            linkFrom = null;
            inspectorUndoFor = null;
            MarkDirty();
            GUI.FocusControl(null);
            Repaint();
        }

        // ───────────────────────── 파일 ─────────────────────────

        void NewMap(int w, int h)
        {
            map = MapData.Create("new_map", "새 맵", w, h);
            path = null;
            dirty = false;
            selected = null;
            undoStack.Clear();
            redoStack.Clear();
            RefreshReport();
        }

        void ShowOpenMenu()
        {
            var menu = new GenericMenu();
            if (Directory.Exists(MapFolder))
            {
                foreach (var f in Directory.GetFiles(MapFolder, "*.json"))
                {
                    string p = f.Replace('\\', '/');
                    menu.AddItem(new GUIContent(Path.GetFileName(p)), p == path, () => { if (ConfirmDiscard()) LoadFrom(p); });
                }
            }
            if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent(MapFolder + " 에 맵이 없어요"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("다른 곳에서 열기..."), false, () =>
            {
                string p = EditorUtility.OpenFilePanel("맵 열기", MapFolder, "json");
                if (!string.IsNullOrEmpty(p) && ConfirmDiscard()) LoadFrom(ToProjectPath(p));
            });
            menu.ShowAsContext();
        }

        void LoadFrom(string p)
        {
            try
            {
                var loaded = JsonUtility.FromJson<MapData>(File.ReadAllText(p));
                if (loaded == null || loaded.width < 1 || loaded.height < 1) throw new System.Exception("맵 크기가 없어요.");
                loaded.FixRows();
                map = loaded;
                path = p;
                dirty = false;
                selected = null;
                undoStack.Clear();
                redoStack.Clear();
                newWidth = map.width;
                newHeight = map.height;
                EditorPrefs.SetString(PrefLastPath, p);
                RefreshReport();
                Repaint();
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("맵 열기 실패", p + "\n\n" + ex.Message, "확인");
            }
        }

        void Save()
        {
            if (string.IsNullOrEmpty(path)) { SaveAs(); return; }
            WriteTo(path);
        }

        void SaveAs()
        {
            Directory.CreateDirectory(MapFolder);
            string p = EditorUtility.SaveFilePanelInProject("맵 저장", string.IsNullOrEmpty(map.id) ? "new_map" : map.id, "json",
                "맵 파일 이름을 정하세요. 보통 맵 id와 같게 둡니다.", MapFolder);
            if (string.IsNullOrEmpty(p)) return;
            WriteTo(p);
        }

        void WriteTo(string p)
        {
            map.FixRows();
            File.WriteAllText(p, JsonUtility.ToJson(map, true));
            path = p;
            dirty = false;
            EditorPrefs.SetString(PrefLastPath, p);
            if (p.StartsWith("Assets/")) AssetDatabase.ImportAsset(p);
            ShowNotification(new GUIContent("저장했어요"));
        }

        bool ConfirmDiscard()
        {
            if (!dirty) return true;
            int choice = EditorUtility.DisplayDialogComplex("맵 편집기", "저장하지 않은 변경이 있어요.", "저장", "취소", "저장 안 함");
            if (choice == 0) { Save(); return !dirty; }
            return choice == 2;
        }

        static string ToProjectPath(string absolute)
        {
            string full = absolute.Replace('\\', '/');
            string root = Application.dataPath.Replace('\\', '/');
            if (full.StartsWith(root)) return "Assets" + full.Substring(root.Length);
            return full;
        }

        // ───────────────────────── 도움 함수 ─────────────────────────

        // 화면을 그리는 도중에 맵 상태를 크게 바꾸면 IMGUI 배치가 어긋나므로, 그리기가 끝난 뒤에 실행한다.
        void Defer(System.Action action)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                action();
                Repaint();
            };
        }

        void MarkDirty()
        {
            dirty = true;
            RefreshReport();
            Repaint();
        }

        void RefreshReport()
        {
            if (map != null) report = MapValidator.Check(map);
        }

        string FamilyToggles(string current)
        {
            var list = MapData.SplitList(current);
            var result = new List<string>();
            var fams = MapCatalog.EnemyFamilies;
            for (int i = 0; i < fams.Length; i += 2)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = i; j < i + 2 && j < fams.Length; j++)
                {
                    bool on = EditorGUILayout.ToggleLeft(fams[j].Name, list.Contains(fams[j].Id), GUILayout.Width(PanelWidth / 2 - 12));
                    if (on) result.Add(fams[j].Id);
                }
                EditorGUILayout.EndHorizontal();
            }
            // 목록에 없는 id(손으로 적은 것)는 지우지 않고 남긴다
            foreach (var id in list)
            {
                bool known = false;
                foreach (var f in fams) if (f.Id == id) known = true;
                if (!known) result.Add(id);
            }
            return string.Join(",", result.ToArray());
        }

        bool SwatchButton(Color color, string label, bool on)
        {
            Rect r = EditorGUILayout.GetControlRect(false, 20);
            bool clicked = GUI.Toggle(r, on, "", EditorStyles.miniButton) != on;
            EditorGUI.DrawRect(new Rect(r.x + 4, r.y + 3, 14, 14), color);
            GUI.Label(new Rect(r.x + 24, r.y + 1, r.width - 26, r.height), label, on ? EditorStyles.boldLabel : EditorStyles.label);
            return clicked;
        }

        static void Outline(Rect r, Color c, int t)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        static float Brightness(Color c)
        {
            return 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        }

        Color ColorOf(string hex)
        {
            if (colorCache == null) colorCache = new Dictionary<string, Color>();
            Color c;
            if (!colorCache.TryGetValue(hex, out c))
            {
                if (!ColorUtility.TryParseHtmlString(hex, out c)) c = Color.magenta;
                colorCache[hex] = c;
            }
            return c;
        }

        void EnsureStyles()
        {
            if (markStyle == null)
            {
                markStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
                markStyle.padding = new RectOffset(0, 0, 0, 0);
            }
            if (distStyle == null)
            {
                distStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
                distStyle.normal.textColor = new Color(0, 0, 0, 0.55f);
            }
        }
    }
}
