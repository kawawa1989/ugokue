using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ugokue.App.Rig;

namespace Ugokue.Tests.EditMode
{
    public class ModelLocationTests
    {
        private const string DataPath = "/repo/unity/Assets";
        private const string StreamingAssetsPath = "/player/Contents/Resources/Data/StreamingAssets";

        private string temporary;

        [Test]
        public void ArgumentWinsOverDefaults()
        {
            string[] args = { "player", "-ugokue-model", "/models/custom" };

            Assert.That(ModelLocation.Resolve(args, true, DataPath, StreamingAssetsPath), Is.EqualTo(Path.GetFullPath("/models/custom")));
            Assert.That(ModelLocation.Resolve(args, false, DataPath, StreamingAssetsPath), Is.EqualTo(Path.GetFullPath("/models/custom")));
        }

        [Test]
        public void EditorDefaultsToRepositoryTestFace()
        {
            string resolved = ModelLocation.Resolve(new[] { "unity" }, true, DataPath, StreamingAssetsPath);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath("/repo/format/samples/test-face")));
        }

        [Test]
        public void PlayerDefaultsToStreamingAssets()
        {
            string resolved = ModelLocation.Resolve(new[] { "player" }, false, DataPath, StreamingAssetsPath);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(StreamingAssetsPath + "/Models/test-face")));
        }

        [Test]
        public void ArgumentWithoutValueFallsBackToDefault()
        {
            string atEnd = ModelLocation.Resolve(new[] { "player", "-ugokue-model" }, false, DataPath, StreamingAssetsPath);
            string beforeFlag = ModelLocation.Resolve(new[] { "player", "-ugokue-model", "-batchmode" }, false, DataPath, StreamingAssetsPath);

            string expected = Path.GetFullPath(StreamingAssetsPath + "/Models/test-face");
            Assert.That(atEnd, Is.EqualTo(expected));
            Assert.That(beforeFlag, Is.EqualTo(expected));
        }

        [Test]
        public void EditorSelectionWinsOverTheTestFaceInEditor()
        {
            string selected = CreateDirectory("selected");

            Assert.That(ModelLocation.Resolve(new[] { "unity" }, true, DataPath, StreamingAssetsPath, selected), Is.EqualTo(Path.GetFullPath(selected)));
        }

        [Test]
        public void EditorSelectionIsIgnoredInPlayerAndBelowTheArgument()
        {
            string selected = CreateDirectory("selected");
            string[] args = { "unity", "-ugokue-model", "/models/custom" };

            Assert.That(ModelLocation.Resolve(new[] { "player" }, false, DataPath, StreamingAssetsPath, selected), Is.EqualTo(Path.GetFullPath(StreamingAssetsPath + "/Models/test-face")));
            Assert.That(ModelLocation.Resolve(args, true, DataPath, StreamingAssetsPath, selected), Is.EqualTo(Path.GetFullPath("/models/custom")));
        }

        [Test]
        public void MissingEditorSelectionFallsBackToTheTestFace()
        {
            string missing = Path.Combine(temporary, "missing");

            Assert.That(ModelLocation.Resolve(new[] { "unity" }, true, DataPath, StreamingAssetsPath, missing), Is.EqualTo(Path.GetFullPath("/repo/format/samples/test-face")));
            Assert.That(ModelLocation.Resolve(new[] { "unity" }, true, DataPath, StreamingAssetsPath, ""), Is.EqualTo(Path.GetFullPath("/repo/format/samples/test-face")));
        }

        [Test]
        public void CandidatesAreTheTestFaceAndGeneratedModels()
        {
            string dataPath = CreateDirectory("unity/Assets");
            File.WriteAllText(Path.Combine(CreateDirectory("artifacts/keyforms/02-twintails/model"), "model.json"), "{}");
            File.WriteAllText(Path.Combine(CreateDirectory("artifacts/keyforms/01-long-straight/model"), "model.json"), "{}");
            CreateDirectory("artifacts/keyforms/03-empty/model");

            var candidates = ModelLocation.Candidates(dataPath);

            Assert.That(candidates.Select(c => c.Name), Is.EqualTo(new[] { "test-face", "01-long-straight", "02-twintails" }));
            Assert.That(candidates[0].Directory, Is.EqualTo(Path.GetFullPath(Path.Combine(temporary, "format/samples/test-face"))));
            Assert.That(candidates[1].Directory, Is.EqualTo(Path.GetFullPath(Path.Combine(temporary, "artifacts/keyforms/01-long-straight/model"))));
        }

        [Test]
        public void CandidatesWithoutGeneratedModelsAreOnlyTheTestFace()
        {
            string dataPath = CreateDirectory("unity/Assets");

            Assert.That(ModelLocation.Candidates(dataPath).Select(c => c.Name), Is.EqualTo(new[] { "test-face" }));
        }

        [SetUp]
        public void CreateTemporary()
        {
            temporary = Path.Combine(Path.GetTempPath(), "ugokue-model-location-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
        }

        [TearDown]
        public void DeleteTemporary()
        {
            Directory.Delete(temporary, true);
        }

        private string CreateDirectory(string relative)
        {
            string path = Path.Combine(temporary, relative);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
