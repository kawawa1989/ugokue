using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Ugokue.Core.Live.Nizima;
using Xunit;

namespace Ugokue.Core.Tests.Live
{
    public class NizimaProtocolTests
    {
        [Fact]
        public void BuildsRequestWithApiVersionIdAndData()
        {
            JObject request = JObject.Parse(NizimaProtocol.Request("RegisterPlugin", "ugokue-1", new JObject { ["Name"] = "ugokue" }));

            Assert.Equal("1.0.0", request.Value<string>("nLPlugin"));
            Assert.Equal("Request", request.Value<string>("Type"));
            Assert.Equal("RegisterPlugin", request.Value<string>("Method"));
            Assert.Equal("ugokue-1", request.Value<string>("Id"));
            Assert.Equal("ugokue", request["Data"].Value<string>("Name"));
            Assert.True(request.Value<long>("Timestamp") > 0);
        }

        [Fact]
        public void ParsesErrorType()
        {
            NizimaMessage message = NizimaProtocol.Parse(
                "{\"nLPlugin\":\"1.0.0\",\"Type\":\"Error\",\"Method\":\"EstablishConnection\",\"Data\":{\"ErrorType\":\"InvalidToken\"}}");

            Assert.True(message.IsError);
            Assert.Equal("InvalidToken", message.ErrorType);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{\"Type\":\"Event\"}")]
        [InlineData("[]")]
        public void ReturnsNullForUnreadableMessages(string json)
        {
            Assert.Null(NizimaProtocol.Parse(json));
        }

        [Fact]
        public void ReadsValuesOfFirstOrSelectedModel()
        {
            var data = new JObject
            {
                ["Models"] = new JArray
                {
                    Model("1", ("A", 1), ("B", 2.5)),
                    Model("7", ("A", -4)),
                },
            };

            Dictionary<string, double> first = NizimaProtocol.ReadLiveParameterValues(data);
            Dictionary<string, double> selected = NizimaProtocol.ReadLiveParameterValues(data, "7");

            Assert.Equal(new Dictionary<string, double> { ["A"] = 1, ["B"] = 2.5 }, first);
            Assert.Equal(new Dictionary<string, double> { ["A"] = -4 }, selected);
            Assert.Null(NizimaProtocol.ReadLiveParameterValues(data, "9"));
        }

        [Fact]
        public void ReturnsNullWhenNoModelIsLoaded()
        {
            Assert.Null(NizimaProtocol.ReadLiveParameterValues(new JObject { ["Models"] = new JArray() }));
            Assert.Null(NizimaProtocol.ReadLiveParameterValues(new JObject()));
        }

        [Fact]
        public void SkipsValuesThatAreNotNumbers()
        {
            JObject model = Model("1", ("A", 1));
            ((JArray)model["LiveParameterValues"]).Add(new JObject { ["Id"] = "B", ["Value"] = "x" });
            ((JArray)model["LiveParameterValues"]).Add(new JObject { ["Value"] = 3 });

            Dictionary<string, double> values = NizimaProtocol.ReadLiveParameterValues(new JObject { ["Models"] = new JArray { model } });

            Assert.Equal(new Dictionary<string, double> { ["A"] = 1 }, values);
        }

        [Fact]
        public void ReadsLiveParameterDefinitions()
        {
            var data = new JObject
            {
                ["LiveParameters"] = new JArray
                {
                    new JObject { ["Id"] = "HandAngle", ["CreatedBy"] = "nizima LIVE", ["Group"] = "Hand", ["Base"] = 0, ["Min"] = -180, ["Max"] = 180, ["Repeat"] = true },
                    new JObject { ["CreatedBy"] = "missing id" },
                },
            };

            List<LiveParameterInfo> parameters = NizimaProtocol.ReadLiveParameters(data);

            LiveParameterInfo parameter = Assert.Single(parameters);
            Assert.Equal(("HandAngle", "nizima LIVE", "Hand", -180.0, 180.0, 0.0, true),
                (parameter.Id, parameter.CreatedBy, parameter.Group, parameter.Min, parameter.Max, parameter.Base, parameter.Repeat));
        }

        private static JObject Model(string id, params (string Id, double Value)[] values)
        {
            var parameters = new JArray();
            foreach ((string parameterId, double value) in values)
            {
                parameters.Add(new JObject { ["Id"] = parameterId, ["Value"] = value });
            }

            return new JObject { ["ModelId"] = id, ["LiveParameterValues"] = parameters, ["CubismParameterValues"] = new JArray() };
        }
    }
}
