using System.Collections.Generic;
using Ugokue.Core.Format;

namespace Ugokue.Core.Rig
{
    /// <summary>
    /// 描画リストの1件。描くパーツ、使うメッシュ、描画順、切り抜きに使うマスクを表す。
    /// </summary>
    public sealed class DrawItem
    {
        private static readonly ClipMask[] NoMasks = new ClipMask[0];

        public DrawItem(Part part, Mesh mesh, int drawOrder, IReadOnlyList<ClipMask> masks = null)
        {
            Part = part;
            Mesh = mesh;
            DrawOrder = drawOrder;
            Masks = masks ?? NoMasks;
        }

        public Part Part { get; }

        public Mesh Mesh { get; }

        public int DrawOrder { get; }

        /// <summary>
        /// clipBy の順のマスク。すべてのマスクの内側（共通部分）だけを描く。空なら切り抜かない。
        /// </summary>
        public IReadOnlyList<ClipMask> Masks { get; }
    }
}
