using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Ugokue.Core.Format;
using Ugokue.Core.Imaging;
using Ugokue.Core.Rig;
using Xunit;

namespace Ugokue.Core.Tests.Rig
{
    public class OcclusionTests
    {
        private const double MaxChangedPixelRatioPerDegree = 0.03;
        private const double MaxFlickerPixelRatio = 0.0005;
        private const int PopChannelTolerance = 64;
        private const double MaxPopRatio = 1.6;
        private const int ChannelTolerance = 8;
        private static readonly byte[] Background = { 240, 240, 246, 255 };

        private static readonly Lazy<ModelPackage> TestFace = new Lazy<ModelPackage>(() => ModelPackage.Load(TestPaths.TestFaceDirectory));
        private static readonly Lazy<Dictionary<string, RgbaImage>> Textures = new Lazy<Dictionary<string, RgbaImage>>(() => RenderRegressionTests.DecodedTextures(TestFace.Value));

        public static IEnumerable<object[]> Sweeps => new[]
        {
            new object[] { "AngleX", "" },
            new object[] { "AngleY", "" },
            new object[] { "AngleX", "MouthOpenY=1" },
            new object[] { "AngleY", "MouthOpenY=1,EyeOpenL=0.5,EyeOpenR=0.5" },
        };

        [Theory]
        [MemberData(nameof(Sweeps))]
        public void ClippedPartsStayInsideTheirMasksDuringSweep(string axis, string fixedValues)
        {
            foreach (ModelRig rig in Sweep(TestFace.Value.Model, axis, fixedValues))
            {
                foreach (DrawItem item in rig.DrawList.Where(d => d.Masks.Count > 0))
                {
                    double[] part = ReferenceRenderer.Coverage(rig, Textures.Value, item.Part.Id);
                    double[] mask = ReferenceRenderer.Coverage(rig, Textures.Value, item.Masks[0].Part.Id);
                    int outside = Enumerable.Range(0, part.Length).Count(i => part[i] > 0 && mask[i] < ReferenceRenderer.MaskAlphaThreshold / 2);
                    Assert.True(outside == 0, $"{item.Part.Id} at {axis}={rig.Parameters.Get(axis)}: {outside} pixels outside {item.Masks[0].Part.Id}");
                }
            }
        }

        [Fact]
        public void IrisWouldStickOutWithoutClipping()
        {
            Model unclipped = JsonConvert.DeserializeObject<Model>(JsonConvert.SerializeObject(TestFace.Value.Model));
            foreach (Part part in unclipped.Parts)
            {
                part.ClipBy.Clear();
            }

            var rig = new ModelRig(unclipped);
            double[] iris = ReferenceRenderer.Coverage(rig, Textures.Value, "iris_r");
            double[] eyeWhite = ReferenceRenderer.Coverage(rig, Textures.Value, "eye_white_r");

            Assert.Contains(Enumerable.Range(0, iris.Length), i => iris[i] > 0 && eyeWhite[i] == 0);
        }

        [Theory]
        [InlineData("AngleX")]
        [InlineData("AngleY")]
        public void FrontHairStaysInFrontOfFaceDuringSweep(string axis)
        {
            foreach (ModelRig rig in Sweep(TestFace.Value.Model, axis, ""))
            {
                List<string> order = rig.DrawList.Select(d => d.Part.Id).ToList();
                Assert.True(order.IndexOf("hair_front") > order.IndexOf("face"), $"{axis}={rig.Parameters.Get(axis)}");
                foreach (string eyePart in new[] { "eye_white_r", "iris_r", "eyebrow_r", "eye_white_l", "iris_l", "eyebrow_l" })
                {
                    Assert.True(order.IndexOf(eyePart) > order.IndexOf("face"), $"{eyePart} at {axis}={rig.Parameters.Get(axis)}");
                }
            }
        }

        [Theory]
        [InlineData(30, "hair_side_l", "ear_l")]
        [InlineData(-30, "hair_side_r", "ear_r")]
        public void FarSideMovesBehindFaceWhenTurned(double angleX, string farHair, string farEar)
        {
            var rig = new ModelRig(TestFace.Value.Model);
            rig.Parameters.Set("AngleX", angleX);
            rig.Update();

            List<string> order = rig.DrawList.Select(d => d.Part.Id).ToList();
            Assert.True(order.IndexOf(farHair) < order.IndexOf("face"));
            Assert.DoesNotContain(farEar, order);
            string nearHair = farHair.EndsWith("_l") ? "hair_side_r" : "hair_side_l";
            Assert.True(order.IndexOf(nearHair) > order.IndexOf("face"));
        }

