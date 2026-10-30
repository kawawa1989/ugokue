using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// 1つのパラメータに対するトラッキング値の平滑化の設定を表す。
    /// </summary>
    public sealed class TrackingParameter
    {
        [JsonProperty("parameter", Order = 0, Required = Required.Always)]
        public string Parameter { get; set; }

        [JsonProperty("smoothing", Order = 1, Required = Required.Always)]
        public double Smoothing { get; set; }
    }
}
