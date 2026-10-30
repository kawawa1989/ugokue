using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ugokue.App.Rig;
using Ugokue.Editor;
using UnityEngine;

namespace Ugokue.Tests.EditMode
{
    public class StreamingModelBuildHookTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "ugokue-streaming-" + Guid.NewGuid().ToString("N"), "StreamingAssets");
        }

        [TearDown]
        public void TearDown()
        {
            string parent = Path.GetDirectoryName(root);
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, true);
            }
        }

        [Test]
        public void CopyModelCopiesModelJsonAndAllPngs()
        {
            string source = ModelLocation.RepositorySampleDirectory(Application.dataPath);

            StreamingModelBuildHook.CopyModel(source, root);

            string destination = Path.Combine(root, "Models", "test-face");
            Assert.That(File.ReadAllBytes(Path.Combine(destination, "model.json")), Is.EqualTo(File.ReadAllBytes(Path.Combine(source, "model.json"))));
            string[] expected = Directory.GetFiles(Path.Combine(source, "textures"), "*.png").Select(Path.GetFileName).OrderBy(n => n).ToArray();
            string[] copied = Directory.GetFiles(Path.Combine(destination, "textures")).Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.That(expected, Is.Not.Empty);
            Assert.That(copied, Is.EqualTo(expected));
            Assert.That(Directory.GetFiles(destination), Has.Length.EqualTo(1));
        }

        [Test]
        public void RemoveModelDeletesCopyMetaFilesAndEmptyFolders()
        {
            StreamingModelBuildHook.CopyModel(ModelLocation.RepositorySampleDirectory(Application.dataPath), root);
            File.WriteAllText(root + ".meta", string.Empty);
            File.WriteAllText(Path.Combine(root, "Models.meta"), string.Empty);
            File.WriteAllText(Path.Combine(root, "Models", "test-face.meta"), string.Empty);

            StreamingModelBuildHook.RemoveModel(root);

            Assert.That(Directory.Exists(root), Is.False);
            Assert.That(File.Exists(root + ".meta"), Is.False);
        }

        [Test]
        public void RemoveModelKeepsOtherStreamingAssets()
        {
            StreamingModelBuildHook.CopyModel(ModelLocation.RepositorySampleDirectory(Application.dataPath), root);
            File.WriteAllText(Path.Combine(root, "other.txt"), "keep");

            StreamingModelBuildHook.RemoveModel(root);

            Assert.That(Directory.Exists(Path.Combine(root, "Models")), Is.False);
            Assert.That(File.Exists(Path.Combine(root, "other.txt")), Is.True);
        }
    }
}
