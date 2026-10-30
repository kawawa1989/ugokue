using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// 1つのメッシュについて、パラメータの格子点ごとの頂点の移動量を持つ。
    /// </summary>
    public sealed class KeyformGroup
    {
        [JsonProperty("id", Order = 0, Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("mesh", Order = 1, Required = Required.Always)]
        public string Mesh { get; set; }

        [JsonProperty("parameters", Order = 2, Required = Required.Always)]
        public List<string> Parameters { get; set; } = new List<string>();

        [JsonProperty("keys", Order = 3, Required = Required.Always)]
        public double[][] Keys { get; set; }

        [JsonProperty("interpolation", Order = 4, Required = Required.Always)]
        public string Interpolation { get; set; } = "linear";

        [JsonProperty("keyforms", Order = 5, Required = Required.Always)]
        public List<Keyform> Keyforms { get; set; } = new List<Keyform>();
    }
}
