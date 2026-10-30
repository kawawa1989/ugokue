using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// ビューで基準から変える、1つのパーツのメッシュ、表示、描画順を表す。
    /// </summary>
    public sealed class ViewOverride
    {
        [JsonProperty("part", Order = 0, Required = Required.Always)]
        public string Part { get; set; }

        [JsonProperty("mesh", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
        public string Mesh { get; set; }

        [JsonProperty("visible", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
        public bool? Visible { get; set; }

        [JsonProperty("drawOrder", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
        public int? DrawOrder { get; set; }
    }
}
