using System.IO;
using System.Linq;
using NUnit.Framework;
using Ugokue.App.Live;
using Ugokue.App.Rig;
using Ugokue.Core.Live;
using Ugokue.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ugokue.Tests.EditMode
{
    public class TrackingDriverTests
    {
        private const double Tick = 1.0 / 60;

        private GameObject gameObject;
        private ModelRenderer modelRenderer;
        private TrackingDriver driver;

        [SetUp]
        public void SetUp()
        {
            gameObject = new GameObject("TrackingDriverTest");
            modelRenderer = gameObject.AddComponent<ModelRenderer>();
            modelRenderer.Load(ModelLocation.RepositorySampleDirectory(Application.dataPath));
            driver = gameObject.AddComponent<TrackingDriver>();
            var serialized = new SerializedObject(driver);
            serialized.FindProperty("target").objectReferenceValue = modelRenderer;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void MainSceneHasDriverWiredToTheModelAndTheReceiver()
        {
            EditorSceneManager.OpenScene(ProjectBootstrap.MainScenePath, OpenSceneMode.Single);

            TrackingDriver[] drivers = Object.FindObjectsByType<TrackingDriver>(FindObjectsSortMode.None);
            Assert.That(drivers, Has.Length.EqualTo(1));
            var serialized = new SerializedObject(drivers[0]);
            Assert.That(serialized.FindProperty("target").objectReferenceValue, Is.SameAs(Object.FindObjectsByType<ModelRenderer>(FindObjectsSortMode.None).Single()));
            Assert.That(serialized.FindProperty("receiver").objectReferenceValue, Is.SameAs(Object.FindObjectsByType<NizimaLiveReceiver>(FindObjectsSortMode.None).Single()));
        }

        [Test]
        public void RecordedInputMovesTheTestFace()
        {
            driver.UsePlayback(LoadRecording("playback-out-of-range"));
            double minAngleX = 0;
            for (double t = 0; t < 3; t += Tick)
            {
                driver.Step(t);
                minAngleX = System.Math.Min(minAngleX, modelRenderer.Rig.Parameters.Get("AngleX"));
                if (driver.State != TrackingState.NoInput)
                {
                    Assert.That(modelRenderer.Rig.Parameters.Get("AngleX"), Is.EqualTo(driver.Adapter.Get("AngleX").Value).Within(1e-9));
                }
            }

            Assert.That(driver.State, Is.EqualTo(TrackingState.Live));
            Assert.That(minAngleX, Is.InRange(-30, -29.9));
        }

        [Test]
        public void CalibrationMakesTheCurrentPoseFront()
        {
            driver.UsePlayback(LoadRecording("playback-short-loss"));
            double t = 0;
            for (; t < 3.7; t += Tick)
            {
                driver.Step(t);
            }

            Assert.That(modelRenderer.Rig.Parameters.Get("AngleZ"), Is.LessThan(-20));
            Assert.That(driver.Calibrate(), Is.True);
            for (int i = 0; i < 6; i++, t += Tick)
            {
                driver.Step(t);
            }

            Assert.That(modelRenderer.Rig.Parameters.Get("AngleZ"), Is.InRange(-1, 1));
            Assert.That(driver.Calibrated, Is.True);
        }

        [Test]
        public void ParametersAreLeftAloneBeforeTheFirstFrame()
        {
            modelRenderer.Rig.Parameters.Set("AngleX", 12);

            driver.Step(0);
            driver.Step(Tick);

            Assert.That(driver.State, Is.EqualTo(TrackingState.NoInput));
            Assert.That(modelRenderer.Rig.Parameters.Get("AngleX"), Is.EqualTo(12));
        }

        [Test]
        public void ReadsThePlaybackArgument()
        {
            Assert.That(TrackingDriver.PlaybackPath(new[] { "player", "-ugokue-tracking-playback", "/rec/messages.jsonl" }), Is.EqualTo(Path.GetFullPath("/rec/messages.jsonl")));
            Assert.That(TrackingDriver.PlaybackPath(new[] { "player" }), Is.Null);
            Assert.That(TrackingDriver.PlaybackPath(new[] { "player", "-ugokue-tracking-playback", "-batchmode" }), Is.Null);
        }

        [TestCase(TrackingState.NoInput, "待っています")]
        [TestCase(TrackingState.Live, "追従中")]
        [TestCase(TrackingState.Holding, "保持")]
        [TestCase(TrackingState.Returning, "正面へ戻しています")]
        public void StatusDescribesTheState(TrackingState state, string text)
        {
            StringAssert.Contains(text, TrackingDriver.DescribeState(state, false, null));
        }

        [Test]
        public void StatusShowsCalibrationAndLatency()
        {
            string text = TrackingDriver.DescribeState(TrackingState.Live, true, new LatencyStats(100, 3.2, 4.8, 9.1));

            StringAssert.Contains("校正済み", text);
            StringAssert.Contains("3.2 ms", text);
            StringAssert.Contains("p95 4.8 ms", text);
        }

        [TestCase(null, false)]
        [TestCase(0.2, false)]
        [TestCase(0.6, true)]
        public void LatencyMarkerLightsWhenTheMouthIsOpen(double? mouthOpen, bool lit)
        {
            Assert.That(TrackingDriver.MarkerLit(mouthOpen), Is.EqualTo(lit));
        }

        private static RecordedTrackingSource LoadRecording(string name)
        {
            return RecordedTrackingSource.Load(Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "dotnet", "Ugokue.Core.Tests", "Live", "Fixtures", name + ".jsonl")));
        }
    }
}
