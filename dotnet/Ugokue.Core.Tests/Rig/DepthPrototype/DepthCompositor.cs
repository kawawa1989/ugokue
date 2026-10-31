using System;
using System.Collections.Generic;
using System.Linq;
using Ugokue.Core.Format;
using Ugokue.Core.Imaging;
using Ugokue.Core.Rig;

namespace Ugokue.Core.Tests.Rig.DepthPrototype
{
    /// <summary>
    /// 画素ごとの前後関係の決め方。
    /// </summary>
    public enum DepthOrdering
    {
        /// <summary>描画順だけで決める（今の方式）。</summary>
        DrawOrder,

        /// <summary>頂点の奥行きだけで決める。同じ奥行きなら描画順。</summary>
        Depth,

        /// <summary>奥行きにパーツごとのかさ上げを足して決める。かさ上げは、正面で描画順と一致するように求める。</summary>
        BiasedDepth,

        /// <summary>奥行きに頂点ごとのかさ上げを足して決める。かさ上げは、正面で描画順と一致するように、重なる場所だけで求める。</summary>
        VertexBiasedDepth,
    }

    /// <summary>
    /// 重なった断片の合成のしかた。
    /// </summary>
    public enum DepthComposition
    {
        /// <summary>画素ごとに断片を前後関係の順に並べて重ねる（半透明の縁も正確）。</summary>
        Sorted,

        /// <summary>GPU の深度の事前描画をまねる。不透明（アルファ 0.5 以上）な断片で手前の深度を決め、それより奥の断片を捨て、残りを描画順に重ねる。</summary>
        Prepass,
    }

    /// <summary>
    /// 1つの画素に重なる、1つのパーツの断片。色はアルファを掛けたもの。
    /// </summary>
    public readonly struct Fragment
    {
        public Fragment(int rank, double depth, double r, double g, double b, double alpha, int[] vertices, double[] weights)
        {
            Rank = rank;
            Depth = depth;
            R = r;
            G = g;
            B = b;
            Alpha = alpha;
            Vertices = vertices;
            Weights = weights;
        }

        public int Rank { get; }

        /// <summary>断片を含む三角形の頂点の番号と、重心座標。</summary>
        public int[] Vertices { get; }

        public double[] Weights { get; }

        public double Depth { get; }

        public double R { get; }

        public double G { get; }

        public double B { get; }

        public double Alpha { get; }
    }

    /// <summary>
    /// 頂点ごとの奥行きで画素ごとに前後関係を決める描き方の試作（#27）。参照レンダラーと同じ規則で三角形を塗り、断片を集めてから合成する。
    /// </summary>
    public static class DepthCompositor
    {
        private const double OpaqueAlpha = 0.5;

        /// <summary>
        /// 描画リストの各パーツの断片を、画素ごとに集める。depths はメッシュの頂点ごとの奥行き（手前が正）を返す。
        /// </summary>
        public static List<Fragment>[] Fragments(ModelRig rig, IReadOnlyDictionary<string, RgbaImage> textures, Func<DrawItem, double[]> depths)
        {
            int width = rig.Model.Canvas.Width;
            int height = rig.Model.Canvas.Height;
            var fragments = new List<Fragment>[width * height];
            IReadOnlyList<DrawItem> drawList = rig.DrawList;
            for (int rank = 0; rank < drawList.Count; rank++)
            {
                DrawItem item = drawList[rank];
                bool[] mask = Mask(rig, textures, item, width, height);
                RgbaImage texture = textures[item.Mesh.Texture];
                double[] z = depths(item);
                int itemRank = rank;
                Rasterize(rig.GetPositions(item.Mesh.Id), item.Mesh, z, width, height, (index, u, v, depth, vertices, weights) =>
                {
                    if (mask != null && !mask[index])
                    {
                        return;
                    }

                    Sample(texture, u, v, out double r, out double g, out double b, out double alpha);
                    if (alpha <= 0)
                    {
                        return;
                    }

                    (fragments[index] ??= new List<Fragment>()).Add(new Fragment(itemRank, depth, r, g, b, alpha, vertices, weights));
                });
            }

            return fragments;
        }

        /// <summary>
        /// 正面の断片から、パーツごとのかさ上げを求める。描画順で上のパーツが、重なるすべての画素で下のパーツより margin 以上手前になる最小の値。
        /// 描画順の下から順に決めるので、必ず解がある。
        /// </summary>
        public static double[] Biases(List<Fragment>[] front, int count, double margin)
        {
            var need = new double[count, count];
            var overlaps = new bool[count, count];
            foreach (List<Fragment> list in front)
            {
                if (list == null)
                {
                    continue;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = 0; j < list.Count; j++)
                    {
                        Fragment lower = list[i];
                        Fragment upper = list[j];
                        if (upper.Rank <= lower.Rank)
                        {
                            continue;
                        }

                        double gap = lower.Depth - upper.Depth;
                        if (!overlaps[upper.Rank, lower.Rank] || gap > need[upper.Rank, lower.Rank])
                        {
                            need[upper.Rank, lower.Rank] = gap;
                            overlaps[upper.Rank, lower.Rank] = true;
                        }
                    }
                }
            }

            var biases = new double[count];
            for (int upper = 0; upper < count; upper++)
            {
                for (int lower = 0; lower < upper; lower++)
                {
                    if (overlaps[upper, lower])
                    {
                        biases[upper] = Math.Max(biases[upper], biases[lower] + need[upper, lower] + margin);
                    }
                }
            }

            return biases;
        }

