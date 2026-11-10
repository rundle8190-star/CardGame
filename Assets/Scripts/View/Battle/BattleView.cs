// 카드 전투 화면 (임시: 그림 없이 상자와 글자만).
// 규칙은 Core 의 BattleEngine 이 맡고, 이 파일은 그리기와 입력만 한다.
//
// 조작: 손패 카드를 누르면 빈 슬롯(또는 골라 둔 슬롯)에 놓인다. 놓인 슬롯을 누르면 손패로 돌아간다.
//       빈 슬롯을 누르면 그 슬롯이 '다음에 놓을 곳'으로 골라진다. "실행"으로 1~5번 슬롯을 차례로 처리한다.
using System;
using System.Collections.Generic;
using CardGame.Core;
using CardGame.Core.Battle;
using UnityEngine;

namespace CardGame.View.Battle
{
    public class BattleView : MonoBehaviour
    {
        [Tooltip("슬롯 하나를 처리하는 간격(초)")]
        public float stepDelay = 0.8f;
        public float fastStepDelay = 0.2f;

        BattleEngine b;
        Action<bool, int> onFinish;
        string title;
        readonly int[] plan = { -1, -1, -1, -1, -1 };
        int planRound = -1;
        int selectedSlot = -1;
        float nextStepAt;
        bool fast;
        float shownPlayer, shownMonster;
        BattleStage stage;
        float fastSpeed = 2f;
        Vector2 logScroll;
        int logCountSeen;
        string toast;
        float toastUntil;

        Texture2D white;
        GUIStyle box, title1, label, small, center, centerSmall, cardStyle, slotStyle, markStyle;

        public bool Active { get { return b != null; } }
        [System.NonSerialized] public string PlayerWeaponLayer;   // 플레이어가 든 무기 그림 (장비창). 비우면 기본
        public BattleEngine Engine { get { return b; } }

        public void Begin(BattleEngine engine, string battleTitle, Action<bool, int> finished)
        {
            b = engine;
            title = battleTitle;
            onFinish = finished;
            planRound = -1;
            selectedSlot = -1;
            fast = false;
            shownPlayer = b.Player.Pos;
            shownMonster = b.Monster.Pos;
            logCountSeen = 0;
            if (b.Round == 0 && b.Result == BattleResult.None) b.StartRound();   // 첫 라운드 (패턴 공개, 손패 채우기)

            // 캐릭터 무대 (시트 모션)
            if (stage != null) stage.End();
            stage = new GameObject("Battle Stage").AddComponent<BattleStage>();
            stage.PlayerWeaponLayer = PlayerWeaponLayer;
            stage.Begin(b);
        }

        void Update()
        {
            if (b == null) return;
            if (stage != null) stage.SetSpeed(fast ? fastSpeed : 1f);
            if (b.Phase == BattlePhase.Resolving && Time.time >= nextStepAt)
            {
                int slot = b.NextSlot;
                int hpP = b.Player.Hp, hpM = b.Monster.Hp, posP = b.Player.Pos, posM = b.Monster.Pos;
                b.Step();
                float wait = fast ? fastStepDelay : stepDelay;
                if (stage != null)
                    wait = slot < BattleEngine.SlotCount ? stage.AnimateSlot(slot, hpP, hpM, posP, posM) : stage.AnimateRoundEnd(hpP, hpM);
                nextStepAt = Time.time + wait;
            }
            if (b.Phase == BattlePhase.Planning && planRound != b.Round)
            {
                for (int i = 0; i < plan.Length; i++) plan[i] = -1;
                planRound = b.Round;
                selectedSlot = -1;
                if (stage != null) stage.Sync();
            }
            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            shownPlayer = Mathf.Lerp(shownPlayer, b.Player.Pos, k);
            shownMonster = Mathf.Lerp(shownMonster, b.Monster.Pos, k);
        }

        // ───────── 그리기 ─────────

