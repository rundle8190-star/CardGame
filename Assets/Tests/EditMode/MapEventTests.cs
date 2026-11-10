// 맵 이벤트(? 노드)와 조사 고르기 자동 테스트.
using System.Collections.Generic;
using System.IO;
using CardGame.Core;
using CardGame.Core.Map;
using NUnit.Framework;

namespace CardGame.Tests
{
    public class JosaTests
    {
        [Test]
        public void 받침에_따라_조사를_고른다()
        {
            Assert.AreEqual("검사를", Josa.Eul("검사"));
            Assert.AreEqual("장검을", Josa.Eul("장검"));
            Assert.AreEqual("검사가", Josa.I("검사"));
            Assert.AreEqual("화살이", Josa.I("화살"));
            Assert.AreEqual("검사는", Josa.Eun("검사"));
            Assert.AreEqual("철퇴로", Josa.Ro("철퇴"));
            Assert.AreEqual("마을로", Josa.Ro("마을"));     // ㄹ 받침은 "로"
            Assert.AreEqual("숲으로", Josa.Ro("숲"));
            Assert.AreEqual("5를", Josa.Eul(5));
            Assert.AreEqual("10을", Josa.Eul(10));
            Assert.AreEqual("2로", Josa.Ro(2));
            Assert.AreEqual("50으로", Josa.Ro(50));
        }
    }

    public class MapEventTests
    {
        // 시작(0,0) — 전투(2,0) — 이벤트(4,0) — 이벤트(6,0) 일렬
        static MapData Line()
        {
            var m = MapData.Create("test", "시험", 8, 1);
            var s = new MapNode { x = 0, y = 0, type = "start" };
            var b = new MapNode { x = 2, y = 0, type = "battle" };
            var e1 = new MapNode { x = 4, y = 0, type = "event" };
            var e2 = new MapNode { x = 6, y = 0, type = "event" };
            m.nodes.AddRange(new[] { s, b, e1, e2 });
            m.links.Add(new MapLink(s, b));
            m.links.Add(new MapLink(b, e1));
            m.links.Add(new MapLink(e1, e2));
            return m;
        }

        static EventEffect Ef(string type, int amount) { return new EventEffect { type = type, amount = amount }; }

        static EventDef One(params EventChoice[] choices)
        {
            return new EventDef { id = "t", title = "시험 이벤트", text = "", choices = choices };
        }

        // 이벤트 노드(4,0)까지 걸어간 탐험
        static Exploration At(EventDef ev, int seed = 1)
        {
            var map = Line();
            var ex = new Exploration(map, new ExplorationConfig { respawnEvery = 0 }, null, seed);
            ex.SetEvents(new List<EventDef> { ev });
            ex.Move(map.NodeAt(2, 0));
            ex.ResolveBattleResult(map.NodeAt(2, 0), true, 50);
            ex.Move(map.NodeAt(4, 0));
            return ex;
        }

