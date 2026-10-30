using System;
using System.Collections.Generic;
using Ugokue.Core.Imaging;

namespace Ugokue.Core.Rig
{
    /// <summary>
    /// ModelRig の現在の形を CPU で描く参照用のレンダラー。画像回帰テストの基準画像を作るのに使う。
    /// </summary>
    public static class ReferenceRenderer
    {
        /// <summary>
        /// 描画リストの順に、各メッシュをテクスチャ付きでキャンバスと同じ大きさの不透明な画像に描く。
        /// </summary>
        public static RgbaImage Render(ModelRig rig, IReadOnlyDictionary<string, RgbaImage> textures, byte[] background)
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

            foreach (DrawItem item in rig.DrawList)
            {
                double[] positions = rig.GetPositions(item.Mesh.Id);
                RgbaImage texture = textures[item.Mesh.Texture];
                foreach (int[] triangle in item.Mesh.Triangles)
                {
                    DrawTriangle(color, width, height, positions, item.Mesh.Uvs, triangle, texture);
                }
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

        private static void DrawTriangle(double[] color, int width, int height, double[] positions, double[][] uvs, int[] triangle, RgbaImage texture)
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
                    Blend(color, (y * width + x) * 3, texture, u, v);
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

        private static void Blend(double[] color, int index, RgbaImage texture, double u, double v)
        {
            double tx = Math.Min(Math.Max(u * texture.Width - 0.5, 0), texture.Width - 1);
            double ty = Math.Min(Math.Max(v * texture.Height - 0.5, 0), texture.Height - 1);
            int x0 = (int)Math.Floor(tx), y0 = (int)Math.Floor(ty);
            int x1 = Math.Min(x0 + 1, texture.Width - 1), y1 = Math.Min(y0 + 1, texture.Height - 1);
            double fx = tx - x0, fy = ty - y0;
            double r = 0, g = 0, b = 0, alpha = 0;
            Accumulate(texture, x0, y0, (1 - fx) * (1 - fy), ref r, ref g, ref b, ref alpha);
            Accumulate(texture, x1, y0, fx * (1 - fy), ref r, ref g, ref b, ref alpha);
            Accumulate(texture, x0, y1, (1 - fx) * fy, ref r, ref g, ref b, ref alpha);
            Accumulate(texture, x1, y1, fx * fy, ref r, ref g, ref b, ref alpha);
            if (alpha <= 0)
            {
                return;
            }

            color[index] = r + color[index] * (1 - alpha);
            color[index + 1] = g + color[index + 1] * (1 - alpha);
            color[index + 2] = b + color[index + 2] * (1 - alpha);
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
