using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// 角度ごとの絵に対応し、その角度で基準から変えるパーツの設定を持つ。
    /// </summary>
    public sealed class View
    {
        [JsonProperty("id", Order = 0, Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("yaw", Order = 1, Required = Required.Always)]
        public double Yaw { get; set; }

        [JsonProperty("pitch", Order = 2, Required = Required.Always)]
        public double Pitch { get; set; }

        [JsonProperty("overrides", Order = 3, Required = Required.Always)]
        public List<ViewOverride> Overrides { get; set; } = new List<ViewOverride>();
    }
}
