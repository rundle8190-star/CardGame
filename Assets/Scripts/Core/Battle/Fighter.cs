// 전투에 선 한쪽(플레이어 또는 몬스터)의 상태.
using System.Collections.Generic;

namespace CardGame.Core.Battle
{
    public enum Side { Player = 0, Monster = 1 }

    public class Fighter
    {
        public readonly Side Side;
        public string Name;
        public int Hp, MaxHp;
        public int Pos;                 // 1 ~ 칸 수

        // 상태이상
        public int Bleed, Poison;

        // 강화 (이번 전투 동안 누적, 상한 없음)
        public int MeleeBonus;          // 모으기: 근접 무기 한 타 +1
        public int LineBonus;           // 집중: 직선 사격 한 타 +1
        public int ThrowBonus;          // 투척 단련: 투척 무기 +2
        public int GuardBonus;          // 단련: 방패 카드 방어 +1
        public int PoisonOnHit;         // 독 바르기: 무기가 피해를 줄 때마다 독 +1
        public int BleedOnSlash;        // 날 세우기: 참격 무기가 피해를 줄 때마다 출혈 +1
        public int RegenPerRound;       // 호흡: 라운드 끝 체력 +1

        // 소모품 (몬스터는 InfiniteAmmo)
        public readonly Dictionary<string, int> Ammo = new Dictionary<string, int>();
        public bool InfiniteAmmo;

        // 다음 라운드 드로우 +N
        public int DrawNext;

        // 이번 라운드 계획: 슬롯마다 카드 id (없으면 null). Cont = 앞 슬롯 대형 무기가 이어지는 칸
        public readonly string[] Plan;
        public readonly bool[] Cont;

        // 슬롯을 넘어 이어지는 효과 (Next 에 쌓았다가 다음 슬롯 시작에 Now 로 옮긴다)
        internal bool StunNext, StunNow;
        internal float MeleeMultNext = 1f, MeleeMultNow = 1f;     // 도약 ×1.5
        internal float LineMultNext = 1f, LineMultNow = 1f;       // 정조준 ×2
        internal bool FreeShotNext, FreeShotNow;                  // 정조준: 소모품 소비 없음
        internal int WeaponBonusNext, WeaponBonusNow;             // 받아넘기기: 다음 슬롯 무기 +2
        internal bool PoisonArrowNext, PoisonArrowNow;            // 독화살 장전
        internal int BarrierNext, BarrierNow;                     // 방벽: 다음 슬롯 방어
        internal bool JumpedLast;                                 // 바로 앞 슬롯에 상대를 넘었는지 (치명 조건)

        public Fighter(Side side, string name, int hp, int maxHp, int pos, int slots)
        {
            Side = side; Name = name; Hp = hp; MaxHp = maxHp; Pos = pos;
            Plan = new string[slots];
            Cont = new bool[slots];
        }

        public bool Dead { get { return Hp <= 0; } }
        public bool StunnedNextSlot { get { return StunNext; } }

        public int AmmoOf(string id)
        {
            if (InfiniteAmmo) return 999;
            int n;
            return Ammo.TryGetValue(id, out n) ? n : 0;
        }

        internal void UseAmmo(string id, int n)
        {
            if (InfiniteAmmo) return;
            Ammo[id] = AmmoOf(id) - n;
        }

        // 실제로 오른 만큼을 돌려준다 (최대 체력이면 0)
        internal int Heal(int n)
        {
            if (n <= 0) return 0;
            int before = Hp;
            Hp = System.Math.Min(MaxHp, Hp + n);
            return Hp - before;
        }

        internal void ClearPlan()
        {
            for (int i = 0; i < Plan.Length; i++) { Plan[i] = null; Cont[i] = false; }
        }

        // 슬롯 시작: 앞 슬롯에서 예약한 효과를 이번 슬롯에 적용한다
        internal void BeginSlot()
        {
            StunNow = StunNext; StunNext = false;
            MeleeMultNow = MeleeMultNext; MeleeMultNext = 1f;
            LineMultNow = LineMultNext; LineMultNext = 1f;
            FreeShotNow = FreeShotNext; FreeShotNext = false;
            WeaponBonusNow = WeaponBonusNext; WeaponBonusNext = 0;
            PoisonArrowNow = PoisonArrowNext; PoisonArrowNext = false;
            BarrierNow = BarrierNext; BarrierNext = 0;
        }

        public string StatusText()
        {
            var parts = new List<string>();
            if (Bleed > 0) parts.Add("출혈 " + Bleed);
            if (Poison > 0) parts.Add("독 " + Poison);
            if (StunNext) parts.Add("스턴(다음 슬롯)");
            if (MeleeBonus > 0) parts.Add("근접 +" + MeleeBonus);
            if (LineBonus > 0) parts.Add("사격 +" + LineBonus);
            if (ThrowBonus > 0) parts.Add("투척 +" + ThrowBonus);
            if (GuardBonus > 0) parts.Add("방어 +" + GuardBonus);
            if (PoisonOnHit > 0) parts.Add("독 바르기 " + PoisonOnHit);
            if (BleedOnSlash > 0) parts.Add("날 세우기 " + BleedOnSlash);
            if (RegenPerRound > 0) parts.Add("호흡 " + RegenPerRound);
            return parts.Count == 0 ? "-" : string.Join(", ", parts.ToArray());
        }
    }
}
