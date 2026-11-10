// 장비: 지금 손에 든 무기 1개와 인벤에 있는 예비 무기들. 순수 C#.
// - 데이터는 Assets/Data/equipment.json (임시 시험 데이터).
// - 같은 group(같은 모션을 쓰는 무기 묶음)끼리만 바꿔 낄 수 있다. 바꾸면 쓰던 무기는 인벤으로 돌아간다.
// - 지금은 전투 캐릭터 그림(무기 레이어)만 바뀐다. 덱·수치·배낭 무게와는 상관없다.
using System;
using System.Collections.Generic;

namespace CardGame.Core.Map
{
    [Serializable]
    public class WeaponDef
    {
        public string id;
        public string name;
        public string layer;   // 전투 캐릭터 그림의 무기 레이어 폴더 이름
        public string group;   // 같은 모션을 쓰는 무기 묶음 (예 one_hand)
    }

    [Serializable]
    public class EquipmentFile
    {
        public string note;
        public string start;              // 시작할 때 손에 든 무기 id
        public string[] startInventory;   // 시작할 때 인벤에 있는 무기 id
        public WeaponDef[] weapons;

        public WeaponDef Weapon(string id)
        {
            if (weapons != null) foreach (var w in weapons) if (w != null && w.id == id) return w;
            return null;
        }
    }

    public class Loadout
    {
        public WeaponDef Equipped { get; private set; }
        public readonly List<WeaponDef> Spare = new List<WeaponDef>();

        // 데이터 파일의 시작 장비로 만든다. 없는 id 는 건너뛴다
        public static Loadout Start(EquipmentFile file)
        {
            var l = new Loadout();
            if (file == null) return l;
            l.Equipped = file.Weapon(file.start);
            if (file.startInventory != null)
                foreach (var id in file.startInventory)
                {
                    var w = file.Weapon(id);
                    if (w != null) l.Spare.Add(w);
                }
            return l;
        }

        // 인벤의 그 무기로 바꿔 낄 수 있는지 (못 하면 이유)
        public string CannotEquip(int spareIndex)
        {
            if (spareIndex < 0 || spareIndex >= Spare.Count) return "없는 무기다.";
            var w = Spare[spareIndex];
            if (Equipped != null && w.group != Equipped.group) return "쓰는 모션이 달라 바꿀 수 없다.";
            return null;
        }

        // 바꿔 낀다. 쓰던 무기는 인벤의 같은 자리로 돌아간다
        public bool Equip(int spareIndex)
        {
            if (CannotEquip(spareIndex) != null) return false;
            var w = Spare[spareIndex];
            if (Equipped != null) Spare[spareIndex] = Equipped;
            else Spare.RemoveAt(spareIndex);
            Equipped = w;
            return true;
        }

        // 전투 캐릭터에 쓸 무기 레이어 (손에 든 게 없으면 null)
        public string Layer { get { return Equipped != null ? Equipped.layer : null; } }
    }
}
