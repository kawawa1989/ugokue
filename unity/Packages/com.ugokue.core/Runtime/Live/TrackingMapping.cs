namespace Ugokue.Core.Live
{
    /// <summary>
    /// 入力元のパラメータ1つを、モデルのパラメータ1つに変換する規則。入力の範囲を出力の範囲に線形に置き換える。
    /// </summary>
    public sealed class TrackingMapping
    {
        public TrackingMapping(string source, string target, double inMin, double inMax, double outMin, double outMax, bool calibrate = false)
        {
            Source = source;
            Target = target;
            InMin = inMin;
            InMax = inMax;
            OutMin = outMin;
            OutMax = outMax;
            Calibrate = calibrate;
        }

        public string Source { get; }

        public string Target { get; }

        public double InMin { get; }

        public double InMax { get; }

        public double OutMin { get; }

        public double OutMax { get; }

        /// <summary>
        /// 校正の対象かどうか。対象なら、校正したときの値を差し引いてから変換する。
        /// </summary>
        public bool Calibrate { get; }

        /// <summary>
        /// 入力の値を出力の値に置き換える。範囲の外の値は、同じ傾きで外へ延ばす。
        /// </summary>
        public double Map(double value)
        {
            if (InMax == InMin)
            {
                return OutMin;
            }

            return OutMin + (value - InMin) / (InMax - InMin) * (OutMax - OutMin);
        }
    }
}
