namespace Ugokue.Core.Live
{
    /// <summary>
    /// トラッキング値の入力元。入力元を差し替えられるよう、最新のフレームだけを渡す。
    /// </summary>
    public interface ITrackingSource
    {
        /// <summary>
        /// 最後に受け取ったフレーム。まだなければ null。新しいフレームは毎回別のオブジェクトになる。
        /// </summary>
        TrackingFrame LatestFrame { get; }
    }
}
