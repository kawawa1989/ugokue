using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Ugokue.Core.Live.Nizima;
using Xunit;

namespace Ugokue.Core.Tests.Live
{
    public class NizimaLiveClientTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task FirstConnectionRegistersSavesTokenAndStreamsAfterEnable()
        {
            using var server = StartServer();
            var tokens = new MemoryTokenStore();
            using var client = StartClient(server, tokens);

            await WaitUntil(() => client.State == NizimaConnectionState.WaitingForEnable);
            Assert.True(server.HasRequest("RegisterPlugin"));
            Assert.False(server.HasRequest("EstablishConnection"));
            Assert.Equal("issued-token", tokens.Load());

            await server.SendEventAsync("NotifyEnabledChanged", new JObject { ["Enabled"] = true });
            await WaitUntil(() => client.State == NizimaConnectionState.Streaming && client.LiveParameters.Count == 2);
            JObject enable = server.Requests.Last(r => r.Value<string>("Method") == "NotifyFrameUpdated");
            Assert.True(enable["Data"].Value<bool>("Enabled"));

            await server.SendEventAsync("NotifyFrameUpdated", Frame(("AngleXLive", 12.5), ("EyeOpenLeft", 0.75)));
            await WaitUntil(() => client.LatestFrame != null);
            Assert.Equal(12.5, client.LatestFrame.Values["AngleXLive"]);
            Assert.Equal(0.75, client.LatestFrame.Values["EyeOpenLeft"]);
            Assert.True(client.LastFrameHadModel);
        }

        [Fact]
        public async Task LaterConnectionUsesSavedToken()
        {
            using var server = StartServer();
            using var client = StartClient(server, new MemoryTokenStore("issued-token"));

            await WaitUntil(() => client.State == NizimaConnectionState.Streaming);

            Assert.True(server.HasRequest("EstablishConnection"));
            Assert.False(server.HasRequest("RegisterPlugin"));
        }

        [Fact]
        public async Task WaitsForEnableWhenPluginIsDisabled()
        {
            using var server = StartServer();
            server.EnabledOnEstablish = false;
            using var client = StartClient(server, new MemoryTokenStore("issued-token"));

            await WaitUntil(() => client.State == NizimaConnectionState.WaitingForEnable);
            Assert.False(server.HasRequest("NotifyFrameUpdated"));

            await server.SendEventAsync("NotifyEnabledChanged", new JObject { ["Enabled"] = true });
            await WaitUntil(() => client.State == NizimaConnectionState.Streaming);
        }

        [Fact]
        public async Task InvalidTokenRegistersAgain()
        {
            using var server = StartServer();
            var tokens = new MemoryTokenStore("removed-token");
            using var client = StartClient(server, tokens);

            await WaitUntil(() => server.HasRequest("RegisterPlugin") && tokens.Load() == "issued-token");

            Assert.True(server.HasRequest("EstablishConnection"));
            await WaitUntil(() => client.State == NizimaConnectionState.WaitingForEnable);
        }

        [Fact]
        public async Task ReconnectsAfterNizimaLiveRestarts()
        {
            using var server = StartServer();
            using var client = StartClient(server, new MemoryTokenStore("issued-token"));
            await WaitUntil(() => client.State == NizimaConnectionState.Streaming);

            server.Stop();
            await WaitUntil(() => client.State != NizimaConnectionState.Streaming);
            server.Start();

            await WaitUntil(() => client.State == NizimaConnectionState.Streaming && client.ConnectionCount == 2);
            await server.SendEventAsync("NotifyFrameUpdated", Frame(("AngleXLive", -3.0)));
            await WaitUntil(() => client.LatestFrame != null && client.LatestFrame.Values["AngleXLive"] == -3.0);
        }

        [Fact]
        public async Task KeepsRetryingUntilNizimaLiveStarts()
        {
            using var server = new FakeNizimaLiveServer(FreePort());
            using var client = StartClient(server, new MemoryTokenStore("issued-token"));

            await WaitUntil(() => client.LastError != null);
            Assert.NotEqual(NizimaConnectionState.Streaming, client.State);

            server.Start();
            await WaitUntil(() => client.State == NizimaConnectionState.Streaming);
        }

        [Fact]
        public async Task FrameWithoutModelsKeepsPreviousValues()
        {
            using var server = StartServer();
            using var client = StartClient(server, new MemoryTokenStore("issued-token"));
            await WaitUntil(() => client.State == NizimaConnectionState.Streaming);

            await server.SendEventAsync("NotifyFrameUpdated", new JObject { ["Models"] = new JArray() });
            await WaitUntil(() => server.Requests.Count >= 3);
            await Task.Delay(200);

            Assert.False(client.LastFrameHadModel);
            Assert.Null(client.LatestFrame);
        }

        [Fact]
        public async Task IgnoresMalformedMessages()
        {
            using var server = StartServer();
            using var client = StartClient(server, new MemoryTokenStore("issued-token"));
            await WaitUntil(() => client.State == NizimaConnectionState.Streaming);

            await server.SendRawAsync("not json");
            await server.SendRawAsync("{\"Type\":\"Event\"}");
            await server.SendEventAsync("NotifyFrameUpdated", Frame(("AngleXLive", 1.0)));

            await WaitUntil(() => client.LatestFrame != null);
            Assert.Equal(NizimaConnectionState.Streaming, client.State);
        }

        [Fact]
        public async Task StopEndsTheConnection()
        {
            using var server = StartServer();
            var client = StartClient(server, new MemoryTokenStore("issued-token"));
            await WaitUntil(() => client.State == NizimaConnectionState.Streaming);

            await client.StopAsync();

            Assert.Equal(NizimaConnectionState.Disconnected, client.State);
            client.Dispose();
        }

        [Fact]
        public void DefaultsToIpv4LoopbackBecauseNizimaLiveListensOnlyThere()
        {
            Assert.Equal(new Uri("ws://127.0.0.1:22022/"), new NizimaLiveClientOptions().Uri);
            Assert.Equal(new Uri("ws://127.0.0.1:23000/"), NizimaLiveClientOptions.LocalUri(23000));
        }

        internal static JObject Frame(params (string Id, double Value)[] values)
        {
            return new JObject
            {
                ["Models"] = new JArray
                {
                    new JObject
                    {
                        ["ModelId"] = "1",
                        ["LiveParameterValues"] = new JArray(values.Select(v => new JObject { ["Id"] = v.Id, ["Value"] = v.Value })),
                        ["CubismParameterValues"] = new JArray(),
                    },
                },
            };
        }

        private static FakeNizimaLiveServer StartServer()
        {
            var server = new FakeNizimaLiveServer
            {
                LiveParameters = new JArray
                {
                    new JObject { ["Id"] = "AngleXLive", ["CreatedBy"] = "nizima LIVE", ["Group"] = "Face", ["Base"] = 0, ["Min"] = -30, ["Max"] = 30, ["Repeat"] = false },
                    new JObject { ["Id"] = "EyeOpenLeft", ["CreatedBy"] = "nizima LIVE", ["Group"] = "Eye", ["Base"] = 1, ["Min"] = 0, ["Max"] = 1, ["Repeat"] = false },
                },
            };
            server.Start();
            return server;
        }

        private static NizimaLiveClient StartClient(FakeNizimaLiveServer server, ITokenStore tokens)
        {
            var client = new NizimaLiveClient(
                new NizimaLiveClientOptions
                {
                    Uri = server.Uri,
                    MinReconnectDelay = TimeSpan.FromMilliseconds(50),
                    MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                },
                tokens);
            client.Start();
            return client;
        }

        private static int FreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow + Timeout;
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