        void OnGUI()
        {
            if (b == null) return;
            EnsureStyles();
            GUI.depth = -100;
            // 세로 900 기준으로 맞춘다 (작은 화면에서는 줄이고 큰 화면에서는 키운다)
            float scale = Mathf.Max(0.5f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float sw = Screen.width / scale, sh = Screen.height / scale;

            // 캐릭터 무대(월드)가 보이도록 무대 띠(laneTop ~ 바닥)는 비워 두고 나머지만 칠한다
            const float laneTop = 136, floorY = 330, floorH = 22;
            if (stage != null)
            {
                Fill(new Rect(0, 0, sw, laneTop), BattleStage.Background);
                Fill(new Rect(0, floorY + floorH, sw, sh - floorY - floorH), BattleStage.Background);
            }
            else Fill(new Rect(0, 0, sw, sh), BattleStage.Background);

            GUI.Label(new Rect(16, 8, sw - 32, 30), title + "    ·    " + b.Round + " 라운드    ·    " + PhaseText(), title1);

            DrawFighter(new Rect(16, 42, 330, 86), b.Player, new Color(0.35f, 0.6f, 1f));
            DrawFighter(new Rect(sw - 346, 42, 330, 86), b.Monster, new Color(1f, 0.4f, 0.35f));
            DrawLane(new Rect(16, laneTop, sw - 32, floorY + floorH - laneTop), floorY, floorH, scale);

            float slotW = Mathf.Min(170, (sw - 32 - 120 - 4 * 8) / 5f);
            DrawSlots(new Rect(16, 362, sw - 32, 76), b.Monster, true, slotW);
            DrawSlots(new Rect(16, 446, sw - 32, 76), b.Player, false, slotW);

            float handTop = 534;
            DrawHand(new Rect(16, handTop, sw - 32, 200));
            DrawButtons(new Rect(16, handTop + 206, sw - 32, 34));
            DrawLog(new Rect(16, handTop + 248, sw - 32, sh - handTop - 256));

            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                var m = Event.current.mousePosition;
                var r = new Rect(Mathf.Min(m.x + 16, sw - 380), Mathf.Max(0, m.y - 70), 360, 60);
                Fill(r, new Color(0, 0, 0, 0.92f));
                GUI.Label(new Rect(r.x + 8, r.y + 6, r.width - 16, r.height - 12), GUI.tooltip, label);
            }
            if (toast != null && Time.time < toastUntil)
            {
                var r = new Rect(sw / 2 - 260, handTop - 34, 520, 30);
                Fill(r, new Color(0.5f, 0.15f, 0.1f, 0.95f));
                GUI.Label(r, toast, center);
            }
            if (b.Result != BattleResult.None && Time.time >= nextStepAt) DrawEnd(sw, sh);   // 마지막 모션이 끝난 뒤

            GUI.matrix = Matrix4x4.identity;
        }

        string PhaseText()
        {
            switch (b.Phase)
            {
                case BattlePhase.Planning: return "카드를 놓는다 (최대 5장)";
                // 화면에 보이는(노랗게 칠한) 슬롯은 방금 처리한 NextSlot - 1 이라 번호도 그것에 맞춘다
                case BattlePhase.Resolving: return b.NextSlot == 0 ? "처리 시작" : b.NextSlot + "번 슬롯 처리 중";
                default: return b.Result == BattleResult.Won ? "승리" : "패배";
            }
        }

