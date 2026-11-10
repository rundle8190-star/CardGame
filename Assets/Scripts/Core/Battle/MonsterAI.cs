// 몬스터 패턴 고르기 (규칙 요약 '몬스터 패턴').
// 조건이 모두 맞는 패턴은 가중치 3, 기본 패턴은 1. 직전 패턴은 연속으로 쓰지 않는다.
using System;
using System.Collections.Generic;

namespace CardGame.Core.Battle
{
    // 패턴 조건을 판단할 때 보는 전투 상황
    public class PatternContext
    {
        public int Cells;
        public int PlayerPos, MonsterPos;
        public int PlayerHp, PlayerMaxHp, MonsterHp, MonsterMaxHp;
        public int PlayerPoison;
        public int LastRoundGuardCards, LastRoundMoveCards, LastRoundRange2Cards;

        public int Distance { get { return Math.Abs(PlayerPos - MonsterPos); } }

        // 몬스터 뒤쪽(플레이어 반대편)으로 물러날 수 있는 칸 수
        public int MonsterBackSpace { get { return MonsterPos > PlayerPos ? Cells - MonsterPos : MonsterPos - 1; } }

        public int PlayerHpPercent { get { return PlayerMaxHp > 0 ? PlayerHp * 100 / PlayerMaxHp : 0; } }
        public int MonsterHpPercent { get { return MonsterMaxHp > 0 ? MonsterHp * 100 / MonsterMaxHp : 0; } }
    }

    public static class MonsterAI
    {
        public const int ConditionWeight = 3;
        public const int DefaultWeight = 1;

        public static bool Matches(ConditionDef c, PatternContext x)
        {
            switch (c.type)
            {
                case "default": return true;
                case "distance": return Compare(x.Distance, c.op, c.value);
                case "monsterBackSpace": return Compare(x.MonsterBackSpace, c.op, c.value);
                case "monsterHpPercent": return Compare(x.MonsterHpPercent, c.op, c.value);
                case "playerHpPercent": return Compare(x.PlayerHpPercent, c.op, c.value);
                case "lastRoundGuardCards": return Compare(x.LastRoundGuardCards, c.op, c.value);
                case "lastRoundMoveCards": return Compare(x.LastRoundMoveCards, c.op, c.value);
                case "lastRoundRange2Cards": return Compare(x.LastRoundRange2Cards, c.op, c.value);
                case "playerPoison": return Compare(x.PlayerPoison, c.op, c.value);
                case "playerAtWall": return x.PlayerPos == 1 || x.PlayerPos == x.Cells;
                // 확인 필요(규칙 요약 5번): "몬스터가 앞섬"을 체력 비율이 플레이어보다 높다는 뜻으로 임시 해석
                case "monsterAhead": return x.MonsterHpPercent > x.PlayerHpPercent;
                default: return false;   // 모르는 조건은 맞지 않는 것으로
            }
        }

        public static bool Compare(int a, string op, int b)
        {
            switch (op)
            {
                case ">=": return a >= b;
                case "<=": return a <= b;
                case "==": return a == b;
                case ">": return a > b;
                case "<": return a < b;
                case "!=": return a != b;
                default: return false;
            }
        }

        public static int WeightOf(PatternDef p, PatternContext x)
        {
            if (p.isDefault) return DefaultWeight;
            if (p.conditions == null || p.conditions.Length == 0) return 0;
            foreach (var c in p.conditions) if (!Matches(c, x)) return 0;
            return ConditionWeight;
        }

        // 고른 패턴을 준다. last 는 직전 라운드 패턴(없으면 null)
        public static PatternDef Choose(List<PatternDef> patterns, PatternContext x, PatternDef last, Random rng)
        {
            if (patterns == null || patterns.Count == 0) return null;
            int total = 0;
            var weights = new int[patterns.Count];
            for (int i = 0; i < patterns.Count; i++)
            {
                if (patterns.Count > 1 && patterns[i] == last) continue;
                weights[i] = WeightOf(patterns[i], x);
                total += weights[i];
            }
            if (total == 0)
            {
                // 뽑을 것이 없으면 기본 패턴 (직전과 같아도)
                foreach (var p in patterns) if (p.isDefault) return p;
                return patterns[0];
            }
            int roll = rng.Next(total);
            for (int i = 0; i < patterns.Count; i++)
            {
                if (roll < weights[i]) return patterns[i];
                roll -= weights[i];
            }
            return patterns[patterns.Count - 1];
        }
    }
}
