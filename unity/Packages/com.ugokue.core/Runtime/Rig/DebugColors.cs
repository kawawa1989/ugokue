using System;

namespace Ugokue.Core.Rig
{
    /// <summary>
    /// マスクと描画順を確かめるデバッグ表示の色。
    /// </summary>
    public static class DebugColors
    {
        /// <summary>
        /// 切り抜かれて見えなくなる部分を示す色（RGBA）。
        /// </summary>
        public static readonly byte[] ClippedAway = { 255, 0, 255, 90 };

        /// <summary>
        /// 描画リストの順番から、奥（青）から手前（赤）へ色相が変わる不透明な色（RGB）を返す。
        /// </summary>
        public static byte[] ForRank(int rank, int count)
        {
            double t = count > 1 ? (double)rank / (count - 1) : 0;
            double hue = (1 - t) * 240;
            return HueToRgb(hue);
        }

        private static byte[] HueToRgb(double hue)
        {
            double h = hue / 60;
            int sector = Math.Min(5, (int)Math.Floor(h));
            double f = h - sector;
            double r, g, b;
            switch (sector)
            {
                case 0: r = 1; g = f; b = 0; break;
                case 1: r = 1 - f; g = 1; b = 0; break;
                case 2: r = 0; g = 1; b = f; break;
                case 3: r = 0; g = 1 - f; b = 1; break;
                case 4: r = f; g = 0; b = 1; break;
                default: r = 1; g = 0; b = 1 - f; break;
            }

            return new[] { ToByte(r), ToByte(g), ToByte(b) };
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Round(value * 255);
        }
    }
}
