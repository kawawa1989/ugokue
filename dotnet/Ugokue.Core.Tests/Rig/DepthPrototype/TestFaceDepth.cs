using System;
using System.Collections.Generic;
using Ugokue.Core.Format;
using Ugokue.Core.Rig;

namespace Ugokue.Core.Tests.Rig.DepthPrototype
{
    /// <summary>
    /// 固定テストモデルの頂点の奥行き。test-model.md の「顔の向き」の表の奥行きを、頭の中心のまわりに yaw → pitch の順で回したもの（手前が正）。
    /// </summary>
    public static class TestFaceDepth
    {
        private const double CenterX = 256;
        private const double CenterY = 236;
        private const double HeadRadius = 120;
        private const double DepthScale = 0.5;

        private static readonly Dictionary<string, double> SurfaceBias = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["face"] = 0,
            ["eye_white_r"] = 0,
            ["eye_white_l"] = 0,
            ["iris_r"] = 0,
            ["iris_l"] = 0,
            ["mouth_inner"] = 0,
            ["teeth"] = 0,
            ["eyelid_r"] = 2,
            ["eyelid_l"] = 2,
            ["eyebrow_r"] = 4,
            ["eyebrow_l"] = 4,
            ["mouth"] = 1,
            ["nose"] = 18,
            ["hair_front"] = 14,
            ["hair_side_r"] = -6,
            ["hair_side_l"] = -6,
        };

        // 表にない頭のパーツは一定の奥行き。首と体は頭と一緒に回らないので、正面の値のまま（前後は描画順のかさ上げで決まる）。
        private static readonly Dictionary<string, double> Flat = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["ear_r"] = -30,
            ["ear_l"] = -30,
            ["hair_back"] = -35,
            ["neck"] = -40,
            ["body"] = -40,
        };

        /// <summary>
        /// 現在の AngleX、AngleY での、描画リストの1件のメッシュの頂点ごとの奥行きを返す。
        /// </summary>
        public static double[] Of(ModelRig rig, DrawItem item)
        {
            double yaw = rig.Parameters.Get("AngleX") * Math.PI / 180;
            double pitch = rig.Parameters.Get("AngleY") * Math.PI / 180;
            bool turns = item.Part.Id != "neck" && item.Part.Id != "body";
            Mesh mesh = item.Mesh;
            var depths = new double[mesh.Vertices.Length];
            for (int v = 0; v < depths.Length; v++)
            {
                double x = mesh.Vertices[v][0] - CenterX;
                double y = mesh.Vertices[v][1] - CenterY;
                double z = Flat.TryGetValue(item.Part.Id, out double flat)
                    ? flat
                    : DepthScale * Math.Sqrt(Math.Max(0, HeadRadius * HeadRadius - x * x - y * y)) + SurfaceBias[item.Part.Id];
                if (!turns)
                {
                    depths[v] = z;
                    continue;
                }

                double z1 = -x * Math.Sin(yaw) + z * Math.Cos(yaw);
                depths[v] = y * Math.Sin(pitch) + z1 * Math.Cos(pitch);
            }

            return depths;
        }
    }
}
