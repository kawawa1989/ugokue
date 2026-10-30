using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ugokue.Core.Live.Nizima;
using Xunit;

namespace Ugokue.Core.Tests.Live
{
    /// <summary>
    /// 実機の nizima LIVE（Plugin API 1.2.0）から記録したメッセージを読み取るテスト。
    /// </summary>
    public class RecordedNizimaMessageTests
    {
        [Fact]
        public void ReadsRegisterPluginResponse()
        {
            NizimaMessage message = Load("register-plugin-response");

            Assert.True(message.IsResponse);
            Assert.Equal(NizimaProtocol.RegisterPlugin, message.Method);
            Assert.Equal("ugokue-1", message.Id);
            Assert.Equal("REDACTED-TOKEN", message.Data.Value<string>("Token"));
        }

        [Fact]
        public void ReadsEnableEventAndEstablishResponse()
        {
            NizimaMessage enabled = Load("notify-enabled-changed-event");
            NizimaMessage established = Load("establish-connection-response");

            Assert.True(enabled.IsEvent);
            Assert.Equal(NizimaProtocol.NotifyEnabledChanged, enabled.Method);
            Assert.True(enabled.Data.Value<bool>("Enabled"));
            Assert.True(established.IsResponse);
            Assert.True(established.Data.Value<bool>("Enabled"));
        }

        [Fact]
        public void ReadsAllLiveParameterDefinitions()
        {
            List<LiveParameterInfo> parameters = NizimaProtocol.ReadLiveParameters(Load("get-live-parameters-response").Data);

            Assert.Equal(165, parameters.Count);
            Assert.Equal(165, parameters.Select(p => p.Id).Distinct().Count());
            Assert.Equal(
                new[] { "ARKit", "LipSync", "mocopi", "nizima LIVE" },
                parameters.Select(p => p.CreatedBy).Distinct().OrderBy(c => c, System.StringComparer.Ordinal));
            LiveParameterInfo yaw = parameters.Single(p => p.Id == "Yaw");
            Assert.Equal(("Face", -30.0, 30.0, 0.0, false), (yaw.Group, yaw.Min, yaw.Max, yaw.Base, yaw.Repeat));
            LiveParameterInfo eyeOpen = parameters.Single(p => p.Id == "EyeOpenLeft");
            Assert.Equal((0.0, 2.0, 1.0), (eyeOpen.Min, eyeOpen.Max, eyeOpen.Base));
            Assert.True(parameters.Single(p => p.Id == "HandLeftAngleX").Repeat);
        }

        [Fact]
        public void ReadsFrameWithTrackedFace()
        {
            NizimaMessage message = Load("frame-face-found");

            Dictionary<string, double> values = NizimaProtocol.ReadLiveParameterValues(message.Data);

            Assert.True(message.IsEvent);
            Assert.Equal(165, values.Count);
            Assert.InRange(values["FaceFound"], 0.9, 1.1);
            Assert.Equal(-39.76066589355469, values["Yaw"], 10);
            Assert.Equal(19.974027633666992, values["Roll"], 10);
            Assert.Equal(0.7257999777793884, values["EyeOpenLeft"], 10);
            Assert.DoesNotContain("ParamEyeLOpen", values.Keys);
        }

        [Fact]
        public void ValuesCanExceedTheDeclaredRange()
        {
            Dictionary<string, double> values = NizimaProtocol.ReadLiveParameterValues(Load("frame-face-found").Data);

            Assert.True(values["Yaw"] < -30);
        }

        [Fact]
        public void ReadsFrameWithoutTrackedFaceAsBaseValues()
        {
            Dictionary<string, double> values = NizimaProtocol.ReadLiveParameterValues(Load("frame-face-lost").Data);

            Assert.Equal(165, values.Count);
            Assert.Equal(0, values["FaceFound"]);
            Assert.Equal(0, values["Yaw"]);
            Assert.Equal(1, values["EyeOpenLeft"]);
        }

        [Fact]
        public void FramesNameTheLoadedModel()
        {
            NizimaMessage message = Load("frame-face-found");

            Assert.Equal("10000", message.Data["Models"][0].Value<string>("ModelId"));
            Assert.NotNull(NizimaProtocol.ReadLiveParameterValues(message.Data, "10000"));
            Assert.Null(NizimaProtocol.ReadLiveParameterValues(message.Data, "1"));
        }

        private static NizimaMessage Load(string name)
        {
            string path = Path.Combine(TestPaths.RepositoryRoot, "dotnet", "Ugokue.Core.Tests", "Live", "Fixtures", name + ".json");
            NizimaMessage message = NizimaProtocol.Parse(File.ReadAllText(path));
            Assert.NotNull(message);
            return message;
        }
    }
}
