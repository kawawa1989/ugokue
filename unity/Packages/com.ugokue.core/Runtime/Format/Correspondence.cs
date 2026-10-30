using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// 角度をまたいだ特徴点と頂点の対応づけを表す。
    /// </summary>
    public sealed class Correspondence
    {
        [JsonProperty("landmarks", Order = 0, Required = Required.Always)]
        public List<Landmark> Landmarks { get; set; } = new List<Landmark>();

        [JsonProperty("vertexMatches", Order = 1, Required = Required.Always)]
        public List<VertexMatch> VertexMatches { get; set; } = new List<VertexMatch>();
    }
}
