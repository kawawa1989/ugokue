using Ugokue.Core.Format;

namespace Ugokue.Core.Rig
{
    /// <summary>
    /// パーツを切り抜くマスク1つ。マスクにするパーツと、現在のビューでそのパーツが使うメッシュを表す。
    /// </summary>
    public sealed class ClipMask
    {
        public ClipMask(Part part, Mesh mesh)
        {
            Part = part;
            Mesh = mesh;
        }

        public Part Part { get; }

        /// <summary>
        /// マスクの形にするメッシュ。パーツがメッシュを持たないときは null で、マスクは空になる。
        /// </summary>
        public Mesh Mesh { get; }
    }
}
