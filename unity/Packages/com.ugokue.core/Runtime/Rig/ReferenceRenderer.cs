using System;
using System.Collections.Generic;
using Ugokue.Core.Format;
using Ugokue.Core.Imaging;

namespace Ugokue.Core.Rig
{
    /// <summary>
    /// ModelRig の現在の形を CPU で描く参照用のレンダラー。画像回帰テストの基準画像を作るのに使う。
    /// </summary>
    public static class ReferenceRenderer
    {
        /// <summary>
        /// マスクの内側とみなすテクスチャのアルファの下限。
        /// </summary>
        public const double MaskAlphaThreshold = 0.5;

        /// <summary>
        /// 描画リストの順に、各メッシュをテクスチャ付きでキャンバスと同じ大きさの不透明な画像に描く。
        /// debug が true なら、描画順の色と切り抜かれる部分を示すデバッグ表示で描く。
        /// </summary>
        public static RgbaImage Render(ModelRig rig, IReadOnlyDictionary<string, RgbaImage> textures, byte[] background, bool debug = false)
        {
            int width = rig.Model.Canvas.Width;
            int height = rig.Model.Canvas.Height;
            var color = new double[width * height * 3];
            for (int i = 0; i < width * height; i++)
            {
                color[i * 3] = background[0];
                color[i * 3 + 1] = background[1];
                color[i * 3 + 2] = background[2];
            }

            IReadOnlyList<DrawItem> drawList = rig.DrawList;
            for (int rank = 0; rank < drawList.Count; rank++)
            {
                DrawItem item = drawList[rank];
                bool[] mask = Mask(rig, textures, item, width, height);
                RgbaImage texture = textures[item.Mesh.Texture];
                if (debug && mask != null)
                {
                    byte[] ghost = DebugColors.ClippedAway;
                    Rasterize(rig, item.Mesh, width, height, (index, u, v) =>
                        BlendFlat(color, index, ghost, SampleAlpha(texture, u, v) * item.Opacity * ghost[3] / 255.0));
                }

                byte[] flat = debug ? DebugColors.ForRank(rank, drawList.Count) : null;
                Rasterize(rig, item.Mesh, width, height, (index, u, v) =>
                {
                    if (mask != null && !mask[index])
                    {
                        return;
                    }

                    if (flat != null)
                    {
                        BlendFlat(color, index, flat, SampleAlpha(texture, u, v) * item.Opacity);
                    }
                    else
                    {
                        BlendTexture(color, index, texture, u, v, item.Opacity);
                    }
                });
            }

            var image = new RgbaImage(width, height);
            for (int i = 0; i < width * height; i++)
            {
                image.Pixels[i * 4] = ToByte(color[i * 3]);
                image.Pixels[i * 4 + 1] = ToByte(color[i * 3 + 1]);
                image.Pixels[i * 4 + 2] = ToByte(color[i * 3 + 2]);
                image.Pixels[i * 4 + 3] = 255;
            }

            return image;
        }

        /// <summary>
        /// 描画リストにあるパーツが、切り抜きと不透明度を適用したうえで各画素に重ねる不透明度 [0, 1] を返す。描画リストにないパーツは null を返す。
        /// </summary>
        public static double[] Coverage(ModelRig rig, IReadOnlyDictionary<string, RgbaImage> textures, string partId)
        {
            int width = rig.Model.Canvas.Width;
            int height = rig.Model.Canvas.Height;
            double[] alpha = null;
            foreach (DrawItem item in rig.DrawList)
            {
                if (item.Part.Id != partId)
                {
                    continue;
                }

                bool[] mask = Mask(rig, textures, item, width, height);
                RgbaImage texture = textures[item.Mesh.Texture];
                double[] target = alpha ??= new double[width * height];
                Rasterize(rig, item.Mesh, width, height, (index, u, v) =>
                {
                    if (mask == null || mask[index])
                    {
                        target[index] = 1 - (1 - target[index]) * (1 - SampleAlpha(texture, u, v) * item.Opacity);
                    }
                });
            }

            return alpha;
        }

        private static bool[] Mask(ModelRig rig, IReadOnlyDictionary<string, RgbaImage> textures, DrawItem item, int width, int height)
        {
            if (item.Masks.Count == 0)
            {
                return null;
            }

            bool[] result = null;
            foreach (ClipMask clip in item.Masks)
            {
                var inside = new bool[width * height];
                if (clip.Mesh != null)
                {
                    RgbaImage texture = textures[clip.Mesh.Texture];
                    Rasterize(rig, clip.Mesh, width, height, (index, u, v) =>
                    {
                        if (SampleAlpha(texture, u, v) >= MaskAlphaThreshold)
                        {
                            inside[index] = true;
                        }
                    });
                }

                if (result == null)
                {
                    result = inside;
                }
                else
                {
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] &= inside[i];
                    }
                }
            }