        [Test]
        public void 데이터_파일의_이벤트_6개를_읽는다()
        {
            string path = Path.Combine(TestData.ProjectRoot, "Assets/Data/events.json");
#if UNITY_5_3_OR_NEWER
            var file = UnityEngine.JsonUtility.FromJson<EventsFile>(File.ReadAllText(path));
#else
            var file = System.Text.Json.JsonSerializer.Deserialize<EventsFile>(File.ReadAllText(path), new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
#endif
            Assert.AreEqual(6, file.events.Length);
            foreach (var e in file.events)
            {
                // 무난한 보상 + 대가 있는 좋은 보상 + 지나가기(돌아가기) = 3개
                Assert.AreEqual(3, e.choices.Length, e.id);
                Assert.IsTrue(e.choices[e.choices.Length - 1].leave, e.id + ": 마지막은 지나가기·돌아가기");
                foreach (var c in e.choices)
                {
                    Assert.LessOrEqual(c.chance, 0, e.id + ": 숨은 확률은 쓰지 않는다");
                    Assert.IsFalse(string.IsNullOrEmpty(c.hint), e.id + ": 버튼에 보상·대가를 적는다");
                    if (c.effects != null) foreach (var ef in c.effects) Assert.AreNotEqual("reveal", ef.type, e.id + ": 주변 살피기는 뺐다");
                }
                Assert.IsTrue(e.choices[1].hint.Contains("대신"), e.id + ": 두 번째는 대가가 적힌 좋은 보상");
            }
        }

        [Test]
        public void 같은_시드면_같은_이벤트와_같은_확률_결과()
        {
            var map = Line();
            var evs = new List<EventDef>();
            for (int i = 0; i < 6; i++) evs.Add(new EventDef { id = "e" + i, title = "e" + i, choices = new[] { new EventChoice { label = "a" } } });
            var a = new Exploration(map, null, null, 42); a.SetEvents(evs);
            var b = new Exploration(Line(), null, null, 42); b.SetEvents(evs);
            Assert.AreEqual(a.EventAt(map.NodeAt(4, 0)).id, b.EventAt(b.Map.NodeAt(4, 0)).id);
            Assert.AreNotEqual(a.EventAt(map.NodeAt(4, 0)).id, a.EventAt(map.NodeAt(6, 0)).id, "이벤트 노드 2곳은 서로 다른 이벤트");
            for (int c = 0; c < 5; c++)
                Assert.AreEqual(a.RollSuccess(map.NodeAt(4, 0), c, 50), b.RollSuccess(b.Map.NodeAt(4, 0), c, 50));
        }

        [Test]
        public void 결과가_나면_노드를_다_쓰고_지나가기는_남긴다()
        {
            var ev = One(new EventChoice { label = "기도", effects = new[] { Ef("hp", 10) } }, new EventChoice { label = "지나간다", leave = true });
            var ex = At(ev);
            var n = ex.CurrentNode;
            ex.ChooseEvent(n, 1);
            Assert.IsFalse(ex.IsCleared(n), "지나가기는 노드를 남긴다");
            ex.ChooseEvent(n, 0);
            Assert.IsTrue(ex.IsCleared(n));
            Assert.IsNull(ex.ChooseEvent(n, 0), "다 쓴 노드는 다시 못 고른다");
        }

        [Test]
        public void 체력은_최대치까지만_오른다()
        {
            var ex = At(One(new EventChoice { label = "기도", effects = new[] { Ef("hp", 10) } }));
            Assert.AreEqual(50, ex.Hp);
            ex.ChooseEvent(ex.CurrentNode, 0);
            Assert.AreEqual(50, ex.Hp);
        }

        [Test]
        public void 재화가_모자라거나_토템이_가득하면_고를_수_없다()
        {
            var c = new EventChoice { label = "제물", needMoney = 10, needTotemRoom = true, effects = new[] { Ef("money", -10), Ef("totem", 1) } };
            var ex = At(One(c));
            // 전투 1번(쉬움 5)만으로는 재화 5
            Assert.Less(ex.Money, 10);
            Assert.IsNotNull(ex.CannotChoose(c));
            Assert.IsNull(ex.ChooseEvent(ex.CurrentNode, 0));
        }

        [Test]
        public void 응급처치_체력_5_이하면_못한다()
        {
            var c = new EventChoice { label = "처치", needHpAbove = 5, effects = new[] { Ef("hp", -5), Ef("money", 15) } };
            var ex = At(One(c));
            int money = ex.Money;
            ex.ChooseEvent(ex.CurrentNode, 0);
            Assert.AreEqual(45, ex.Hp);
            Assert.AreEqual(money + 15, ex.Money);
        }

        [Test]
        public void 다음_전투_효과가_쌓이고_비워진다()
        {
            var ex = At(One(new EventChoice { label = "턴다", effects = new[] { new EventEffect { type = "weapon", amount = 1, name = "용병의 무기" }, Ef("next_battle_enemy_hp", 10), Ef("next_battle_poison", 2) } }));
            int bag = ex.Bag.Count;
            ex.ChooseEvent(ex.CurrentNode, 0);
            Assert.AreEqual(bag + 1, ex.Bag.Count);
            Assert.AreEqual("용병의 무기", ex.Bag[ex.Bag.Count - 1].name);
            Assert.AreEqual(10, ex.NextBattleEnemyHp);
            Assert.AreEqual(2, ex.NextBattlePoison);
            ex.ClearNextBattleMods();
            Assert.AreEqual(0, ex.NextBattleEnemyHp);
        }

        [Test]
        public void 싸운다는_전투를_열고_이기면_재화_2배()
        {
            var ex = At(One(new EventChoice { label = "싸운다", effects = new[] { new EventEffect { type = "battle", amount = 2, name = "도적" } } }));
            var n = ex.CurrentNode;
            var o = ex.ChooseEvent(n, 0);
            Assert.IsTrue(o.Battle);
            Assert.AreEqual(2, o.BattleMoneyMult);
            Assert.IsFalse(ex.IsCleared(n), "전투가 끝나야 정리된다");
            int money = ex.Money;
            int lv = ex.DifficultyOf(n);
            ex.ResolveBattleResult(n, true, 40, o.BattleMoneyMult, o.BattleEnemy);
            Assert.AreEqual(money + ex.Config.battleMoney[lv] * 2, ex.Money);
            Assert.IsTrue(ex.IsCleared(n));
            Assert.AreEqual("도적의 무기", ex.Bag[ex.Bag.Count - 1].name);
        }

        [Test]
        public void 돌아간다는_직전_노드로_옮긴다()
        {
            var ex = At(One(new EventChoice { label = "돌아간다", leave = true, effects = new[] { Ef("go_back", 1) } }));
            var n = ex.CurrentNode;
            var o = ex.ChooseEvent(n, 0);
            Assert.IsTrue(o.MovedBack);
            Assert.AreEqual(2, ex.X);
            Assert.IsFalse(ex.IsCleared(n));
        }

        [Test]
        public void 쉰다는_지나온_전투_노드에_적을_되살린다()
        {
            var ex = At(One(new EventChoice { label = "쉰다", effects = new[] { Ef("hp", 8), Ef("respawn", 1) } }));
            var battle = ex.Map.NodeAt(2, 0);
            Assert.IsTrue(ex.IsCleared(battle));
            ex.ChooseEvent(ex.CurrentNode, 0);
            Assert.IsFalse(ex.IsCleared(battle));
        }

        [Test]
        public void 살핀다는_이번_노드에서만_시야를_넓힌다()
        {
            var ex = At(One(new EventChoice { label = "살핀다", effects = new[] { Ef("reveal", 1) } }));
            // 시야 1: (4,0)에서 이웃 (6,0)까지 보임. (0,0) 시작은 2번 너머라 '?' 또는 기억
            var start = ex.Map.NodeAt(0, 0);
            Assert.AreNotEqual(CellView.Visible, ex.NodeView(start));
            ex.ChooseEvent(ex.CurrentNode, 0);
            Assert.AreEqual(CellView.Visible, ex.NodeView(start));
        }

        [Test]
        public void 확률_선택지는_좋은_쪽_나쁜_쪽이_따로_적용된다()
        {
            var c = new EventChoice
            {
                label = "연다", chance = 50,
                effects = new[] { Ef("money", 20) },
                failEffects = new[] { Ef("next_battle_poison", 2) },
            };
            bool sawGood = false, sawBad = false;
            for (int seed = 1; seed < 40 && !(sawGood && sawBad); seed++)
            {
                var ex = At(One(c), seed);
                int money = ex.Money;
                var o = ex.ChooseEvent(ex.CurrentNode, 0);
                if (o.Success) { sawGood = true; Assert.AreEqual(money + 20, ex.Money); Assert.AreEqual(0, ex.NextBattlePoison); }
                else { sawBad = true; Assert.AreEqual(money, ex.Money); Assert.AreEqual(2, ex.NextBattlePoison); }
            }
            Assert.IsTrue(sawGood && sawBad, "시드에 따라 두 결과가 모두 나와야 한다");
        }

        [Test]
        public void 고른_선택지의_대답과_말한_사람이_결과에_담긴다()
        {
            var ev = new EventDef
            {
                id = "t", title = "다친 용병", text = "", speaker = "다친 용병", line = "붕대 좀 있나?",
                choices = new[]
                {
                    new EventChoice { label = "준다", reply = "고맙다." },
                    new EventChoice { label = "턴다", reply = "이 날강도…", replySpeaker = "화난 용병" },
                    new EventChoice { label = "지나간다", leave = true },
                },
            };
            var ex = At(ev);
            var o = ex.ChooseEvent(ex.CurrentNode, 0);
            Assert.AreEqual("다친 용병", o.Speaker);
            Assert.AreEqual("고맙다.", o.Reply);
            ex = At(ev);
            o = ex.ChooseEvent(ex.CurrentNode, 1);
            Assert.AreEqual("화난 용병", o.Speaker, "replySpeaker 가 있으면 그 사람");
            ex = At(ev);
            o = ex.ChooseEvent(ex.CurrentNode, 2);
            Assert.IsNull(o.Reply, "대답이 없으면 비어 있다");
        }

        [Test]
        public void 데이터_파일의_대사는_말하는_사람과_함께_있다()
        {
            string path = Path.Combine(TestData.ProjectRoot, "Assets/Data/events.json");
#if UNITY_5_3_OR_NEWER
            var file = UnityEngine.JsonUtility.FromJson<EventsFile>(File.ReadAllText(path));
#else
            var file = System.Text.Json.JsonSerializer.Deserialize<EventsFile>(File.ReadAllText(path), new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
#endif
            int talking = 0;
            foreach (var e in file.events)
            {
                if (!string.IsNullOrEmpty(e.line)) { talking++; Assert.IsFalse(string.IsNullOrEmpty(e.speaker), e.id + ": 대사에는 말하는 사람이 있어야 한다"); }
                foreach (var c in e.choices)
                    if (!string.IsNullOrEmpty(c.reply))
                        Assert.IsFalse(string.IsNullOrEmpty(e.speaker) && string.IsNullOrEmpty(c.replySpeaker), e.id + "/" + c.label + ": 대답한 사람을 알 수 있어야 한다");
                // 상황 설명은 ~다 체 (~요 로 끝나지 않는다)
                Assert.IsFalse(e.text.TrimEnd().EndsWith("요."), e.id + ": " + e.text);
            }
            Assert.GreaterOrEqual(talking, 2, "사람이 나오는 이벤트(용병, 도적)에는 대사가 있다");
        }
    }

    public class MapLinkGateTests
    {
        [Test]
        public void 막힌_지름길은_이웃으로_치지_않는다()
        {
            var m = MapData.Create("t", "t", 6, 1);
            var a = new MapNode { x = 0, y = 0, type = "start" };
            var b = new MapNode { x = 2, y = 0, type = "battle" };
            var c = new MapNode { x = 4, y = 0, type = "battle" };
            m.nodes.AddRange(new[] { a, b, c });
            m.links.Add(new MapLink(a, b));
            m.links.Add(new MapLink(a, c) { gate = "visit:4,0", obstacle = "bridge" });
            var nb = m.Neighbors(a);
            Assert.Contains(b, nb);
            Assert.IsFalse(nb.Contains(c), "막힌 길로는 갈 수 없다");
        }

        [Test]
        public void 삼각주_맵을_읽는다()
        {
            string path = Path.Combine(TestData.ProjectRoot, "Assets/Data/Maps/alt_delta.json");
#if UNITY_5_3_OR_NEWER
            var map = UnityEngine.JsonUtility.FromJson<MapData>(File.ReadAllText(path));
#else
            var map = System.Text.Json.JsonSerializer.Deserialize<MapData>(File.ReadAllText(path), new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
#endif
            map.FixRows();
            Assert.AreEqual(144, map.width);
            Assert.AreEqual(119, map.nodes.Count);
            Assert.AreEqual(152, map.links.Count);
            Assert.AreEqual(2, map.links.FindAll(l => l.IsGated).Count, "다리 지름길 2개");
            var resolved = RandomNodeGenerator.Resolve(map, 7);
            Assert.AreEqual(0, resolved.nodes.FindAll(n => n.type == "random").Count, "랜덤 칸은 모두 정해진다");
            var ex = new Exploration(resolved, null, null, 7);
            Assert.Greater(ex.Neighbors.Count, 0, "시작 지점에서 갈 곳이 있다");
        }
    }
}
