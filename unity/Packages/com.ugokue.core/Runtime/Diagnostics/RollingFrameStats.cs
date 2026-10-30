using System;

namespace Ugokue.Core.Diagnostics
{
    /// <summary>
    /// 直近の一定フレーム数について、fps とフレーム時間の移動平均を求める。
    /// </summary>
    public sealed class RollingFrameStats
    {
        private readonly double[] frameTimes;
        private int count;
        private int next;
        private double sum;

        public RollingFrameStats(int windowSize)
        {
            if (windowSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(windowSize), windowSize, "Window size must be positive.");
            }

            frameTimes = new double[windowSize];
        }

        public double AverageFrameTimeMs => count == 0 ? 0 : sum / count;

        public double AverageFps => AverageFrameTimeMs > 0 ? 1000.0 / AverageFrameTimeMs : 0;

        /// <summary>
        /// 1フレーム分のフレーム時間を追加し、最も古い値を範囲から外す。
        /// </summary>
        public void Add(double frameTimeMs)
        {
            if (count == frameTimes.Length)
            {
                sum -= frameTimes[next];
            }
            else
            {
                count++;
            }

            frameTimes[next] = frameTimeMs;
            sum += frameTimeMs;
            next = (next + 1) % frameTimes.Length;
        }
    }
}
