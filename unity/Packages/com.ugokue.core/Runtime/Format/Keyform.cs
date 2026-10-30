using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// 1つの格子点での、基準の形からの各頂点の移動量を表す。
    /// </summary>
    public sealed class Keyform
    {
        [JsonProperty("at", Order = 0, Required = Required.Always)]
        public double[] At { get; set; }

        [JsonProperty("offsets", Order = 1, Required = Required.Always)]
        public double[][] Offsets { get; set; }
    }
}
