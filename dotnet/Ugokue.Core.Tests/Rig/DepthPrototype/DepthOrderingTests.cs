using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Ugokue.Core.Format;
using Ugokue.Core.Imaging;
using Ugokue.Core.Rig;
using Xunit;
using Xunit.Abstractions;

namespace Ugokue.Core.Tests.Rig.DepthPrototype
{
    /// <summary>
    /// 頂点ごとの奥行きで画素ごとに前後関係を決める方式の試作（#27）を、固定テストモデルで今の方式（描画順とビュー）と比べる。
    /// </summary>
    public class DepthOrderingTests
    {
        // かさ上げの余裕。小さいと回したときに前後が入れ替わりやすく、大きいと描画順に近づく。
        private const double SmallMargin = 1;
        private const double SafeMargin = 5;
        private const int ChannelTolerance = 8;
        private const double MaxChangedPixelRatioPerDegree = 0.03;
        private const double MaxFlickerPixelRatio = 0.0005;
        private static readonly byte[] Background = { 240, 240, 246, 255 };

        private static readonly Lazy<ModelPackage> TestFace = new Lazy<ModelPackage>(() => ModelPackage.Load(TestPaths.TestFaceDirectory));
        private static readonly Lazy<Dictionary<string, RgbaImage>> Textures = new Lazy<Dictionary<string, RgbaImage>>(() => RenderRegressionTests.DecodedTextures(TestFace.Value));
        private static readonly Lazy<RgbaImage> Front = new Lazy<RgbaImage>(() => ReferenceRenderer.Render(Pose(TestFace.Value.Model, 0, 0), Textures.Value, Background));

        // ビューの切り替えを使わないモデル（正面のビューだけ）。奥行きで前後を決めるときに使う。
        private static readonly Lazy<Model> WithoutViews = new Lazy<Model>(() =>
        {
            Model model = JsonConvert.DeserializeObject<Model>(JsonConvert.SerializeObject(TestFace.Value.Model));
            model.Views.RemoveAll(v => v.Id != "front");
            return model;
        });

        private static readonly Lazy<List<Fragment>[]> FrontFragments = new Lazy<List<Fragment>[]>(() => Fragments(Pose(WithoutViews.Value, 0, 0)));
        private static readonly Lazy<IReadOnlyList<DrawItem>> FrontDrawList = new Lazy<IReadOnlyList<DrawItem>>(() => Pose(WithoutViews.Value, 0, 0).DrawList);
        private static readonly ConcurrentDictionary<double, double[]> PartBiases = new ConcurrentDictionary<double, double[]>();
        private static readonly ConcurrentDictionary<double, double[][]> VertexBiases = new ConcurrentDictionary<double, double[][]>();

        private readonly ITestOutputHelper output;

        public DepthOrderingTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(30, 0)]
        [InlineData(-18, 10)]
        public void DrawOrderCompositionMatchesTheReferenceRenderer(double angleX, double angleY)
        {
            ModelRig rig = Pose(TestFace.Value.Model, angleX, angleY);

            RgbaImage composed = Composite(rig, DepthOrdering.DrawOrder, DepthComposition.Sorted, SafeMargin);

            Assert.Equal(0, Different(composed, ReferenceRenderer.Render(rig, Textures.Value, Background)));
        }

        [Fact]
        public void DepthAloneBreaksTheFrontView()
        {
            // 横髪は奥行きでは顔の表面より 6 奥にあるが、顔の上に描く。アニメの重ね順は物理的な奥行きと一致しない。
            RgbaImage depth = Composite(Pose(WithoutViews.Value, 0, 0), DepthOrdering.Depth, DepthComposition.Sorted, SafeMargin);

            int different = Different(depth, Front.Value);
            output.WriteLine($"{different} pixels differ from the front view");
            Assert.True(different > 1000, $"only {different} pixels differ");
        }

        [Theory]
        [InlineData(DepthOrdering.BiasedDepth, SmallMargin)]
        [InlineData(DepthOrdering.BiasedDepth, SafeMargin)]
        [InlineData(DepthOrdering.VertexBiasedDepth, SmallMargin)]
        [InlineData(DepthOrdering.VertexBiasedDepth, SafeMargin)]
        public void BiasesKeepTheFrontViewUnchanged(DepthOrdering ordering, double margin)
        {
            RgbaImage biased = Composite(Pose(WithoutViews.Value, 0, 0), ordering, DepthComposition.Sorted, margin);

            Assert.Equal(0, Different(biased, Front.Value));
        }

