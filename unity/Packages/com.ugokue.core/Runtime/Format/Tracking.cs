using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// トラッキング値を受け取ったときの、モデルごとの調整をまとめて持つ。
    /// </summary>
    public sealed class Tracking
    {
        [JsonProperty("parameters", Order = 0, Required = Required.Always)]
        public List<TrackingParameter> Parameters { get; set; } = new List<TrackingParameter>();
    }
}
