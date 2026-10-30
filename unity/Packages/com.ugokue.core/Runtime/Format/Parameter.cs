using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// モデルを動かす入力値の定義を表す。
    /// </summary>
    public sealed class Parameter
    {
        [JsonProperty("id", Order = 0, Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("min", Order = 1, Required = Required.Always)]
        public double Min { get; set; }

        [JsonProperty("max", Order = 2, Required = Required.Always)]
        public double Max { get; set; }

        [JsonProperty("default", Order = 3, Required = Required.Always)]
        public double Default { get; set; }
    }
}