        [Theory]
        [InlineData("AngleX")]
        [InlineData("AngleY")]
        public void SweepHasNoFlickerOrLargeJumpsWithEnoughMargin(string axis)
        {
            List<RgbaImage> frames = Enumerable.Range(-30, 61).Select(angle => Composite(Swept(axis, angle), DepthOrdering.VertexBiasedDepth, DepthComposition.Sorted, SafeMargin)).ToList();
            int pixels = frames[0].Width * frames[0].Height;
            int worst = 0;
            for (int f = 1; f < frames.Count; f++)
            {
                int changed = Different(frames[f - 1], frames[f]);
                worst = Math.Max(worst, changed);
                Assert.True(changed <= pixels * MaxChangedPixelRatioPerDegree, $"{axis} {f - 31}→{f - 30}: {changed} pixels changed");
                if (f + 1 < frames.Count)
                {
                    int blips = Flicker(frames[f - 1], frames[f], frames[f + 1]);
                    Assert.True(blips <= pixels * MaxFlickerPixelRatio, $"{axis} {f - 30}: {blips} pixels flicker for one degree");
                }
            }

            output.WriteLine($"{axis}: at most {worst} pixels change per degree");
        }

        [Fact]
        public void SmallMarginMakesBackHairJumpInFrontOfTheNeck()
        {
            // 首と体は頭と一緒に回らないので、上を 1° 向いただけで後ろ髪の下の方が首と体より手前になり、一度に入れ替わる。
            RgbaImage level = Composite(Swept("AngleY", 0), DepthOrdering.VertexBiasedDepth, DepthComposition.Sorted, SmallMargin);
            RgbaImage up = Composite(Swept("AngleY", 1), DepthOrdering.VertexBiasedDepth, DepthComposition.Sorted, SmallMargin);

            int changed = Different(level, up);
            output.WriteLine($"{changed} pixels change from AngleY 0 to 1");
            Assert.True(changed > level.Width * level.Height * MaxChangedPixelRatioPerDegree, $"only {changed} pixels change");
        }

        [Fact]
        public void FarSideHairGoesBehindTheCheekOnlyWithSmallMargin()
        {
            // 奥行きどおりなら、奥の横髪は 30° で頬の後ろに回る。急な入れ替わりが出ない余裕にすると、今の方式（描画順。ADR 0005）とほとんど変わらない。
            ModelRig current = Pose(TestFace.Value.Model, 30, 0);
            ModelRig depth = Pose(WithoutViews.Value, 30, 0);
            List<Fragment>[] fragments = Fragments(depth);
            int now = Visible(Fragments(current), Rank(current, "hair_side_l"), f => f.Rank);
            int small = Visible(fragments, Rank(depth, "hair_side_l"), Key(SmallMargin));
            int safe = Visible(fragments, Rank(depth, "hair_side_l"), Key(SafeMargin));

            output.WriteLine($"hair_side_l visible: {now} pixels now, {small} with margin {SmallMargin}, {safe} with margin {SafeMargin}");
            Assert.True(small < now * 0.9, $"{small} of {now}");
            Assert.True(safe >= now * 0.99, $"{safe} of {now}");
        }

        [Fact]
        public void PrepassLeavesSeamsAtSoftEdges()
        {
            // GPU の深度の事前描画では、半透明の縁が深度を書かないため、断片を並べて重ねた結果と縁で食い違う。
            ModelRig rig = Pose(WithoutViews.Value, 0, 0);

            int seams = Different(
                Composite(rig, DepthOrdering.VertexBiasedDepth, DepthComposition.Prepass, SafeMargin),
                Composite(rig, DepthOrdering.VertexBiasedDepth, DepthComposition.Sorted, SafeMargin));

            output.WriteLine($"{seams} seam pixels at the front view");
            Assert.True(seams > 0);
        }

        [Fact]
        public void WritesSweepForReview()
        {
            // UGOKUE_DEPTH_SWEEP_OUT を設定したときだけ、今の方式、頂点ごとのかさ上げ（余裕 1、余裕 5、余裕 5 で深度の事前描画）を横に並べた角度スイープを書き出す。
            string directory = Environment.GetEnvironmentVariable("UGOKUE_DEPTH_SWEEP_OUT");
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            int frame = 0;
            foreach (string axis in new[] { "AngleX", "AngleY" })
            {
                for (int angle = -30; angle <= 30; angle++)
                {
                    RgbaImage now = ReferenceRenderer.Render(axis == "AngleX" ? Pose(TestFace.Value.Model, angle, 0) : Pose(TestFace.Value.Model, 0, angle), Textures.Value, Background);
                    ModelRig rig = Swept(axis, angle);
                    RgbaImage small = Composite(rig, DepthOrdering.VertexBiasedDepth, DepthComposition.Sorted, SmallMargin);
                    RgbaImage safe = Composite(rig, DepthOrdering.VertexBiasedDepth, DepthComposition.Sorted, SafeMargin);
                    RgbaImage prepass = Composite(rig, DepthOrdering.VertexBiasedDepth, DepthComposition.Prepass, SafeMargin);
                    File.WriteAllBytes(Path.Combine(directory, $"frame{frame++:D3}.png"), Png.Encode(SideBySide(now, small, safe, prepass)));
                }
            }
        }