        void DrawFighter(Rect r, Fighter f, Color c)
        {
            Fill(r, new Color(0.15f, 0.15f, 0.18f, 0.7f));   // UI 상자는 반투명
            int hp = stage != null ? stage.ShownHp(f) : f.Hp;   // 맞는 순간에 줄어들게
            GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, 24), f.Name + "    체력 " + Mathf.Max(0, hp) + " / " + f.MaxHp + "    (" + f.Pos + "칸)", label);
            var bar = new Rect(r.x + 10, r.y + 32, r.width - 20, 16);
            Fill(bar, new Color(0.3f, 0.1f, 0.1f));
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01((float)hp / f.MaxHp), bar.height), c);
            GUI.Label(new Rect(r.x + 10, r.y + 52, r.width - 20, 34), "상태: " + f.StatusText(), small);
        }

        void DrawLane(Rect r, float floorY, float floorH, float scale)
        {
            int n = b.Cells;
            float cw = Mathf.Min(150, r.width / n);
            float x0 = r.x + (r.width - cw * n) / 2;
            for (int i = 1; i <= n; i++)
            {
                // 바닥 칸: 캐릭터 발밑 띠
                var cell = new Rect(x0 + (i - 1) * cw + 2, floorY, cw - 4, floorH);
                bool edge = i == 1 || i == n;
                Fill(cell, edge ? new Color(0.3f, 0.26f, 0.22f) : new Color(0.22f, 0.26f, 0.2f));
                foreach (var fd in b.Fields)
                    if (fd.Cell == i)
                        Fill(new Rect(cell.x, cell.y, cell.width, 6), fd.Kind == "fire" ? new Color(0.9f, 0.35f, 0.1f) : new Color(0.45f, 0.8f, 0.2f));
                GUI.Label(new Rect(cell.x + 4, cell.y + 2, cell.width, 18), i + (edge ? " 벽" : ""), small);
            }
            if (stage != null)
            {
                stage.LaneX0 = x0; stage.CellW = cw; stage.FloorY = floorY; stage.GuiScale = scale;
                GUI.Label(new Rect(x0 + (shownMonster - 1) * cw, r.y + 2, cw, 18), b.Monster.Name, centerSmall);
                GUI.Label(new Rect(x0 + (shownPlayer - 1) * cw, r.y + 2, cw, 18), "나", centerSmall);
            }
            else
            {
                DrawMark(x0, cw, r, shownMonster, b.Monster.Name, new Color(0.85f, 0.3f, 0.25f));
                DrawMark(x0, cw, r, shownPlayer, "나", new Color(0.25f, 0.5f, 0.95f));
            }
        }

        void DrawMark(float x0, float cw, Rect lane, float pos, string text, Color c)
        {
            var m = new Rect(x0 + (pos - 1) * cw + cw * 0.2f, lane.y + 24, cw * 0.6f, lane.height - 48);
            Fill(m, c);
            GUI.Label(m, text, markStyle);
        }

        void DrawSlots(Rect r, Fighter f, bool enemy, float slotW)
        {
            GUI.Label(new Rect(r.x, r.y, 116, r.height), enemy ? "적의 계획\n" + (b.CurrentPattern != null ? b.CurrentPattern.name : "") : "내 배치", label);
            int active = b.Phase == BattlePhase.Resolving ? b.NextSlot - 1 : -1;
            for (int i = 0; i < BattleEngine.SlotCount; i++)
            {
                var sr = new Rect(r.x + 120 + i * (slotW + 8), r.y, slotW, r.height);
                Color bg = i == active ? new Color(0.55f, 0.45f, 0.15f, 0.85f) : new Color(0.17f, 0.17f, 0.2f, 0.7f);
                if (!enemy && b.Phase == BattlePhase.Planning && i == selectedSlot) bg = new Color(0.2f, 0.35f, 0.55f, 0.85f);
                if (!enemy && b.IsBlocked(i)) bg = new Color(0.1f, 0.1f, 0.1f);
                Fill(sr, bg);

                string id; bool cont;
                if (enemy || b.Phase != BattlePhase.Planning) { id = f.Plan[i]; cont = f.Cont[i]; }
                else PlannedCard(i, out id, out cont);

                var card = b.Db.Card(id);
                string text = (i + 1) + "번\n";
                string tip = null;
                if (!enemy && b.IsBlocked(i)) text += "막힘 (짐 무게)";
                else if (card == null) text += "-";
                else
                {
                    text += cont ? "↳ " + card.name + " (이어서)" : card.name + "\n" + Summary(card);
                    tip = card.name + ": " + card.description;
                }

                if (!enemy && b.Phase == BattlePhase.Planning)
                {
                    if (GUI.Button(sr, new GUIContent(text, tip), slotStyle)) ClickSlot(i);
                }
                else GUI.Label(sr, new GUIContent(text, tip), slotStyle);
            }
        }

        void PlannedCard(int slot, out string id, out bool cont)
        {
            id = null; cont = false;
            if (plan[slot] >= 0 && plan[slot] < b.Hand.Count) { id = b.Hand[plan[slot]]; return; }
            if (slot > 0 && plan[slot - 1] >= 0 && plan[slot - 1] < b.Hand.Count)
            {
                var prev = b.Db.Card(b.Hand[plan[slot - 1]]);
                if (prev != null && prev.large) { id = prev.id; cont = true; }
            }
        }

        void DrawHand(Rect r)
        {
            GUI.Label(new Rect(r.x, r.y, 116, 40), "손패 " + b.Hand.Count + "\n덱 " + b.DrawPile.Count + " · 버린 카드 " + b.Discard.Count, small);
            if (b.Phase != BattlePhase.Planning) { GUI.Label(new Rect(r.x + 120, r.y, 400, 30), "슬롯을 처리하는 중…", label); return; }
            float cw = 128, ch = 96;
            int perRow = Mathf.Max(1, (int)((r.width - 120) / (cw + 8)));
            int shown = 0;
            for (int h = 0; h < b.Hand.Count; h++)
            {
                if (Array.IndexOf(plan, h) >= 0) continue;   // 이미 놓은 카드
                var card = b.Db.Card(b.Hand[h]);
                if (card == null) continue;
                int row = shown / perRow, col = shown % perRow;
                var cr = new Rect(r.x + 120 + col * (cw + 8), r.y + row * (ch + 8), cw, ch);
                if (GUI.Button(cr, new GUIContent(card.name + "\n" + Summary(card), card.name + ": " + card.description), cardStyle)) Place(h);
                shown++;
            }
            if (shown == 0) GUI.Label(new Rect(r.x + 120, r.y, 400, 30), "남은 손패가 없다.", label);
        }

        void DrawButtons(Rect r)
        {
            bool planning = b.Phase == BattlePhase.Planning;
            GUI.enabled = planning;
            if (GUI.Button(new Rect(r.x + 120, r.y, 160, r.height), "실행 ▶")) Run();
            if (GUI.Button(new Rect(r.x + 290, r.y, 110, r.height), "모두 빼기")) { for (int i = 0; i < plan.Length; i++) plan[i] = -1; selectedSlot = -1; }
            GUI.enabled = true;
            fast = GUI.Toggle(new Rect(r.x + 420, r.y + 8, 140, r.height), fast, " 빠르게 처리");
            GUI.Label(new Rect(r.x + 570, r.y + 8, r.width - 570, r.height), "지금 거리 " + b.Distance + "칸   ·   카드에 마우스를 올리면 설명이 나온다.   ·   놓은 슬롯을 누르면 다시 뺀다.", small);
        }

        void DrawLog(Rect r)
        {
            Fill(r, new Color(0.05f, 0.05f, 0.06f, 0.7f));
            float lineH = 20;
            var view = new Rect(0, 0, r.width - 20, Mathf.Max(r.height, b.Log.Count * lineH + 8));
            if (b.Log.Count != logCountSeen) { logCountSeen = b.Log.Count; logScroll.y = float.MaxValue; }
            logScroll = GUI.BeginScrollView(r, logScroll, view);
            for (int i = 0; i < b.Log.Count; i++) GUI.Label(new Rect(8, 4 + i * lineH, view.width - 8, lineH), b.Log[i], small);
            GUI.EndScrollView();
        }

        void DrawEnd(float sw, float sh)
        {
            var r = new Rect(sw / 2 - 220, sh / 2 - 100, 440, 200);
            Fill(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.55f));
            Fill(r, new Color(0.15f, 0.15f, 0.18f, 0.85f));
            bool won = b.Result == BattleResult.Won;
            GUI.Label(new Rect(r.x, r.y + 20, r.width, 36), won ? "승리" : "패배", title1);
            GUI.Label(new Rect(r.x + 20, r.y + 64, r.width - 40, 50), won ? "남은 체력 " + b.Player.Hp + " / " + Josa.Ro(b.Player.MaxHp) + " 맵에 돌아간다." : "쓰러졌다. 이번 탐험은 여기까지다.", center);
            if (GUI.Button(new Rect(r.x + 120, r.y + 130, 200, 40), "맵으로 돌아가기")) ReturnToMap();
        }

        // 전투가 끝난 뒤 맵으로 돌아간다 (결과와 남은 체력을 넘긴다)
        public void ReturnToMap()
        {
            if (b == null || b.Result == BattleResult.None) return;
            var cb = onFinish;
            bool won = b.Result == BattleResult.Won;
            int hp = Mathf.Max(0, b.Player.Hp);
            b = null;
            if (stage != null) { stage.End(); stage = null; }
            if (cb != null) cb(won, hp);
        }

        // ───────── 입력 ─────────

        bool SlotOpen(int s)
        {
            if (b.IsBlocked(s) || plan[s] >= 0) return false;
            string id; bool cont;
            PlannedCard(s, out id, out cont);
            return !cont;
        }

        void Place(int handIdx)
        {
            string why = null;
            if (selectedSlot >= 0 && SlotOpen(selectedSlot))
            {
                why = TryPlace(handIdx, selectedSlot);
                if (why == null) { selectedSlot = -1; return; }
            }
            for (int s = 0; s < BattleEngine.SlotCount; s++)
            {
                if (!SlotOpen(s)) continue;
                string w = TryPlace(handIdx, s);
                if (w == null) return;
                if (why == null) why = w;
            }
            Toast(why ?? "빈 슬롯이 없다.");
        }

        string TryPlace(int handIdx, int slot)
        {
            var test = (int[])plan.Clone();
            test[slot] = handIdx;
            string why = b.ValidatePlan(test);
            if (why == null) Array.Copy(test, plan, plan.Length);
            return why;
        }

        void ClickSlot(int s)
        {
            if (b.IsBlocked(s)) return;
            if (plan[s] >= 0) { plan[s] = -1; return; }
            string id; bool cont;
            PlannedCard(s, out id, out cont);
            if (cont) { plan[s - 1] = -1; return; }
            selectedSlot = selectedSlot == s ? -1 : s;
        }

        void Run()
        {
            string why = b.Commit(plan);
            if (why != null) { Toast(why); return; }
            nextStepAt = Time.time + 0.3f;
        }

        void Toast(string s) { toast = s; toastUntil = Time.time + 2.5f; }

        // ───────── 도움 ─────────

        public static string Summary(CardDef c)
        {
            switch (c.category)
            {
                case "weapon":
                {
                    string t = c.IsMelee ? "근접" : c.IsLine ? "직선" : c.IsThrow ? "투척" : "무기";
                    t += " " + c.power + (c.hits > 1 ? "×" + c.hits : "");
                    if (c.pierce > 0) t += " 관통" + c.pierce;
                    if (c.IsMelee && c.MeleeRange > 1) t += " 사거리" + c.MeleeRange;
                    if (c.large) t += " [2칸]";
                    return t;
                }
                case "shield": return "방어 " + c.guard;
                case "move": return "이동";
                case "buff": return "강화";
                case "heal": return "회복";
                case "counter": return "반격";
                case "draw": return "드로우";
                default: return c.category;
            }
        }

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void EnsureStyles()
        {
            if (box != null) return;
            white = Texture2D.whiteTexture;
            box = new GUIStyle(GUI.skin.box);
            title1 = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title1.normal.textColor = Color.white;
            label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            label.normal.textColor = Color.white;
            small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            small.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            centerSmall = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
            cardStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            slotStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            slotStyle.normal.textColor = Color.white;
            slotStyle.hover.textColor = new Color(1f, 0.9f, 0.5f);
            markStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            markStyle.normal.textColor = Color.white;
        }
    }
}
