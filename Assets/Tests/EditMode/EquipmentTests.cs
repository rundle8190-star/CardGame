// 장비창(무기 바꿔 끼기) 자동 테스트.
using System.IO;
using CardGame.Core.Map;
using NUnit.Framework;

namespace CardGame.Tests
{
    public class EquipmentTests
    {
        static EquipmentFile Sample()
        {
            return new EquipmentFile
            {
                start = "sword",
                startInventory = new[] { "bearded_axe", "mace", "bow", "nope" },
                weapons = new[]
                {
                    new WeaponDef { id = "sword", name = "검", layer = "sword", group = "one_hand" },
                    new WeaponDef { id = "bearded_axe", name = "도끼", layer = "bearded_axe", group = "one_hand" },
                    new WeaponDef { id = "mace", name = "철퇴", layer = "mace", group = "one_hand" },
                    new WeaponDef { id = "bow", name = "활", layer = "bow", group = "bow" },
                },
            };
        }

        [Test]
        public void 시작하면_검을_들고_인벤에_예비_무기가_있다()
        {
            var g = Loadout.Start(Sample());
            Assert.AreEqual("sword", g.Equipped.id);
            Assert.AreEqual(3, g.Spare.Count, "없는 id(nope)는 건너뛴다");
            Assert.AreEqual("sword", g.Layer);
        }

        [Test]
        public void 바꿔_끼면_쓰던_무기는_인벤으로_돌아간다()
        {
            var g = Loadout.Start(Sample());
            Assert.IsTrue(g.Equip(0));
            Assert.AreEqual("bearded_axe", g.Equipped.id);
            Assert.AreEqual("bearded_axe", g.Layer, "전투 그림은 도끼 레이어");
            Assert.AreEqual("sword", g.Spare[0].id, "쓰던 검은 같은 자리로");
            Assert.AreEqual(3, g.Spare.Count);
        }

        [Test]
        public void 모션이_다른_무기는_바꿔_낄_수_없다()
        {
            var g = Loadout.Start(Sample());
            int bow = g.Spare.FindIndex(w => w.id == "bow");
            Assert.IsNotNull(g.CannotEquip(bow));
            Assert.IsFalse(g.Equip(bow));
            Assert.AreEqual("sword", g.Equipped.id);
        }

        [Test]
        public void 데이터_파일의_무기는_모두_전투_그림이_있다()
        {
            string path = Path.Combine(TestData.ProjectRoot, "Assets/Data/equipment.json");
#if UNITY_5_3_OR_NEWER
            var file = UnityEngine.JsonUtility.FromJson<EquipmentFile>(File.ReadAllText(path));
#else
            var file = System.Text.Json.JsonSerializer.Deserialize<EquipmentFile>(File.ReadAllText(path), new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
#endif
            Assert.IsNotNull(file.Weapon(file.start));
            foreach (var id in file.startInventory) Assert.IsNotNull(file.Weapon(id), id);
            string[] motions = { "idle_shield_loop", "sword_regular_a", "sword_regular_b", "sword_regular_c", "sword_heavy_combo", "sword_regular_combo", "sword_block", "sword_dash", "hit_knockback" };
            foreach (var w in file.weapons)
                foreach (var m in motions)
                {
                    string png = Path.Combine(TestData.ProjectRoot, "Assets/Art/characters/woodcutter_ual2_x1_5/layers/" + w.layer + "/" + m + ".png");
                    Assert.IsTrue(File.Exists(png), w.id + " / " + m);
                }
        }
    }
}
