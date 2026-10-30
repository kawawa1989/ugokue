using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// テクスチャを貼る三角形メッシュを表す。
    /// </summary>
    public sealed class Mesh
    {
        [JsonProperty("id", Order = 0, Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("part", Order = 1, Required = Required.Always)]
        public string Part { get; set; }

        [JsonProperty("texture", Order = 2, Required = Required.Always)]
        public string Texture { get; set; }

        [JsonProperty("vertices", Order = 3, Required = Required.Always)]
        public double[][] Vertices { get; set; }

        [JsonProperty("uvs", Order = 4, Required = Required.Always)]
        public double[][] Uvs { get; set; }

        [JsonProperty("triangles", Order = 5, Required = Required.Always)]
        public int[][] Triangles { get; set; }
    }
}
