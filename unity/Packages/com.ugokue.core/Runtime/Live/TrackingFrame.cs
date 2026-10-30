using System;
using System.Collections.Generic;

namespace Ugokue.Core.Live
{
    /// <summary>
    /// 入力元から1回に届いたトラッキング値。入力元のパラメータ名と値の組を持つ。
    /// </summary>
    public sealed class TrackingFrame
    {
        public TrackingFrame(double receivedAtSeconds, IReadOnlyDictionary<string, double> values, long? sourceTimestampUnixMs = null)
        {
            ReceivedAtSeconds = receivedAtSeconds;
            Values = values ?? throw new ArgumentNullException(nameof(values));
            SourceTimestampUnixMs = sourceTimestampUnixMs;
        }

        /// <summary>
        /// 受け取った時刻（秒）。<see cref="TrackingClock"/> の値で、フレームどうしの間隔を測るのに使う。
        /// </summary>
        public double ReceivedAtSeconds { get; }

        public IReadOnlyDictionary<string, double> Values { get; }

        /// <summary>
        /// 入力元がメッセージを送った時刻（UNIX 時間のミリ秒）。入力元が時刻を付けなければ null。
        /// </summary>
        public long? SourceTimestampUnixMs { get; }
    }
}
