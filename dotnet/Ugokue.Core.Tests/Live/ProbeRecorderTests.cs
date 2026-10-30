using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Ugokue.Core.Live.Nizima;
using Ugokue.Live.Probe;
using Xunit;

namespace Ugokue.Core.Tests.Live
{
    public sealed class ProbeRecorderTests : IDisposable
    {
        private readonly string outputDirectory = Path.Combine(Path.GetTempPath(), "ugokue-probe-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task RecordsMessagesFramesAndParameters()
        {
            using var server = new FakeNizimaLiveServer
            {
                LiveParameters = new JArray
                {
                    new JObject { ["Id"] = "AngleXLive", ["CreatedBy"] = "nizima LIVE", ["Group"] = "Face", ["Base"] = 0, ["Min"] = -30, ["Max"] = 30, ["Repeat"] = false },
                },
            };
            server.Start();
            using var client = new NizimaLiveClient(new NizimaLiveClientOptions { Uri = server.Uri, PluginName = "ugokue probe" }, new MemoryTokenStore());
            var recorder = new ProbeRecorder(client, outputDirectory);
            client.Start();

            await WaitUntil(() => client.State == NizimaConnectionState.WaitingForEnable);
            await server.SendEventAsync("NotifyEnabledChanged", new JObject { ["Enabled"] = true });
            await WaitUntil(() => client.LiveParameters.Count == 1);
            for (int i = 0; i < 3; i++)
            {
                await server.SendEventAsync("NotifyFrameUpdated", NizimaLiveClientTests.Frame(("AngleXLive", i), ("MouthOpen", 0.5)));
            }

            await server.SendEventAsync("NotifyFrameUpdated", new JObject { ["Models"] = new JArray() });
            await WaitUntil(() => recorder.FramesWithModel == 3 && recorder.FramesWithoutModel == 1);
            await client.StopAsync();
            JObject summary = recorder.Finish();

            Assert.Equal(summary.ToString(), File.ReadAllText(Path.Combine(outputDirectory, "summary.json")));
            Assert.Equal("ugokue probe", summary.Value<string>("pluginName"));
            Assert.Equal(1, summary.Value<int>("connectionCount"));
            Assert.Equal(3, summary["frames"].Value<int>("withModel"));
            Assert.Equal(1, summary["frames"].Value<int>("withoutModel"));
            Assert.Equal(new[] { "1" }, summary["frames"]["modelIds"].Values<string>());
            Assert.Equal(new[] { "AngleXLive", "MouthOpen" }, summary["observedLiveParameterIds"].Values<string>());
            Assert.Equal("AngleXLive", summary["liveParameters"][0].Value<string>("Id"));
            Assert.Contains("Streaming", summary["stateChanges"].Select(s => s.Value<string>("state")));
            Assert.NotNull(summary["rate"]);

            string[] lines = File.ReadAllLines(Path.Combine(outputDirectory, "messages.jsonl"));
            JObject[] messages = lines.Select(JObject.Parse).ToArray();
            Assert.All(messages, m => Assert.True(m.Value<double>("atSeconds") >= 0));
            Assert.Equal(4, messages.Count(m => m["message"].Value<string>("Method") == "NotifyFrameUpdated" && m["message"].Value<string>("Type") == "Event"));
            Assert.Contains(messages, m => m["message"].Value<string>("Method") == "RegisterPlugin");
        }

        [Fact]
        public async Task KeepsUnreadableMessagesAsText()
        {
            using var server = new FakeNizimaLiveServer();
            server.Start();
            using var client = new NizimaLiveClient(new NizimaLiveClientOptions { Uri = server.Uri }, new MemoryTokenStore("issued-token"));
            var recorder = new ProbeRecorder(client, outputDirectory);
            client.Start();
            await WaitUntil(() => client.State == NizimaConnectionState.Streaming);

            await server.SendRawAsync("not json");
            await WaitUntil(() => recorder.MessageCount >= 4);
            await client.StopAsync();
            JObject summary = recorder.Finish();

            JObject[] messages = File.ReadAllLines(Path.Combine(outputDirectory, "messages.jsonl")).Select(JObject.Parse).ToArray();
            Assert.Contains(messages, m => m["message"].Type == JTokenType.String && m.Value<string>("message") == "not json");
            Assert.Equal(0, summary["frames"].Value<int>("withModel"));
            Assert.Equal(JTokenType.Null, summary["rate"].Type);
        }

        public void Dispose()
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("Condition was not met in time.");
                }

                await Task.Delay(20);
            }
        }
    }
}
