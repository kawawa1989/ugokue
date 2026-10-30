using System.Collections.Generic;
using Ugokue.Core.Format;
using Ugokue.Core.Rig;
using Xunit;

namespace Ugokue.Core.Tests.Rig
{
    public class KeyformInterpolatorTests
    {
        [Theory]
        [InlineData(-30, 0, 0.0)]
        [InlineData(-15, 0, 0.5)]
        [InlineData(0, 0, 1.0)]
        [InlineData(10, 1, 1.0 / 3)]
        [InlineData(30, 1, 1.0)]
        public void LocatesSegmentAndFraction(double value, int expectedLower, double expectedT)
        {
            KeyformInterpolator.Locate(new double[] { -30, 0, 30 }, value, out int lower, out double t);

            Assert.Equal(expectedLower, lower);
            Assert.Equal(expectedT, t, 12);
        }

        [Theory]
        [InlineData(-100, 0, 0.0)]
        [InlineData(100, 1, 1.0)]
        public void ClampsValuesOutsideTheGrid(double value, int expectedLower, double expectedT)
        {
            KeyformInterpolator.Locate(new double[] { -30, 0, 30 }, value, out int lower, out double t);

            Assert.Equal(expectedLower, lower);
            Assert.Equal(expectedT, t, 12);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 10)]
        [InlineData(0.25, 2.5)]
        [InlineData(-1, 0)]
        [InlineData(2, 10)]
        public void InterpolatesOneAxisWithClamping(double value, double expected)
        {
            Model model = OneMeshModel(("Open", 0, 1, 0));
            model.Deformation.KeyformGroups.Add(Group("g", new[] { "Open" }, new[] { new double[] { 0, 1 } },
                (new double[] { 0 }, 0), (new double[] { 1 }, 10)));
            var values = new ParameterValues(model);
            var interpolator = new KeyformInterpolator(model.Deformation.KeyformGroups[0], 1);
            values.Set("Open", value);
            var target = new double[2];

            interpolator.AddOffsets(values, target);

            Assert.Equal(expected, target[1], 12);
            Assert.Equal(0, target[0]);
        }

        [Fact]
        public void InterpolatesBilinearlyBetweenFourCorners()
        {
            Model model = OneMeshModel(("X", -1, 1, 0), ("Y", -1, 1, 0));
            model.Deformation.KeyformGroups.Add(Group("g", new[] { "X", "Y" }, new[] { new double[] { 0, 1 }, new double[] { 0, 1 } },
                (new double[] { 0, 0 }, 0), (new double[] { 0, 1 }, 10), (new double[] { 1, 0 }, 20), (new double[] { 1, 1 }, 70)));
            var values = new ParameterValues(model);
            var interpolator = new KeyformInterpolator(model.Deformation.KeyformGroups[0], 1);
            values.Set("X", 0.5);
            values.Set("Y", 0.25);
            var target = new double[2];

            interpolator.AddOffsets(values, target);

            double expected = 0.5 * 0.75 * 0 + 0.5 * 0.25 * 10 + 0.5 * 0.75 * 20 + 0.5 * 0.25 * 70;
            Assert.Equal(expected, target[1], 12);
        }

        [Fact]
        public void ReturnsKeyformExactlyAtGridPoints()
        {
            Model model = OneMeshModel(("X", -30, 30, 0), ("Y", -30, 30, 0));
            var keyforms = new List<(double[], double)>();
            foreach (double x in new double[] { -30, 0, 30 })
            {
                foreach (double y in new double[] { -30, 0, 30 })
                {
                    keyforms.Add((new[] { x, y }, x * 100 + y + 0.125));
                }
            }

            model.Deformation.KeyformGroups.Add(Group("g", new[] { "X", "Y" }, new[] { new double[] { -30, 0, 30 }, new double[] { -30, 0, 30 } }, keyforms.ToArray()));
            var values = new ParameterValues(model);
            var interpolator = new KeyformInterpolator(model.Deformation.KeyformGroups[0], 1);

            foreach ((double[] at, double dy) in keyforms)
            {
                values.Set("X", at[0]);
                values.Set("Y", at[1]);
                var target = new double[2];
                interpolator.AddOffsets(values, target);
                Assert.Equal(dy, target[1]);
            }
        }

        [Fact]
        public void AddsToExistingOffsets()
        {
            Model model = OneMeshModel(("Open", 0, 1, 1));
            model.Deformation.KeyformGroups.Add(Group("g", new[] { "Open" }, new[] { new double[] { 0, 1 } },
                (new double[] { 0 }, 0), (new double[] { 1 }, 4)));
            var interpolator = new KeyformInterpolator(model.Deformation.KeyformGroups[0], 1);
            var target = new double[] { 1, 2 };

            interpolator.AddOffsets(new ParameterValues(model), target);

            Assert.Equal(new double[] { 1, 6 }, target);
        }

        [Fact]
        public void RejectsMissingGridPoint()
        {
            Model model = OneMeshModel(("Open", 0, 1, 0));
            model.Deformation.KeyformGroups.Add(Group("g", new[] { "Open" }, new[] { new double[] { 0, 1 } }, (new double[] { 0 }, 0)));

            Assert.Throws<System.ArgumentException>(() => new KeyformInterpolator(model.Deformation.KeyformGroups[0], 1));
        }

        [Fact]
        public void ParameterValuesClampAndIgnoreUnknownIds()
        {
            var values = new ParameterValues(OneMeshModel(("Open", 0, 1, 0.5)));

            values.Set("Open", 3);
            values.Set("Unknown", 3);
            values.Set("Open", double.NaN);

            Assert.Equal(1, values.Get("Open"));
            Assert.Equal(0, values.Get("Unknown"));
            values.ResetToDefaults();
            Assert.Equal(0.5, values.Get("Open"));
        }

        internal static Model OneMeshModel(params (string Id, double Min, double Max, double Default)[] parameters)
        {
            var model = new Model();
            foreach (var p in parameters)
            {
                model.Parameters.Add(new Parameter { Id = p.Id, Min = p.Min, Max = p.Max, Default = p.Default });
            }

            return model;
        }

        private static KeyformGroup Group(string id, string[] parameters, double[][] keys, params (double[] At, double Dy)[] keyforms)
        {
            var group = new KeyformGroup { Id = id, Mesh = "m", Parameters = new List<string>(parameters), Keys = keys };
            foreach ((double[] at, double dy) in keyforms)
            {
                group.Keyforms.Add(new Keyform { At = at, Offsets = new[] { new[] { 0, dy } } });
            }

            return group;
        }
    }
}
