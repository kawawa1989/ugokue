using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// あるメッシュの頂点が、あるビューの絵のどこに対応するかを表す。
    /// </summary>
    public sealed class VertexMatch
    {
        [JsonProperty("mesh", Order = 0, Required = Required.Always)]
        public string Mesh { get; set; }

        [JsonProperty("vertex", Order = 1, Required = Required.Always)]
        public int Vertex { get; set; }

        [JsonProperty("view", Order = 2, Required = Required.Always)]
        public string View { get; set; }

        [JsonProperty("position", Order = 3, Required = Required.Always)]
        public double[] Position { get; set; }

        [JsonProperty("confidence", Order = 4, Required = Required.Always)]
        public double Confidence { get; set; }

        [JsonProperty("source", Order = 5, Required = Required.Always)]
        public string Source { get; set; }
    }
}
