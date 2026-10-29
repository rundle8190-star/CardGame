// 쿼터뷰 타일 굽기. 3D 타일 모델(프리팹, FBX)을 쿼터뷰 카메라로 찍어 도트 PNG 로 저장한다.
// 메뉴 CardGame > 쿼터뷰 타일 굽기.
//
// 모델 규칙
//   - 바닥 한 칸 = 가로세로 1 x 1 (Unity 단위). 바닥 가운데가 원점(0, 0, 0), 바닥 높이는 y = 0.
//   - 모델 이름이 파일 이름이 된다. 지형이면 MapCatalog 의 지형 id (field, forest, water ...),
//     변형은 field_2, field_3 ... 노드면 노드 종류 id (battle, farm, exit ...).
// 출력
//   - 가로 = 한 칸 픽셀 수(기본 64), 세로 = 바닥 마름모(가로의 절반) + 위로 솟는 높이.
//   - 바닥 마름모의 아래 꼭짓점이 그림 맨 아래에 온다(피벗: 아래 가운데).
//   - 카메라: 직교, 아래로 30도, 옆으로 45도 → 바닥이 정확히 2:1 마름모가 된다.
//     캐릭터를 구울 때도 같은 각도와 같은 픽셀 크기를 써야 섞었을 때 어울린다.
// 렌더러
//   - 이 프로젝트 기본 렌더러는 2D 렌더러라서 3D 모델의 빛이 안 들어갈 수 있다.
//     URP 설정(Settings/UniversalRP)의 Renderer List 에 Universal Renderer(3D)를 하나 더하고,
//     그 번호를 "렌더러 번호"에 넣으면 굽기에만 그 렌더러를 쓴다.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CardGame.EditorTools
{
    public class QuarterTileBaker : EditorWindow
    {
        enum Output { Terrain, Node }

        [SerializeField] List<GameObject> models = new List<GameObject>();
        [SerializeField] Output output = Output.Terrain;
        [SerializeField] int tilePixelWidth = MapArtTool.DefaultTilePixelWidth;
        [SerializeField] int extraHeightPixels = 64;
        [SerializeField] float pitch = 30f;
        [SerializeField] float yaw = 45f;
        [SerializeField] Vector3 lightAngles = new Vector3(50f, -30f, 0f);
        [SerializeField] float lightIntensity = 1.2f;
        [SerializeField] Color ambient = new Color(0.45f, 0.45f, 0.5f);
        [SerializeField] Texture2D palette;
        [SerializeField] float alphaCut = 0.5f;
        [SerializeField] int rendererIndex = -1;

        Vector2 scroll;

        [MenuItem("CardGame/쿼터뷰 타일 굽기")]
        static void Open()
        {
            GetWindow<QuarterTileBaker>("쿼터뷰 타일 굽기").minSize = new Vector2(340, 420);
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("3D 모델을 쿼터뷰로 찍어 도트 PNG 로 저장해요. 모델 바닥은 1×1 칸, 바닥 가운데가 원점이어야 해요. 모델 이름이 파일 이름이 돼요 (예: field, field_2, forest, battle).", MessageType.Info);

            EditorGUILayout.LabelField("모델", EditorStyles.boldLabel);
            int remove = -1;
            for (int i = 0; i < models.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                models[i] = (GameObject)EditorGUILayout.ObjectField(models[i], typeof(GameObject), false);
                if (GUILayout.Button("빼기", GUILayout.Width(40))) remove = i;
                EditorGUILayout.EndHorizontal();
            }
            if (remove >= 0) models.RemoveAt(remove);
            var added = (GameObject)EditorGUILayout.ObjectField("모델 추가", null, typeof(GameObject), false);
            if (added != null) models.Add(added);
            if (GUILayout.Button("Project 창에서 고른 모델 모두 추가"))
                foreach (var o in Selection.gameObjects)
                    if (EditorUtility.IsPersistent(o) && !models.Contains(o)) models.Add(o);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("출력", EditorStyles.boldLabel);
            output = (Output)EditorGUILayout.Popup("종류", (int)output, new[] { "지형 타일 (Art/tiles)", "노드 (Art/map_nodes)" });
            tilePixelWidth = EditorGUILayout.IntField("한 칸 가로 픽셀", tilePixelWidth);
            tilePixelWidth = Mathf.Clamp(tilePixelWidth / 2 * 2, 8, 512);
            extraHeightPixels = EditorGUILayout.IntField("위로 솟는 높이 픽셀", extraHeightPixels);
            extraHeightPixels = Mathf.Clamp(extraHeightPixels, 0, 1024);
            EditorGUILayout.LabelField("그림 크기", tilePixelWidth + " × " + (tilePixelWidth / 2 + extraHeightPixels));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("카메라와 조명", EditorStyles.boldLabel);
            pitch = EditorGUILayout.FloatField("아래로 기울기 (30 = 2:1)", pitch);
            yaw = EditorGUILayout.FloatField("옆으로 돌리기", yaw);
            lightAngles = EditorGUILayout.Vector3Field("빛 방향", lightAngles);
            lightIntensity = EditorGUILayout.FloatField("빛 세기", lightIntensity);
            ambient = EditorGUILayout.ColorField("주변광", ambient);
            rendererIndex = EditorGUILayout.IntField("렌더러 번호 (-1 기본)", rendererIndex);
            EditorGUILayout.LabelField("3D 빛이 안 들어가면 URP 설정에 Universal Renderer 를 더하고 그 번호를 넣으세요.", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("도트 처리", EditorStyles.boldLabel);
            palette = (Texture2D)EditorGUILayout.ObjectField("팔레트 그림 (선택)", palette, typeof(Texture2D), false);
            EditorGUILayout.LabelField("팔레트를 넣으면 모든 픽셀을 팔레트의 가장 가까운 색으로 바꿔요.", EditorStyles.miniLabel);
            alphaCut = EditorGUILayout.Slider("투명 기준", alphaCut, 0.05f, 0.95f);

            EditorGUILayout.Space(10);
            GUI.enabled = models.Count > 0;
            if (GUILayout.Button("굽기", GUILayout.Height(30))) BakeAll();
            GUI.enabled = true;
            EditorGUILayout.EndScrollView();
        }

        void BakeAll()
        {
            string folder = output == Output.Terrain ? MapArtTool.TileFolder : MapArtTool.NodeFolder;
            Directory.CreateDirectory(folder);
            Color32[] pal = LoadPalette();
            var saved = new List<string>();
            try
            {
                for (int i = 0; i < models.Count; i++)
                {
                    var m = models[i];
                    if (m == null) continue;
                    EditorUtility.DisplayProgressBar("타일 굽기", m.name, (float)i / models.Count);
                    string path = folder + "/" + FileName(m.name) + ".png";
                    if (Bake(m, path, pal)) saved.Add(path);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            AssetDatabase.Refresh();
            foreach (var p in saved) MapArtTool.FixImportSettingsOf(p, tilePixelWidth);
            EditorUtility.DisplayDialog("타일 굽기", saved.Count + "장을 " + folder + " 에 저장했어요.", "확인");
        }

        bool Bake(GameObject model, string path, Color32[] pal)
        {
            int w = tilePixelWidth;
            int h = tilePixelWidth / 2 + extraHeightPixels;
            Scene scene = EditorSceneManager.NewPreviewScene();
            RenderTexture rt = null;
            var oldAmbient = RenderSettings.ambientLight;
            try
            {
                var inst = PrefabUtility.InstantiatePrefab(model, scene) as GameObject;
                if (inst == null)
                {
                    inst = Instantiate(model);
                    SceneManager.MoveGameObjectToScene(inst, scene);
                }
                inst.transform.position = Vector3.zero;

                var lightGo = new GameObject("BakeLight");
                SceneManager.MoveGameObjectToScene(lightGo, scene);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = lightIntensity;
                light.shadows = LightShadows.None;
                lightGo.transform.rotation = Quaternion.Euler(lightAngles);

                var camGo = new GameObject("BakeCamera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.scene = scene;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 200f;
                if (rendererIndex >= 0) cam.GetUniversalAdditionalCameraData().SetRenderer(rendererIndex);

                // 바닥 한 칸의 대각선(√2)이 화면 가로 w 픽셀이 되게 맞춘다
                float pixelsPerUnit = w / Mathf.Sqrt(2f);
                cam.orthographicSize = h / (2f * pixelsPerUnit);
                cam.aspect = (float)w / h;
                cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                // 바닥 가운데(원점)가 그림 아래에서 w/4 픽셀 위에 오도록 카메라를 올린다
                float lift = (h / 2f - w / 4f) / pixelsPerUnit;
                cam.transform.position = -cam.transform.forward * 50f + cam.transform.up * lift;

                rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                rt.filterMode = FilterMode.Point;
                cam.targetTexture = rt;
                RenderSettings.ambientLight = ambient;
                cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;

                var px = tex.GetPixels32();
                byte cut = (byte)(alphaCut * 255f);
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a < cut) { px[i] = new Color32(0, 0, 0, 0); continue; }
                    px[i].a = 255;
                    if (pal != null && pal.Length > 0) px[i] = Nearest(px[i], pal);
                }
                bool empty = true;
                foreach (var c in px) if (c.a > 0) { empty = false; break; }
                if (empty) Debug.LogWarning("타일 굽기: " + model.name + " 이(가) 비어 있어요. 모델 위치(원점)와 크기, 렌더러 번호를 확인하세요.");
                tex.SetPixels32(px);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                DestroyImmediate(tex);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError("타일 굽기 실패 (" + model.name + "): " + e.Message);
                return false;
            }
            finally
            {
                RenderSettings.ambientLight = oldAmbient;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        Color32[] LoadPalette()
        {
            if (palette == null) return null;
            string p = AssetDatabase.GetAssetPath(palette);
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) return null;
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(File.ReadAllBytes(p))) return null;
            var set = new HashSet<int>();
            var colors = new List<Color32>();
            foreach (var c in tex.GetPixels32())
            {
                if (c.a < 128) continue;
                int key = (c.r << 16) | (c.g << 8) | c.b;
                if (set.Add(key)) colors.Add(new Color32(c.r, c.g, c.b, 255));
            }
            DestroyImmediate(tex);
            return colors.ToArray();
        }

        static Color32 Nearest(Color32 c, Color32[] pal)
        {
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < pal.Length; i++)
            {
                int dr = c.r - pal[i].r, dg = c.g - pal[i].g, db = c.b - pal[i].b;
                // 사람 눈에 가깝게 초록에 무게를 더 준다
                int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                if (d < bestD) { bestD = d; best = i; }
            }
            return pal[best];
        }

        static string FileName(string modelName)
        {
            return modelName.Trim().ToLowerInvariant().Replace(' ', '_');
        }
    }
}
