using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 받아 둔 에셋으로 테스트용 굽기 설정 두 개를 자동으로 만든다.
    ///  - test_sword_shield : 레인저 옷 + 한손검 + 임시 방패 + 검방패 모션
    ///  - test_axe          : 농부 옷 + 수염 + 도끼 + 도끼 모션
    /// </summary>
    public static class CharacterBakeTestSetups
    {
        const string A = "Assets/Art3D/";
        const string SetupFolder = A + "BakeSetups";

        const string UAL = A + "Animations/UAL/UAL1_Standard.fbx";
        const string SS = A + "Animations/Mixamo/SwordShield/";
        const string AXE = A + "Animations/Mixamo/Axe/";
        const string SINGLE = A + "Animations/Mixamo/Singles/X Bot@";
        // UAL을 Generic(리타깃 없음)으로 가져온 사본. 원본(휴머노이드)은 기존 설정이 쓰므로 그대로 둔다.
        const string UAL_GENERIC_DIR = A + "Animations/UAL_Generic";
        const string UAL_GENERIC = UAL_GENERIC_DIR + "/UAL1_Standard_Generic.fbx";
        static readonly string[] UalAttacks = { "Sword_Attack", "Punch_Cross", "Punch_Jab" };

        public static CharacterBakeSetup[] CreateAll()
        {
            var missing = new List<string>();
            var r15 = Ranger("ranger_x1_5", 1.5f, false, missing);
            var iso = Ranger("ranger_iso_x1_5", 1.5f, true, missing);
            var a = SwordShield(missing);
            var b = Axe(missing);
            var u = UalRanger("ranger_ual_x1_5", false, missing);
            var ui = UalRanger("ranger_ual_iso_x1_5", true, missing);
            AssetDatabase.SaveAssets();
            if (missing.Count > 0)
                Debug.LogWarning("[캐릭터 굽기] 찾지 못한 파일/모션 (건너뜀):\n" + string.Join("\n", missing));
            return new[] { r15, iso, a, b, u, ui };
        }

        static CharacterBakeSetup SwordShield(List<string> missing)
        {
            var s = NewSetup("test_sword_shield");
            s.baseModel = Load<GameObject>(A + "Characters/Base/Superhero_Male_FullBody.fbx", missing);
            AddPart(s, A + "Characters/Outfits/Male_Ranger.fbx", missing); // 두건이 있어서 머리카락은 뺐다
            s.weapon.prefab = Load<GameObject>(A + "Weapons/Arming_Sword.fbx", missing);
            s.shield.useTempShield = true;
            s.outputFolder = "Assets/Art/characters/test_sword_shield";

            AddClip(s, UAL, "Sword_Idle", false, missing);
            AddClip(s, UAL, "Sword_Attack", true, missing);
            AddClip(s, UAL, "Hit_Chest", false, missing);
            AddClip(s, UAL, "Death01", false, missing);
            AddClip(s, SS + "sword and shield idle.fbx", null, false, missing);
            AddClip(s, SS + "sword and shield slash.fbx", null, true, missing, 7); // 눈으로 확인한 타격 프레임
            AddClip(s, SS + "sword and shield slash (2).fbx", null, true, missing, 8);
            AddClip(s, SS + "sword and shield attack.fbx", null, true, missing);
            AddClip(s, SS + "sword and shield block.fbx", null, false, missing);
            AddClip(s, SS + "sword and shield impact.fbx", null, false, missing);
            AddClip(s, SS + "sword and shield death.fbx", null, false, missing);
            AddClip(s, SINGLE + "One Hand Sword Combo.fbx", null, true, missing, 36);
            AddClip(s, SINGLE + "Standing Dodge Backward.fbx", null, false, missing);
            Save(s);
            return s;
        }

        /// <summary>
        /// 레인저 + 한손검 + 임시 방패, 받아 둔 검방패 모션 전부 (제자리 돌기 4개는 뺌: 횡스크롤에서는 좌우 반전으로 대신한다).
        /// size: 기본(1미터 36px) 대비 도트 크기 배율. 1.5면 키 약 96px, 2면 약 130px.
        /// </summary>
        static CharacterBakeSetup Ranger(string name, float size, bool isometric, List<string> missing)
        {
            var s = NewSetup(name);
            s.fps = 24; // 12fps는 끊겨 보여서 두 배로
            if (isometric)
            {
                // 2.5D 쿼터뷰: 위에서 30도, 옆에서 45도 돌려 캐릭터가 화면 오른쪽 아래를 본다 (바닥 마름모가 2:1)
                s.yawToFront = 45f;
                s.pitch = 30f;
            }
            s.baseModel = Load<GameObject>(A + "Characters/Base/Superhero_Male_FullBody.fbx", missing);
            AddPart(s, A + "Characters/Outfits/Male_Ranger.fbx", missing);
            s.weapon.prefab = Load<GameObject>(A + "Weapons/Arming_Sword.fbx", missing);
            s.shield.useTempShield = true;
            s.outputFolder = "Assets/Art/characters/" + name;
            s.pixelsPerMeter = Mathf.Round(36f * size);
            s.frameWidth = Mathf.RoundToInt(128 * size / 8f) * 8;
            s.frameHeight = Mathf.RoundToInt(96 * size / 8f) * 8;
            s.groundPixel = Mathf.RoundToInt(6 * size);
            s.maxFrames = 240;

            // 기존 Quaternius 검 모션
            AddClip(s, UAL, "Sword_Idle", false, missing);
            AddClip(s, UAL, "Sword_Attack", true, missing);
            AddClip(s, UAL, "Hit_Chest", false, missing);
            AddClip(s, UAL, "Death01", false, missing);

            // Mixamo 검방패 팩
            string[] idle = { "idle", "idle (2)", "idle (3)", "idle (4)" };
            foreach (var n in idle) SS_(s, n, false, missing);
            SS_(s, "attack", true, missing);
            SS_(s, "attack (2)", true, missing);
            SS_(s, "attack (3)", true, missing);
            SS_(s, "attack (4)", true, missing);
            SS_(s, "slash", true, missing, 7);   // 눈으로 확인한 타격 프레임
            SS_(s, "slash (2)", true, missing, 8);
            SS_(s, "slash (3)", true, missing);
            SS_(s, "slash (4)", true, missing);
            SS_(s, "slash (5)", true, missing);
            SS_(s, "kick", true, missing);
            foreach (var n in new[] { "block", "block (2)", "block idle", "crouch block", "crouch block (2)", "crouch block idle",
                                      "crouch", "crouch idle", "crouching", "crouching (2)", "crouching (3)",
                                      "impact", "impact (2)", "impact (3)", "death", "death (2)",
                                      "jump", "jump (2)", "walk", "walk (2)", "run", "run (2)",
                                      "strafe", "strafe (2)", "strafe (3)", "strafe (4)",
                                      "power up", "casting", "casting (2)" })
                SS_(s, n, false, missing);
            AddClip(s, SS + "sheath sword 1.fbx", null, false, missing);
            AddClip(s, SS + "sheath sword 2.fbx", null, false, missing);

            // 낱개 Mixamo 모션
            AddClip(s, SINGLE + "One Hand Sword Combo.fbx", null, true, missing, 36);
            AddClip(s, SINGLE + "Standing Dodge Backward.fbx", null, false, missing);
            AddClip(s, SINGLE + "Draw Sword 2.fbx", null, false, missing);
            AddClip(s, SINGLE + "Falling Back Death.fbx", null, false, missing);
            AddClip(s, SINGLE + "Flying Back Death.fbx", null, false, missing);
            AddClip(s, SINGLE + "Standing React Death Forward.fbx", null, false, missing);
            Save(s);
            return s;
        }

        /// <summary>
        /// 같은 레인저를 UAL 모션 전부로 굽는 설정. 뼈대가 같으므로 리타깃 없이 그대로 재생하고, 자세 보정은 끈다.
        /// 모션마다 첫 프레임의 가장 낮은 발이 바닥에 닿도록 높이만 한 번 맞추고, 바닥 아래로 내려가는 것만 막는다.
        /// </summary>
        static CharacterBakeSetup UalRanger(string name, bool isometric, List<string> missing)
        {
            EnsureGenericUal();
            var s = NewSetup(name);
            s.fps = 24;
            if (isometric) { s.yawToFront = 45f; s.pitch = 30f; }
            s.baseModel = Load<GameObject>(A + "Characters/Base/Superhero_Male_FullBody.fbx", missing);
            AddPart(s, A + "Characters/Outfits/Male_Ranger.fbx", missing);
            s.weapon.prefab = Load<GameObject>(A + "Weapons/Arming_Sword.fbx", missing);
            s.shield.useTempShield = true;
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

        /// <summary>UAL 원본을 복사해 Generic으로 가져온다 (이미 있으면 그대로).</summary>
        static void EnsureGenericUal()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(UAL_GENERIC) != null) return;
            string src = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), UAL);
            if (!System.IO.File.Exists(src)) return;
            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), UAL_GENERIC_DIR);
            System.IO.Directory.CreateDirectory(dir);
            // 설정(.meta) 없이 파일만 복사해서, 가져오기 규칙(CharacterArtImport)이 Generic으로 설정하게 한다
            System.IO.File.Copy(src, System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), UAL_GENERIC), true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void SS_(CharacterBakeSetup s, string n, bool attack, List<string> missing, int hitOverride = -1)
            => AddClip(s, SS + "sword and shield " + n + ".fbx", null, attack, missing, hitOverride);

        static CharacterBakeSetup Axe(List<string> missing)
        {
            var s = NewSetup("test_axe");
            s.baseModel = Load<GameObject>(A + "Characters/Base/Superhero_Male_FullBody.fbx", missing);
            AddPart(s, A + "Characters/Outfits/Male_Peasant.fbx", missing);
            AddPart(s, A + "Characters/Hair/Hair_Buzzed.fbx", missing);
            AddPart(s, A + "Characters/Hair/Hair_Beard.fbx", missing);
            s.weapon.prefab = Load<GameObject>(A + "Weapons/Bearded_Axe.fbx", missing);
            s.heightScale = 1.05f;
            s.outputFolder = "Assets/Art/characters/test_axe";

            AddClip(s, AXE + "standing idle.fbx", null, false, missing);
            AddClip(s, AXE + "standing melee attack downward.fbx", null, true, missing, 10);
            AddClip(s, AXE + "standing melee attack horizontal.fbx", null, true, missing, 11); // 눈으로 확인한 타격 프레임
            AddClip(s, AXE + "standing melee combo attack ver. 1.fbx", null, true, missing, 23);
            AddClip(s, AXE + "standing block react large.fbx", null, false, missing);
            AddClip(s, AXE + "standing react large gut.fbx", null, false, missing);
            AddClip(s, AXE + "standing taunt battlecry.fbx", null, false, missing);
            AddClip(s, SINGLE + "Orc Idle.fbx", null, false, missing);
            AddClip(s, SINGLE + "Falling Back Death.fbx", null, false, missing);
            AddClip(s, UAL, "Hit_Head", false, missing);
            Save(s);
            return s;
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
