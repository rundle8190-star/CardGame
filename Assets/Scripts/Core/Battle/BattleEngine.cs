// 카드 전투 규칙 (규칙 요약 Design/rules.md, v0.7.3).
// 한 라운드: 몬스터가 패턴을 공개 → 플레이어가 손패에서 최대 5장 배치 → 1~5번 슬롯을 차례로 처리 → 라운드 끝(출혈·독).
// 한 슬롯: ① 양쪽 이동을 동시에 계산 ② 최종 위치로 적중·피해·상태이상·소모품 정산 ③ 장판.
//
// 규칙집에 없거나 애매한 부분은 "확인 필요" 표시를 단 임시 해석으로 두었다. 기획 확정되면 그 자리만 고친다.
using System;
using System.Collections.Generic;

namespace CardGame.Core.Battle
{
    [Serializable]
    public class AmmoEntry
    {
        public string id;
        public int count;
        public AmmoEntry() { }
        public AmmoEntry(string id, int count) { this.id = id; this.count = count; }
    }

    [Serializable]
    public class BattleConfig
    {
        public int playerStartCell = 2;
        public int handSize = 7;
        public int handMax = 9;
        public int fieldSlots = 3;        // 장판 유지 슬롯 수
        public int fireDamage = 2;        // 불 장판 슬롯 끝 피해
        public int maxRounds = 30;        // 끝나지 않는 전투를 막는 안전장치 (임시)
        // 확인 필요(규칙 요약 2번): 소모품 초기 수량. 정해지기 전까지 비워 둔다(소모품 카드는 쓰면 실패).
        public List<AmmoEntry> playerAmmo = new List<AmmoEntry>();
    }

    public class BattleSetup
    {
        public int Cells = 7;
        public int PlayerHp = 50, PlayerMaxHp = 50;
        public int PlayerPos;             // 0 이면 config.playerStartCell
        public List<string> PlayerDeck = new List<string>();
        public MonsterDef Monster;
        public int MonsterHp;             // 0 이면 데이터 체력
        public int MonsterPos;            // 0 이면 데이터 시작 칸을 칸 수에 맞춰 옮긴 값
        public int BlockedSlots;          // 무게 초과로 막힌 플레이어 슬롯 수 (뒤 슬롯부터)
        public int PlayerPoison;          // 독을 안고 시작 (맵 이벤트)
        public int Seed;
        public bool ShuffleDeck = true;
    }

    public enum BattleResult { None, Won, Lost }
    public enum BattlePhase { Planning, Resolving, Over }

    public class Field
    {
        public int Cell;
        public string Kind;     // poison, fire
        public Side Owner;      // 만든 쪽. 상대에게만 효과
        public int SlotsLeft;
    }

    public class BattleEngine
    {
        public const int SlotCount = 5;

        public readonly BattleDatabase Db;
        public readonly BattleConfig Config;
        public readonly int Cells;
        public readonly Fighter Player, Monster;
        public readonly List<Field> Fields = new List<Field>();
        public readonly List<string> Log = new List<string>();

        public int Round { get; private set; }
        public BattlePhase Phase { get; private set; }
        public BattleResult Result { get; private set; }
        public int NextSlot { get; private set; }     // 다음에 처리할 슬롯 (0~4). 5 = 라운드 끝 차례
        public int BlockedSlots { get; private set; }
        public PatternDef CurrentPattern { get; private set; }
        PatternDef lastPattern;
        readonly List<PatternDef> patterns;

        // 플레이어 덱
        public readonly List<string> DrawPile = new List<string>();
        public readonly List<string> Hand = new List<string>();
        public readonly List<string> Discard = new List<string>();
        public readonly List<string> Removed = new List<string>();   // 이번 전투에서 못 쓰게 된 카드 (던진 투창)
        readonly List<string> inPlay = new List<string>();
        readonly List<string> removeAfterRound = new List<string>();

        // 몬스터 패턴 조건용: 플레이어가 직전 라운드에 쓴 카드 수
        public int LastRoundGuardCards { get; private set; }
        public int LastRoundMoveCards { get; private set; }
        public int LastRoundRange2Cards { get; private set; }

        readonly Random rng;

        static readonly HashSet<string> RepeatLarge = new HashSet<string> { "whirlwind_slash", "dash_thrust" };

