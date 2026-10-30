using System;
using System.Collections.Generic;

namespace Ugokue.Core.Diagnostics
{
    /// <summary>
    /// ウォームアップ期間を除いて、指定した秒数のあいだフレームごとの計測値を記録する。
    /// </summary>
    public sealed class FrameRecorder
    {
        private readonly List<FrameSample> samples = new List<FrameSample>();
        private double? firstTimestampSeconds;
        private double startTimestampSeconds;

        public FrameRecorder(double durationSeconds, double warmupSeconds = 0)
        {
            if (!(durationSeconds > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "Duration must be positive.");
            }

            if (!(warmupSeconds >= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(warmupSeconds), warmupSeconds, "Warmup must not be negative.");
            }

            DurationSeconds = durationSeconds;
            WarmupSeconds = warmupSeconds;
        }

        public double DurationSeconds { get; }

        public double WarmupSeconds { get; }

        public bool IsComplete { get; private set; }

        public IReadOnlyList<FrameSample> Samples => samples;

        /// <summary>
        /// 1フレーム分の計測値を追加し、記録を続けるかどうかを返す。
        /// </summary>
        public bool Add(double timestampSeconds, double frameTimeMs, double? cpuTimeMs)
        {
            if (IsComplete)
            {
                return false;
            }

            firstTimestampSeconds ??= timestampSeconds;
            if (samples.Count == 0)
            {
                if (timestampSeconds - firstTimestampSeconds.Value < WarmupSeconds)
                {
                    return true;
                }

                startTimestampSeconds = timestampSeconds;
            }

            double elapsed = timestampSeconds - startTimestampSeconds;
            samples.Add(new FrameSample(samples.Count, elapsed, frameTimeMs, cpuTimeMs));

            if (elapsed >= DurationSeconds)
            {
                IsComplete = true;
            }

            return !IsComplete;
        }
    }
}
