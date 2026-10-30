using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 받아 둔 에셋으로 테스트용 굽기 설정을 자동으로 만든다. (Mixamo 모션은 2026-10-29에 폐기)
    ///  - ranger_ual_x1_5 / ranger_ual_iso_x1_5   : 레인저 + UAL1 모션 전부 (검·임시 방패 함께 그림)
    ///  - ranger_ual2_x1_5 : 레인저 + UAL2 전투 모션. 몸과 무기(한손 무기 13종, 방패 6종)를 따로 굽고 부위별 색 교체 팔레트를 만든다.
    ///  - woodcutter_ual_x1_5 / woodcutter_ual2_x1_5 : 같은 몸에 농부 옷·머리·수염을 입힌 나무꾼. 레인저와 같은 모션·무기.
    ///  (아이소 설정 ranger_ual2_iso_x1_5 는 2026-10-30부터 더 굽지 않는다. 예전 파일은 그대로 둔다.)
    /// </summary>
    public static class CharacterBakeTestSetups
    {
        const string A = "Assets/Art3D/";
        const string SetupFolder = A + "BakeSetups";

        const string UAL = A + "Animations/UAL/UAL1_Standard.fbx";
        // UAL을 Generic(리타깃 없음)으로 가져온 사본. 원본(휴머노이드)은 기존 설정이 쓰므로 그대로 둔다.
        const string UAL_GENERIC_DIR = A + "Animations/UAL_Generic";
        const string UAL_GENERIC = UAL_GENERIC_DIR + "/UAL1_Standard_Generic.fbx";
        static readonly string[] UalAttacks = { "Sword_Attack", "Punch_Cross", "Punch_Jab" };

        const string UAL2 = A + "Animations/UAL2/UAL2_Standard.fbx";
        const string UAL2_GENERIC = UAL_GENERIC_DIR + "/UAL2_Standard_Generic.fbx";
        const string MW = A + "Weapons/Medieval/";
        // UAL2에서 굽는 전투 모션 (true = 공격이라 타격 프레임을 찾는다). Sword_Regular_C_Rec 는 파일에 없다.
        static readonly (string name, bool attack)[] Ual2Clips =
        {
            ("Sword_Regular_A", true), ("Sword_Regular_A_Rec", false),
            ("Sword_Regular_B", true), ("Sword_Regular_B_Rec", false),
            ("Sword_Regular_C", true),
            ("Sword_Regular_Combo", true), ("Sword_Heavy_Combo", true),
            ("Sword_Block", false), ("Sword_Dash", true),
            ("Idle_Shield_Loop", false), ("Idle_Shield_Break", false),
            ("Shield_Dash", true), ("Shield_OneShot", true),
            ("Melee_Hook", true), ("Melee_Hook_Rec", false),
            ("Hit_Knockback", false), ("OverhandThrow", true), ("Consume", false),
        };
        // 예전 Mixamo 모션을 쓰던 설정 (파일이 지워져서 빈 껍데기). 만들 때 휴지통으로 옮긴다.
        /// <summary>캐릭터 한 명의 겉모습: 몸 위에 입힐 옷·머리 파일</summary>
        class Look
        {
            public string id; public string[] parts;
            public Color hairTint = Color.white;                 // 머리카락 텍스처가 회색이라 색을 곱한다
            public Vector2 clothHue = new Vector2(55f, 170f);    // 색 교체에서 '천'으로 볼 옷 색 범위
            public Vector2 clothSat = new Vector2(0.2f, 1f);
            public float clothMinValue = 0.04f;
        }
        static readonly Look Ranger = new Look { id = "ranger", parts = new[] { "Characters/Outfits/Male_Ranger.fbx" } };
        static readonly Look Woodcutter = new Look
        {
            id = "woodcutter",
            parts = new[] { "Characters/Outfits/Male_Peasant.fbx", "Characters/Hair/Hair_SimpleParted.fbx", "Characters/Hair/Hair_Beard.fbx", "Characters/Hair/Eyebrows_Regular.fbx" },
            hairTint = new Color(0.55f, 0.36f, 0.2f),
            // 농부 옷은 초록이 없다. 베이지 윗옷(색상 약 45도, 채도 0.2~0.3)을 천으로, 진한 갈색 바지·신발은 가죽으로
            clothHue = new Vector2(35f, 70f), clothSat = new Vector2(0.12f, 0.45f), clothMinValue = 0.35f,
        };

        // 한손 무기 13종 (레이어 이름, 파일, 손잡이 찾는 법 1 칼 / 2 도끼·망치, 손잡이 뒤집기, 위치 보정(m), 크기)
        static readonly (string name, string path, int mode, bool flip, Vector3 offset, float scale)[] OneHanded =
        {
            ("sword", MW + "Swords/Sword.fbx", 1, false, Vector3.zero, 1f),
            ("falchion", MW + "Swords/Falchion.fbx", 1, false, Vector3.zero, 1f),
            ("messer", MW + "Swords/Messer.fbx", 1, false, Vector3.zero, 1f),
            ("dagger", MW + "Swords/Dagger.fbx", 1, false, Vector3.zero, 1f),
            ("arming_sword", A + "Weapons/Arming_Sword.fbx", 1, false, Vector3.zero, 1f),
            ("cutlass", A + "Weapons/Cutlass.fbx", 1, false, Vector3.zero, 1f),
            ("scimitar", A + "Weapons/Scimitar.fbx", 1, false, Vector3.zero, 1f),
            ("kopesh", A + "Weapons/Kopesh.fbx", 1, false, Vector3.zero, 1f),
            ("mace", MW + "Maces/Mace.fbx", 2, false, Vector3.zero, 1f),
            ("spiked_mace", MW + "Maces/Spiked Mace.fbx", 2, false, Vector3.zero, 1f),
            ("war_hammer", A + "Weapons/War_Hammer.fbx", 2, false, Vector3.zero, 1f),
            ("hatchet", A + "Weapons/Hatchet.fbx", 2, false, Vector3.zero, 1f),
            ("bearded_axe", A + "Weapons/Bearded_Axe.fbx", 2, false, Vector3.zero, 1f),
        };
        // 방패 6종: 지금의 68% 크기, 판은 몸 천 색, 테두리는 금속 칸으로 색 교체
        const float ShieldScale = 0.68f;
        static readonly string[] Shields = { "A", "B", "C", "D", "E", "F" };

        static readonly string[] MixamoSetups = { "ranger_x1_5", "ranger_iso_x1_5", "ranger_x2", "test_sword_shield", "test_axe" };

        public static CharacterBakeSetup[] CreateAll()
        {
            var missing = new List<string>();
            RemoveMixamoSetups();
            var u = UalChar(Ranger, "ranger_ual_x1_5", false, missing);
            var ui = UalChar(Ranger, "ranger_ual_iso_x1_5", true, missing);
            var u2 = Ual2Char(Ranger, "ranger_ual2_x1_5", missing);
            var w = UalChar(Woodcutter, "woodcutter_ual_x1_5", false, missing);
            var w2 = Ual2Char(Woodcutter, "woodcutter_ual2_x1_5", missing);
            AssetDatabase.SaveAssets();
            if (missing.Count > 0)
                Debug.LogWarning("[캐릭터 굽기] 찾지 못한 파일/모션 (건너뜀):\n" + string.Join("\n", missing));
            return new[] { u, ui, u2, w, w2 };
        }

        /// <summary>Mixamo 모션을 쓰던 굽기 설정을 휴지통으로 옮긴다 (git 첫 기록 c808919 에 남아 있다).</summary>
        static void RemoveMixamoSetups()
        {
            foreach (var n in MixamoSetups)
            {
                string path = $"{SetupFolder}/{n}.asset";
                if (AssetDatabase.LoadAssetAtPath<CharacterBakeSetup>(path) == null) continue;
                if (AssetDatabase.MoveAssetToTrash(path)) Debug.Log($"[캐릭터 굽기] Mixamo 설정 정리: {path} → 휴지통");
            }
        }

        [MenuItem("CardGame/캐릭터 굽기/캐릭터 2종 무기·방패 레이어 굽기 (옆)")]
        static void BakeCharsV2()
        {
            var missing = new List<string>();
            RemoveMixamoSetups();
            var list = new[]
            {
                Ual2Char(Ranger, "ranger_ual2_x1_5", missing),
                Ual2Char(Woodcutter, "woodcutter_ual2_x1_5", missing),
                UalChar(Woodcutter, "woodcutter_ual_x1_5", false, missing),
            };
            AssetDatabase.SaveAssets();
            if (missing.Count > 0) Debug.LogWarning("[캐릭터 굽기] 찾지 못한 파일/모션 (건너뜀):\n" + string.Join("\n", missing));
            var t0 = EditorApplication.timeSinceStartup;
            foreach (var s in list) CharacterBaker.BakeAll(s);
            Debug.Log($"[캐릭터 굽기] 캐릭터 2종 굽기 끝 ({EditorApplication.timeSinceStartup - t0:0}초)");
        }

        /// <summary>
        /// UAL2 전투 모션 (옆 시점만). 무기·방패는 몸 시트에 그리지 않고 레이어로 따로 굽는다.
        /// 무기마다 손잡이 잡는 법(그립)은 위 OneHanded 표에서 한 번만 정한다.
        /// </summary>
        static CharacterBakeSetup Ual2Char(Look look, string name, List<string> missing)
        {
            EnsureGeneric(UAL2, UAL2_GENERIC);
            var s = NewSetup(name);
            UalCommon(s, look, name, false, missing);
            // 몸 시트에는 무기·방패 없음
            s.weapon = new CharacterBakeSetup.WeaponSlot();
            s.shield = new CharacterBakeSetup.ShieldSlot();
            s.paletteSwap = true;
            s.autoPaletteColors = 24;
            s.layerClipPrefixes.AddRange(new[] { "Sword_", "Idle_Shield", "Shield_", "Hit_" });
            foreach (var w in OneHanded)
            {
                var go = Load<GameObject>(w.path, missing);
                if (go != null) s.layers.Add(new CharacterBakeSetup.LayerItem
                {
                    name = w.name, prefab = go, boneName = "hand_r", bladeMode = w.mode, flipGrip = w.flip,
                    extraPosition = w.offset, scale = w.scale, strikePoint = w.name == "sword",
                });
            }
            foreach (var L in Shields)
            {
                var sh = Load<GameObject>(MW + $"Shields/Shield {L}.fbx", missing);
                if (sh != null) s.layers.Add(new CharacterBakeSetup.LayerItem
                {
                    name = "shield_" + L.ToLowerInvariant(), isShield = true, prefab = sh, boneName = "lowerarm_l",
                    scale = ShieldScale, swapColors = true,
                });
            }

            foreach (var (n, attack) in Ual2Clips) AddClip(s, UAL2_GENERIC, n, attack, missing);
            Save(s);
            return s;
        }

        /// <summary>
        /// UAL 모션 전부로 굽는 설정 (검 + 임시 원판 방패). 뼈대가 같으므로 리타깃 없이 그대로 재생하고, 자세 보정은 끈다.
        /// 모션마다 첫 프레임의 가장 낮은 발이 바닥에 닿도록 높이만 한 번 맞추고, 바닥 아래로 내려가는 것만 막는다.
        /// </summary>
        static CharacterBakeSetup UalChar(Look look, string name, bool isometric, List<string> missing)
        {
            EnsureGeneric(UAL, UAL_GENERIC);
            var s = NewSetup(name);
            UalCommon(s, look, name, isometric, missing);
            s.weapon.prefab = Load<GameObject>(A + "Weapons/Arming_Sword.fbx", missing);
            s.shield.useTempShield = true;

            var names = new List<string>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(UAL_GENERIC))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) names.Add(c.name);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0) missing.Add(UAL_GENERIC + " (모션 없음)");
            foreach (var n in names)
            {
                string shortName = n.Substring(n.LastIndexOf('|') + 1);
                AddClip(s, UAL_GENERIC, shortName, Array.IndexOf(UalAttacks, shortName) >= 0, missing);
            }
            Save(s);
            return s;
        }

        /// <summary>UAL 설정 공통: 남자 몸 + 옷·머리, 1.5배 도트, 24fps, 뼈대가 같아 그대로 재생 (자세 보정 끔)</summary>
        static void UalCommon(CharacterBakeSetup s, Look look, string name, bool isometric, List<string> missing)
        {
            s.fps = 24;
            if (isometric) { s.yawToFront = 45f; s.pitch = 30f; }
            s.baseModel = Load<GameObject>(A + "Characters/Base/Superhero_Male_FullBody.fbx", missing);
            foreach (var p in look.parts) AddPart(s, A + p, missing);
            foreach (var r in s.textureRules)
                if (r.materialNameContains.StartsWith("MI_Hair")) r.tint = look.hairTint;
            s.clothHue = look.clothHue; s.clothSaturation = look.clothSat; s.clothMinValue = look.clothMinValue;
            s.outputFolder = "Assets/Art/characters/" + name;
            s.pixelsPerMeter = 54f;
            s.frameWidth = 192;
            s.frameHeight = 144;
            s.groundPixel = 9;
            s.maxFrames = 240;

            s.directPlayback = true;
            s.groundFromFirstFrame = true;
            s.straightenHips = false;
            s.flattenFeet = false;
            s.plantFeet = false;
            s.keepAboveGround = true;
        }

        /// <summary>UAL 원본을 복사해 Generic으로 가져온다 (이미 있으면 그대로).</summary>
        static void EnsureGeneric(string srcAsset, string dstAsset)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(dstAsset) != null) return;
            string cwd = System.IO.Directory.GetCurrentDirectory();
            string src = System.IO.Path.Combine(cwd, srcAsset);
            if (!System.IO.File.Exists(src)) return;
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(cwd, UAL_GENERIC_DIR));
            // 설정(.meta) 없이 파일만 복사해서, 가져오기 규칙(CharacterArtImport)이 Generic으로 설정하게 한다
            System.IO.File.Copy(src, System.IO.Path.Combine(cwd, dstAsset), true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static CharacterBakeSetup NewSetup(string name)
        {
            string path = $"{SetupFolder}/{name}.asset";
            var s = AssetDatabase.LoadAssetAtPath<CharacterBakeSetup>(path);
            if (s == null)
            {
                if (!AssetDatabase.IsValidFolder(SetupFolder)) AssetDatabase.CreateFolder("Assets/Art3D", "BakeSetups");
                s = ScriptableObject.CreateInstance<CharacterBakeSetup>();
                AssetDatabase.CreateAsset(s, path);
            }
            // 다시 만들면 목록을 처음부터 채운다
            s.parts.Clear();
            s.clips.Clear();
            s.textureRules.Clear();
            s.weapon = new CharacterBakeSetup.WeaponSlot();
            s.shield = new CharacterBakeSetup.ShieldSlot();
            s.heightScale = 1f;
            s.pixelsPerMeter = 36f;
            s.frameWidth = 128;
            s.frameHeight = 96;
            s.groundPixel = 6;
            s.maxFrames = 60;
            s.fps = 12;
            s.flattenFeet = true;
            s.straightenHips = true;
            s.keepAboveGround = true;
            s.plantFeet = true;
            s.plantDistance = 0.08f;
            s.groundLockHeight = 0.2f;
            s.yawToFront = 0f;
            s.pitch = 0f;
            s.hideBodyUnderClothes = true;
            s.autoExpandFrame = true;
            s.directPlayback = false;
            s.groundFromFirstFrame = false;
            // 반짝임 줄이기 값 (설정 파일에 예전 값이 남아 있을 수 있어 매번 맞춘다)
            s.autoPalette = true;
            s.autoPaletteColors = 24;
            s.superSample = 4;
            s.textureMipBias = 2f;
            s.hideTinyPartsPixels = 1.5f;
            s.snapToPixel = true;
            s.flickerFilter = true;
            s.flickerMaxSpeed = 4f;
            s.layers.Clear();
            s.layerClipPrefixes.Clear();
            s.layerPaletteColors = 12;
            s.paletteSwap = false;
            s.skinColors = 5; s.clothColors = 6; s.leatherColors = 6; s.metalColors = 4; s.darkColors = 3;
            AddDefaultTextureRules(s);
            return s;
        }

        static void AddDefaultTextureRules(CharacterBakeSetup s)
        {
            void Rule(string mat, string tex)
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(A + tex);
                if (t != null) s.textureRules.Add(new CharacterBakeSetup.TextureRule { materialNameContains = mat, texture = t });
            }
            Rule("MI_Superhero_Male", "Characters/Base/Textures/T_Superhero_Male_Dark.png");
            Rule("MI_Superhero_Female", "Characters/Base/Textures/T_Superhero_Female_Dark_BaseColor.png");
            Rule("MI_Eyes", "Characters/Base/Textures/T_Eye_Brown.png");
            Rule("MI_Hair_1", "Characters/Hair/Textures/T_Hair_1_BaseColor.png");
            Rule("MI_Hair_2", "Characters/Hair/Textures/T_Hair_2_BaseColor.png");
            Rule("MI_Peasant", "Characters/Outfits/Textures/T_Peasant_BaseColor.png");
            Rule("MI_Ranger", "Characters/Outfits/Textures/T_Ranger_BaseColor.png");
            Rule("MI_Regular_Male", "Characters/Outfits/Textures/T_Regular_Male_Dark_BaseColor.png");
            Rule("MI_Regular_Female", "Characters/Outfits/Textures/T_Regular_Female_Dark_BaseColor.png");
        }

        static void AddPart(CharacterBakeSetup s, string path, List<string> missing)
        {
            var go = Load<GameObject>(path, missing);
            if (go != null) s.parts.Add(go);
        }

        static void AddClip(CharacterBakeSetup s, string fbx, string clipName, bool attack, List<string> missing, int hitOverride = -1)
        {
            var clip = FindClip(fbx, clipName);
            if (clip == null) { missing.Add(fbx + (clipName != null ? " : " + clipName : "")); return; }
            // 고정 타격 프레임은 12fps 기준 번호로 적어 두었다. fps가 다르면 같은 시각의 번호로 바꾼다.
            int hit = hitOverride >= 0 ? Mathf.RoundToInt(hitOverride * s.fps / 12f) : -1;
            s.clips.Add(new CharacterBakeSetup.ClipEntry { clip = clip, findHitFrame = attack, bake = true, hitFrameOverride = hit });
        }

        /// <summary>FBX 안의 모션을 이름으로 찾는다. 이름이 없으면 첫 번째 모션.</summary>
        public static AnimationClip FindClip(string fbx, string clipName)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
            {
                if (!(o is AnimationClip c) || c.name.StartsWith("__preview__")) continue;
                if (clipName == null) return c;
                string n = c.name;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                if (string.Equals(n, clipName, StringComparison.OrdinalIgnoreCase)) return c;
            }
            return null;
        }

        static T Load<T>(string path, List<string> missing) where T : UnityEngine.Object
        {
            var o = AssetDatabase.LoadAssetAtPath<T>(path);
            if (o == null) missing.Add(path);
            return o;
        }

        static void Save(CharacterBakeSetup s)
        {
            EditorUtility.SetDirty(s);
        }
    }
}
