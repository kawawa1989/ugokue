using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// パラメータに比例してパーツを回転・移動する、手続き的な動きを表す。
    /// </summary>
    public sealed class ProceduralMotion
    {
        [JsonProperty("type", Order = 0, Required = Required.Always)]
        public string Type { get; set; }

        [JsonProperty("parameter", Order = 1, Required = Required.Always)]
        public string Parameter { get; set; }

        [JsonProperty("part", Order = 2, Required = Required.Always)]
        public string Part { get; set; }

        [JsonProperty("degreesPerUnit", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
        public double? DegreesPerUnit { get; set; }

        [JsonProperty("offsetPerUnit", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
        public double[] OffsetPerUnit { get; set; }
    }
}
