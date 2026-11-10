// 전투 화면의 캐릭터 무대. 두 캐릭터를 칸 위에 세우고, 슬롯마다 카드에 맞는 모션을 틀어 준다.
// 카드 → 모션 연결은 Assets/Data/battle_motions.json (임시 데이터).
// 규칙은 이미 BattleEngine 이 끝낸 결과를 받아서 보여 주기만 한다 (체력 표시는 맞는 순간에 줄어든다).
using System;
using System.Collections.Generic;
using System.IO;
using CardGame.Core.Battle;
using UnityEngine;

namespace CardGame.View.Battle
{
    [Serializable]
    public class MotionEntry { public string card; public string motion; public bool reverse; }

    [Serializable]
    public class MotionMap
    {
        public string playerCharacter = "woodcutter";
        public string monsterCharacter = "ranger";
        public string weaponLayer = "sword";
        public int playerClothRow;
        public int monsterClothRow = 2;
        public string idle = "ual2:idle_shield_loop";
        public string hit = "ual2:hit_knockback";
        public string death = "ual:death01";
        public MotionEntry[] cards;

        public MotionEntry Of(string cardId)
        {
            if (cards != null) foreach (var e in cards) if (e.card == cardId) return e;
            return null;
        }

        public static MotionMap Load()
        {
            try { return JsonUtility.FromJson<MotionMap>(File.ReadAllText(Path.Combine(Application.dataPath, "Data/battle_motions.json"))); }
            catch (Exception e) { Debug.LogWarning("battle_motions.json 을 읽지 못했어요: " + e.Message); return new MotionMap(); }
        }
    }

    public class BattleStage : MonoBehaviour
    {
        // 화면 위 칸 위치 (BattleView 가 그릴 때마다 알려 준다, 세로 900 기준 좌표)
        [NonSerialized] public float LaneX0, CellW, FloorY, GuiScale = 1f;
        [NonSerialized] public float SheetPixel = 2f;   // 시트 1픽셀 = 화면(세로 900 기준) 몇 픽셀

        static readonly Vector3 Origin = new Vector3(10000, 10000, 0);

        class Side
        {
            public SheetActor Actor;
            public string Character;
            public string Weapon;   // 이 캐릭터가 든 무기 레이어
            public float Pos, From, To, MoveStart, MoveDur;
            public int ShownHp;
        }

        MotionMap map;
        BattleEngine b;
        Side player, monster;
        Camera cam;
        Vector3 camPos; float camSize; Color camBg; CameraClearFlags camClear;
        readonly List<KeyValuePair<float, Action>> timeline = new List<KeyValuePair<float, Action>>();
        float speed = 1f;
        public static readonly Color Background = new Color(0.08f, 0.08f, 0.1f, 1f);

        public int ShownHp(Fighter f) { var s = f == b.Player ? player : monster; return s != null ? s.ShownHp : f.Hp; }

        // 플레이어가 든 무기 레이어 (장비창에서 고른 것). 비우면 battle_motions.json 의 weaponLayer
        [NonSerialized] public string PlayerWeaponLayer;

        public void Begin(BattleEngine engine)
        {
            b = engine;
            map = MotionMap.Load();
            transform.position = Origin;
            player = MakeSide("Player", map.playerCharacter, map.playerClothRow, 10, b.Player, string.IsNullOrEmpty(PlayerWeaponLayer) ? map.weaponLayer : PlayerWeaponLayer);
            monster = MakeSide("Monster", map.monsterCharacter, map.monsterClothRow, 20, b.Monster, map.weaponLayer);
            if (SheetLibrary.Missing.Count > 0) Debug.LogWarning("전투 모션 시트 없음: " + string.Join(", ", SheetLibrary.Missing.ToArray()));

            cam = Camera.main;
            if (cam != null)
            {
                camPos = cam.transform.position; camSize = cam.orthographicSize; camBg = cam.backgroundColor; camClear = cam.clearFlags;
                cam.transform.position = Origin + new Vector3(0, 0, -10);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Background;
            }
        }

        Side MakeSide(string name, string character, int clothRow, int order, Fighter f, string weapon)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<SheetActor>();
            a.Init(character, clothRow, order);
            a.SetIdle(SheetLibrary.Load(character, map.idle, weapon));
            return new Side { Actor = a, Character = character, Weapon = weapon, Pos = f.Pos, From = f.Pos, To = f.Pos, ShownHp = f.Hp };
        }

        public void End()
        {
            if (cam != null)
            {
                cam.transform.position = camPos; cam.orthographicSize = camSize; cam.backgroundColor = camBg; cam.clearFlags = camClear;
            }
            Destroy(gameObject);
        }

        public void SetSpeed(float s)
        {
            speed = s;
            if (player != null) { player.Actor.Speed = s; monster.Actor.Speed = s; }
        }

        // 라운드 시작 등: 표시를 실제 값에 맞춘다
        public void Sync()
        {
            foreach (var s in new[] { player, monster })
            {
                var f = s == player ? b.Player : b.Monster;
                s.ShownHp = f.Hp;
                s.From = s.To = s.Pos = f.Pos;
            }
        }

