using System;
using System.Collections.Generic;
using System.Linq;

namespace Ugokue.Core.Live
{
    /// <summary>
    /// 直近の遅延の測定値（ミリ秒）を決まった数だけ持ち、平均・p95・最大を求める。
    /// </summary>
    public sealed class LatencyMeter
    {
        private readonly Queue<double> samples = new Queue<double>();
        private readonly int capacity;

        public LatencyMeter(int capacity = 300)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            this.capacity = capacity;
        }

        /// <summary>
        /// 測定値を1つ加える。負の値と NaN は、時計のずれによるものとして捨てる。
        /// </summary>
        public void Add(double milliseconds)
        {
            if (double.IsNaN(milliseconds) || milliseconds < 0)
            {
                return;
            }

            samples.Enqueue(milliseconds);
            while (samples.Count > capacity)
            {
                samples.Dequeue();
            }
        }

        /// <summary>
        /// 直近の測定値の統計を返す。測定値がなければ null を返す。
        /// </summary>
        public LatencyStats Snapshot()
        {
            if (samples.Count == 0)
            {
                return null;
            }

            double[] sorted = samples.OrderBy(x => x).ToArray();
            int p95Index = (int)Math.Ceiling(0.95 * sorted.Length) - 1;
            return new LatencyStats(sorted.Length, sorted.Average(), sorted[Math.Max(0, p95Index)], sorted[sorted.Length - 1]);
        }
    }

    /// <summary>
    /// 遅延の統計（ミリ秒）。
    /// </summary>
    public sealed class LatencyStats
    {
        public LatencyStats(int count, double averageMs, double p95Ms, double maxMs)
        {
            Count = count;
            AverageMs = averageMs;
            P95Ms = p95Ms;
            MaxMs = maxMs;
        }

        public int Count { get; }

        public double AverageMs { get; }

        public double P95Ms { get; }

        public double MaxMs { get; }
    }
}
