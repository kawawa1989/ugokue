using System.Linq;
using NUnit.Framework;
using Ugokue.App.Diagnostics;
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
