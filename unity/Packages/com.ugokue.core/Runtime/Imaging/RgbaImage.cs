using System;

namespace Ugokue.Core.Imaging
{
    /// <summary>
    /// 8 ビットのストレートアルファの RGBA 画像。画素は左上から行ごとに並ぶ。
    /// </summary>
    public sealed class RgbaImage
    {
        public RgbaImage(int width, int height)
            : this(width, height, new byte[checked(width * height * 4)])
        {
        }

        public RgbaImage(int width, int height, byte[] pixels)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Image size must be positive.");
            }

            if (pixels == null || pixels.Length != width * height * 4)
            {
                throw new ArgumentException("Pixel buffer size does not match the image size.", nameof(pixels));
            }

            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }

        public int Height { get; }

        public byte[] Pixels { get; }
    }
}
