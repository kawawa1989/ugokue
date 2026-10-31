using System;
using System.Collections.Generic;
using System.Linq;
using Ugokue.Core.Format;

namespace Ugokue.Core.Rig
{
    /// <summary>
    /// 顔の向きのキーフォームを手で修正するための操作。手の修正は、自動のキーフォームに足し合わせる編集用のグループ（&lt;メッシュ&gt;_angle_edit）に持つ。
    /// </summary>
    public static class KeyformEditing
    {
        public const string EditGroupSuffix = "_angle_edit";

        public static readonly string[] AngleParameters = { "AngleX", "AngleY" };

        /// <summary>
        /// メッシュの編集用のグループの ID。
        /// </summary>
        public static string EditGroupId(string meshId)
        {
            return meshId + EditGroupSuffix;
        }

        /// <summary>
        /// メッシュの顔の向きの格子。そのメッシュの顔の向きのグループがなければ、モデルのほかのメッシュのものを使う。どれもなければ null。
        /// </summary>
        public static double[][] AngleGrid(Model model, string meshId)
        {
            List<KeyformGroup> groups = model.Deformation.KeyformGroups.Where(IsAngleGroup).ToList();
            KeyformGroup own = groups.FirstOrDefault(g => g.Mesh == meshId && g.Id != EditGroupId(meshId)) ?? groups.FirstOrDefault();
            return own?.Keys;
        }

        /// <summary>
        /// メッシュの編集用のグループを返す。なければ、顔の向きの格子と同じで移動量が 0 のグループを作ってモデルに加える。
        /// </summary>
        public static KeyformGroup EditGroup(Model model, string meshId)
        {
            string id = EditGroupId(meshId);
            KeyformGroup existing = model.Deformation.KeyformGroups.FirstOrDefault(g => g.Id == id);
            if (existing != null)
            {
                return existing;
            }

            Mesh mesh = model.Meshes.FirstOrDefault(m => m.Id == meshId) ?? throw new ArgumentException("Unknown mesh: " + meshId, nameof(meshId));
            double[][] grid = AngleGrid(model, meshId) ?? throw new InvalidOperationException("The model has no AngleX × AngleY keyform group.");
            var group = new KeyformGroup
            {
                Id = id,
                Mesh = meshId,
                Parameters = AngleParameters.ToList(),
                Keys = grid.Select(k => (double[])k.Clone()).ToArray(),
            };
            foreach (double x in grid[0])
            {
                foreach (double y in grid[1])
                {
                    group.Keyforms.Add(new Keyform
                    {
                        At = new[] { x, y },
                        Offsets = Enumerable.Range(0, mesh.Vertices.Length).Select(_ => new double[2]).ToArray(),
                    });
                }
            }

            model.Deformation.KeyformGroups.Add(group);
            return group;
        }

        /// <summary>
        /// 格子点 (angleX, angleY) のキーフォーム。格子点でなければ例外。
        /// </summary>
        public static Keyform KeyformAt(KeyformGroup group, double angleX, double angleY)
        {
            return group.Keyforms.FirstOrDefault(k => k.At[0] == angleX && k.At[1] == angleY)
                ?? throw new ArgumentException($"({angleX}, {angleY}) is not a grid point of {group.Id}.");
        }

        /// <summary>
        /// 中心 (x, y) から radius 以内の頂点の重み (1 − (d / radius)²)²。外は 0。positions は変形後の頂点位置（x と y を交互に並べたもの）。
        /// </summary>
        public static double[] Weights(double[] positions, double x, double y, double radius)
        {
            var weights = new double[positions.Length / 2];
            for (int v = 0; v < weights.Length; v++)
            {
                double dx = positions[v * 2] - x;
                double dy = positions[v * 2 + 1] - y;
                double ratio = (dx * dx + dy * dy) / (radius * radius);
                weights[v] = ratio < 1 ? (1 - ratio) * (1 - ratio) : 0;
            }

            return weights;
        }

        /// <summary>
        /// 各頂点の移動量に、重み × (dx, dy) を足す。
        /// </summary>
        public static void Move(Keyform keyform, double[] weights, double dx, double dy)
        {
            for (int v = 0; v < weights.Length; v++)
            {
                keyform.Offsets[v][0] += weights[v] * dx;
                keyform.Offsets[v][1] += weights[v] * dy;
            }
        }

        /// <summary>
        /// 各頂点の手の修正を、重みの割合だけ 0 に近づける（重み 1 で自動の位置に戻る）。
        /// </summary>
        public static void Reset(Keyform keyform, double[] weights)
        {
            for (int v = 0; v < weights.Length; v++)
            {
                double keep = 1 - Math.Min(1, Math.Max(0, weights[v]));
                keyform.Offsets[v][0] *= keep;
                keyform.Offsets[v][1] *= keep;
            }
        }

        /// <summary>
        /// 移動量がすべて 0 の編集用のグループをモデルから外し、外した数を返す。
        /// </summary>
        public static int RemoveEmptyEditGroups(Model model)
        {
            return model.Deformation.KeyformGroups.RemoveAll(g =>
                g.Id.EndsWith(EditGroupSuffix, StringComparison.Ordinal) && g.Keyforms.All(k => k.Offsets.All(o => o[0] == 0 && o[1] == 0)));
        }

        private static bool IsAngleGroup(KeyformGroup group)
        {
            return group.Parameters.SequenceEqual(AngleParameters);
        }
    }
}
