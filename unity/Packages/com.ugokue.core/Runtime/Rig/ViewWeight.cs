using Ugokue.Core.Format;

namespace Ugokue.Core.Rig
{
    /// <summary>
    /// 現在の角度で見た目に寄与するビューと、その重み。
    /// </summary>
    public readonly struct ViewWeight
    {
        public ViewWeight(View view, double weight)
        {
            View = view;
            Weight = weight;
        }

        public View View { get; }

        public double Weight { get; }
    }
}
