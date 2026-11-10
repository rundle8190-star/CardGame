// 맵 이벤트(? 노드): 데이터(Assets/Data/events.json)에서 읽은 선택지를 고르고 결과를 탐험에 적용한다. 순수 C#.
// - 노드마다 어떤 이벤트가 나올지는 탐험 시작 수(Seed)로 정한다. 다시 들어가도 같다.
// - 확률 선택지도 Seed 로 정해서, 창을 닫았다 다시 열어도 결과가 바뀌지 않는다.
// - 결과가 난 이벤트는 다 쓴 노드가 된다. "지나간다"처럼 leave 선택지는 노드를 남겨 둔다.
using System;
using System.Collections.Generic;

namespace CardGame.Core.Map
{
    [Serializable]
    public class EventEffect
    {
        public string type;    // hp, money, material, weapon, totem, battle, next_battle_enemy_hp, next_battle_poison, respawn, reveal, go_back
        public int amount;
        public string name;    // weapon: 무기 이름, battle: 적 이름
    }

    [Serializable]
    public class EventChoice
    {
        public string label;
        public string hint;
        public bool leave;            // 노드를 다 쓰지 않는 선택지 (지나간다, 돌아간다)
        public int chance;            // 0 이면 항상 effects. 1~99 면 그 확률로 effects, 아니면 failEffects
        public string result;
        public EventEffect[] effects;
        public string failResult;
        public EventEffect[] failEffects;
        public int needMoney;         // 재화가 이만큼 있어야 고를 수 있다
        public int needHpAbove;       // 체력이 이보다 많아야 고를 수 있다
        public bool needTotemRoom;    // 토템 칸이 남아 있어야 고를 수 있다
        public string reply;          // 고른 뒤 말하는 사람의 대답 (없으면 비움)
        public string replySpeaker;   // 대답하는 사람 (비우면 이벤트의 speaker)
    }

    [Serializable]
    public class EventDef
    {
        public string id;
        public string title;
        public string text;
        public string speaker;        // 사람이 나오는 이벤트: 말하는 사람 (없으면 비움)
        public string line;           // 그 사람의 첫 대사
        public EventChoice[] choices;
    }

    [Serializable]
    public class EventsFile
    {
        public string note;
        public EventDef[] events;
    }

    // 선택한 결과. 전투가 붙어 있으면 화면이 전투를 연다.
    public class EventOutcome
    {
        public string Text;
        public bool Battle;
        public int BattleMoneyMult = 1;
        public string BattleEnemy;
        public bool MovedBack;
        public bool Success = true;    // 확률 선택지에서 좋은 쪽이 나왔는지
        public string Speaker;         // 대답한 사람과 대답 (없으면 null)
        public string Reply;
    }

    public partial class Exploration
    {
        public MapNode Previous { get; private set; }     // 직전에 있던 노드 (돌아가기)
        int bonusVision;                                  // 이번 노드에서만 넓어진 시야
        int VisionHops { get { return Config.visionHops + bonusVision; } }

        // 다음 카드 전투에 붙는 효과 (화면이 전투를 만들 때 가져간다)
        public int NextBattleEnemyHp { get; private set; }
        public int NextBattlePoison { get; private set; }

        public void ClearNextBattleMods() { NextBattleEnemyHp = 0; NextBattlePoison = 0; }

        List<EventDef> events = new List<EventDef>();
        readonly Dictionary<MapNode, EventDef> eventOf = new Dictionary<MapNode, EventDef>();

        // 이벤트 목록을 넣으면 이벤트 노드마다 하나씩 나눠 준다 (목록을 섞어서 차례로, 다 쓰면 다시 처음부터)
        public void SetEvents(IList<EventDef> list)
        {
            events = new List<EventDef>();
            if (list != null) foreach (var e in list) if (e != null && e.choices != null && e.choices.Length > 0) events.Add(e);
            eventOf.Clear();
            if (events.Count == 0) return;

            var nodes = Map.nodes.FindAll(n => n.type == "event");
            nodes.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var order = new List<EventDef>(events);
            Shuffle(order, new Random(Seed ^ 0x5EED));
            for (int i = 0; i < nodes.Count; i++) eventOf[nodes[i]] = order[i % order.Count];
        }

        // 노드 이름: 이벤트 노드는 이벤트 제목
        public string NodeTitle(MapNode n)
        {
            var e = EventAt(n);
            return e != null && !string.IsNullOrEmpty(e.title) ? e.title : Label(n);
        }

        public EventDef EventAt(MapNode n)
        {
            EventDef e;
            return n != null && eventOf.TryGetValue(n, out e) ? e : null;
        }

        // 고를 수 없으면 이유, 고를 수 있으면 null
        public string CannotChoose(EventChoice c)
        {
            if (End != ExplorationEnd.None) return "탐험이 끝났다.";
            if (c.needMoney > 0 && Money < c.needMoney) return "재화가 " + c.needMoney + " 필요하다.";
            if (c.needHpAbove > 0 && Hp <= c.needHpAbove) return "체력이 " + (c.needHpAbove + 1) + " 이상이어야 한다.";
            if (c.needTotemRoom && Totems >= Config.maxTotems) return "토템을 더 가질 수 없다.";
            if (c.effects != null)
                foreach (var ef in c.effects)
                    if (ef != null && ef.type == "go_back" && (Previous == null || !Map.Neighbors(CurrentNode).Contains(Previous))) return "돌아갈 길이 없다.";
            return null;
        }

