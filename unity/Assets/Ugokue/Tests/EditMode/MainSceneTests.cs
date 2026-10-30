using System.Linq;
using NUnit.Framework;
using Ugokue.App.Diagnostics;
using Ugokue.App.Rig;
using Ugokue.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ugokue.Tests.EditMode
{
    public class MainSceneTests
    {
        [Test]
        public void MainSceneIsTheOnlyEnabledBuildScene()
        {
            string[] enabled = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

            Assert.That(enabled, Is.EqualTo(new[] { ProjectBootstrap.MainScenePath }));
        }

        [Test]
        public void MainSceneHasCameraAndPerformanceMonitor()
        {
            EditorSceneManager.OpenScene(ProjectBootstrap.MainScenePath, OpenSceneMode.Single);

            Assert.That(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<PerformanceMonitor>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
        }

        [Test]
        public void MainSceneHasModelRendererWithPartMaterialAndSliders()
        {
            EditorSceneManager.OpenScene(ProjectBootstrap.MainScenePath, OpenSceneMode.Single);

            ModelRenderer[] renderers = Object.FindObjectsByType<ModelRenderer>(FindObjectsSortMode.None);
            Assert.That(renderers, Has.Length.EqualTo(1));
            var material = (Material)new SerializedObject(renderers[0]).FindProperty("partMaterial").objectReferenceValue;
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo(ProjectBootstrap.PartShaderName));
            Assert.That(Object.FindObjectsByType<ParameterSliders>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
        }

        [TestCase("maskMaterial", ProjectBootstrap.MaskMaterialPath, ProjectBootstrap.MaskShaderName)]
        [TestCase("debugMaterial", ProjectBootstrap.DebugMaterialPath, ProjectBootstrap.DebugShaderName)]
        public void MainSceneModelRendererHasMaskAndDebugMaterials(string property, string assetPath, string shaderName)
        {
            EditorSceneManager.OpenScene(ProjectBootstrap.MainScenePath, OpenSceneMode.Single);

            ModelRenderer modelRenderer = Object.FindObjectsByType<ModelRenderer>(FindObjectsSortMode.None).Single();
            var material = (Material)new SerializedObject(modelRenderer).FindProperty(property).objectReferenceValue;
            Assert.That(material, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(material), Is.EqualTo(assetPath));
            Assert.That(material.shader.name, Is.EqualTo(shaderName));
        }

        [Test]
        public void MainSceneHasDrawOrderDebugViewTargetingTheModel()
        {
            EditorSceneManager.OpenScene(ProjectBootstrap.MainScenePath, OpenSceneMode.Single);

            DrawOrderDebugView[] views = Object.FindObjectsByType<DrawOrderDebugView>(FindObjectsSortMode.None);
            Assert.That(views, Has.Length.EqualTo(1));
            var target = new SerializedObject(views[0]).FindProperty("target").objectReferenceValue;
            Assert.That(target, Is.SameAs(Object.FindObjectsByType<ModelRenderer>(FindObjectsSortMode.None).Single()));
        }

        [Test]
        public void FrameTimingStatsAreEnabled()
        {
            Assert.That(PlayerSettings.enableFrameTimingStats, Is.True);
        }

        [Test]
        public void PlayerKeepsRunningWhenUnfocused()
        {
            Assert.That(PlayerSettings.runInBackground, Is.True);
        }
    }
}
