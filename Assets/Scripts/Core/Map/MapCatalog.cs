// 맵에 쓰는 지형과 노드 종류 목록.
// 종류를 더하거나 이름·색을 바꾸려면 이 파일만 고치면 맵 편집기에 바로 반영된다.
// 지형 문자(Code)는 저장된 맵 파일에 그대로 쓰이므로, 이미 쓰고 있는 문자는 바꾸지 않는다.
using System;

namespace CardGame.Core.Map
{
    public sealed class TerrainDef
    {
        public char Code;          // 맵 파일에 저장되는 한 글자
        public string Id;
        public string Name;        // 편집기에 보이는 이름
        public bool Walkable;      // 지나갈 수 있는지
        public int BattleCells;    // 이 지형에서 싸울 때 전투 칸 수 (5/7/9)
        public string ColorHex;    // 편집기 표시 색

        public TerrainDef(char code, string id, string name, bool walkable, int battleCells, string colorHex)
        {
            Code = code; Id = id; Name = name; Walkable = walkable; BattleCells = battleCells; ColorHex = colorHex;
        }
    }

    public sealed class NodeTypeDef
    {
        public string Id;          // 맵 파일에 저장되는 이름
        public string Name;        // 편집기에 보이는 이름
        public string Mark;        // 칸 위에 찍히는 글자
        public string ColorHex;
        public bool HasBattle;     // 전투가 일어나는 노드인지 (전투 칸 수·적 설정이 보임)
        public bool HasTarget;     // 이동할 맵이나 던전 id를 적는 노드인지
        public bool IsRandom;      // 탐험을 시작할 때 다른 노드로 바뀌는 랜덤 칸인지

        public NodeTypeDef(string id, string name, string mark, string colorHex, bool hasBattle, bool hasTarget, bool isRandom = false)
        {
            Id = id; Name = name; Mark = mark; ColorHex = colorHex; HasBattle = hasBattle; HasTarget = hasTarget; IsRandom = isRandom;
        }
    }

    public sealed class EnemyFamilyDef
    {
        public string Id;
        public string Name;
        public EnemyFamilyDef(string id, string name) { Id = id; Name = name; }
    }

    public static class MapCatalog
    {
        public const char DefaultTerrain = '.';

        public static readonly TerrainDef[] Terrains =
        {
            new TerrainDef('.', "field",    "들판",      true,  9, "#9CC56B"),
            new TerrainDef('=', "road",     "길",        true,  7, "#D8C48E"),
            new TerrainDef('f', "forest",   "숲",        true,  7, "#4E8B45"),
            new TerrainDef('F', "deep",     "깊은 숲",   true,  5, "#2F5E2E"),
            new TerrainDef('h', "hill",     "언덕",      true,  7, "#B59A62"),
            new TerrainDef('s', "swamp",    "늪",        true,  5, "#6B7A4A"),
            new TerrainDef('n', "snow",     "눈밭",      true,  9, "#E4ECF2"),
            new TerrainDef('r', "ruin",     "폐허",      true,  5, "#9A8F86"),
            new TerrainDef('b', "bridge",   "다리",      true,  5, "#A0703F"),
            new TerrainDef('M', "mountain", "산 (막힘)", false, 0, "#6E6259"),
            new TerrainDef('w', "water",    "물 (막힘)", false, 0, "#4A86C5"),
            new TerrainDef('#', "void",     "맵 밖",     false, 0, "#202020"),
        };

        public static readonly NodeTypeDef[] NodeTypes =
        {
            new NodeTypeDef("start",         "시작 지점",       "S",  "#FFFFFF", false, false),
            new NodeTypeDef("battle",        "전투",            "전", "#D9534F", true,  false),
            new NodeTypeDef("elite",         "엘리트 전투",     "엘", "#8E1F1B", true,  false),
            new NodeTypeDef("farm",          "파밍 지역",       "파", "#E8A33D", false, false),
            new NodeTypeDef("event",         "이벤트",          "?",  "#8E6CC8", false, false),
            new NodeTypeDef("totem",         "토템",            "토", "#3FB8AF", false, false),
            new NodeTypeDef("ruin_village",  "폐허 마을",       "마", "#7A6A5A", false, false),
            new NodeTypeDef("merchant_camp", "떠돌이 상인 야영지", "상", "#F2D04B", false, false),
            new NodeTypeDef("small_dungeon", "작은 던전",       "던", "#5B5B9A", false, true),
            new NodeTypeDef("big_dungeon",   "큰 던전 (보스)",  "보", "#2B2B5E", false, true),
            new NodeTypeDef("exit",          "탈출 후보",       "탈", "#4CAF50", false, false),
            new NodeTypeDef("map_gate",      "다른 맵으로 이동", "→", "#1E88E5", false, true),
            new NodeTypeDef("boss_spot",     "보스 출몰지",     "B",  "#B0302A", false, false),
            // 랜덤 칸: 탐험마다 RandomNodeGenerator 가 후보 중 하나로 바꾼다
            new NodeTypeDef("random",        "랜덤 칸",         "랜", "#B8B8C8", false, false, true),
        };

        // 규칙서 5장의 몬스터 계열
        public static readonly EnemyFamilyDef[] EnemyFamilies =
        {
            new EnemyFamilyDef("charger",       "돌격형"),
            new EnemyFamilyDef("shieldbearer",  "방패병"),
            new EnemyFamilyDef("archer",        "사수"),
            new EnemyFamilyDef("poison_hunter", "독 사냥꾼"),
            new EnemyFamilyDef("heavy",         "중장갑"),
            new EnemyFamilyDef("assassin",      "암살자"),
            new EnemyFamilyDef("spearman",      "창병"),
            new EnemyFamilyDef("berserker",     "광전사"),
        };

        public static readonly string[] Difficulties = { "", "easy", "normal", "hard" };
        public static readonly string[] DifficultyNames = { "자동 (깊이에 따라)", "쉬움", "보통", "어려움" };

        public static TerrainDef Terrain(char code)
        {
            foreach (var t in Terrains) if (t.Code == code) return t;
            return Terrains[0];
        }

        public static NodeTypeDef NodeType(string id)
        {
            foreach (var n in NodeTypes) if (n.Id == id) return n;
            return null;
        }

        public static int NodeTypeIndex(string id)
        {
            for (int i = 0; i < NodeTypes.Length; i++) if (NodeTypes[i].Id == id) return i;
            return -1;
        }

        public static string FamilyName(string id)
        {
            foreach (var f in EnemyFamilies) if (f.Id == id) return f.Name;
            return id;
        }
    }
}
