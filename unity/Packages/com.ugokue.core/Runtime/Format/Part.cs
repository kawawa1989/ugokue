using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// 角度をまたいで同じ ID で追跡する、意味を持つ部品を表す。
    /// </summary>
    public sealed class Part
    {
        [JsonProperty("id", Order = 0, Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("role", Order = 1, Required = Required.Always)]
        public string Role { get; set; }

        [JsonProperty("parent", Order = 2, Required = Required.AllowNull)]
        public string Parent { get; set; }

        [JsonProperty("anchor", Order = 3, Required = Required.Always)]
        public double[] Anchor { get; set; }

        [JsonProperty("drawOrder", Order = 4, Required = Required.Always)]
        public int DrawOrder { get; set; }

        [JsonProperty("mesh", Order = 5, Required = Required.AllowNull)]
        public string Mesh { get; set; }

        [JsonProperty("clipBy", Order = 6, Required = Required.Always)]
        public List<string> ClipBy { get; set; } = new List<string>();
    }
}