        [Theory]
        [MemberData(nameof(Sweeps))]
        public void SweepHasNoFlickerOrLargeJumps(string axis, string fixedValues)
        {
            List<(double Angle, byte[] Pixels)> frames = RenderSweep(axis, fixedValues, ModelRig.DefaultViewTransitionDegrees);
            int pixels = frames[0].Pixels.Length / 4;
            for (int f = 1; f < frames.Count; f++)
            {
                byte[] previous = frames[f - 1].Pixels;
                byte[] current = frames[f].Pixels;
                int changed = Enumerable.Range(0, pixels).Count(i => Differs(previous, current, i, ChannelTolerance));
                double ratio = (double)changed / pixels;
                Assert.True(ratio <= MaxChangedPixelRatioPerDegree, $"{axis} {frames[f - 1].Angle}→{frames[f].Angle}: {ratio:P2} of pixels changed");

                if (f + 1 < frames.Count)
                {
                    int blips = CountFlicker(previous, current, frames[f + 1].Pixels);
                    Assert.True(blips <= pixels * MaxFlickerPixelRatio, $"{axis} {frames[f].Angle}: {blips} pixels flicker for one degree");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Sweeps))]
        public void SweepHasNoPoppingAtViewBoundaries(string axis, string fixedValues)
        {
            List<(double Angle, byte[] Pixels)> frames = RenderSweep(axis, fixedValues, ModelRig.DefaultViewTransitionDegrees);

            (double angle, int changed, double median) = WorstPop(frames);

            Assert.True(changed <= median * MaxPopRatio, $"{axis} at {angle}: {changed} pixels changed strongly (median {median})");
        }

        [Fact]
        public void PopCheckDetectsViewSwitchWithoutFading()
        {
            List<(double Angle, byte[] Pixels)> frames = RenderSweep("AngleX", "", 0);

            (double angle, int changed, double median) = WorstPop(frames);

            Assert.True(changed > median * MaxPopRatio, $"worst step at {angle}: {changed} pixels (median {median})");
        }

        [Fact]
        public void FlickerCheckDetectsPartMissingForOneDegree()
        {
            Model flickering = JsonConvert.DeserializeObject<Model>(JsonConvert.SerializeObject(TestFace.Value.Model));
            flickering.Views.Add(new View
            {
                Id = "glitch",
                Yaw = 10,
                Pitch = 0,
                Overrides = new List<ViewOverride> { new ViewOverride { Part = "hair_front", Visible = false } },
            });
            RgbaImage[] frames = new[] { 9.0, 10.0, 11.0 }
                .Select(angle =>
                {
                    var rig = new ModelRig(angle == 10 ? flickering : TestFace.Value.Model);
                    rig.Parameters.Set("AngleX", angle);
                    rig.Update();
                    return ReferenceRenderer.Render(rig, Textures.Value, Background);
                })
                .ToArray();

            int blips = CountFlicker(frames[0].Pixels, frames[1].Pixels, frames[2].Pixels);

            Assert.True(blips > frames[0].Width * frames[0].Height * MaxFlickerPixelRatio, $"only {blips} pixels flicker");
        }

        private static int CountFlicker(byte[] previous, byte[] current, byte[] next)
        {
            int pixels = previous.Length / 4;
            return Enumerable.Range(0, pixels).Count(i =>
                !Differs(previous, next, i, ChannelTolerance) && Differs(previous, current, i, ChannelTolerance) && Differs(current, next, i, ChannelTolerance));
        }

        private static (double Angle, int Changed, double Median) WorstPop(List<(double Angle, byte[] Pixels)> frames)
        {
            int pixels = frames[0].Pixels.Length / 4;
            var steps = new List<(double Angle, int Changed)>();
            for (int f = 1; f < frames.Count; f++)
            {
                byte[] previous = frames[f - 1].Pixels;
                byte[] current = frames[f].Pixels;
                steps.Add((frames[f].Angle, Enumerable.Range(0, pixels).Count(i => Differs(previous, current, i, PopChannelTolerance))));
            }

            int[] sorted = steps.Select(s => s.Changed).OrderBy(c => c).ToArray();
            double median = sorted[sorted.Length / 2];
            (double angle, int changed) = steps.OrderByDescending(s => s.Changed).First();
            return (angle, changed, median);
        }

        private static List<(double Angle, byte[] Pixels)> RenderSweep(string axis, string fixedValues, double transitionDegrees)
        {
            return Sweep(TestFace.Value.Model, axis, fixedValues, transitionDegrees)
                .Select(rig => (rig.Parameters.Get(axis), ReferenceRenderer.Render(rig, Textures.Value, Background).Pixels))
                .ToList();
        }

        private static bool Differs(byte[] a, byte[] b, int pixel, int tolerance)
        {
            int k = pixel * 4;
            return Math.Abs(a[k] - b[k]) > tolerance
                || Math.Abs(a[k + 1] - b[k + 1]) > tolerance
                || Math.Abs(a[k + 2] - b[k + 2]) > tolerance;
        }

        private static IEnumerable<ModelRig> Sweep(Model model, string axis, string fixedValues, double transitionDegrees = ModelRig.DefaultViewTransitionDegrees)
        {
            for (int angle = -30; angle <= 30; angle++)
            {
                var rig = new ModelRig(model) { ViewTransitionDegrees = transitionDegrees };
                foreach (string assignment in fixedValues.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] pair = assignment.Split('=');
                    rig.Parameters.Set(pair[0], double.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture));
                }

                rig.Parameters.Set(axis, angle);
                rig.Update();
                yield return rig;
            }
        }
    }
}
