// 맵 그림 연결 도구.
// Assets/Art/tiles/<지형 id>.png (변형: <지형 id>_2.png ...) 와 Assets/Art/map_nodes/<노드 종류 id>.png 를 찾아
// 쿼터뷰 타일용 가져오기 설정(점 필터, 압축 없음, 피벗 아래 가운데)을 맞추고 플레이 화면에 연결한다.
// 같은 이름으로 덮어쓰면 그림이 바뀐다.
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CardGame.View.Map;
using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    public static class MapArtTool
    {
        public const string TileFolder = "Assets/Art/tiles";
        public const string NodeFolder = "Assets/Art/map_nodes";
        public const int DefaultTilePixelWidth = 64;

        [MenuItem("CardGame/맵 그림 가져오기 설정 맞추기")]
        static void FixAllMenu()
        {
            int n = FixImportSettings(TileFolder, DefaultTilePixelWidth) + FixImportSettings(NodeFolder, DefaultTilePixelWidth);
            EditorUtility.DisplayDialog("맵 그림", "그림 " + n + "장의 설정을 맞췄어요.", "확인");
        }

        // 폴더 안 PNG 의 가져오기 설정을 맞춘다. 바꾼 장수를 돌려준다.
        public static int FixImportSettings(string folder, int pixelsPerUnit)
        {
            if (!Directory.Exists(folder)) return 0;
            int changed = 0;
            foreach (var file in Directory.GetFiles(folder, "*.png"))
                if (FixImportSettingsOf(file.Replace('\\', '/'), pixelsPerUnit)) changed++;
            return changed;
        }

        public static bool FixImportSettingsOf(string path, int pixelsPerUnit)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return false;

            var st = new TextureImporterSettings();
            ti.ReadTextureSettings(st);
            bool ok = ti.textureType == TextureImporterType.Sprite
                      && ti.spriteImportMode == SpriteImportMode.Single
                      && Mathf.Approximately(ti.spritePixelsPerUnit, pixelsPerUnit)
                      && ti.filterMode == FilterMode.Point
                      && ti.textureCompression == TextureImporterCompression.Uncompressed
                      && !ti.mipmapEnabled
                      && st.spriteAlignment == (int)SpriteAlignment.BottomCenter;
            if (ok) return false;

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = pixelsPerUnit;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.ReadTextureSettings(st);
            st.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            ti.SetTextureSettings(st);
            ti.SaveAndReimport();
            return true;
        }

        // 플레이 화면에 그림 목록을 채운다
        public static void FillArt(MapPlayController controller)
        {
            int ppu = controller.tilePixelWidth > 0 ? controller.tilePixelWidth : DefaultTilePixelWidth;
            FixImportSettings(TileFolder, ppu);
            FixImportSettings(NodeFolder, ppu);
            controller.tileArt = Collect(TileFolder, true);
            controller.nodeArt = Collect(NodeFolder, false);
            EditorUtility.SetDirty(controller);
        }

        static List<MapPlayController.ArtEntry> Collect(string folder, bool allowVariants)
        {
            var list = new List<MapPlayController.ArtEntry>();
            if (!Directory.Exists(folder)) return list;
            foreach (var file in Directory.GetFiles(folder, "*.png"))
            {
                string path = file.Replace('\\', '/');
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;
                string id = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (allowVariants) id = Regex.Replace(id, "_[0-9]+$", "");
                list.Add(new MapPlayController.ArtEntry { id = id, sprite = sprite });
            }
            return list;
        }
    }
}
