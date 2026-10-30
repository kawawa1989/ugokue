using System;
using System.Linq;
using Ugokue.Core.Format;
using Ugokue.Core.Rig;
using Xunit;

namespace Ugokue.Core.Tests.Rig
{
    public class ModelRigTests
    {
        private static readonly Lazy<Model> TestFace = new Lazy<Model>(() => ModelPackage.Load(TestPaths.TestFaceDirectory).Model);

        [Fact]
        public void DefaultParametersGiveBaseVertices()
        {
            var rig = new ModelRig(TestFace.Value);

            foreach (Mesh mesh in rig.Model.Meshes)
            {
                double[] positions = rig.GetPositions(mesh.Id);
                for (int v = 0; v < mesh.Vertices.Length; v++)
                {
                    Assert.Equal(mesh.Vertices[v][0], positions[v * 2]);
                    Assert.Equal(mesh.Vertices[v][1], positions[v * 2 + 1]);
                }
            }
        }

        [Theory]
        [InlineData(30, -30, 0, 0, 1)]
        [InlineData(-30, 30, 1, 0, 0)]
        [InlineData(0, 30, 0, 1, 1)]
        [InlineData(30, 0, 1, 1, 0)]
        public void MatchesAuthoredShapeAtKeyformPoints(double angleX, double angleY, double eyeOpenL, double eyeOpenR, double mouthOpenY)
        {
            var rig = new ModelRig(TestFace.Value);
            rig.Parameters.Set("AngleX", angleX);
            rig.Parameters.Set("AngleY", angleY);
            rig.Parameters.Set("EyeOpenL", eyeOpenL);
            rig.Parameters.Set("EyeOpenR", eyeOpenR);
            rig.Parameters.Set("MouthOpenY", mouthOpenY);
            rig.Update();

            foreach (Mesh mesh in rig.Model.Meshes)
            {
                double[] expected = mesh.Vertices.SelectMany(v => v).ToArray();
                foreach (KeyformGroup group in rig.Model.Deformation.KeyformGroups.Where(g => g.Mesh == mesh.Id))
                {
                    double[] at = group.Parameters.Select(p => rig.Parameters.Get(p)).ToArray();
                    Keyform keyform = group.Keyforms.Single(k => k.At.SequenceEqual(at));
                    for (int v = 0; v < mesh.Vertices.Length; v++)
                    {
                        expected[v * 2] += keyform.Offsets[v][0];
                        expected[v * 2 + 1] += keyform.Offsets[v][1];
                    }
                }

                Assert.Equal(expected, rig.GetPositions(mesh.Id));
            }
        }

        [Fact]
        public void FaceMatchesVertexCorrespondenceInEveryView()
        {
            var rig = new ModelRig(TestFace.Value);
            Assert.NotEmpty(rig.Model.Correspondence.VertexMatches);

            foreach (VertexMatch match in rig.Model.Correspondence.VertexMatches)
            {
                View view = rig.Model.Views.Single(v => v.Id == match.View);
                rig.Parameters.Set("AngleX", view.Yaw);
                rig.Parameters.Set("AngleY", view.Pitch);
                rig.Update();

                double[] positions = rig.GetPositions(match.Mesh);
                Assert.InRange(Math.Abs(positions[match.Vertex * 2] - match.Position[0]), 0, 0.011);
                Assert.InRange(Math.Abs(positions[match.Vertex * 2 + 1] - match.Position[1]), 0, 0.011);
            }
        }

        [Fact]
        public void ClampsParametersOutsideTheirRange()
        {
            var clamped = new ModelRig(TestFace.Value);
            clamped.Parameters.Set("AngleX", 100);
            clamped.Update();
            var edge = new ModelRig(TestFace.Value);
            edge.Parameters.Set("AngleX", 30);
            edge.Update();

            Assert.Equal(edge.GetPositions("face"), clamped.GetPositions("face"));
        }

        [Theory]
        [InlineData(0, 0, "front")]
        [InlineData(14, 0, "front")]
        [InlineData(15, 0, "front")]
        [InlineData(16, 0, "right")]
        [InlineData(-20, 25, "up-left")]
        [InlineData(30, -30, "down-right")]
        public void SelectsNearestViewAndFirstOnTie(double angleX, double angleY, string expected)
        {
            var rig = new ModelRig(TestFace.Value);
            rig.Parameters.Set("AngleX", angleX);
            rig.Parameters.Set("AngleY", angleY);
            rig.Update();

            Assert.Equal(expected, rig.CurrentView.Id);
        }

        [Fact]
        public void DrawListHidesOverriddenPartsAndSortsByDrawOrderThenPartOrder()
        {
            var rig = new ModelRig(TestFace.Value);
            rig.Parameters.Set("AngleX", 30);
            rig.Update();

            string[] ids = rig.DrawList.Select(d => d.Part.Id).ToArray();
            Assert.DoesNotContain("ear_l", ids);
            Assert.Contains("ear_r", ids);
            Assert.DoesNotContain("head", ids);
            string[] expected = rig.Model.Parts
                .Select((p, i) => (p, i))
                .Where(x => x.p.Mesh != null && x.p.Id != "ear_l")
                .OrderBy(x => rig.CurrentView.Overrides.FirstOrDefault(o => o.Part == x.p.Id)?.DrawOrder ?? x.p.DrawOrder)
                .ThenBy(x => x.i)
                .Select(x => x.p.Id)
                .ToArray();
            Assert.Equal(expected, ids);
        }

        [Fact]
        public void RotatesHeadDescendantsAroundAnchorThenTranslatesForBreath()
        {
            var rig = new ModelRig(TestFace.Value);
            rig.Parameters.Set("AngleZ", 30);
            rig.Parameters.Set("Breath", 1);
            rig.Update();

            double[] anchor = rig.Model.Parts.Single(p => p.Id == "head").Anchor;
            double radians = 30 * Math.PI / 180;
            double[] face = rig.Model.Meshes.Single(m => m.Id == "face").Vertices[0];
            double dx = face[0] - anchor[0];
            double dy = face[1] - anchor[1];
            double[] positions = rig.GetPositions("face");
            Assert.Equal(anchor[0] + dx * Math.Cos(radians) - dy * Math.Sin(radians), positions[0], 9);
            Assert.Equal(anchor[1] + dx * Math.Sin(radians) + dy * Math.Cos(radians) - 3, positions[1], 9);

            double[] body = rig.Model.Meshes.Single(m => m.Id == "body").Vertices[0];
            Assert.Equal(body[0], rig.GetPositions("body")[0]);
            Assert.Equal(body[1] - 3, rig.GetPositions("body")[1]);
        }

        [Fact]
        public void DrawListUsesViewDrawOrderOverride()
        {
            var rig = new ModelRig(TestFace.Value);
            rig.Parameters.Set("AngleX", 30);
            rig.Parameters.Set("AngleY", 30);
            rig.Update();

            Assert.Equal("up-right", rig.CurrentView.Id);
            Assert.Equal(4, rig.DrawList.Single(d => d.Part.Id == "ear_l").DrawOrder);
            Assert.Equal("ear_l", rig.DrawList[0].Part.Id);
        }
    }
}
