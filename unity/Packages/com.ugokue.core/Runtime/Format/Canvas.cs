using Newtonsoft.Json;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// キャンバスの大きさ（ピクセル）を表す。
    /// </summary>
    public sealed class Canvas
    {
        [JsonProperty("width", Order = 0, Required = Required.Always)]
        public int Width { get; set; }

        [JsonProperty("height", Order = 1, Required = Required.Always)]
        public int Height { get; set; }
    }
}
