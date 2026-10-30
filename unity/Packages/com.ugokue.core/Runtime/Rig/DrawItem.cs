using Ugokue.Core.Format;

namespace Ugokue.Core.Rig
{
    /// <summary>
    /// 描画リストの1件。描くパーツ、使うメッシュ、描画順を表す。
    /// </summary>
    public sealed class DrawItem
    {
        public DrawItem(Part part, Mesh mesh, int drawOrder)
        {
            Part = part;
            Mesh = mesh;
            DrawOrder = drawOrder;
        }

        public Part Part { get; }

        public Mesh Mesh { get; }

        public int DrawOrder { get; }
    }
}
