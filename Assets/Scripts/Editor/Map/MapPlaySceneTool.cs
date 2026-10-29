// 맵 플레이 씬(Assets/Scenes/MapPlay.unity)을 만들고, 고른 맵으로 바로 플레이한다.
// 메뉴 CardGame > 맵 플레이 씬 만들기, 또는 맵 편집기의 "▶ 플레이" 버튼에서 쓴다.
using System.IO;
using CardGame.View.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CardGame.EditorTools
{
    public static class MapPlaySceneTool
    {
        public const string ScenePath = "Assets/Scenes/MapPlay.unity";
        public const string DefaultMap = "Assets/Data/Maps/plains.json";

        [MenuItem("CardGame/맵 플레이 씬 만들기")]
        static void CreateSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateScene(DefaultMap);
        }

        static void CreateScene(string mapPath)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0, 0, -10);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 7;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            var go = new GameObject("Map Player");
            var controller = go.AddComponent<MapPlayController>();
            controller.mapFile = AssetDatabase.LoadAssetAtPath<TextAsset>(mapPath);
            MapArtTool.FillArt(controller);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
        }

        public static void PlayMap(string mapPath)
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("맵 플레이", "이미 플레이 중이에요. 플레이를 멈춘 뒤 다시 눌러 주세요.", "확인");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (!File.Exists(ScenePath)) CreateScene(mapPath);
            else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var controller = Object.FindObjectOfType<MapPlayController>();
            if (controller == null) controller = new GameObject("Map Player").AddComponent<MapPlayController>();
            var file = AssetDatabase.LoadAssetAtPath<TextAsset>(mapPath);
            if (file == null)
            {
                EditorUtility.DisplayDialog("맵 플레이", mapPath + " 를 찾지 못했어요. Assets 폴더 안에 저장한 맵만 플레이할 수 있어요.", "확인");
                return;
            }
            controller.mapFile = file;
            MapArtTool.FillArt(controller);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            EditorSceneManager.SaveScene(controller.gameObject.scene);

            EditorApplication.isPlaying = true;
        }

        static void AddToBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes) if (s.path == ScenePath) return;
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
