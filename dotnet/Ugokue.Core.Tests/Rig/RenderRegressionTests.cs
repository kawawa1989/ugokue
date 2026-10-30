using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ugokue.Core.Format;
using Ugokue.Core.Imaging;
using Ugokue.Core.Rig;
using Xunit;

namespace Ugokue.Core.Tests.Rig
{
    public class RenderRegressionTests
    {
        private const int ChannelTolerance = 8;
        private const double AllowedDifferentPixelRatio = 0.001;
        private static readonly byte[] Background = { 240, 240, 246, 255 };

        private static readonly Lazy<ModelPackage> TestFace = new Lazy<ModelPackage>(() => ModelPackage.Load(TestPaths.TestFaceDirectory));

        public static IEnumerable<object[]> Cases => new[]
        {
            new object[] { "default", "" },
            new object[] { "angle-right", "AngleX=30" },
            new object[] { "angle-up-left", "AngleX=-30,AngleY=30" },
            new object[] { "angle-down", "AngleY=-30" },
            new object[] { "angle-between", "AngleX=12.5,AngleY=-7.5" },
            new object[] { "eyes-closed", "EyeOpenL=0,EyeOpenR=0" },
            new object[] { "mouth-open", "MouthOpenY=1" },
            new object[] { "tilt-breath", "AngleZ=20,Breath=1" },
            new object[] { "combined", "AngleX=-18,AngleY=10,AngleZ=-8,EyeOpenL=0.4,EyeOpenR=0.7,MouthOpenY=0.5,Breath=0.5" },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void RenderMatchesBaseline(string name, string parameters)
        {
            ModelPackage package = TestFace.Value;
            var rig = new ModelRig(package.Model);
            foreach (string assignment in parameters.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = assignment.Split('=');
                rig.Parameters.Set(pair[0], double.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture));
            }

            rig.Update();
            Dictionary<string, RgbaImage> textures = package.Textures.ToDictionary(t => t.Key, t => Png.Decode(t.Value));
            RgbaImage actual = ReferenceRenderer.Render(rig, textures, Background);

            string baselinePath = Path.Combine(BaselineDirectory, name + ".png");
            if (Environment.GetEnvironmentVariable("UGOKUE_UPDATE_BASELINES") == "1")
            {
                Directory.CreateDirectory(BaselineDirectory);
                File.WriteAllBytes(baselinePath, Png.Encode(actual));
                return;
            }

            if (!File.Exists(baselinePath))
            {
                WriteFailure(name, actual, null);
                Assert.Fail($"Baseline {baselinePath} is missing. Run with UGOKUE_UPDATE_BASELINES=1 to create it.");
            }

            RgbaImage expected = Png.Decode(File.ReadAllBytes(baselinePath));
            (int different, RgbaImage diff) = Compare(expected, actual);
            int allowed = (int)(expected.Width * expected.Height * AllowedDifferentPixelRatio);
            if (different > allowed)
            {
                WriteFailure(name, actual, diff);
            }

            Assert.True(different <= allowed, $"{name}: {different} pixels differ by more than {ChannelTolerance} (allowed {allowed}).");
        }

        [Fact]
        public void ComparisonDetectsMovedPart()
        {
            ModelPackage package = TestFace.Value;
            Dictionary<string, RgbaImage> textures = package.Textures.ToDictionary(t => t.Key, t => Png.Decode(t.Value));
            var rig = new ModelRig(package.Model);
            RgbaImage before = ReferenceRenderer.Render(rig, textures, Background);
            rig.Parameters.Set("EyeOpenL", 0);
            rig.Update();
            RgbaImage after = ReferenceRenderer.Render(rig, textures, Background);

            (int different, _) = Compare(before, after);

            Assert.True(different > before.Width * before.Height * AllowedDifferentPixelRatio, $"only {different} pixels differ");
        }

        private static string BaselineDirectory => Path.Combine(TestPaths.RepositoryRoot, "dotnet", "Ugokue.Core.Tests", "Rig", "Baselines");

        private static (int Different, RgbaImage Diff) Compare(RgbaImage expected, RgbaImage actual)
        {
            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(expected.Height, actual.Height);
            var diff = new RgbaImage(expected.Width, expected.Height);
            int different = 0;
            for (int i = 0; i < expected.Width * expected.Height; i++)
            {
                int max = 0;
                for (int c = 0; c < 4; c++)
                {
                    max = Math.Max(max, Math.Abs(expected.Pixels[i * 4 + c] - actual.Pixels[i * 4 + c]));
                }

                bool differs = max > ChannelTolerance;
                different += differs ? 1 : 0;
                diff.Pixels[i * 4] = differs ? (byte)255 : (byte)(expected.Pixels[i * 4] / 4);
                diff.Pixels[i * 4 + 1] = differs ? (byte)0 : (byte)(expected.Pixels[i * 4 + 1] / 4);
                diff.Pixels[i * 4 + 2] = differs ? (byte)0 : (byte)(expected.Pixels[i * 4 + 2] / 4);
                diff.Pixels[i * 4 + 3] = 255;
            }

            return (different, diff);
        }

        private static void WriteFailure(string name, RgbaImage actual, RgbaImage diff)
        {
            string directory = Environment.GetEnvironmentVariable("UGOKUE_RENDER_OUTPUT")
                ?? Path.Combine(TestPaths.RepositoryRoot, "artifacts", "render-regression");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name + ".actual.png"), Png.Encode(actual));
            if (diff != null)
            {
                File.WriteAllBytes(Path.Combine(directory, name + ".diff.png"), Png.Encode(diff));
            }
        }
    }
}
