// 전투 데이터: Assets/Data 의 cards, decks, monsters, patterns JSON 을 담는 그릇과 찾아보기.
// JSON 읽기는 View(유니티 JsonUtility) 또는 시험 도구(System.Text.Json)가 맡고, 여기는 순수 C# 만 쓴다.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CardGame.Core.Battle
{
    [Serializable]
    public class ConsumableDef
    {
        public string id;
        public string name;
        public int perUse;
    }

    [Serializable]
    public class CardDef
    {
        public string id;
        public string name;
        public string category;     // weapon, shield, move, buff, heal, counter, draw
        public string attackType;   // melee, line, throw (무기만)
        public string[] tags;
        public int power;
        public int hits = 1;
        public int guard;
        public int pierce;
        public int range;           // 근접 사거리 (0 이면 1)
        public bool large;          // 2슬롯 대형 무기
        public ConsumableDef consumable;
        public string description;

        public bool HasTag(string t)
        {
            if (tags == null) return false;
            foreach (var x in tags) if (x == t) return true;
            return false;
        }

        public bool IsWeapon { get { return category == "weapon"; } }
        public bool IsMelee { get { return IsWeapon && attackType == "melee"; } }
        public bool IsLine { get { return IsWeapon && attackType == "line"; } }
        public bool IsThrow { get { return IsWeapon && attackType == "throw"; } }
        public int MeleeRange { get { return range > 0 ? range : 1; } }
        public bool UsesAmmo { get { return consumable != null && !string.IsNullOrEmpty(consumable.id); } }
    }

    [Serializable] public class CardsFile { public string version; public CardDef[] cards; }

    [Serializable] public class DeckEntry { public string card; public int count; }

    [Serializable]
    public class DeckDef
    {
        public string id;
        public string name;
        public DeckEntry[] cards;
    }

    [Serializable] public class DecksFile { public string version; public DeckDef[] decks; }

    [Serializable]
    public class MonsterDef
    {
        public string id;
        public string name;
        public int hp;
        public int startCell;       // 7칸 맵 기준 시작 칸
        public string concept;
        public string[] cards;
    }

    [Serializable] public class MonstersFile { public string version; public MonsterDef[] monsters; }

    [Serializable]
    public class ConditionDef
    {
        public string type;
        public string op;
        public int value;
    }

    [Serializable]
    public class PatternDef
    {
        public string monster;
        public string name;
        public bool isDefault;
        public string conditionText;
        public ConditionDef[] conditions;
        public string[] slots;      // null 또는 "" = 앞 슬롯 대형 무기가 차지한 칸
        public string description;
    }

    [Serializable] public class PatternsFile { public string version; public PatternDef[] patterns; }

    // 네 파일을 한데 모아 id 로 찾아 쓴다
    public class BattleDatabase
    {
        public readonly Dictionary<string, CardDef> Cards = new Dictionary<string, CardDef>();
        public readonly List<DeckDef> Decks = new List<DeckDef>();
        public readonly Dictionary<string, MonsterDef> Monsters = new Dictionary<string, MonsterDef>();
        public readonly List<PatternDef> Patterns = new List<PatternDef>();

        public BattleDatabase(CardsFile cards, DecksFile decks, MonstersFile monsters, PatternsFile patterns)
        {
            if (cards != null && cards.cards != null)
                foreach (var c in cards.cards) if (c != null && !string.IsNullOrEmpty(c.id)) Cards[c.id] = c;
            if (decks != null && decks.decks != null) Decks.AddRange(decks.decks);
            if (monsters != null && monsters.monsters != null)
                foreach (var m in monsters.monsters) if (m != null && !string.IsNullOrEmpty(m.id)) Monsters[m.id] = m;
            if (patterns != null && patterns.patterns != null) Patterns.AddRange(patterns.patterns);
        }

        public CardDef Card(string id)
        {
            CardDef c;
            return id != null && Cards.TryGetValue(id, out c) ? c : null;
        }

        public DeckDef Deck(string id)
        {
            foreach (var d in Decks) if (d.id == id) return d;
            return Decks.Count > 0 ? Decks[0] : null;
        }

        public MonsterDef Monster(string id)
        {
            MonsterDef m;
            return id != null && Monsters.TryGetValue(id, out m) ? m : null;
        }

        public List<PatternDef> PatternsOf(string monsterId)
        {
            var list = new List<PatternDef>();
            foreach (var p in Patterns) if (p.monster == monsterId) list.Add(p);
            return list;
        }

        // 데이터에 없는 카드 id 를 찾아 알려 준다 (없으면 빈 목록)
        public List<string> Problems()
        {
            var list = new List<string>();
            foreach (var d in Decks)
                if (d.cards != null)
                    foreach (var e in d.cards) if (Card(e.card) == null) list.Add("덱 " + d.id + ": 없는 카드 " + e.card);
            foreach (var p in Patterns)
                if (p.slots != null)
                    foreach (var s in p.slots) if (!string.IsNullOrEmpty(s) && Card(s) == null) list.Add("패턴 " + p.monster + "/" + p.name + ": 없는 카드 " + s);
            return list;
        }
    }

    public static class BattleJson
    {
        // 두 JSON 읽기 도구가 모두 받아들이도록 null 을 손본다.
        //  - 숫자 칸("range", "value")의 null → 0
        //  - 배열 안의 null(패턴의 대형 무기 칸) → ""
        public static string Clean(string json)
        {
            json = Regex.Replace(json, "\"(range|value|perUse|power|guard|pierce)\"\\s*:\\s*null", "\"$1\": 0");
            json = Regex.Replace(json, "(?<=[\\[,]\\s*)null(?=\\s*[,\\]])", "\"\"");
            return json;
        }
    }
}