        public BattleEngine(BattleDatabase db, BattleConfig config, BattleSetup s)
        {
            Db = db;
            Config = config ?? new BattleConfig();
            Cells = Math.Max(3, s.Cells);
            rng = new Random(s.Seed);
            BlockedSlots = Math.Max(0, Math.Min(SlotCount, s.BlockedSlots));

            int pPos = s.PlayerPos > 0 ? s.PlayerPos : Config.playerStartCell;
            Player = new Fighter(Side.Player, "나", s.PlayerHp, s.PlayerMaxHp, Clamp(pPos), SlotCount);
            foreach (var a in Config.playerAmmo) if (a != null && !string.IsNullOrEmpty(a.id)) Player.Ammo[a.id] = a.count;

            var m = s.Monster;
            int mHp = s.MonsterHp > 0 ? s.MonsterHp : (m != null ? m.hp : 30);
            int mPos = s.MonsterPos > 0 ? s.MonsterPos : MonsterStartCell(m, Cells);
            Monster = new Fighter(Side.Monster, m != null ? m.name : "몬스터", mHp, mHp, Clamp(mPos), SlotCount);
            Monster.InfiniteAmmo = true;   // 확인 필요: 몬스터 소모품은 무한으로 임시 처리
            if (Monster.Pos == Player.Pos) Monster.Pos = Player.Pos < Cells ? Player.Pos + 1 : Player.Pos - 1;

            patterns = m != null ? db.PatternsOf(m.id) : new List<PatternDef>();

            DrawPile.AddRange(s.PlayerDeck);
            if (s.ShuffleDeck) Shuffle(DrawPile);

            Add(Josa.I(Monster.Name) + " 나타났다. 체력 " + Monster.Hp + ", 전장 " + Cells + "칸");
            if (s.PlayerPoison > 0) { Player.Poison = s.PlayerPoison; Add("나: 독 " + Josa.Ro(s.PlayerPoison) + " 시작한다."); }
        }

        // 7칸 기준 시작 칸을 벽에서의 거리가 같도록 칸 수에 맞춰 옮긴다 (6 → 칸 수 −1, 7 → 칸 수)
        public static int MonsterStartCell(MonsterDef m, int cells)
        {
            int start = m != null && m.startCell > 0 ? m.startCell : 6;
            return Math.Max(1, Math.Min(cells, cells - (7 - start)));
        }

        public int Distance { get { return Math.Abs(Player.Pos - Monster.Pos); } }
        public bool IsBlocked(int slot) { return slot >= SlotCount - BlockedSlots; }
        public int HandTarget { get { return Math.Min(Config.handMax, Config.handSize + Player.DrawNext); } }

        // ───────── 라운드 흐름 ─────────

        // 몬스터 패턴을 공개하고 손패를 채운다. 화면은 이 뒤에 플레이어 배치를 받는다.
        public void StartRound()
        {
            if (Result != BattleResult.None) return;
            Round++;
            if (Round > Config.maxRounds) { Finish(BattleResult.Lost, Config.maxRounds + "라운드 안에 끝내지 못했다 — 패배"); return; }
            Add("━━ " + Round + " 라운드 ━━");

            CurrentPattern = MonsterAI.Choose(patterns, Context(), lastPattern, rng);
            Monster.ClearPlan();
            if (CurrentPattern != null && CurrentPattern.slots != null)
            {
                for (int i = 0; i < SlotCount && i < CurrentPattern.slots.Length; i++)
                {
                    string id = CurrentPattern.slots[i];
                    if (string.IsNullOrEmpty(id))
                    {
                        // 앞 슬롯 대형 무기가 차지한 칸
                        if (i > 0 && Monster.Plan[i - 1] != null && !Monster.Cont[i - 1]) { Monster.Plan[i] = Monster.Plan[i - 1]; Monster.Cont[i] = true; }
                    }
                    else Monster.Plan[i] = id;
                }
                Add(Monster.Name + "의 계획: " + CurrentPattern.name);
            }

            int target = HandTarget;
            Player.DrawNext = 0;
            while (Hand.Count < target && Draw()) { }
            Player.ClearPlan();
            NextSlot = 0;
            Phase = BattlePhase.Planning;
        }

