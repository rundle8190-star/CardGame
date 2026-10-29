using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 받아 둔 에셋으로 테스트용 굽기 설정을 자동으로 만든다. (Mixamo 모션은 2026-10-29에 폐기)
    ///  - ranger_ual_x1_5 / ranger_ual_iso_x1_5   : 레인저 + UAL1 모션 전부 (검·임시 방패 함께 그림)
    ///  - ranger_ual2_x1_5 / ranger_ual2_iso_x1_5 : 레인저 + UAL2 전투 모션. 몸과 무기(한손검 5종, 방패)를 따로 굽고 부위별 색 교체 팔레트를 만든다.
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
        static readonly string[] MixamoSetups = { "ranger_x1_5", "ranger_iso_x1_5", "ranger_x2", "test_sword_shield", "test_axe" };

        public static CharacterBakeSetup[] CreateAll()
        {
            var missing = new List<string>();
            RemoveMixamoSetups();
            var u = UalRanger("ranger_ual_x1_5", false, missing);
            var ui = UalRanger("ranger_ual_iso_x1_5", true, missing);
            var u2 = Ual2Ranger("ranger_ual2_x1_5", false, missing);
            var u2i = Ual2Ranger("ranger_ual2_iso_x1_5", true, missing);
            AssetDatabase.SaveAssets();
            if (missing.Count > 0)
                Debug.LogWarning("[캐릭터 굽기] 찾지 못한 파일/모션 (건너뜀):\n" + string.Join("\n", missing));
            return new[] { u, ui, u2, u2i };
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

        [MenuItem("CardGame/캐릭터 굽기/UAL2 무기 레이어·색 교체 테스트 굽기 (옆+아이소)")]
        static void BakeUal2Test()
        {
            var missing = new List<string>();
            RemoveMixamoSetups();
            var a = Ual2Ranger("ranger_ual2_x1_5", false, missing);
            var b = Ual2Ranger("ranger_ual2_iso_x1_5", true, missing);
            AssetDatabase.SaveAssets();
            if (missing.Count > 0) Debug.LogWarning("[캐릭터 굽기] 찾지 못한 파일/모션 (건너뜀):\n" + string.Join("\n", missing));
            var t0 = EditorApplication.timeSinceStartup;
            CharacterBaker.BakeAll(a);
            CharacterBaker.BakeAll(b);
            Debug.Log($"[캐릭터 굽기] UAL2 테스트 굽기 끝 ({EditorApplication.timeSinceStartup - t0:0}초)");
        }

        /// <summary>
        /// 레인저 + UAL2 전투 모션. 무기는 몸 시트에 그리지 않고 레이어로 따로 굽는다.
        /// 무기마다 손잡이 잡는 법(그립)은 여기서 한 번만 정한다.
        /// </summary>
        static CharacterBakeSetup Ual2Ranger(string name, bool isometric, List<string> missing)
        {
            EnsureGeneric(UAL2, UAL2_GENERIC);
            var s = NewSetup(name);
            UalCommon(s, name, isometric, missing);
            // 몸 시트에는 무기·방패 없음
            s.weapon = new CharacterBakeSetup.WeaponSlot();
            s.shield = new CharacterBakeSetup.ShieldSlot();
            s.paletteSwap = true;
            s.autoPaletteColors = 24;
            s.layerClipPrefixes.AddRange(new[] { "Sword_", "Idle_Shield", "Shield_", "Hit_" });
            void W(string layer, string path, bool strike = false)
            {
                var go = Load<GameObject>(path, missing);
                if (go != null) s.layers.Add(new CharacterBakeSetup.LayerItem { name = layer, prefab = go, boneName = "hand_r", bladeMode = 1, strikePoint = strike });
            }
            W("sword", MW + "Swords/Sword.fbx", true);
            W("falchion", MW + "Swords/Falchion.fbx");
            W("messer", MW + "Swords/Messer.fbx");
            W("dagger", MW + "Swords/Dagger.fbx");
            W("arming_sword", A + "Weapons/Arming_Sword.fbx");
            var sh = Load<GameObject>(MW + "Shields/Shield A.fbx", missing);
            if (sh != null) s.layers.Add(new CharacterBakeSetup.LayerItem { name = "shield_a", isShield = true, prefab = sh, boneName = "lowerarm_l" });

            foreach (var (n, attack) in Ual2Clips) AddClip(s, UAL2_GENERIC, n, attack, missing);
            Save(s);
            return s;
        }

        /// <summary>
        /// 같은 레인저를 UAL 모션 전부로 굽는 설정. 뼈대가 같으므로 리타깃 없이 그대로 재생하고, 자세 보정은 끈다.
        /// 모션마다 첫 프레임의 가장 낮은 발이 바닥에 닿도록 높이만 한 번 맞추고, 바닥 아래로 내려가는 것만 막는다.
        /// </summary>
        static CharacterBakeSetup UalRanger(string name, bool isometric, List<string> missing)
        {
            EnsureGeneric(UAL, UAL_GENERIC);
            var s = NewSetup(name);
            UalCommon(s, name, isometric, missing);
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

        /// <summary>UAL 설정 공통: 레인저 몸, 1.5배 도트, 24fps, 뼈대가 같아 그대로 재생 (자세 보정 끔)</summary>
        static void UalCommon(CharacterBakeSetup s, string name, bool isometric, List<string> missing)
        {
            s.fps = 24;
            if (isometric) { s.yawToFront = 45f; s.pitch = 30f; }
            s.baseModel = Load<GameObject>(A + "Characters/Base/Superhero_Male_FullBody.fbx", missing);
            AddPart(s, A + "Characters/Outfits/Male_Ranger.fbx", missing);
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