        /// <summary>
        /// 正面の断片から、パーツの頂点ごとのかさ上げを求める。描画順で上のパーツの断片が、同じ画素の下のパーツの断片より margin 以上手前になるように、
        /// その断片を含む三角形の頂点を持ち上げる（頂点の値の重心座標の補間が、画素ごとの必要量を下回らない）。重ならない頂点は 0 のまま。
        /// </summary>
        public static double[][] VertexBiases(List<Fragment>[] front, IReadOnlyList<DrawItem> drawList, double margin)
        {
            var biases = drawList.Select(d => new double[d.Mesh.Vertices.Length]).ToArray();
            var byRank = new List<(Fragment Upper, Fragment Lower)>[drawList.Count];
            foreach (List<Fragment> list in front)
            {
                if (list == null)
                {
                    continue;
                }

                foreach (Fragment upper in list)
                {
                    foreach (Fragment lower in list)
                    {
                        if (upper.Rank > lower.Rank)
                        {
                            (byRank[upper.Rank] ??= new List<(Fragment, Fragment)>()).Add((upper, lower));
                        }
                    }
                }
            }

            for (int rank = 0; rank < drawList.Count; rank++)
            {
                if (byRank[rank] == null)
                {
                    continue;
                }

                foreach ((Fragment upper, Fragment lower) in byRank[rank])
                {
                    double need = lower.Depth + Interpolate(biases[lower.Rank], lower) + margin - upper.Depth;
                    for (int k = 0; k < 3; k++)
                    {
                        if (upper.Weights[k] > 0)
                        {
                            biases[rank][upper.Vertices[k]] = Math.Max(biases[rank][upper.Vertices[k]], need);
                        }
                    }
                }
            }

            return biases;
        }

        /// <summary>
        /// 断片を合成して、不透明な画像にする。
        /// </summary>
        public static RgbaImage Composite(
            List<Fragment>[] fragments, int width, int height, byte[] background, DepthOrdering ordering, DepthComposition composition, double[] biases = null, double[][] vertexBiases = null)
        {
            var image = new RgbaImage(width, height);
            for (int p = 0; p < width * height; p++)
            {
                double r = background[0], g = background[1], b = background[2];
                List<Fragment> list = fragments[p];
                if (list != null)
                {
                    foreach (Fragment f in Ordered(list, ordering, composition, biases, vertexBiases))
                    {
                        r = f.R + r * (1 - f.Alpha);
                        g = f.G + g * (1 - f.Alpha);
                        b = f.B + b * (1 - f.Alpha);
                    }
                }

                image.Pixels[p * 4] = ToByte(r);
                image.Pixels[p * 4 + 1] = ToByte(g);
                image.Pixels[p * 4 + 2] = ToByte(b);
                image.Pixels[p * 4 + 3] = 255;
            }

            return image;
        }

        private static IEnumerable<Fragment> Ordered(List<Fragment> list, DepthOrdering ordering, DepthComposition composition, double[] biases, double[][] vertexBiases)
        {
            if (ordering == DepthOrdering.DrawOrder)
            {
                return list.OrderBy(f => f.Rank);
            }

            Func<Fragment, double> key = ordering switch
            {
                DepthOrdering.BiasedDepth => f => f.Depth + biases[f.Rank],
                DepthOrdering.VertexBiasedDepth => f => f.Depth + Interpolate(vertexBiases[f.Rank], f),
                _ => f => f.Depth,
            };
            if (composition == DepthComposition.Sorted)
            {
                return list.OrderBy(key).ThenBy(f => f.Rank);
            }

            double front = double.NegativeInfinity;
            foreach (Fragment f in list)
            {
                if (f.Alpha >= OpaqueAlpha)
                {
                    front = Math.Max(front, key(f));
                }
            }

            return list.Where(f => key(f) >= front).OrderBy(f => f.Rank);
        }

        private static double Interpolate(double[] values, Fragment f)
        {
            return f.Weights[0] * values[f.Vertices[0]] + f.Weights[1] * values[f.Vertices[1]] + f.Weights[2] * values[f.Vertices[2]];
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
                    Rasterize(rig.GetPositions(clip.Mesh.Id), clip.Mesh, null, width, height, (index, u, v, _, _, _) =>
                    {
                        Sample(texture, u, v, out _, out _, out _, out double alpha);
                        if (alpha >= ReferenceRenderer.MaskAlphaThreshold)
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

        private static void Rasterize(double[] positions, Mesh mesh, double[] depths, int width, int height, Action<int, double, double, double, int[], double[]> pixel)
        {
            foreach (int[] triangle in mesh.Triangles)
            {
                int a = triangle[0], b = triangle[1], c = triangle[2];
                double ax = positions[a * 2], ay = positions[a * 2 + 1];
                double bx = positions[b * 2], by = positions[b * 2 + 1];
                double cx = positions[c * 2], cy = positions[c * 2 + 1];
                double area = Edge(ax, ay, bx, by, cx, cy);
                if (area == 0)
                {
                    continue;
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

                        double u = (wa * mesh.Uvs[a][0] + wb * mesh.Uvs[b][0] + wc * mesh.Uvs[c][0]) / area;
                        double v = (wa * mesh.Uvs[a][1] + wb * mesh.Uvs[b][1] + wc * mesh.Uvs[c][1]) / area;
                        double depth = depths == null ? 0 : (wa * depths[a] + wb * depths[b] + wc * depths[c]) / area;
                        pixel(y * width + x, u, v, depth, new[] { a, b, c }, new[] { wa / area, wb / area, wc / area });
                    }
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
