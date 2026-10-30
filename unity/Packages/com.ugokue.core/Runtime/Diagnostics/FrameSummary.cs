using System;
using System.Collections.Generic;
using System.Linq;

namespace Ugokue.Core.Diagnostics
{
    /// <summary>
    /// 記録したフレーム群の集計値（平均 fps、フレーム時間の平均・パーセンタイル・最大）を表す。
    /// </summary>
    public sealed class FrameSummary
    {
        private FrameSummary(
            int frameCount,
            double durationSeconds,
            double averageFps,
            double averageFrameTimeMs,
            double p95FrameTimeMs,
            double p99FrameTimeMs,
            double maxFrameTimeMs,
            double? averageCpuTimeMs)
        {
            FrameCount = frameCount;
            DurationSeconds = durationSeconds;
            AverageFps = averageFps;
            AverageFrameTimeMs = averageFrameTimeMs;
            P95FrameTimeMs = p95FrameTimeMs;
            P99FrameTimeMs = p99FrameTimeMs;
            MaxFrameTimeMs = maxFrameTimeMs;
            AverageCpuTimeMs = averageCpuTimeMs;
        }

        public int FrameCount { get; }

        public double DurationSeconds { get; }

        public double AverageFps { get; }

        public double AverageFrameTimeMs { get; }

        public double P95FrameTimeMs { get; }

        public double P99FrameTimeMs { get; }

        public double MaxFrameTimeMs { get; }

        public double? AverageCpuTimeMs { get; }

        /// <summary>
        /// フレーム群から集計値を求める。
        /// </summary>
        public static FrameSummary From(IReadOnlyList<FrameSample> samples)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            if (samples.Count == 0)
            {
                throw new ArgumentException("At least one sample is required.", nameof(samples));
            }

            double[] frameTimes = samples.Select(s => s.FrameTimeMs).OrderBy(t => t).ToArray();
            double averageFrameTime = frameTimes.Average();
            double[] cpuTimes = samples.Where(s => s.CpuTimeMs.HasValue).Select(s => s.CpuTimeMs.Value).ToArray();

            return new FrameSummary(
                samples.Count,
                samples[samples.Count - 1].TimestampSeconds - samples[0].TimestampSeconds,
                averageFrameTime > 0 ? 1000.0 / averageFrameTime : 0,
                averageFrameTime,
                Percentile(frameTimes, 0.95),
                Percentile(frameTimes, 0.99),
                frameTimes[frameTimes.Length - 1],
                cpuTimes.Length > 0 ? cpuTimes.Average() : (double?)null);
        }

        private static double Percentile(double[] sortedValues, double ratio)
        {
            int rank = (int)Math.Ceiling(ratio * sortedValues.Length) - 1;
            return sortedValues[Math.Max(0, Math.Min(rank, sortedValues.Length - 1))];
        }
    }
}