            return result;
        }

        private static void Rasterize(ModelRig rig, Mesh mesh, int width, int height, Action<int, double, double> pixel)
        {
            double[] positions = rig.GetPositions(mesh.Id);
            foreach (int[] triangle in mesh.Triangles)
            {
                RasterizeTriangle(positions, mesh.Uvs, triangle, width, height, pixel);
            }
        }

        private static void RasterizeTriangle(double[] positions, double[][] uvs, int[] triangle, int width, int height, Action<int, double, double> pixel)
        {
            int a = triangle[0], b = triangle[1], c = triangle[2];
            double ax = positions[a * 2], ay = positions[a * 2 + 1];
            double bx = positions[b * 2], by = positions[b * 2 + 1];
            double cx = positions[c * 2], cy = positions[c * 2 + 1];
            double area = Edge(ax, ay, bx, by, cx, cy);
            if (area == 0)
            {
                return;
            }

            if (area < 0)
            {
                (b, c) = (c, b);
                (bx, by, cx, cy) = (cx, cy, bx, by);
                area = -area;
            }

            int minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
            int maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
            int maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
            for (int y = minY; y <= maxY; y++)
            {
                double py = y + 0.5;
                for (int x = minX; x <= maxX; x++)
                {
                    double px = x + 0.5;
                    double wa = Edge(bx, by, cx, cy, px, py);
                    double wb = Edge(cx, cy, ax, ay, px, py);
                    double wc = Edge(ax, ay, bx, by, px, py);
                    if (!Inside(wa, bx, by, cx, cy) || !Inside(wb, cx, cy, ax, ay) || !Inside(wc, ax, ay, bx, by))
                    {
                        continue;
                    }

                    double u = (wa * uvs[a][0] + wb * uvs[b][0] + wc * uvs[c][0]) / area;
                    double v = (wa * uvs[a][1] + wb * uvs[b][1] + wc * uvs[c][1]) / area;
                    pixel(y * width + x, u, v);
                }
            }
        }

        private static double Edge(double x0, double y0, double x1, double y1, double px, double py)
        {
            return (x1 - x0) * (py - y0) - (y1 - y0) * (px - x0);
        }

        private static bool Inside(double w, double x0, double y0, double x1, double y1)
        {
            if (w != 0)
            {
                return w > 0;
            }

            double dy = y1 - y0;
            return dy > 0 || (dy == 0 && x1 - x0 > 0);
        }

        private static void BlendTexture(double[] color, int pixel, RgbaImage texture, double u, double v, double opacity)
        {
            Sample(texture, u, v, out double r, out double g, out double b, out double alpha);
            r *= opacity;
            g *= opacity;
            b *= opacity;
            alpha *= opacity;
            if (alpha <= 0)
            {
                return;
            }

            int index = pixel * 3;
            color[index] = r + color[index] * (1 - alpha);
            color[index + 1] = g + color[index + 1] * (1 - alpha);
            color[index + 2] = b + color[index + 2] * (1 - alpha);
        }

        private static void BlendFlat(double[] color, int pixel, byte[] rgb, double alpha)
        {
            if (alpha <= 0)
            {
                return;
            }

            int index = pixel * 3;
            for (int c = 0; c < 3; c++)
            {
                color[index + c] = rgb[c] * alpha + color[index + c] * (1 - alpha);
            }
        }

        private static double SampleAlpha(RgbaImage texture, double u, double v)
        {
            Sample(texture, u, v, out _, out _, out _, out double alpha);
            return alpha;
        }

        private static void Sample(RgbaImage texture, double u, double v, out double r, out double g, out double b, out double alpha)
        {
            double tx = Math.Min(Math.Max(u * texture.Width - 0.5, 0), texture.Width - 1);
            double ty = Math.Min(Math.Max(v * texture.Height - 0.5, 0), texture.Height - 1);
            int x0 = (int)Math.Floor(tx), y0 = (int)Math.Floor(ty);
            int x1 = Math.Min(x0 + 1, texture.Width - 1), y1 = Math.Min(y0 + 1, texture.Height - 1);
            double fx = tx - x0, fy = ty - y0;
            r = g = b = alpha = 0;
            Accumulate(texture, x0, y0, (1 - fx) * (1 - fy), ref r, ref g, ref b, ref alpha);
            Accumulate(texture, x1, y0, fx * (1 - fy), ref r, ref g, ref b, ref alpha);
            Accumulate(texture, x0, y1, (1 - fx) * fy, ref r, ref g, ref b, ref alpha);
            Accumulate(texture, x1, y1, fx * fy, ref r, ref g, ref b, ref alpha);
        }

        private static void Accumulate(RgbaImage texture, int x, int y, double weight, ref double r, ref double g, ref double b, ref double alpha)
        {
            if (weight == 0)
            {
                return;
            }

            int k = (y * texture.Width + x) * 4;
            double a = texture.Pixels[k + 3] / 255.0 * weight;
            r += texture.Pixels[k] * a;
            g += texture.Pixels[k + 1] * a;
            b += texture.Pixels[k + 2] * a;
            alpha += a;
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Min(255, Math.Max(0, Math.Round(value)));
        }
    }
}
