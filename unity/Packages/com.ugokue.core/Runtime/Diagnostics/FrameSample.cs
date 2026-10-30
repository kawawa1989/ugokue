namespace Ugokue.Core.Diagnostics
{
    /// <summary>
    /// 1フレーム分の計測値を表す。
    /// </summary>
    public readonly struct FrameSample
    {
        public FrameSample(int index, double timestampSeconds, double frameTimeMs, double? cpuTimeMs)
        {
            Index = index;
            TimestampSeconds = timestampSeconds;
            FrameTimeMs = frameTimeMs;
            CpuTimeMs = cpuTimeMs;
        }

        public int Index { get; }

        public double TimestampSeconds { get; }

        public double FrameTimeMs { get; }

        public double? CpuTimeMs { get; }
    }
}
