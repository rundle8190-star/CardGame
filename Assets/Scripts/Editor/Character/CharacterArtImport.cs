using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    /// <summary>
    /// Assets/Art3D 아래 3D 파일을 처음 가져올 때 설정을 자동으로 맞춘다.
    ///  - Characters/Base, Animations : 휴머노이드(사람형 리그)로 가져온다. 모션을 다른 몸에 옮겨 쓰기 위해 필요.
    ///  - Characters/Outfits, Hair : 뼈 이름으로 몸에 붙이므로 일반(Generic) 리그.
    ///  - Weapons : 리그 없음.
    ///  - Quaternius 파일은 축 변환을 굽는다. (Quaternius 안내 그림 1번)
    /// 이미 한 번 가져온 파일은 건드리지 않는다. 다시 적용하려면 메뉴 "CardGame/캐릭터 굽기/Art3D 가져오기 설정 다시 적용".
    /// </summary>
    public class CharacterArtImport : AssetPostprocessor
    {
        const string Root = "Assets/Art3D/";
        static bool forceApply;

        static bool IsMixamo(string p) => p.Contains("/Animations/Mixamo/");
        // 캐릭터와 뼈대가 같은 모션을 리타깃 없이 그대로 쓰기 위한 Generic 사본
        static bool IsGenericAnim(string p) => p.Contains("/Animations/UAL_Generic/");
        static bool IsAnimation(string p) => p.StartsWith(Root + "Animations/");
        static bool IsBaseBody(string p) => p.StartsWith(Root + "Characters/Base/");
        static bool IsPart(string p) => p.StartsWith(Root + "Characters/Outfits/") || p.StartsWith(Root + "Characters/Hair/");
        static bool IsWeapon(string p) => p.StartsWith(Root + "Weapons/");

        // 반복 재생되는 모션인지 이름으로 추측한다. (굽기에는 영향이 적고, 미리보기 편의용)
        public static bool LooksLooping(string clipName)
        {
            string n = clipName.ToLowerInvariant();
            return n.Contains("loop") || n.Contains("idle") || n.Contains("walk") || n.Contains("run")
                || n.Contains("strafe") || n.Contains("jog") || n.Contains("sprint");
        }

        void OnPreprocessModel()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(Root)) return;
            if (!assetImporter.importSettingsMissing && !forceApply) return;

            var mi = (ModelImporter)assetImporter;
            mi.bakeAxisConversion = !IsMixamo(p) && !IsWeapon(p);
            mi.importCameras = false;
            mi.importLights = false;

            if (IsGenericAnim(p))
            {
                mi.animationType = ModelImporterAnimationType.Generic;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = true;
            }
            else if (IsBaseBody(p) || IsAnimation(p))
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = IsAnimation(p);
            }
            else if (IsPart(p))
            {
                mi.animationType = ModelImporterAnimationType.Generic;
                mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
                mi.importAnimation = false;
            }
            else if (IsWeapon(p))
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
            }
        }

        void OnPreprocessAnimation()
        {
            string p = assetPath.Replace('\\', '/');
            if (!IsAnimation(p)) return;
            if (!assetImporter.importSettingsMissing && !forceApply) return;

            var mi = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            string fileName = System.IO.Path.GetFileNameWithoutExtension(p);
            if (IsGenericAnim(p))
            {
                // 뼈 움직임을 그대로 쓴다. 반복 여부만 이름으로 정한다.
                foreach (var c in clips) c.loopTime = LooksLooping(c.name);
                mi.clipAnimations = clips;
                return;
            }
            foreach (var c in clips)
            {
                // 회전, 높이는 몸 동작에 포함시킨다. (쓰러지는 모션이 제대로 눕도록)
                // 앞뒤 이동만 빼서 캐릭터가 제자리에서 움직이게 한다. 실제 이동은 게임에서 스프라이트를 옮긴다.
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = false;

                // Mixamo 파일은 클립 이름이 전부 "mixamo.com" 이라 파일 이름으로 바꿔 준다.
                if (IsMixamo(p) && clips.Length == 1) c.name = fileName.Replace("X Bot@", "");

                c.loopTime = LooksLooping(c.name);
            }
            mi.clipAnimations = clips;
        }

        [MenuItem("CardGame/캐릭터 굽기/Art3D 가져오기 설정 다시 적용")]
        static void Reapply()
        {
            if (!EditorUtility.DisplayDialog("다시 적용",
                "Assets/Art3D 안 3D 파일의 가져오기 설정을 기본값으로 되돌리고 다시 가져옵니다.\n직접 바꾼 설정은 사라집니다. 계속할까요?",
                "다시 적용", "취소")) return;

            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Art3D" }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));

            forceApply = true;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string path in paths)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                forceApply = false;
            }
            Debug.Log($"[캐릭터 굽기] {paths.Count}개 3D 파일의 가져오기 설정을 다시 적용했습니다.");
        }
    }
}