        bool Draw()
        {
            if (DrawPile.Count == 0)
            {
                if (Discard.Count == 0) return false;
                DrawPile.AddRange(Discard);
                Discard.Clear();
                Shuffle(DrawPile);
            }
            Hand.Add(DrawPile[DrawPile.Count - 1]);
            DrawPile.RemoveAt(DrawPile.Count - 1);
            return true;
        }

        // 손패 배치가 맞는지 본다. handIndex[slot] = 손패 번호, -1 = 빈 슬롯. 대형 무기는 자기 칸만 적고 다음 칸은 -1.
        // 문제가 없으면 null, 있으면 이유.
        public string ValidatePlan(int[] handIndex)
        {
            if (handIndex == null || handIndex.Length != SlotCount) return "슬롯 수가 맞지 않는다.";
            var used = new HashSet<int>();
            for (int i = 0; i < SlotCount; i++)
            {
                int h = handIndex[i];
                if (h < 0) continue;
                if (h >= Hand.Count) return "없는 손패다.";
                if (!used.Add(h)) return "같은 카드를 두 번 놓았다.";
                if (IsBlocked(i)) return (i + 1) + "번 슬롯은 무게 때문에 막혀 있다.";
                var c = Db.Card(Hand[h]);
                if (c == null) return "데이터에 없는 카드예요: " + Hand[h];
                if (c.large)
                {
                    if (i + 1 >= SlotCount || IsBlocked(i + 1)) return Josa.Eun(c.name) + " 2슬롯 카드라 다음 슬롯도 비어 있어야 한다.";
                    if (handIndex[i + 1] >= 0) return c.name + " 다음 슬롯은 비워 둬야 한다.";
                    i++;   // 다음 칸은 대형 무기 차지
                }
            }
            return null;
        }

        public string Commit(int[] handIndex)
        {
            if (Phase != BattlePhase.Planning) return "지금은 배치할 수 없다.";
            string why = ValidatePlan(handIndex);
            if (why != null) return why;

            Player.ClearPlan();
            var usedIdx = new List<int>();
            for (int i = 0; i < SlotCount; i++)
            {
                int h = handIndex[i];
                if (h < 0) continue;
                string id = Hand[h];
                Player.Plan[i] = id;
                usedIdx.Add(h);
                if (Db.Card(id).large) { Player.Plan[i + 1] = id; Player.Cont[i + 1] = true; i++; }
            }
            usedIdx.Sort();
            for (int k = usedIdx.Count - 1; k >= 0; k--)
            {
                inPlay.Add(Hand[usedIdx[k]]);
                Hand.RemoveAt(usedIdx[k]);
            }
            Phase = BattlePhase.Resolving;
            NextSlot = 0;
            return null;
        }

        // 시험용: 손패 없이 양쪽 계획을 바로 넣는다. 대형 무기 뒤 칸은 null 로 두면 자동으로 이어진다.
        public void SetPlansForTest(string[] player, string[] monster)
        {
            FillPlan(Player, player);
            FillPlan(Monster, monster);
            Phase = BattlePhase.Resolving;
            NextSlot = 0;
        }

