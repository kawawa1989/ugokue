using System;
using System.IO;
using System.IO.Compression;

namespace Ugokue.Core.Imaging
{
    /// <summary>
    /// インターレースなしの 8 ビット PNG（グレー、グレー＋アルファ、RGB、RGBA）の読み込みと、RGBA の PNG の書き出し。
    /// </summary>
    public static class Png
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>
        /// PNG のバイト列を RGBA 画像にする。対応しない形式なら例外を投げる。
        /// </summary>
        public static RgbaImage Decode(byte[] data)
        {
            if (data == null || data.Length < Signature.Length || !StartsWithSignature(data))
            {
                throw new InvalidDataException("Not a PNG file.");
            }

            int width = 0, height = 0, channels = 0;
            using (var idat = new MemoryStream())
            {
                int pos = Signature.Length;
                while (pos + 8 <= data.Length)
                {
                    int length = ReadInt32(data, pos);
                    string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
                    int body = pos + 8;
                    if (length < 0 || body + length + 4 > data.Length)
                    {
                        throw new InvalidDataException("Truncated PNG chunk.");
                    }

                    if (type == "IHDR")
                    {
                        width = ReadInt32(data, body);
                        height = ReadInt32(data, body + 4);
                        channels = ChannelCount(data[body + 8], data[body + 9], data[body + 12]);
                    }
                    else if (type == "IDAT")
                    {
                        idat.Write(data, body, length);
                    }
                    else if (type == "IEND")
                    {
                        break;
                    }

                    pos = body + length + 4;
                }

                if (channels == 0 || width <= 0 || height <= 0)
                {
                    throw new InvalidDataException("PNG header is missing.");
                }

                byte[] raw = Inflate(idat.ToArray(), height * (width * channels + 1));
                return ToRgba(Unfilter(raw, width, height, channels), width, height, channels);
            }
        }

        /// <summary>
        /// RGBA 画像を、行ごとにフィルタなしで並べた PNG のバイト列にする。
        /// </summary>
        public static byte[] Encode(RgbaImage image)
        {
            int stride = image.Width * 4;
            var raw = new byte[image.Height * (stride + 1)];
            for (int y = 0; y < image.Height; y++)
            {
                Buffer.BlockCopy(image.Pixels, y * stride, raw, y * (stride + 1) + 1, stride);
            }

            using (var output = new MemoryStream())
            {
                output.Write(Signature, 0, Signature.Length);
                var header = new byte[13];
                WriteInt32(header, 0, image.Width);
                WriteInt32(header, 4, image.Height);
                header[8] = 8;
                header[9] = 6;
                WriteChunk(output, "IHDR", header);
                WriteChunk(output, "IDAT", Deflate(raw));
                WriteChunk(output, "IEND", new byte[0]);
                return output.ToArray();
            }
        }

        private static bool StartsWithSignature(byte[] data)
        {
            for (int i = 0; i < Signature.Length; i++)
            {
                if (data[i] != Signature[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static int ChannelCount(byte bitDepth, byte colorType, byte interlace)
        {
            if (bitDepth != 8 || interlace != 0)
            {
                throw new InvalidDataException($"Unsupported PNG: bit depth {bitDepth}, interlace {interlace}.");
            }

            switch (colorType)
            {
                case 0: return 1;
                case 2: return 3;
                case 4: return 2;
                case 6: return 4;
                default: throw new InvalidDataException($"Unsupported PNG color type {colorType}.");
            }
        }

        private static byte[] Inflate(byte[] zlib, int expectedLength)
        {
            if (zlib.Length < 6)
            {
                throw new InvalidDataException("PNG image data is empty.");
            }

            var result = new byte[expectedLength];
            using (var input = new MemoryStream(zlib, 2, zlib.Length - 6))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < expectedLength)
                {
                    int n = deflate.Read(result, read, expectedLength - read);
                    if (n == 0)
                    {
                        throw new InvalidDataException("PNG image data is truncated.");
                    }

                    read += n;
                }
            }

            return result;
        }

        private static byte[] Deflate(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x9C);
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                {
                    deflate.Write(raw, 0, raw.Length);
                }

                uint a = 1, b = 0;
                foreach (byte value in raw)
                {
                    a = (a + value) % 65521;
                    b = (b + a) % 65521;
                }

                var adler = new byte[4];
                WriteInt32(adler, 0, (int)((b << 16) | a));
                output.Write(adler, 0, 4);
                return output.ToArray();
            }
        }

        private static byte[] Unfilter(byte[] raw, int width, int height, int channels)
        {
            int stride = width * channels;
            var pixels = new byte[height * stride];
            for (int y = 0; y < height; y++)
            {
                int filter = raw[y * (stride + 1)];
                int src = y * (stride + 1) + 1;
                int dst = y * stride;
                for (int x = 0; x < stride; x++)
                {
                    int left = x >= channels ? pixels[dst + x - channels] : 0;
                    int up = y > 0 ? pixels[dst - stride + x] : 0;
                    int upLeft = x >= channels && y > 0 ? pixels[dst - stride + x - channels] : 0;
                    int predictor;
                    switch (filter)
                    {
                        case 0: predictor = 0; break;
                        case 1: predictor = left; break;
                        case 2: predictor = up; break;
                        case 3: predictor = (left + up) / 2; break;
                        case 4: predictor = Paeth(left, up, upLeft); break;
                        default: throw new InvalidDataException($"Unknown PNG filter {filter}.");
                    }

                    pixels[dst + x] = (byte)(raw[src + x] + predictor);
                }
            }

            return pixels;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : (pb <= pc ? b : c);
        }

        private static RgbaImage ToRgba(byte[] pixels, int width, int height, int channels)
        {
            var image = new RgbaImage(width, height);
            byte[] rgba = image.Pixels;
            for (int i = 0; i < width * height; i++)
            {
                int s = i * channels;
                int d = i * 4;
                switch (channels)
                {
                    case 1:
                        rgba[d] = rgba[d + 1] = rgba[d + 2] = pixels[s];
                        rgba[d + 3] = 255;
                        break;
                    case 2:
                        rgba[d] = rgba[d + 1] = rgba[d + 2] = pixels[s];
                        rgba[d + 3] = pixels[s + 1];
                        break;
                    case 3:
                        rgba[d] = pixels[s];
                        rgba[d + 1] = pixels[s + 1];
                        rgba[d + 2] = pixels[s + 2];
                        rgba[d + 3] = 255;
                        break;
                    default:
                        Buffer.BlockCopy(pixels, s, rgba, d, 4);
                        break;
                }
            }

            return image;
        }

        private static void WriteChunk(Stream output, string type, byte[] body)
        {
            var header = new byte[8];
            WriteInt32(header, 0, body.Length);
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            Buffer.BlockCopy(typeBytes, 0, header, 4, 4);
            output.Write(header, 0, 8);
            output.Write(body, 0, body.Length);
            uint crc = 0xFFFFFFFF;
            crc = UpdateCrc(crc, typeBytes);
            crc = UpdateCrc(crc, body);
            var crcBytes = new byte[4];
            WriteInt32(crcBytes, 0, (int)(crc ^ 0xFFFFFFFF));
            output.Write(crcBytes, 0, 4);
        }

        private static uint UpdateCrc(uint crc, byte[] bytes)
        {
            foreach (byte value in bytes)
            {
                crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }

        private static int ReadInt32(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
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
