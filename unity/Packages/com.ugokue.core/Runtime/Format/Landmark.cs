using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// ある特徴点が、あるビューの絵のどこにあるかを表す。
    /// </summary>
    public sealed class Landmark
    {
        [JsonProperty("id", Order = 0, Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("part", Order = 1, Required = Required.Always)]
        public string Part { get; set; }

        [JsonProperty("view", Order = 2, Required = Required.Always)]
        public string View { get; set; }

        [JsonProperty("position", Order = 3, Required = Required.Always)]
        public double[] Position { get; set; }

        [JsonProperty("confidence", Order = 4, Required = Required.Always)]
        public double Confidence { get; set; }

        [JsonProperty("source", Order = 5, Required = Required.Always)]
        public string Source { get; set; }
    }
}
