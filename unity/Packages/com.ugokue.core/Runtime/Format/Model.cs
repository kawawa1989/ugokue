using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// 中間形式の model.json の最上位を表す。
    /// </summary>
    public sealed class Model
    {
        public const string FormatName = "ugokue-model";

        public const string CurrentVersion = "0.1.0";

        [JsonProperty("format", Order = 0, Required = Required.Always)]
        public string Format { get; set; } = FormatName;

        [JsonProperty("version", Order = 1, Required = Required.Always)]
        public string Version { get; set; } = CurrentVersion;

        [JsonProperty("canvas", Order = 2, Required = Required.Always)]
        public Canvas Canvas { get; set; } = new Canvas();

        [JsonProperty("parameters", Order = 3, Required = Required.Always)]
        public List<Parameter> Parameters { get; set; } = new List<Parameter>();

        [JsonProperty("parts", Order = 4, Required = Required.Always)]
        public List<Part> Parts { get; set; } = new List<Part>();

        [JsonProperty("meshes", Order = 5, Required = Required.Always)]
        public List<Mesh> Meshes { get; set; } = new List<Mesh>();

        [JsonProperty("views", Order = 6, Required = Required.Always)]
        public List<View> Views { get; set; } = new List<View>();

        [JsonProperty("correspondence", Order = 7, Required = Required.Always)]
        public Correspondence Correspondence { get; set; } = new Correspondence();

        [JsonProperty("deformation", Order = 8, Required = Required.Always)]
        public Deformation Deformation { get; set; } = new Deformation();

        [JsonProperty("physics", Order = 9, NullValueHandling = NullValueHandling.Ignore)]
        public Physics Physics { get; set; }

        [JsonProperty("tracking", Order = 10, NullValueHandling = NullValueHandling.Ignore)]
        public Tracking Tracking { get; set; }
    }
}
