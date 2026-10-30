using System.Collections.Generic;
using Ugokue.Core.Format;

namespace Ugokue.Core.Rig
{
    /// <summary>
    /// 描画リストの1件。描くパーツ、使うメッシュ、描画順、切り抜きに使うマスク、不透明度を表す。
    /// </summary>
    public sealed class DrawItem
    {
        private static readonly ClipMask[] NoMasks = new ClipMask[0];

        public DrawItem(Part part, Mesh mesh, int drawOrder, IReadOnlyList<ClipMask> masks = null, double opacity = 1, int instance = 0)
        {
            Part = part;
            Mesh = mesh;
            DrawOrder = drawOrder;
            Masks = masks ?? NoMasks;
            Opacity = opacity;
            Instance = instance;
        }

        public Part Part { get; }

        public Mesh Mesh { get; }

        public int DrawOrder { get; }

        /// <summary>
        /// clipBy の順のマスク。すべてのマスクの内側（共通部分）だけを描く。空なら切り抜かない。
        /// </summary>
        public IReadOnlyList<ClipMask> Masks { get; }

        /// <summary>
        /// テクスチャのアルファに掛ける不透明度 [0, 1]。
        /// </summary>
        public double Opacity { get; }

        /// <summary>
        /// 同じパーツの項目の番号。ビューの切り替えの途中で、1つのパーツが複数の項目になるときに 1 以上になる。
        /// </summary>
        public int Instance { get; }
    }
}
