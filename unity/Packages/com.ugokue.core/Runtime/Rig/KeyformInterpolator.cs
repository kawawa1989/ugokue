using System;
using Ugokue.Core.Format;

namespace Ugokue.Core.Rig
{
    /// <summary>
    /// 1つのキーフォームグループについて、パラメータの値から各頂点の移動量を多重線形補間で求める。
    /// </summary>
    public sealed class KeyformInterpolator
    {
        private readonly string[] parameters;
        private readonly double[][] keys;
        private readonly int[] strides;
        private readonly double[][] offsets;
        private readonly int[] lower;
        private readonly double[] fraction;

        public KeyformInterpolator(KeyformGroup group, int vertexCount)
        {
            MeshId = group.Mesh;
            VertexCount = vertexCount;
            parameters = group.Parameters.ToArray();
            keys = group.Keys;
            strides = new int[parameters.Length];
            int size = 1;
            for (int axis = parameters.Length - 1; axis >= 0; axis--)
            {
                strides[axis] = size;
                size *= keys[axis].Length;
            }

            offsets = new double[size][];
            foreach (Keyform keyform in group.Keyforms)
            {
                var flat = new double[vertexCount * 2];
                for (int v = 0; v < vertexCount; v++)
                {
                    flat[v * 2] = keyform.Offsets[v][0];
                    flat[v * 2 + 1] = keyform.Offsets[v][1];
                }

                offsets[GridIndex(keyform.At)] = flat;
            }

            for (int i = 0; i < size; i++)
            {
                if (offsets[i] == null)
                {
                    throw new ArgumentException($"Keyform group '{group.Id}' is missing a grid point.", nameof(group));
                }
            }

            lower = new int[parameters.Length];
            fraction = new double[parameters.Length];
        }

        public string MeshId { get; }

        public int VertexCount { get; }

        /// <summary>
        /// 現在のパラメータの値での移動量を、x と y を交互に並べた配列 <paramref name="target"/> に足し込む。
        /// </summary>
        public void AddOffsets(ParameterValues values, double[] target)
        {
            for (int axis = 0; axis < parameters.Length; axis++)
            {
                Locate(keys[axis], values.Get(parameters[axis]), out lower[axis], out fraction[axis]);
            }

            int corners = 1 << parameters.Length;
            for (int corner = 0; corner < corners; corner++)
            {
                double weight = 1;
                int index = 0;
                for (int axis = 0; axis < parameters.Length; axis++)
                {
                    bool upper = (corner & (1 << axis)) != 0;
                    weight *= upper ? fraction[axis] : 1 - fraction[axis];
                    index += (lower[axis] + (upper ? 1 : 0)) * strides[axis];
                }

                if (weight == 0)
                {
                    continue;
                }

                double[] keyform = offsets[index];
                for (int i = 0; i < keyform.Length; i++)
                {
                    target[i] += weight * keyform[i];
                }
            }
        }

        /// <summary>
        /// 値を軸の範囲に収め、それを囲む区間の下側の格子番号と、区間内の位置 [0, 1] を求める。
        /// </summary>
        public static void Locate(double[] axis, double value, out int lowerIndex, out double t)
        {
            double clamped = Math.Min(Math.Max(value, axis[0]), axis[axis.Length - 1]);
            int segment = 0;
            while (segment < axis.Length - 2 && clamped > axis[segment + 1])
            {
                segment++;
            }

            lowerIndex = segment;
            t = (clamped - axis[segment]) / (axis[segment + 1] - axis[segment]);
        }

        private int GridIndex(double[] at)
        {
            int index = 0;
            for (int axis = 0; axis < parameters.Length; axis++)
            {
                int k = Array.IndexOf(keys[axis], at[axis]);
                if (k < 0)
                {
                    throw new ArgumentException($"Keyform at {at[axis]} is not on the grid of '{parameters[axis]}'.");
                }

                index += k * strides[axis];
            }

            return index;
        }
    }
}