        private static ModelRig Swept(string axis, double angle)
        {
            return axis == "AngleX" ? Pose(WithoutViews.Value, angle, 0) : Pose(WithoutViews.Value, 0, angle);
        }

        private static List<Fragment>[] Fragments(ModelRig rig)
        {
            return DepthCompositor.Fragments(rig, Textures.Value, item => TestFaceDepth.Of(rig, item));
        }

        private static ModelRig Pose(Model model, double angleX, double angleY)
        {
            var rig = new ModelRig(model);
            rig.Parameters.Set("AngleX", angleX);
            rig.Parameters.Set("AngleY", angleY);
            rig.Update();
            return rig;
        }

        private static RgbaImage Composite(ModelRig rig, DepthOrdering ordering, DepthComposition composition, double margin)
        {
            // かさ上げは描画順の位置ごとの値なので、ビューで描画リストが変わるモデル（DrawOrder の比較）では使わない。
            double[] parts = ordering == DepthOrdering.BiasedDepth ? PartBiases.GetOrAdd(margin, m => DepthCompositor.Biases(FrontFragments.Value, FrontDrawList.Value.Count, m)) : null;
            double[][] vertices = ordering == DepthOrdering.VertexBiasedDepth ? FrontVertexBiases(margin) : null;
            return DepthCompositor.Composite(Fragments(rig), rig.Model.Canvas.Width, rig.Model.Canvas.Height, Background, ordering, composition, parts, vertices);
        }

        private static double[][] FrontVertexBiases(double margin)
        {
            return VertexBiases.GetOrAdd(margin, m => DepthCompositor.VertexBiases(FrontFragments.Value, FrontDrawList.Value, m));
        }

        private static Func<Fragment, double> Key(double margin)
        {
            double[][] biases = FrontVertexBiases(margin);
            return f => f.Depth + f.Weights[0] * biases[f.Rank][f.Vertices[0]] + f.Weights[1] * biases[f.Rank][f.Vertices[1]] + f.Weights[2] * biases[f.Rank][f.Vertices[2]];
        }

        private static int Rank(ModelRig rig, string partId)
        {
            return rig.DrawList.Select(d => d.Part.Id).ToList().IndexOf(partId);
        }

        private static int Visible(List<Fragment>[] fragments, int rank, Func<Fragment, double> key)
        {
            // そのパーツの断片が、画素でいちばん手前になる画素の数。
            int count = 0;
            foreach (List<Fragment> list in fragments)
            {
                if (list != null && list.Any(f => f.Rank == rank))
                {
                    count += list.OrderBy(key).ThenBy(f => f.Rank).Last().Rank == rank ? 1 : 0;
                }
            }

            return count;
        }

        private static RgbaImage SideBySide(params RgbaImage[] images)
        {
            int width = images[0].Width, height = images[0].Height;
            var result = new RgbaImage(width * images.Length, height);
            for (int i = 0; i < images.Length; i++)
            {
                for (int y = 0; y < height; y++)
                {
                    Array.Copy(images[i].Pixels, y * width * 4, result.Pixels, (y * width * images.Length + i * width) * 4, width * 4);
                }
            }

            return result;
        }

        private static int Flicker(RgbaImage previous, RgbaImage current, RgbaImage next)
        {
            int count = 0;
            for (int i = 0; i < previous.Width * previous.Height; i++)
            {
                count += !Differs(previous, next, i) && Differs(previous, current, i) && Differs(current, next, i) ? 1 : 0;
            }

            return count;
        }

        private static int Different(RgbaImage a, RgbaImage b)
        {
            int count = 0;
            for (int i = 0; i < a.Width * a.Height; i++)
            {
                count += Differs(a, b, i) ? 1 : 0;
            }

            return count;
        }

        private static bool Differs(RgbaImage a, RgbaImage b, int pixel)
        {
            int k = pixel * 4;
            return Math.Abs(a.Pixels[k] - b.Pixels[k]) > ChannelTolerance
                || Math.Abs(a.Pixels[k + 1] - b.Pixels[k + 1]) > ChannelTolerance
                || Math.Abs(a.Pixels[k + 2] - b.Pixels[k + 2]) > ChannelTolerance;
        }
    }
}
