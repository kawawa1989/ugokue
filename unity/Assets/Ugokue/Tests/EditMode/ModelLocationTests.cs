using System.IO;
using NUnit.Framework;
using Ugokue.App.Rig;

namespace Ugokue.Tests.EditMode
{
    public class ModelLocationTests
    {
        private const string DataPath = "/repo/unity/Assets";
        private const string StreamingAssetsPath = "/player/Contents/Resources/Data/StreamingAssets";

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
    }
}
