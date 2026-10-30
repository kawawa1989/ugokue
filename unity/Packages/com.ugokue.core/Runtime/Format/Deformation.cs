using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// キーフォームによる変形と手続き的な動きをまとめて持つ。
    /// </summary>
    public sealed class Deformation
    {
        [JsonProperty("keyformGroups", Order = 0, Required = Required.Always)]
        public List<KeyformGroup> KeyformGroups { get; set; } = new List<KeyformGroup>();

        [JsonProperty("procedural", Order = 1, Required = Required.Always)]
        public List<ProceduralMotion> Procedural { get; set; } = new List<ProceduralMotion>();
    }
}
