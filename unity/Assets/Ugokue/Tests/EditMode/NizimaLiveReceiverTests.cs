using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ugokue.App.Live;
using Ugokue.Core.Live;
using Ugokue.Core.Live.Nizima;
using Ugokue.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ugokue.Tests.EditMode
{
    public class NizimaLiveReceiverTests
    {
        [Test]
        public void MainSceneHasOneReceiverOnTheDefaultPort()
        {
            EditorSceneManager.OpenScene(ProjectBootstrap.MainScenePath, OpenSceneMode.Single);

            NizimaLiveReceiver[] receivers = Object.FindObjectsByType<NizimaLiveReceiver>(FindObjectsSortMode.None);
            Assert.That(receivers, Has.Length.EqualTo(1));
            Assert.That(new SerializedObject(receivers[0]).FindProperty("port").intValue, Is.EqualTo(NizimaLiveClientOptions.DefaultPort));
        }

        [Test]
        public void TokenIsKeptInPersistentData()
        {
            Assert.That(
                NizimaLiveReceiver.TokenPath,
                Is.EqualTo(Path.Combine(Application.persistentDataPath, "nizima-live-token")));
        }

        [TestCase(NizimaConnectionState.Disconnected, "nizima LIVE")]
        [TestCase(NizimaConnectionState.WaitingForRegistration, "許可")]
        [TestCase(NizimaConnectionState.WaitingForEnable, "有効")]
        public void StatusShowsAHintUntilStreaming(NizimaConnectionState state, string hint)
        {
            string text = NizimaLiveReceiver.DescribeStatus(state, null, true, null);

            StringAssert.StartsWith("nizima LIVE: " + state, text);
            StringAssert.Contains(hint, text);
        }

        [Test]
        public void StatusShowsRateWhileStreaming()
        {
            string text = NizimaLiveReceiver.DescribeStatus(NizimaConnectionState.Streaming, new ReceiveRateStats(59.8, 16.7, 18.2, 33.4), true, null);

            StringAssert.Contains("59.8 fps", text);
            StringAssert.Contains("p95 18.2 ms", text);
        }

        [Test]
        public void StatusWarnsWhenFramesHaveNoModel()
        {
            string text = NizimaLiveReceiver.DescribeStatus(NizimaConnectionState.Streaming, null, false, null);

            StringAssert.Contains("モデル", text);
        }

        [Test]
        public void ValuesAreListedInIdOrder()
        {
            var frame = new TrackingFrame(1, new Dictionary<string, double> { ["MouthOpen"] = 0.25, ["AngleX"] = -12.345 });

            string[] lines = NizimaLiveReceiver.DescribeValues(frame).ToArray();

            Assert.That(lines, Is.EqualTo(new[] { "AngleX  -12.35", "MouthOpen  0.25" }));
        }
    }
}
