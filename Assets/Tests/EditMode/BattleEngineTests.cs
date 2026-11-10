// 전투 규칙 자동 테스트. 규칙 요약(Design/rules.md)의 예시를 그대로 옮겼다.
// 유니티 Test Runner(EditMode)에서 돌고, 유니티 밖(dotnet test)에서도 같은 파일로 돈다.
using System;
using System.Collections.Generic;
using System.IO;
using CardGame.Core.Battle;
using NUnit.Framework;

namespace CardGame.Tests
{
    public static class TestData
    {
        static BattleDatabase db;

        public static string ProjectRoot
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("CARDGAME_ROOT");
                if (!string.IsNullOrEmpty(env)) return env;
                return Directory.GetCurrentDirectory();   // 유니티에서는 프로젝트 폴더
            }
        }

        static T Read<T>(string relPath)
        {
            string text = BattleJson.Clean(File.ReadAllText(Path.Combine(ProjectRoot, relPath)));
#if UNITY_5_3_OR_NEWER
            return UnityEngine.JsonUtility.FromJson<T>(text);
#else
            return System.Text.Json.JsonSerializer.Deserialize<T>(text, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
#endif
        }

        // 규칙집 카드 + 임시 검사 몬스터·패턴·덱
        public static BattleDatabase Db
        {
            get
            {
                if (db != null) return db;
                var cards = Read<CardsFile>("Assets/Data/cards.json");
                var temp = "Assets/Data/test_swordsman.json";
                db = new BattleDatabase(cards, Read<DecksFile>(temp), Read<MonstersFile>(temp), Read<PatternsFile>(temp));
                return db;
            }
        }

        public static List<string> SwordDeck()
        {
            var list = new List<string>();
            foreach (var e in Db.Deck("sword_test").cards) for (int i = 0; i < e.count; i++) list.Add(e.card);
            return list;
        }

        // 위치와 체력을 정해 전투를 만든다 (라운드 시작 없이)
        public static BattleEngine Make(int playerPos, int monsterPos, int cells = 7, int playerHp = 50, int monsterHp = 40)
        {
            var s = new BattleSetup
            {
                Cells = cells,
                PlayerHp = playerHp, PlayerMaxHp = 50,
                PlayerPos = playerPos,
                Monster = Db.Monster("swordsman"),
                MonsterHp = monsterHp,
                MonsterPos = monsterPos,
                PlayerDeck = SwordDeck(),
                Seed = 1,
            };
            return new BattleEngine(Db, new BattleConfig(), s);
        }

        public static string[] Plan(params string[] ids)
        {
            var p = new string[BattleEngine.SlotCount];
            for (int i = 0; i < ids.Length && i < p.Length; i++) p[i] = ids[i];
            return p;
        }
    }

    public class BattleDataTests
    {
        [Test]
        public void 데이터_검사덱과_패턴에_없는_카드가_없다()
        {
            Assert.IsEmpty(TestData.Db.Problems());
            Assert.AreEqual(20, TestData.SwordDeck().Count);
            Assert.IsNotNull(TestData.Db.Monster("swordsman"));
            Assert.AreEqual(5, TestData.Db.PatternsOf("swordsman").Count);
        }

        [Test]
        public void 패턴_빈칸은_앞_대형무기가_이어진다()
        {
            var p = TestData.Db.PatternsOf("swordsman").Find(x => x.name == "필사의 일격");
            Assert.IsTrue(string.IsNullOrEmpty(p.slots[3]));
        }

        [Test]
        public void 몬스터_시작칸은_칸수에_맞춰_옮긴다()
        {
            var m = TestData.Db.Monster("swordsman");   // 7칸 기준 6
            Assert.AreEqual(6, BattleEngine.MonsterStartCell(m, 7));
            Assert.AreEqual(4, BattleEngine.MonsterStartCell(m, 5));
            Assert.AreEqual(8, BattleEngine.MonsterStartCell(m, 9));
        }
    }

    public class BattleSlotTests
    {
        [Test]
        public void 롱소드_거리1_명중_거리2_빗나감()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("longsword"), TestData.Plan());
            b.ResolveSlot(0);
            Assert.AreEqual(35, b.Monster.Hp);

            b = TestData.Make(3, 5);
            b.SetPlansForTest(TestData.Plan("longsword"), TestData.Plan());
            b.ResolveSlot(0);
            Assert.AreEqual(40, b.Monster.Hp);
        }

        [Test]
        public void 레이피어는_사거리2()
        {
            var b = TestData.Make(3, 5);
            b.SetPlansForTest(TestData.Plan("rapier"), TestData.Plan());
            b.ResolveSlot(0);
            Assert.AreEqual(36, b.Monster.Hp);
        }

        [Test]
        public void 양쪽_동시에_공격한다()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("longsword"), TestData.Plan("longsword"));
            b.ResolveSlot(0);
            Assert.AreEqual(45, b.Player.Hp);
            Assert.AreEqual(35, b.Monster.Hp);
        }

        [Test]
        public void 방어는_넘친_만큼만_들어온다()
        {
            // 받아넘기기 방어 3, 롱소드 5 → 2
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("parry"), TestData.Plan("longsword"));
            b.ResolveSlot(0);
            Assert.AreEqual(48, b.Player.Hp);
        }

        [Test]
        public void 받아넘기기_공격받으면_다음슬롯_무기_플러스2()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("parry", "longsword"), TestData.Plan("longsword"));
            b.ResolveSlot(0);
            b.ResolveSlot(1);
            Assert.AreEqual(33, b.Monster.Hp);   // 5 + 2
        }

        [Test]
        public void 관통은_방어를_무시한다()
        {
            // 처형 일격 9, 관통 9 → 받아넘기기 방어 3 무시
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("execution_strike"), TestData.Plan("parry"));
            b.ResolveSlot(0);
            Assert.AreEqual(31, b.Monster.Hp);
        }

        [Test]
        public void 전진은_상대를_넘지_못한다()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("advance"), TestData.Plan());
            b.ResolveSlot(0);
            Assert.AreEqual(3, b.Player.Pos);
        }

        [Test]
        public void 같은칸이면_NPC_우선()
        {
            var b = TestData.Make(3, 5);
            b.SetPlansForTest(TestData.Plan("advance"), TestData.Plan("advance"));
            b.ResolveSlot(0);
            Assert.AreEqual(4, b.Monster.Pos);
            Assert.AreEqual(3, b.Player.Pos);
        }

        [Test]
        public void 벽에서는_더_물러나지_못한다()
        {
            var b = TestData.Make(1, 4);
            b.SetPlansForTest(TestData.Plan("dodge"), TestData.Plan());
            b.ResolveSlot(0);
            Assert.AreEqual(1, b.Player.Pos);
        }

        [Test]
        public void 구르기_붙어있으면_뒤로_넘고_그_슬롯엔_근접이_닿지_않는다()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("roll"), TestData.Plan("longsword"));
            b.ResolveSlot(0);
            Assert.AreEqual(5, b.Player.Pos);
            Assert.AreEqual(50, b.Player.Hp);
        }

        [Test]
        public void 구르기_떨어져_있으면_앞으로_1칸()
        {
            var b = TestData.Make(2, 6);
            b.SetPlansForTest(TestData.Plan("roll"), TestData.Plan());
            b.ResolveSlot(0);
            Assert.AreEqual(3, b.Player.Pos);
        }

        [Test]
        public void 치명_넘은_다음슬롯에_상대가_공격방어반격_안하면_2배()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("roll", "longsword"), TestData.Plan(null, "advance"));
            b.ResolveSlot(0);
            b.ResolveSlot(1);
            Assert.AreEqual(30, b.Monster.Hp);   // 5 × 2
        }

        [Test]
        public void 치명_상대가_방어하면_없다()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("roll", "longsword"), TestData.Plan(null, "parry"));
            b.ResolveSlot(0);
            b.ResolveSlot(1);
            Assert.AreEqual(38, b.Monster.Hp);   // 5 − 방어 3
        }

        [Test]
        public void 톱날검_출혈2_라운드끝_피해_줄지_않음()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("serrated_sword"), TestData.Plan());
            for (int i = 0; i < 6; i++) b.Step();   // 5슬롯 + 라운드 끝
            Assert.AreEqual(2, b.Monster.Bleed);
            Assert.AreEqual(40 - 4 - 2, b.Monster.Hp);
        }

        [Test]
        public void 칼갈기는_다음슬롯부터_참격에_출혈1()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("sharpen", "longsword"), TestData.Plan());
            b.ResolveSlot(0);
            b.ResolveSlot(1);
            Assert.AreEqual(1, b.Monster.Bleed);
        }

        [Test]
        public void 레이피어는_찌르기라_칼갈기_출혈이_없다()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("sharpen", "rapier"), TestData.Plan());
            b.ResolveSlot(0);
            b.ResolveSlot(1);
            Assert.AreEqual(0, b.Monster.Bleed);
        }

        [Test]
        public void 힘모으기_같은슬롯엔_적용안되고_다음슬롯부터_누적()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("gather_strength", "longsword"), TestData.Plan());
            b.ResolveSlot(0);
            Assert.AreEqual(40, b.Monster.Hp);
            b.ResolveSlot(1);
            Assert.AreEqual(34, b.Monster.Hp);   // 5 + 1
        }

        [Test]
        public void 회전베기는_두슬롯_모두_공격()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("whirlwind_slash", null), TestData.Plan());
            b.ResolveSlot(0);
            b.ResolveSlot(1);
            Assert.AreEqual(40 - 12, b.Monster.Hp);
        }

        [Test]
        public void 받아넘기기는_대형무기도_막는다()
        {
            // 회전 베기 6 − 방어 3 = 3, 두 슬롯 모두 받아넘기기로 막으면 6
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("parry", "parry"), TestData.Plan("whirlwind_slash", null));
            b.ResolveSlot(0);
            b.ResolveSlot(1);
            Assert.AreEqual(44, b.Player.Hp);
        }

        [Test]
        public void 철방패는_대형무기를_못막는다()
        {
            var b = TestData.Make(3, 4);
            b.SetPlansForTest(TestData.Plan("iron_shield"), TestData.Plan("whirlwind_slash", null));
            b.ResolveSlot(0);
            Assert.AreEqual(44, b.Player.Hp);   // 방어 7 무시, 피해 6
        }

        [Test]
        public void 같은슬롯_동시사망은_플레이어_패배()
        {
            var b = TestData.Make(3, 4, playerHp: 5, monsterHp: 5);
            b.SetPlansForTest(TestData.Plan("longsword"), TestData.Plan("longsword"));
            b.Step();
            Assert.AreEqual(BattleResult.Lost, b.Result);
        }

        [Test]
        public void 응급처치_체력4_최대체력_넘지_않음()
        {
            var b = TestData.Make(2, 6, playerHp: 48);
            b.SetPlansForTest(TestData.Plan("first_aid"), TestData.Plan());
            b.ResolveSlot(0);
            Assert.AreEqual(50, b.Player.Hp);
        }

        [Test]
        public void 회복_로그는_실제로_오른_만큼_적는다()
        {
            var b = TestData.Make(2, 6, playerHp: 48);
            b.SetPlansForTest(TestData.Plan("first_aid"), TestData.Plan());
            b.ResolveSlot(0);
            Assert.IsTrue(b.Log.Exists(l => l.EndsWith("체력 +2")), "48 -> 50 이면 +2");

            var full = TestData.Make(2, 6);
            full.SetPlansForTest(TestData.Plan("first_aid"), TestData.Plan());
            full.ResolveSlot(0);
            Assert.IsTrue(full.Log.Exists(l => l.EndsWith("체력 +0")), "최대 체력이면 +0");
        }

        [Test]
        public void 숨고르기_다음라운드_손패_플러스1()
        {
            var b = TestData.Make(2, 6, playerHp: 40);
            b.SetPlansForTest(TestData.Plan("catch_breath"), TestData.Plan());
            for (int i = 0; i < 6; i++) b.Step();
            Assert.AreEqual(42, b.Player.Hp);
            Assert.AreEqual(8, b.Hand.Count);   // 7 + 1
        }
    }

    public class BattleFlowTests
    {
        [Test]
        public void 라운드시작_손패7장_몬스터패턴_공개()
        {
            var b = TestData.Make(2, 6);
            b.StartRound();
            Assert.AreEqual(7, b.Hand.Count);
            Assert.IsNotNull(b.CurrentPattern);
            Assert.AreEqual(BattlePhase.Planning, b.Phase);
        }

        [Test]
        public void 대형무기는_다음슬롯이_비어야_한다()
        {
            var b = TestData.Make(2, 6);
            b.StartRound();
            b.Hand.Clear();
            b.Hand.AddRange(new[] { "whirlwind_slash", "longsword" });
            Assert.IsNotNull(b.ValidatePlan(new[] { -1, -1, -1, -1, 0 }));   // 5번 슬롯엔 못 놓음
            Assert.IsNotNull(b.ValidatePlan(new[] { 0, 1, -1, -1, -1 }));    // 이어지는 칸에 다른 카드
            Assert.IsNull(b.ValidatePlan(new[] { 0, -1, 1, -1, -1 }));
            Assert.IsNull(b.Commit(new[] { 0, -1, 1, -1, -1 }));
            Assert.IsTrue(b.Player.Cont[1]);
            Assert.AreEqual(0, b.Hand.Count);
        }

        [Test]
        public void 끝까지_자동으로_돌려도_멈추지_않고_승패가_난다()
        {
            for (int seed = 1; seed <= 30; seed++)
            {
                var s = new BattleSetup { Cells = 5 + (seed % 3) * 2, Monster = TestData.Db.Monster("swordsman"), PlayerDeck = TestData.SwordDeck(), Seed = seed };
                var b = new BattleEngine(TestData.Db, new BattleConfig(), s);
                b.StartRound();
                var rng = new Random(seed);
                int guard = 0;
                while (b.Result == BattleResult.None && guard++ < 1000)
                {
                    // 손패 앞에서부터 놓을 수 있는 대로 놓는다
                    var plan = new[] { -1, -1, -1, -1, -1 };
                    int h = 0;
                    for (int i = 0; i < 5 && h < b.Hand.Count; i++, h++)
                    {
                        plan[i] = h;
                        if (b.ValidatePlan(plan) != null) { plan[i] = -1; continue; }
                        if (TestData.Db.Card(b.Hand[h]).large) i++;
                    }
                    Assert.IsNull(b.Commit(plan));
                    while (b.Phase == BattlePhase.Resolving) b.Step();
                }
                Assert.AreNotEqual(BattleResult.None, b.Result, "seed " + seed);
            }
        }
    }

    public class MonsterAITests
    {
        [Test]
        public void 조건패턴_가중치3_기본1_안맞으면0()
        {
            var pats = TestData.Db.PatternsOf("swordsman");
            var ctx = new PatternContext { Cells = 7, PlayerPos = 2, MonsterPos = 6, PlayerHp = 50, PlayerMaxHp = 50, MonsterHp = 40, MonsterMaxHp = 40 };
            Assert.AreEqual(1, MonsterAI.WeightOf(pats.Find(p => p.isDefault), ctx));
            Assert.AreEqual(3, MonsterAI.WeightOf(pats.Find(p => p.name == "거리 좁히기"), ctx));   // 거리 4
            Assert.AreEqual(0, MonsterAI.WeightOf(pats.Find(p => p.name == "등 뒤 노리기"), ctx));
        }

        [Test]
        public void 직전_패턴은_연속으로_쓰지_않는다()
        {
            var pats = TestData.Db.PatternsOf("swordsman");
            var ctx = new PatternContext { Cells = 7, PlayerPos = 2, MonsterPos = 6, PlayerHp = 50, PlayerMaxHp = 50, MonsterHp = 40, MonsterMaxHp = 40 };
            var last = pats.Find(p => p.name == "거리 좁히기");
            var rng = new Random(3);
            for (int i = 0; i < 50; i++) Assert.AreNotSame(last, MonsterAI.Choose(pats, ctx, last, rng));
        }
    }
}
