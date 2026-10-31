using System;
using System.Linq;
using Ugokue.Core.Format;
using Ugokue.Core.Rig;
using Xunit;

namespace Ugokue.Core.Tests.Rig
{
    public class KeyformEditingTests
    {
        private static Model TestFace()
        {
            return ModelPackage.Load(TestPaths.TestFaceDirectory).Model;
        }

        private static KeyformGroup AutomaticGroup(Model model, string meshId)
        {
            return model.Deformation.KeyformGroups.Single(g => g.Mesh == meshId && g.Id == meshId + "_angle");
        }

        [Fact]
        public void EditGroupCopiesTheAngleGridWithZeroOffsetsOnce()
        {
            Model model = TestFace();
            int before = model.Deformation.KeyformGroups.Count;

            KeyformGroup group = KeyformEditing.EditGroup(model, "face");
            KeyformGroup again = KeyformEditing.EditGroup(model, "face");

            Assert.Same(group, again);
            Assert.Equal(before + 1, model.Deformation.KeyformGroups.Count);
            Assert.Equal("face_angle_edit", group.Id);
            Assert.Equal(new[] { "AngleX", "AngleY" }, group.Parameters);
            Assert.Equal(AutomaticGroup(model, "face").Keys, group.Keys);
            int vertices = model.Meshes.Single(m => m.Id == "face").Vertices.Length;
            Assert.Equal(9, group.Keyforms.Count);
            Assert.All(group.Keyforms, k => Assert.Equal(vertices, k.Offsets.Length));
            Assert.All(group.Keyforms.SelectMany(k => k.Offsets), o => Assert.Equal(new double[] { 0, 0 }, o));
        }

        [Fact]
        public void MeshWithoutAngleGroupUsesTheGridOfTheModel()
        {
            Model model = TestFace();

            KeyformGroup group = KeyformEditing.EditGroup(model, "body");

            Assert.Equal(AutomaticGroup(model, "face").Keys, group.Keys);
        }

        [Fact]
        public void WeightsFallOffWithDistanceInsideTheRadius()
        {
            double[] positions = { 0, 0, 5, 0, 10, 0, 20, 0 };

            double[] weights = KeyformEditing.Weights(positions, 0, 0, 10);

            Assert.Equal(1, weights[0], 12);
            Assert.Equal(0.5625, weights[1], 12);
            Assert.Equal(0, weights[2], 12);
            Assert.Equal(0, weights[3], 12);
        }

        [Fact]
        public void MoveChangesOnlyTheSelectedGridPoint()
        {
            Model model = TestFace();
            KeyformGroup group = KeyformEditing.EditGroup(model, "nose");
            Keyform keyform = KeyformEditing.KeyformAt(group, 30, 0);
            var weights = new double[keyform.Offsets.Length];
            weights[0] = 1;
            weights[1] = 0.5;

            KeyformEditing.Move(keyform, weights, 4, -2);

            Assert.Equal(new double[] { 4, -2 }, keyform.Offsets[0]);
            Assert.Equal(new double[] { 2, -1 }, keyform.Offsets[1]);
            Assert.All(group.Keyforms.Where(k => k != keyform).SelectMany(k => k.Offsets), o => Assert.Equal(new double[] { 0, 0 }, o));
        }

        [Fact]
        public void KeyformAtRejectsAnglesOffTheGrid()
        {
            KeyformGroup group = KeyformEditing.EditGroup(TestFace(), "nose");

            Assert.Throws<ArgumentException>(() => KeyformEditing.KeyformAt(group, 15, 0));
        }

        [Fact]
        public void EditIsAddedToTheAutomaticOffsetsAndInterpolatedBetweenGridPoints()
        {
            Model model = TestFace();
            double[] before30 = Positions(model, "nose", 30, 0);
            double[] before15 = Positions(model, "nose", 15, 0);
            Keyform keyform = KeyformEditing.KeyformAt(KeyformEditing.EditGroup(model, "nose"), 30, 0);
            var weights = new double[keyform.Offsets.Length];
            weights[0] = 1;

            KeyformEditing.Move(keyform, weights, 6, 0);

            double[] after30 = Positions(model, "nose", 30, 0);
            double[] after15 = Positions(model, "nose", 15, 0);
            Assert.Equal(before30[0] + 6, after30[0], 9);
            Assert.Equal(before15[0] + 3, after15[0], 9);
            Assert.Equal(before30[2], after30[2], 9);
        }

        [Fact]
        public void ResetMovesTheWeightedPartBackToTheAutomaticPosition()
        {
            Keyform keyform = KeyformEditing.KeyformAt(KeyformEditing.EditGroup(TestFace(), "nose"), 30, 0);
            var all = Enumerable.Repeat(1.0, keyform.Offsets.Length).ToArray();
            KeyformEditing.Move(keyform, all, 4, 4);
            var weights = new double[keyform.Offsets.Length];
            weights[0] = 1;
            weights[1] = 0.25;

            KeyformEditing.Reset(keyform, weights);

            Assert.Equal(new double[] { 0, 0 }, keyform.Offsets[0]);
            Assert.Equal(new double[] { 3, 3 }, keyform.Offsets[1]);
            Assert.Equal(new double[] { 4, 4 }, keyform.Offsets[2]);
        }

        [Fact]
        public void SavedEditsLoadBackAndEmptyEditGroupsAreRemoved()
        {
            Model model = TestFace();
            Keyform keyform = KeyformEditing.KeyformAt(KeyformEditing.EditGroup(model, "nose"), -30, 30);
            var weights = new double[keyform.Offsets.Length];
            weights[2] = 1;
            KeyformEditing.Move(keyform, weights, 1.5, -2.25);
            KeyformEditing.EditGroup(model, "face");

            Assert.Equal(1, KeyformEditing.RemoveEmptyEditGroups(model));
            Model loaded = ModelJson.Read(ModelJson.Write(model));

            KeyformGroup group = loaded.Deformation.KeyformGroups.Single(g => g.Id == "nose_angle_edit");
            Assert.DoesNotContain(loaded.Deformation.KeyformGroups, g => g.Id == "face_angle_edit");
            Assert.Equal(new double[] { 1.5, -2.25 }, KeyformEditing.KeyformAt(group, -30, 30).Offsets[2]);
        }

        private static double[] Positions(Model model, string meshId, double angleX, double angleY)
        {
            var rig = new ModelRig(model);
            rig.Parameters.Set("AngleX", angleX);
            rig.Parameters.Set("AngleY", angleY);
            rig.Update();
            return (double[])rig.GetPositions(meshId).Clone();
        }
    }
}
