using System.Diagnostics;

namespace Ugokue.Core.Live
{
    /// <summary>
    /// トラッキングの時刻を測る、単調増加する時計。受信の時刻と、値を使う側の時刻をこの時計でそろえる。
    /// </summary>
    public static class TrackingClock
    {
        private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

        /// <summary>
        /// 時計を作ってからの経過秒数。
        /// </summary>
        public static double Now => Stopwatch.Elapsed.TotalSeconds;
    }
}