        // 확률 선택지가 좋은 쪽으로 나오는지 (Seed·노드·선택지 번호로 정해진다)
        public bool RollSuccess(MapNode n, int choiceIndex, int chance)
        {
            if (chance <= 0 || chance >= 100) return true;
            return Roll(n, choiceIndex) < chance;
        }

        int Roll(MapNode n, int choiceIndex)
        {
            unchecked
            {
                uint h = (uint)Seed * 2654435761u;
                h ^= (uint)(n.x * 73856093) ^ (uint)(n.y * 19349663) ^ (uint)(choiceIndex * 83492791);
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (int)(h % 100);
            }
        }

        public EventOutcome ChooseEvent(MapNode n, int choiceIndex)
        {
            var e = EventAt(n);
            if (e == null || choiceIndex < 0 || choiceIndex >= e.choices.Length) return null;
            if (n != CurrentNode || IsCleared(n)) return null;
            var c = e.choices[choiceIndex];
            if (CannotChoose(c) != null) return null;

            var o = new EventOutcome();
            o.Success = RollSuccess(n, choiceIndex, c.chance);
            var effects = o.Success ? c.effects : c.failEffects;
            o.Text = o.Success ? c.result : c.failResult;
            if (!string.IsNullOrEmpty(c.reply))
            {
                o.Reply = c.reply;
                o.Speaker = !string.IsNullOrEmpty(c.replySpeaker) ? c.replySpeaker : e.speaker;
            }
            AddLog(e.title + " — " + c.label + (string.IsNullOrEmpty(o.Text) ? "" : ": " + o.Text));

            var notes = new List<string>();
            if (effects != null)
                foreach (var ef in effects)
                {
                    if (ef == null) continue;
                    string note = Apply(ef, o);
                    if (!string.IsNullOrEmpty(note)) notes.Add(note);
                    if (End != ExplorationEnd.None) break;
                }
            if (notes.Count > 0)
            {
                string line = string.Join(", ", notes.ToArray());
                o.Text = (string.IsNullOrEmpty(o.Text) ? "" : o.Text + "\n") + "(" + line + ")";
                AddLog("  " + line);
            }
            // 다 쓴 노드: 지나가기·돌아가기는 남기고, 전투는 이긴 뒤에 정리한다
            if (!c.leave && !o.Battle && End == ExplorationEnd.None && !o.MovedBack) MarkCleared(n);
            if (string.IsNullOrEmpty(o.Text)) o.Text = c.leave ? "그냥 지나갔다." : "";
            return o;
        }

        // 효과 하나를 적용하고, 결과 한 줄을 돌려준다
        string Apply(EventEffect ef, EventOutcome o)
        {
            int a = ef.amount;
            switch (ef.type)
            {
                case "hp":
                {
                    int before = Hp;
                    Hp = Math.Min(Config.maxHp, Hp + a);
                    if (Hp <= 0) { Hp = 0; Die("쓰러졌다."); }
                    return "체력 " + before + "→" + Hp;
                }
                case "money":
                    Money = Math.Max(0, Money + a);
                    return "재화 " + (a >= 0 ? "+" : "") + a;
                case "material":
                    for (int i = 0; i < a; i++) Bag.Add(new BagItem("재료", "material", 1));
                    return "재료 +" + a;
                case "weapon":
                {
                    string name = string.IsNullOrEmpty(ef.name) ? "무기" : ef.name;
                    for (int i = 0; i < Math.Max(1, a); i++) Bag.Add(new BagItem(name, "weapon", Config.dropWeaponWeight));
                    return Josa.Eul(name) + " 얻음";
                }
                case "totem":
                {
                    int got = 0;
                    for (int i = 0; i < Math.Max(1, a) && Totems < Config.maxTotems; i++) { Totems++; got++; }
                    return got > 0 ? "토템 +" + got + " (" + Totems + "/" + Config.maxTotems + ")" : "토템 칸이 가득 차서 못 받았다";
                }
                case "battle":
                    o.Battle = true;
                    o.BattleMoneyMult = Math.Max(1, a);
                    o.BattleEnemy = ef.name;
                    return null;
                case "next_battle_enemy_hp":
                    NextBattleEnemyHp += a;
                    return "다음 전투 적 체력 +" + a;
                case "next_battle_poison":
                    NextBattlePoison += a;
                    return "다음 전투를 독 " + Josa.Ro(a) + " 시작";
                case "respawn":
                {
                    int n = 0;
                    for (int i = 0; i < Math.Max(1, a); i++) if (Respawn()) n++;
                    return n > 0 ? "지나온 전투 노드에 적이 다시 나타남" : "다시 나타날 적은 없었다";
                }
                case "reveal":
                    bonusVision += Math.Max(1, a);
                    Reveal();
                    return "이 노드에서만 시야 +" + Math.Max(1, a);
                case "go_back":
                {
                    var back = Previous;
                    if (back != null && Move(back)) { o.MovedBack = true; return "왔던 곳으로 돌아감"; }
                    return "돌아갈 길이 없다";
                }
            }
            return "알 수 없는 효과: " + ef.type;
        }
    }
}