        // 슬롯 하나가 처리된 직후 부른다. 모션을 걸고, 다음 단계까지 기다릴 시간(초)을 준다.
        public float AnimateSlot(int slot, int hpP0, int hpM0, int posP0, int posM0)
        {
            timeline.Clear();
            float t0 = Time.time;
            var pm = StartMotion(player, b.Player, slot, posP0);
            var mm = StartMotion(monster, b.Monster, slot, posM0);

            float end = Mathf.Max(0.7f, Mathf.Max(pm.Key, mm.Key));
            end = Mathf.Max(end, Damage(player, b.Player, hpP0, mm.Value, pm.Value));
            end = Mathf.Max(end, Damage(monster, b.Monster, hpM0, pm.Value, mm.Value));
            end = Mathf.Min(end, 5f);
            return end / speed + 0.15f;
        }

        // 라운드 끝(출혈·독) 처럼 모션 없이 체력만 바뀌는 단계
        public float AnimateRoundEnd(int hpP0, int hpM0)
        {
            timeline.Clear();
            float end = 0.5f;
            end = Mathf.Max(end, Damage(player, b.Player, hpP0, -1f, null));
            end = Mathf.Max(end, Damage(monster, b.Monster, hpM0, -1f, null));
            return end / speed + 0.1f;
        }

        // 카드 모션 시작. (끝나는 시간, 타격 시간) 을 준다. 타격 시간 −1 = 공격 아님
        KeyValuePair<float, float> StartMotion(Side s, Fighter f, int slot, int pos0)
        {
            s.From = pos0; s.To = f.Pos; s.MoveStart = Time.time; s.MoveDur = 0.35f / speed;
            string id = f.Plan[slot];
            var card = b.Db.Card(id);
            if (card == null || (f.Cont[slot] && card.id != "whirlwind_slash" && card.id != "dash_thrust"))
            {
                s.Pos = s.To;
                return new KeyValuePair<float, float>(0f, -1f);
            }
            var e = map.Of(card.id);
            var m = e != null ? SheetLibrary.Load(s.Character, e.motion, s.Weapon) : null;
            if (m == null) return new KeyValuePair<float, float>(0.5f, card.IsWeapon ? 0.3f : -1f);

            s.Actor.Play(m, e.reverse);
            if (pos0 != f.Pos)
            {
                // 이동 모션은 모션 길이에 맞춰 미끄러진다 (돌진은 타격 프레임까지)
                float d = m.HitTime > 0 ? m.HitTime : m.Duration * (e.reverse ? 0.6f : 0.8f);
                s.MoveDur = Mathf.Max(0.25f, d) / speed;
            }
            float hit = card.IsWeapon ? (m.HitTime >= 0 ? m.HitTime : m.Duration * 0.5f) : -1f;
            return new KeyValuePair<float, float>(m.Duration, hit);
        }

        // 맞은 쪽: 상대 타격 순간에 체력을 줄이고 맞는 모션(또는 번쩍임), 쓰러지면 쓰러지는 모션
        float Damage(Side s, Fighter f, int hp0, float hitAt, float? ownHit)
        {
            if (f.Hp >= hp0) { if (f.Hp > hp0) s.ShownHp = f.Hp; return 0f; }
            float at = hitAt >= 0 ? hitAt : 0.35f;
            var cur = s.Actor.Current;
            bool attacking = ownHit.HasValue && ownHit.Value >= 0 && cur != null && at < cur.Duration;
            Schedule(at, () =>
            {
                s.ShownHp = f.Hp;
                if (f.Hp <= 0) s.Actor.Play(SheetLibrary.Load(s.Character, map.death, s.Weapon), false, true);
                else if (attacking) s.Actor.Flash();
                else s.Actor.Play(SheetLibrary.Load(s.Character, map.hit, s.Weapon));
            });
            var hitM = SheetLibrary.Load(s.Character, f.Hp <= 0 ? map.death : map.hit, s.Weapon);
            return at + (hitM != null ? hitM.Duration : 0.5f);
        }

        void Schedule(float after, Action a) { timeline.Add(new KeyValuePair<float, Action>(Time.time + after / speed, a)); }

        void Update()
        {
            for (int i = 0; i < timeline.Count; i++)
                if (Time.time >= timeline[i].Key) { var a = timeline[i].Value; timeline.RemoveAt(i); i--; a(); }

            foreach (var s in new[] { player, monster })
            {
                if (s == null) continue;
                float k = s.MoveDur > 0 ? Mathf.Clamp01((Time.time - s.MoveStart) / s.MoveDur) : 1f;
                s.Pos = Mathf.Lerp(s.From, s.To, k);
            }
        }

        void LateUpdate()
        {
            if (cam == null || player == null) return;
            cam.transform.position = Origin + new Vector3(0, 0, -10);
            float worldPerPx = 2f * cam.orthographicSize / Screen.height;
            foreach (var s in new[] { player, monster })
            {
                var other = s == player ? monster : player;
                if (Mathf.Abs(other.Pos - s.Pos) > 0.01f) s.Actor.Facing = other.Pos > s.Pos ? 1 : -1;
                float vx = LaneX0 + (s.Pos - 0.5f) * CellW;
                Vector3 w = cam.ScreenToWorldPoint(new Vector3(vx * GuiScale, Screen.height - FloorY * GuiScale, 0));
                s.Actor.transform.position = new Vector3(w.x, w.y, 0);
                var cur = s.Actor.Current;
                float ppu = cur != null && cur.Body.Length > 0 ? cur.Body[0].pixelsPerUnit : 64f;
                s.Actor.PixelScale = SheetPixel * GuiScale * worldPerPx * ppu;
            }
        }
    }
}
