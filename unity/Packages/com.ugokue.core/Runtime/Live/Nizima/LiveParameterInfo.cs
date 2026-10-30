namespace Ugokue.Core.Live.Nizima
{
    /// <summary>
    /// nizima LIVE の LiveParameter の定義（GetLiveParameters で取得する）。
    /// </summary>
    public sealed class LiveParameterInfo
    {
        public LiveParameterInfo(string id, string createdBy, string group, double min, double max, double baseValue, bool repeat)
        {
            Id = id;
            CreatedBy = createdBy;
            Group = group;
            Min = min;
            Max = max;
            Base = baseValue;
            Repeat = repeat;
        }

        public string Id { get; }

        public string CreatedBy { get; }

        public string Group { get; }

        public double Min { get; }

        public double Max { get; }

        public double Base { get; }

        /// <summary>
        /// 最小値と最大値がつながる（角度など）とき true。
        /// </summary>
        public bool Repeat { get; }
    }
}
