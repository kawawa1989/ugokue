using Ugokue.App.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ugokue.Editor
{
    /// <summary>
    /// アプリのメインシーンを生成し、ビルド対象に登録する。
    /// </summary>
    public static class ProjectBootstrap
    {
        public const string MainScenePath = "Assets/Scenes/Main.unity";

        /// <summary>
        /// カメラと PerformanceMonitor を持つメインシーンを作り直して保存する。
        /// </summary>
        [MenuItem("Ugokue/Recreate Main Scene")]
        public static void CreateMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            cameraObject.transform.position = new Vector3(0, 0, -10);

            new GameObject("PerformanceMonitor").AddComponent<PerformanceMonitor>();

            AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, MainScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
            AssetDatabase.SaveAssets();
        }
    }
}
