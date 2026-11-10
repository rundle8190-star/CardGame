// 구워 둔 캐릭터 시트(PNG + JSON)를 읽어 재생한다.
// 시트: Assets/Art/characters/<캐릭터>_<세트>_x1_5/<모션>.png (+ .json: fps, 타격 프레임, 반복 여부)
//  - ual2 세트는 색 교체용 인덱스 시트(<모션>_idx.png)와 팔레트(_palette_swap.png)를 쓰고, 무기는 layers/<무기>/<모션>.png 를 겹친다.
//  - ual 세트는 색이 들어간 시트를 그대로 쓴다 (무기·색 교체 없음).
// 지금은 에디터에서만 시트를 읽는다 (AssetDatabase). 빌드용 연결은 나중에.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CardGame.View.Battle
{
    public class SheetMotion
    {
        public string Key;
        public Sprite[] Body;
        public Sprite[] Weapon;      // 없으면 null
        public bool PaletteSwap;     // Body 가 인덱스 시트인지
        public float Fps = 24;
        public int HitFrame = -1;
        public bool Loop;

        public float Duration { get { return Body.Length / Fps; } }
        public float HitTime { get { return HitFrame >= 0 ? HitFrame / Fps : -1f; } }
    }

    public static class SheetLibrary
    {
        [Serializable]
        class Info { public float fps; public int frames; public int hitFrame = -1; public bool loop; }

        static readonly Dictionary<string, SheetMotion> cache = new Dictionary<string, SheetMotion>();
        public static readonly List<string> Missing = new List<string>();

        public static string Folder(string character, string set) { return "Assets/Art/characters/" + character + "_" + set + "_x1_5"; }

        // motionRef: "ual2:sword_regular_a" 처럼 세트:모션
        public static SheetMotion Load(string character, string motionRef, string weaponLayer)
        {
            if (string.IsNullOrEmpty(motionRef)) return null;
            string key = character + "|" + motionRef + "|" + weaponLayer;
            SheetMotion m;
            if (cache.TryGetValue(key, out m)) return m;

            string set = "ual2", motion = motionRef;
            int c = motionRef.IndexOf(':');
            if (c >= 0) { set = motionRef.Substring(0, c); motion = motionRef.Substring(c + 1); }
            string folder = Folder(character, set);
            bool ual2 = set == "ual2";

            m = null;
            try
            {
                var info = JsonUtility.FromJson<Info>(File.ReadAllText(folder + "/" + motion + ".json"));
                var body = ual2 ? Sprites(folder + "/" + motion + "_idx.png") : null;
                bool swap = body != null && body.Length > 0;
                if (!swap) body = Sprites(folder + "/" + motion + ".png");
                if (body != null && body.Length > 0)
                {
                    m = new SheetMotion
                    {
                        Key = motionRef,
                        Body = body,
                        PaletteSwap = swap,
                        Fps = info.fps > 0 ? info.fps : 24,
                        HitFrame = info.hitFrame,
                        Loop = info.loop,
                    };
                    if (ual2 && !string.IsNullOrEmpty(weaponLayer))
                    {
                        var w = Sprites(folder + "/layers/" + weaponLayer + "/" + motion + ".png");
                        if (w != null && w.Length == body.Length) m.Weapon = w;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("시트를 읽지 못했어요: " + folder + "/" + motion + " (" + e.Message + ")"); }

            if (m == null) Missing.Add(character + " " + motionRef);
            cache[key] = m;
            return m;
        }

        public static Texture2D Palette(string character)
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Folder(character, "ual2") + "/_palette_swap.png");
#else
            return null;
#endif
        }

        // 굽기 도구가 '<이름>_00, _01 …' 로 잘라 둔 스프라이트를 순서대로
        static Sprite[] Sprites(string assetPath)
        {
#if UNITY_EDITOR
            if (!File.Exists(assetPath)) return null;
            var list = new List<Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(assetPath)) { var s = o as Sprite; if (s != null) list.Add(s); }
            list.Sort((a, b) => Index(a.name).CompareTo(Index(b.name)));
            return list.ToArray();
#else
            return null;
#endif
        }

        static int Index(string name)
        {
            int u = name.LastIndexOf('_');
            int i;
            return u >= 0 && int.TryParse(name.Substring(u + 1), out i) ? i : 0;
        }
    }

    // 몸 + 무기 두 장을 겹쳐 그리는 캐릭터 하나
    public class SheetActor : MonoBehaviour
    {
        SpriteRenderer body, weapon;
        Material paletteMat, plainMat;
        SheetMotion idle, current;
        float t;
        bool reverse, hold;
        float flashUntil;
        public float Speed = 1f;
        public int Facing = 1;          // 1 오른쪽, -1 왼쪽 (시트는 오른쪽을 본다)
        public float PixelScale = 1f;   // 시트 1픽셀을 월드 몇 단위로

        public SheetMotion Current { get { return current; } }
        public bool Dead { get; private set; }

        public void Init(string character, int clothRow, int sortingOrder)
        {
            body = NewRenderer("body", sortingOrder);
            weapon = NewRenderer("weapon", sortingOrder + 1);
            plainMat = body.sharedMaterial;
            var shader = Shader.Find("CardGame/PaletteSwap");
            var pal = SheetLibrary.Palette(character);
            if (shader != null && pal != null)
            {
                paletteMat = new Material(shader);
                paletteMat.SetTexture("_PaletteTex", pal);
                paletteMat.SetFloat("_RowCloth", clothRow);
            }
        }

        SpriteRenderer NewRenderer(string n, int order)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sortingOrder = order;
            return r;
        }

        public void SetIdle(SheetMotion m) { idle = m; if (current == null) Play(m); }

        public void Play(SheetMotion m, bool playReverse = false, bool holdLast = false)
        {
            if (m == null || Dead) return;
            current = m; t = 0; reverse = playReverse; hold = holdLast;
            if (holdLast) Dead = true;
            Apply();
        }

        public void Flash() { flashUntil = Time.time + 0.15f; }

        public void Revive() { Dead = false; hold = false; current = null; if (idle != null) Play(idle); }

        void Update()
        {
            if (current == null) return;
            t += Time.deltaTime * Speed;
            int n = current.Body.Length;
            if (!current.Loop && t * current.Fps >= n && !hold && idle != null && current != idle) { current = idle; t = 0; reverse = false; }
            Apply();
        }

        void Apply()
        {
            var m = current;
            int n = m.Body.Length;
            int f = (int)(t * m.Fps);
            if (m.Loop) f %= n; else f = Mathf.Min(f, n - 1);
            if (reverse) f = n - 1 - f;
            body.sprite = m.Body[f];
            body.sharedMaterial = m.PaletteSwap && paletteMat != null ? paletteMat : plainMat;
            weapon.sprite = m.Weapon != null ? m.Weapon[f] : null;
            body.color = Time.time < flashUntil ? new Color(1f, 0.45f, 0.45f) : Color.white;
            transform.localScale = new Vector3(Facing * PixelScale, PixelScale, 1);
        }
    }
}