        void FillPlan(Fighter f, string[] ids)
        {
            f.ClearPlan();
            if (ids == null) return;
            for (int i = 0; i < SlotCount && i < ids.Length; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id))
                {
                    if (i > 0 && f.Plan[i - 1] != null && !f.Cont[i - 1] && Db.Card(f.Plan[i - 1]) != null && Db.Card(f.Plan[i - 1]).large)
                    { f.Plan[i] = f.Plan[i - 1]; f.Cont[i] = true; }
                }
                else f.Plan[i] = id;
            }
        }

        // 다음 한 단계: 슬롯 하나 또는 라운드 끝. 더 할 것이 남았으면 true.
        public bool Step()
        {
            if (Phase != BattlePhase.Resolving || Result != BattleResult.None) return false;
            if (NextSlot < SlotCount)
            {
                ResolveSlot(NextSlot);
                NextSlot++;
                return Result == BattleResult.None;
            }
            EndRound();
            if (Result == BattleResult.None) StartRound();
            return false;
        }

        // ───────── 슬롯 처리 ─────────

        class Act
        {
            public Fighter F;
            public CardDef Card;
            public bool Cont;
            public bool Canceled;
            public bool Attacks;
            public int AmmoNeed;
            public int Start;
            public int Moved;
            public bool Passed;     // 이번 슬롯에 상대를 넘었다
            public int Guard;
            public bool BlocksLarge;  // 대형 무기도 막는 방어 (탑 방패, 받아넘기기)
            public bool Backstab;   // 치명(등 뒤 공격)
            public int ThrowTarget;
            public bool Is(string id) { return Card != null && !Canceled && Card.id == id; }
            public bool Fresh(string id) { return Is(id) && !Cont; }
        }

        public void ResolveSlot(int s)
        {
            Add("─ " + (s + 1) + "번 슬롯 ─");
            Player.BeginSlot();
            Monster.BeginSlot();
            var pa = MakeAct(Player, s);
            var ma = MakeAct(Monster, s);
            Add(Describe(pa) + "  /  " + Describe(ma));

            // ① 이동 (동시에)
            Move(pa, ma);

            // 치명: 앞 슬롯에 넘었고, 이번 슬롯에 상대가 공격·방어·반격을 안 하면
            pa.Backstab = Player.JumpedLast && !Defends(ma);
            ma.Backstab = Monster.JumpedLast && !Defends(pa);

            // ② 정산: 회복 → 공격(양쪽) → 밀기·끌기 → 강화 등 다음 슬롯부터 적용되는 효과
            Heals(pa); Heals(ma);
            var shifts = new List<KeyValuePair<Fighter, int>>();
            Attack(pa, ma, s, shifts);
            Attack(ma, pa, s, shifts);
            foreach (var kv in shifts) Shift(kv.Key, kv.Value);
            AfterEffects(pa); AfterEffects(ma);

            Player.JumpedLast = pa.Passed;
            Monster.JumpedLast = ma.Passed;

            // ③ 장판
            Fields_(s);

            CheckDeath();
        }

        Act MakeAct(Fighter f, int s)
        {
            var a = new Act { F = f, Start = f.Pos };
            a.Card = Db.Card(f.Plan[s]);
            a.Cont = f.Cont[s];
            if (a.Card == null) return a;

            a.Attacks = a.Card.IsWeapon && (!a.Cont || RepeatLarge.Contains(a.Card.id));
            // 스턴: 이번 슬롯의 무기 행동 취소 (이동·방어 카드는 그대로)
            if (f.StunNow && a.Card.IsWeapon)
            {
                a.Canceled = true; a.Attacks = false;
                Add(f.Name + ": 스턴 — " + a.Card.name + " 취소");
                return a;
            }
            if (a.Attacks && a.Card.UsesAmmo)
            {
                a.AmmoNeed = a.Card.id == "throwing_dagger" ? 1 : Math.Max(1, a.Card.consumable.perUse);
                if (a.Card.IsLine && f.FreeShotNow) a.AmmoNeed = 0;
                if (f.AmmoOf(a.Card.consumable.id) < a.AmmoNeed)
                {
                    a.Canceled = true; a.Attacks = false;
                    Add(f.Name + ": " + Josa.I(a.Card.consumable.name) + " 없어서 " + a.Card.name + " 실패");
                    return a;
                }
            }
            if (a.Card.category == "shield" && !a.Cont)
            {
                a.Guard = a.Card.guard + f.GuardBonus;
                a.BlocksLarge = a.Card.id == "tower_shield" || a.Card.id == "parry";
            }
            a.Guard += f.BarrierNow;   // 앞 슬롯 방벽
            return a;
        }

        static bool Defends(Act a)
        {
            if (a.Card == null || a.Canceled) return a.Guard > 0;
            return a.Attacks || a.Guard > 0 || a.Card.category == "counter";
        }

        // ───────── ① 이동 ─────────

        void PlanMove(Act a, int oppStart, out int target, out bool pass)
        {
            target = a.Start; pass = false;
            if (a.Card == null || a.Canceled) return;
            int dir = oppStart > a.Start ? 1 : -1;
            int d = Math.Abs(oppStart - a.Start);
            int fwd = 0, back = 0;
            switch (a.Card.id)
            {
                case "advance": fwd = 1; break;
                case "leap": fwd = 2; break;
                case "shield_charge": fwd = 1; break;
                case "charge_axe": fwd = 1; break;
                case "dash_thrust": fwd = 1; break;                 // 두 슬롯 모두
                case "lance": if (!a.Cont) fwd = 3; break;
                case "dodge": case "poison_mist": case "retreat_shot": back = 1; break;
                case "retreat": back = 2; break;
                case "roll":
                    if (d == 1) { int t = oppStart + dir; if (InBounds(t)) { target = t; pass = true; } }
                    else fwd = 1;
                    break;
                case "vault":
                    if (d == 1)
                    {
                        // 확인 필요: "뒤 2칸"을 상대 너머 2칸으로 해석. 벽에 막히면 1칸, 그것도 막히면 제자리
                        int t = oppStart + 2 * dir;
                        if (!InBounds(t)) t = oppStart + dir;
                        if (InBounds(t)) { target = t; pass = true; }
                    }
                    else fwd = 1;
                    break;
            }
            if (fwd > 0) target = a.Start + dir * Math.Min(fwd, d - 1);           // 상대를 넘지 못함
            if (back > 0) target = Clamp(a.Start - dir * back);
        }

        void Move(Act pa, Act ma)
        {
            int ps = pa.Start, ms = ma.Start;
            int tp, tm; bool passP, passM;
            PlanMove(pa, ms, out tp, out passP);
            PlanMove(ma, ps, out tm, out passM);
            int side = Math.Sign(ms - ps);   // 처음에 몬스터가 플레이어의 어느 쪽에 있었는지

            if (tp == tm)
            {
                // 같은 칸: NPC 우선. 플레이어는 원래 자리, 거기도 막히면 가까운 빈 칸
                tp = ps != tm ? ps : FreeNear(ps, tm, -side);
                passP = false;
            }
            else if (!passP && !passM && Math.Sign(tm - tp) != side)
            {
                // 둘 다 넘기 이동이 아닌데 엇갈림 = 중간에서 부딪힘.
                // 확인 필요: NPC 가 목표 칸을 차지하고 플레이어는 그 바로 앞 칸에 멈추는 것으로 임시 처리
                int c = tm - side;
                tp = InBounds(c) && c != tm ? c : FreeNear(ps, tm, -side);
            }
            if (tp == tm) tp = FreeNear(tp, tm, -side);

            pa.Passed = passP && Math.Sign(tm - tp) != side;
            ma.Passed = passM && Math.Sign(tm - tp) != side;
            Player.Pos = tp; Monster.Pos = tm;
            pa.Moved = Math.Abs(tp - ps); ma.Moved = Math.Abs(tm - ms);
            if (pa.Moved > 0 || ma.Moved > 0) Add("이동: 나 " + ps + "→" + tp + ", " + Monster.Name + " " + ms + "→" + tm + (pa.Passed ? " (내가 넘음)" : "") + (ma.Passed ? " (상대가 넘음)" : ""));
            pa.ThrowTarget = ms; ma.ThrowTarget = ps;   // 투척은 던질 때 상대가 서 있던 칸
        }

        // avoid 를 피해 from 에서 가장 가까운 빈 칸 (prefer 방향 먼저)
        int FreeNear(int from, int avoid, int prefer)
        {
            if (prefer == 0) prefer = -1;
            for (int k = 0; k < Cells; k++)
            {
                int a = from + prefer * k, b = from - prefer * k;
                if (InBounds(a) && a != avoid) return a;
                if (InBounds(b) && b != avoid) return b;
            }
            return from;
        }

        // ───────── ② 정산 ─────────

        void Heals(Act a)
        {
            if (a.Card == null || a.Canceled || a.Cont) return;
            var f = a.F;
            switch (a.Card.id)
            {
                case "first_aid": Add(f.Name + ": 체력 +" + f.Heal(4)); break;
                case "bandage": { int h = f.Heal(2); f.Bleed = 0; Add(f.Name + ": 체력 +" + h + ", 출혈 해제"); break; }
                case "catch_breath": Add(f.Name + ": 체력 +" + f.Heal(2)); break;
            }
        }

        void Attack(Act a, Act defAct, int slot, List<KeyValuePair<Fighter, int>> shifts)
        {
            if (!a.Attacks) return;
            var atk = a.F; var def = defAct.F; var c = a.Card;
            int d = Math.Abs(def.Pos - atk.Pos);

            bool hit; string miss = null;
            if (c.IsMelee)
            {
                hit = d >= 1 && d <= c.MeleeRange && !defAct.Passed;
                if (!hit) miss = defAct.Passed ? "상대가 넘어가는 중" : "거리 " + d + " (사거리 " + c.MeleeRange + ")";
            }
            else if (c.IsLine)
            {
                hit = d >= 2;
                if (!hit) miss = "붙어 있으면 직선 사격 불가";
            }
            else if (c.IsThrow)
            {
                hit = def.Pos == a.ThrowTarget;
                if (!hit) miss = "상대가 " + a.ThrowTarget + "칸에서 벗어남";
            }
            else { hit = false; miss = "알 수 없는 공격 방식"; }

            // 소모품은 맞든 빗나가든 쓴다
            int hits = Math.Max(1, c.hits);
            if (a.AmmoNeed > 0)
            {
                if (c.id == "throwing_dagger") { hits = Math.Min(hits, atk.AmmoOf(c.consumable.id)); atk.UseAmmo(c.consumable.id, hits); }
                else atk.UseAmmo(c.consumable.id, a.AmmoNeed);
            }
            if (c.id == "javelin" && atk.Side == Side.Player) removeAfterRound.Add(c.id);  // 확인 필요: 회수 방법이 없어 이번 전투 동안 못 씀

            // 던진 칸에 장판 (맞든 안 맞든)
            if (c.id == "poison_jar") AddField(a.ThrowTarget, "poison", atk.Side);
            if (c.id == "firebomb") AddField(a.ThrowTarget, "fire", atk.Side);

            if (!hit) { Add(Of(atk) + c.name + ": 빗나감 (" + miss + ")"); return; }

            if (c.id == "poison_jar") { def.Poison += 2; Add(Of(atk) + "독 단지 명중: " + def.Name + " 독 +2"); }

            bool poisonArrowUsed = false;
            for (int h = 0; h < hits; h++)
            {
                if (def.Dead && h > 0) break;
                int raw = RawDamage(a, d, slot);
                if (raw <= 0 && c.power <= 0 && c.id != "rend_wound" && c.id != "poison_burst") continue;  // 밀기 등 피해 없는 무기

                bool stun = c.HasTag("blunt") || (c.id == "vital_strike" && a.Backstab);
                int guard = defAct.Guard;
                // 규칙 요약 4번 (임시 합의): 탑 방패와 받아넘기기는 대형 무기도 막는다. 다른 방패는 대형 무기에 방어 0
                if (c.large && !defAct.BlocksLarge) guard = 0;
                int dealt;
                if (stun) dealt = raw;
                else
                {
                    int pierce = Math.Min(c.pierce, raw);
                    dealt = pierce + Math.Max(0, raw - pierce - guard);
                }
                def.Hp -= dealt;
                string tag = (hits > 1 ? " " + (h + 1) + "타" : "") + (a.Backstab ? " [치명]" : "");
                Add(Of(atk) + c.name + tag + ": 피해 " + dealt + (dealt < raw ? " (방어로 " + (raw - dealt) + " 막음)" : "") + (stun ? ", 스턴" : ""));
                if (stun) def.StunNext = true;

                // 맞은 쪽 반응
                if (defAct.Fresh("parry")) defAct.F.WeaponBonusNext = 2;
                if (defAct.Fresh("spiked_shield")) { atk.Hp -= 2; Add(Of(def) + "가시 방패: 반사 2"); }
                // 확인 필요(규칙 요약 7번): 반격 자세는 방어로 줄어든 뒤 실제로 받은 피해만큼 반사
                if (defAct.Fresh("counter_stance") && c.IsMelee && dealt > 0) { atk.Hp -= dealt; Add(Of(def) + "반격 자세: 반사 " + dealt); }

                if (dealt > 0) OnDamage(a, def, ref poisonArrowUsed);
            }

            // 밀기·끌기 (이동 정산이 끝난 뒤 한꺼번에)
            int away = Math.Sign(def.Pos - atk.Pos);
            if (c.id == "shove" || c.id == "bow_swing") shifts.Add(new KeyValuePair<Fighter, int>(def, away));
            if (c.id == "hook_sickle" && d >= 2) shifts.Add(new KeyValuePair<Fighter, int>(def, -away));
        }

        // 한 타 피해: 가산을 먼저 더하고 배율을 곱한 뒤 내림 (합의된 임시 순서)
        int RawDamage(Act a, int d, int slot)
        {
            var atk = a.F; var c = a.Card;
            var def = a.F == Player ? Monster : Player;
            int p = c.power;
            if (c.id == "rend_wound") p = def.Bleed * 2;
            if (c.id == "poison_burst") p = def.Poison * 2;

            if (c.IsMelee) p += atk.MeleeBonus;
            if (c.IsLine && c.HasTag("shot")) p += atk.LineBonus;
            if (c.HasTag("throw")) p += atk.ThrowBonus;
            if (c.id == "hand_axe" && slot == 2) p += 4;
            if (c.id == "charge_axe" && a.Moved > 0) p += 3;
            if (c.id == "dash_thrust" && a.Moved > 0) p += 2;
            if (c.id == "lance") p += 2 * a.Moved;
            if (c.id == "flail" && d == 1) p -= 2;
            if (c.id == "assassin_dagger" && a.Backstab) p += 2;
            p += atk.WeaponBonusNow;

            double mult = 1;
            if (c.IsMelee) mult *= atk.MeleeMultNow;
            if (c.IsLine) mult *= atk.LineMultNow;
            if (a.Backstab) mult *= 2;
            return Math.Max(0, (int)Math.Floor(p * mult));
        }

        void OnDamage(Act a, Fighter def, ref bool poisonArrowUsed)
        {
            var atk = a.F; var c = a.Card;
            int poison = 0, bleed = 0;
            if (c.id == "venom_dagger") poison += 1;
            if (c.id == "venom_fang") poison += 2;
            if (c.id == "serrated_sword") bleed += 2;
            if (c.HasTag("slash")) bleed += atk.BleedOnSlash;
            poison += atk.PoisonOnHit;
            if (c.IsLine && atk.PoisonArrowNow && !poisonArrowUsed) { poison += 2; poisonArrowUsed = true; }
            if (poison > 0) { def.Poison += poison; Add(def.Name + ": 독 +" + poison); }
            if (bleed > 0) { def.Bleed += bleed; Add(def.Name + ": 출혈 +" + bleed); }
        }

        void Shift(Fighter f, int dir)
        {
            if (dir == 0) return;
            var other = f == Player ? Monster : Player;
            int np = f.Pos + dir;
            if (!InBounds(np) || np == other.Pos) { Add(f.Name + ": 벽이나 상대에 막혀 밀리지 않음"); return; }
            Add(f.Name + ": " + f.Pos + "→" + np + "칸 밀림");
            f.Pos = np;
        }

        // 다음 슬롯부터 적용되는 효과
        void AfterEffects(Act a)
        {
            if (a.Card == null || a.Canceled || a.Cont) return;
            var f = a.F;
            switch (a.Card.id)
            {
                case "leap": f.MeleeMultNext = 1.5f; break;
                case "aim": f.LineMultNext = 2f; f.FreeShotNext = true; break;
                case "load_poison_arrow": f.PoisonArrowNext = true; break;
                case "apply_poison": f.PoisonOnHit++; Add(f.Name + ": 독 바르기 (누적 " + f.PoisonOnHit + ")"); break;
                case "sharpen": f.BleedOnSlash++; Add(f.Name + ": 날 세우기 (누적 " + f.BleedOnSlash + ")"); break;
                case "gather_strength": f.MeleeBonus++; Add(f.Name + ": 힘 모으기 (누적 " + f.MeleeBonus + ")"); break;
                case "focus": f.LineBonus++; break;
                case "toughen": f.GuardBonus++; break;
                case "throw_training": f.ThrowBonus += 2; break;
                case "breathing": f.RegenPerRound++; break;
                case "barrier": f.BarrierNext = a.Card.guard + f.GuardBonus; break;
                case "regroup": case "stockpile": case "catch_breath": f.DrawNext++; break;
                case "poison_mist": AddField(a.Start, "poison", f.Side); break;
            }
        }

        // ───────── ③ 장판 ─────────

        void AddField(int cell, string kind, Side owner)
        {
            foreach (var f in Fields)
                if (f.Cell == cell && f.Kind == kind && f.Owner == owner) { f.SlotsLeft = Config.fieldSlots; return; }
            Fields.Add(new Field { Cell = cell, Kind = kind, Owner = owner, SlotsLeft = Config.fieldSlots });
            Add(cell + "칸에 " + (kind == "fire" ? "불" : "독") + " 장판이 생김");
        }

        void Fields_(int slot)
        {
            for (int i = Fields.Count - 1; i >= 0; i--)
            {
                var f = Fields[i];
                var victim = f.Owner == Side.Player ? Monster : Player;
                if (victim.Pos == f.Cell)
                {
                    if (f.Kind == "poison") { victim.Poison += 1; Add(victim.Name + ": 독 장판 — 독 +1"); }
                    else if (f.Kind == "fire") { victim.Hp -= Config.fireDamage; Add(victim.Name + ": 불 장판 — 피해 " + Config.fireDamage); }
                }
                f.SlotsLeft--;
                if (f.SlotsLeft <= 0) Fields.RemoveAt(i);
            }
        }

        // ───────── 라운드 끝 ─────────

        void EndRound()
        {
            Add("─ 라운드 끝 ─");
            foreach (var f in new[] { Player, Monster })
            {
                if (f.Bleed > 0) { f.Hp -= f.Bleed; Add(f.Name + ": 출혈 피해 " + f.Bleed + " (출혈은 줄지 않음)"); }
                if (f.Poison > 0) { f.Hp -= f.Poison; Add(f.Name + ": 독 피해 " + f.Poison + " (독 1 줄어듦)"); f.Poison--; }
                if (f.RegenPerRound > 0 && f.Hp > 0) { Add(f.Name + ": 호흡 — 체력 +" + f.Heal(f.RegenPerRound)); }
            }

            // 직전 라운드 카드 수 (몬스터 패턴 조건)
            int guard = 0, move = 0, range2 = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (Player.Cont[i]) continue;
                var c = Db.Card(Player.Plan[i]);
                if (c == null) continue;
                if (c.category == "shield" || c.category == "counter") guard++;
                if (c.category == "move") move++;
                if (c.IsLine || c.IsThrow || (c.IsMelee && c.MeleeRange >= 2)) range2++;
            }
            LastRoundGuardCards = guard; LastRoundMoveCards = move; LastRoundRange2Cards = range2;

            foreach (var id in inPlay)
            {
                int k = removeAfterRound.IndexOf(id);
                if (k >= 0) { removeAfterRound.RemoveAt(k); Removed.Add(id); }
                else Discard.Add(id);
            }
            inPlay.Clear();
            removeAfterRound.Clear();
            lastPattern = CurrentPattern;

            CheckDeath();
        }

        void CheckDeath()
        {
            if (Result != BattleResult.None) return;
            // 같은 때 둘 다 쓰러지면 플레이어 패배
            if (Player.Hp <= 0) Finish(BattleResult.Lost, Monster.Hp <= 0 ? "둘 다 쓰러졌다 — 패배 (동시에 쓰러지면 패배)" : "쓰러졌다 — 패배");
            else if (Monster.Hp <= 0) Finish(BattleResult.Won, Josa.Eul(Monster.Name) + " 쓰러뜨렸다 — 승리");
        }

        void Finish(BattleResult r, string msg)
        {
            Result = r;
            Phase = BattlePhase.Over;
            Add(msg);
        }

        // ───────── 도움 ─────────

        public PatternContext Context()
        {
            return new PatternContext
            {
                Cells = Cells,
                PlayerPos = Player.Pos, MonsterPos = Monster.Pos,
                PlayerHp = Player.Hp, PlayerMaxHp = Player.MaxHp,
                MonsterHp = Monster.Hp, MonsterMaxHp = Monster.MaxHp,
                PlayerPoison = Player.Poison,
                LastRoundGuardCards = LastRoundGuardCards,
                LastRoundMoveCards = LastRoundMoveCards,
                LastRoundRange2Cards = LastRoundRange2Cards,
            };
        }

        string Describe(Act a)
        {
            string who = a.F.Name;
            if (a.Card == null) return who + ": (빈 슬롯)";
            if (a.Cont) return who + ": " + a.Card.name + " (이어서)";
            return who + ": " + a.Card.name;
        }

        // "내 장검", "검사의 장검"
        string Of(Fighter f) { return f == Player ? "내 " : f.Name + "의 "; }

        bool InBounds(int c) { return c >= 1 && c <= Cells; }
        int Clamp(int c) { return Math.Max(1, Math.Min(Cells, c)); }
        void Add(string s) { Log.Add(s); }

        void Shuffle(List<string> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }
    }
}
