using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// 入力パラメータの変化で揺れ、その振れを出力パラメータに書き込む振り子を表す。
    /// </summary>
    public sealed class Pendulum
    {
        [JsonProperty("id", Order = 0, Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("inputs", Order = 1, Required = Required.Always)]
        public List<PendulumInput> Inputs { get; set; } = new List<PendulumInput>();

        [JsonProperty("output", Order = 2, Required = Required.Always)]
        public string Output { get; set; }

        [JsonProperty("stiffness", Order = 3, Required = Required.Always)]
        public double Stiffness { get; set; }

        [JsonProperty("damping", Order = 4, Required = Required.Always)]
        public double Damping { get; set; }

        [JsonProperty("scale", Order = 5, Required = Required.Always)]
        public double Scale { get; set; }
    }
}
