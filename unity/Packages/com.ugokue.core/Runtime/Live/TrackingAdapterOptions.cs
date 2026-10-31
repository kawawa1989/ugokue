namespace Ugokue.Core.Live
{
    /// <summary>
    /// 入力アダプターの数値の設定。既定値の根拠は ADR 0006 にある。
    /// </summary>
    public sealed class TrackingAdapterOptions
    {
        /// <summary>
        /// 顔を捉えているかを表す値が、これ以上のフレームだけを有効とする。
        /// </summary>
        public double PresenceThreshold { get; set; } = 0.9;

        /// <summary>
        /// 最後の有効なフレームからこの秒数たったら、入力が途切れたとみなす。
        /// </summary>
        public double StaleSeconds { get; set; } = 0.2;

        /// <summary>
        /// 途切れてから、最後の有効な値を保持する秒数。
        /// </summary>
        public double HoldSeconds { get; set; } = 1.0;

        /// <summary>
        /// 保持のあと、パラメータの初期値へ戻すときの時定数（秒）。
        /// </summary>
        public double ReturnTimeConstant { get; set; } = 0.5;

        /// <summary>
        /// 入力が戻ってから、遅い平滑化をかける秒数。0 なら復帰の処理をしない。
        /// </summary>
        public double RecoverySeconds { get; set; } = 0.5;

        /// <summary>
        /// 入力が戻った直後の平滑化の時定数（秒）。
        /// </summary>
        public double RecoveryTimeConstant { get; set; } = 0.12;

        /// <summary>
        /// モデルに設定がないパラメータの平滑化の強さ。1/60 秒あたりに前の値を残す割合で、0 なら平滑化しない。
        /// </summary>
        public double DefaultSmoothing { get; set; } = 0.5;
    }
}
