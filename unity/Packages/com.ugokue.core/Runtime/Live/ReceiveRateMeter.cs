using System;
using System.Collections.Generic;
using System.Linq;

namespace Ugokue.Core.Live
{
    /// <summary>
    /// 直近のフレームの受信時刻から、毎秒の受信回数と受信間隔の統計を求める。
    /// </summary>
    public sealed class ReceiveRateMeter
    {
        private readonly Queue<double> timestamps = new Queue<double>();
        private readonly object gate = new object();

        public ReceiveRateMeter(double windowSeconds = 5)
        {
            if (!(windowSeconds > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(windowSeconds), windowSeconds, "Window must be positive.");
            }

            WindowSeconds = windowSeconds;
        }

        public double WindowSeconds { get; }

        /// <summary>
        /// 受信時刻（秒）を1つ加え、窓より古い時刻を捨てる。
        /// </summary>
        public void Add(double timestampSeconds)
        {
            lock (gate)
            {
                timestamps.Enqueue(timestampSeconds);
                while (timestamps.Count > 0 && timestamps.Peek() < timestampSeconds - WindowSeconds)
                {
                    timestamps.Dequeue();
                }
            }
        }

        /// <summary>
        /// 窓の中のフレームから求めた統計を返す。間隔が1つもなければ null を返す。
        /// </summary>
        public ReceiveRateStats Snapshot()
        {
            double[] times;
            lock (gate)
            {
                times = timestamps.ToArray();
            }

            if (times.Length < 2)
            {
                return null;
            }

            double[] intervals = new double[times.Length - 1];
            for (int i = 1; i < times.Length; i++)
            {
                intervals[i - 1] = (times[i] - times[i - 1]) * 1000;
            }

            double span = times[times.Length - 1] - times[0];
            double[] sorted = intervals.OrderBy(x => x).ToArray();
            int p95Index = (int)Math.Ceiling(0.95 * sorted.Length) - 1;
            return new ReceiveRateStats(
                intervals.Length / span,
                intervals.Average(),
                sorted[Math.Max(0, p95Index)],
                sorted[sorted.Length - 1]);
        }
    }

    /// <summary>
    /// 受信回数と受信間隔の統計。
    /// </summary>
    public sealed class ReceiveRateStats
    {
        public ReceiveRateStats(double framesPerSecond, double averageIntervalMs, double p95IntervalMs, double maxIntervalMs)
        {
            FramesPerSecond = framesPerSecond;
            AverageIntervalMs = averageIntervalMs;
            P95IntervalMs = p95IntervalMs;
            MaxIntervalMs = maxIntervalMs;
        }

        public double FramesPerSecond { get; }

        public double AverageIntervalMs { get; }

        public double P95IntervalMs { get; }

        public double MaxIntervalMs { get; }
    }
}
