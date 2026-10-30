using System;
using System.IO;
using System.IO.Compression;
using Ugokue.Core.Format;
using Ugokue.Core.Imaging;
using Xunit;

namespace Ugokue.Core.Tests.Imaging
{
    public class PngTests
    {
        [Fact]
        public void EncodeThenDecodeKeepsPixels()
        {
            var image = new RgbaImage(5, 3);
            var random = new Random(1);
            random.NextBytes(image.Pixels);

            RgbaImage decoded = Png.Decode(Png.Encode(image));

            Assert.Equal(5, decoded.Width);
            Assert.Equal(3, decoded.Height);
            Assert.Equal(image.Pixels, decoded.Pixels);
        }

        [Fact]
        public void DecodesEveryFilterType()
        {
            byte[] row0 = { 10, 20, 30, 40, 50, 60, 70, 80 };
            byte[] row1 = { 1, 2, 3, 4, 5, 6, 7, 8 };
            var expected = new byte[16];
            Buffer.BlockCopy(row0, 0, expected, 0, 8);
            Buffer.BlockCopy(row1, 0, expected, 8, 8);

            for (byte filter = 0; filter <= 4; filter++)
            {
                byte[] raw = new byte[18];
                raw[0] = 0;
                Buffer.BlockCopy(row0, 0, raw, 1, 8);
                raw[9] = filter;
                for (int x = 0; x < 8; x++)
                {
                    int left = x >= 4 ? row1[x - 4] : 0;
                    int up = row0[x];
                    int upLeft = x >= 4 ? row0[x - 4] : 0;
                    int predictor = filter switch
                    {
                        1 => left,
                        2 => up,
                        3 => (left + up) / 2,
                        4 => Paeth(left, up, upLeft),
                        _ => 0,
                    };
                    raw[10 + x] = (byte)(row1[x] - predictor);
                }

                RgbaImage decoded = Png.Decode(BuildPng(2, 2, 6, raw));

                Assert.Equal(expected, decoded.Pixels);
            }
        }

        [Fact]
        public void ExpandsRgbAndGrayToRgba()
        {
            RgbaImage rgb = Png.Decode(BuildPng(1, 1, 2, new byte[] { 0, 10, 20, 30 }));
            RgbaImage gray = Png.Decode(BuildPng(1, 1, 0, new byte[] { 0, 99 }));
            RgbaImage grayAlpha = Png.Decode(BuildPng(1, 1, 4, new byte[] { 0, 99, 7 }));

            Assert.Equal(new byte[] { 10, 20, 30, 255 }, rgb.Pixels);
            Assert.Equal(new byte[] { 99, 99, 99, 255 }, gray.Pixels);
            Assert.Equal(new byte[] { 99, 99, 99, 7 }, grayAlpha.Pixels);
        }

        [Fact]
        public void DecodesEveryTestModelTexture()
        {
            ModelPackage package = ModelPackage.Load(TestPaths.TestFaceDirectory);

            foreach (Mesh mesh in package.Model.Meshes)
            {
                RgbaImage image = Png.Decode(package.Textures[mesh.Texture]);
                Assert.True(image.Width > 0 && image.Height > 0, mesh.Texture);
            }
        }

        [Fact]
        public void RejectsNonPng()
        {
            Assert.Throws<InvalidDataException>(() => Png.Decode(new byte[] { 1, 2, 3 }));
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : (pb <= pc ? b : c);
        }

        private static byte[] BuildPng(int width, int height, byte colorType, byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
                var header = new byte[13];
                WriteInt32(header, 0, width);
                WriteInt32(header, 4, height);
                header[8] = 8;
                header[9] = colorType;
                WriteChunk(output, "IHDR", header);
                using (var compressed = new MemoryStream())
                {
                    using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                    {
                        zlib.Write(raw);
                    }

                    WriteChunk(output, "IDAT", compressed.ToArray());
                }

                WriteChunk(output, "IEND", Array.Empty<byte>());
                return output.ToArray();
            }
        }

        private static void WriteChunk(Stream output, string type, byte[] body)
        {
            var length = new byte[4];
            WriteInt32(length, 0, body.Length);
            output.Write(length);
            output.Write(System.Text.Encoding.ASCII.GetBytes(type));
            output.Write(body);
            output.Write(new byte[4]);
        }

        private static void WriteInt32(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }
    }
}
