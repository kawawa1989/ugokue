using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// 振り子を揺らす入力パラメータとその重みを表す。
    /// </summary>
    public sealed class PendulumInput
    {
        [JsonProperty("parameter", Order = 0, Required = Required.Always)]
        public string Parameter { get; set; }

        [JsonProperty("weight", Order = 1, Required = Required.Always)]
        public double Weight { get; set; }
    }
}
