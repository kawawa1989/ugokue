using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// 振り子による揺れの設定をまとめて持つ。
    /// </summary>
    public sealed class Physics
    {
        [JsonProperty("pendulums", Order = 0, Required = Required.Always)]
        public List<Pendulum> Pendulums { get; set; } = new List<Pendulum>();
    }
}
