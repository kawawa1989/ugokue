using Ugokue.App.Diagnostics;
using Ugokue.App.Rig;
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

        public const string PartMaterialPath = "Assets/Ugokue/Materials/ModelPart.mat";

        public const string PartShaderName = "Ugokue/UnlitTransparent";

        /// <summary>
        /// カメラ、PerformanceMonitor、モデルの描画とパラメータのスライダーを持つメインシーンを作り直して保存する。
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

            var modelObject = new GameObject("Model");
            var modelRenderer = modelObject.AddComponent<ModelRenderer>();
            var serialized = new SerializedObject(modelRenderer);
            serialized.FindProperty("partMaterial").objectReferenceValue = EnsurePartMaterial();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var sliders = new SerializedObject(modelObject.AddComponent<ParameterSliders>());
            sliders.FindProperty("target").objectReferenceValue = modelRenderer;
            sliders.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, MainScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// パーツの描画に使うマテリアルを返す。アセットがなければ作成する。
        /// </summary>
        public static Material EnsurePartMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(PartMaterialPath);
            if (material != null)
            {
                return material;
            }

            EnsureFolder("Assets/Ugokue", "Materials");
            material = new Material(Shader.Find(PartShaderName));
            AssetDatabase.CreateAsset(material, PartMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
