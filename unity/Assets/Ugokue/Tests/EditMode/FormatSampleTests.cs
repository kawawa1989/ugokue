using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Ugokue.Core.Format;

namespace Ugokue.Tests.EditMode
{
    public class FormatSampleTests
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private static string SampleDirectory => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../../format/samples/minimal"));

        private string tempDirectory;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "ugokue-format-sample-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void MinimalSampleLoadsWithPngTextures()
        {
            ModelPackage package = ModelPackage.Load(SampleDirectory);

            Assert.That(package.Model.Format, Is.EqualTo("ugokue-model"));
            Assert.That(package.Model.Version, Does.Match(@"^0\.1\.[0-9]+$"));
            Assert.That(package.Model.Meshes, Is.Not.Empty);
            foreach (Mesh mesh in package.Model.Meshes)
            {
                Assert.That(package.Textures.ContainsKey(mesh.Texture), Is.True, mesh.Texture);
                byte[] bytes = package.Textures[mesh.Texture];
                Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(PngSignature.Length), mesh.Texture);
                Assert.That(bytes.Take(PngSignature.Length).ToArray(), Is.EqualTo(PngSignature), mesh.Texture);
            }
        }

        [Test]
        public void MinimalSampleRoundTripsThroughSave()
        {
            string first = Path.Combine(tempDirectory, "first");
            string second = Path.Combine(tempDirectory, "second");

            ModelPackage.Load(SampleDirectory).Save(first);
            ModelPackage reloaded = ModelPackage.Load(first);
            reloaded.Save(second);

            string original = File.ReadAllText(Path.Combine(SampleDirectory, ModelPackage.ModelFileName));
            string firstText = File.ReadAllText(Path.Combine(first, ModelPackage.ModelFileName));
            string secondText = File.ReadAllText(Path.Combine(second, ModelPackage.ModelFileName));
            Assert.That(secondText, Is.EqualTo(firstText));
            Assert.That(JsonValueEquals(JToken.Parse(firstText), JToken.Parse(original)), Is.True);
            foreach (Mesh mesh in reloaded.Model.Meshes)
            {
                Assert.That(
                    File.ReadAllBytes(Path.Combine(second, mesh.Texture)),
                    Is.EqualTo(File.ReadAllBytes(Path.Combine(SampleDirectory, mesh.Texture))),
                    mesh.Texture);
            }
        }

        /// <summary>
        /// 2つの JSON が値として等しいかを、整数と小数の書き方の違いを区別せずに判定する。
        /// </summary>
        private static bool JsonValueEquals(JToken a, JToken b)
        {
            if (IsNumber(a) && IsNumber(b))
            {
                return a.Value<double>() == b.Value<double>();
            }

            if (a.Type != b.Type)
            {
                return false;
            }

            if (a is JObject objectA)
            {
                var objectB = (JObject)b;
                return objectA.Count == objectB.Count
                    && objectA.Properties().All(p => objectB.TryGetValue(p.Name, out JToken value) && JsonValueEquals(p.Value, value));
            }

            if (a is JArray arrayA)
            {
                var arrayB = (JArray)b;
                return arrayA.Count == arrayB.Count && arrayA.Zip(arrayB, JsonValueEquals).All(equal => equal);
            }

            return JToken.DeepEquals(a, b);
        }

        private static bool IsNumber(JToken token)
        {
            return token.Type == JTokenType.Integer || token.Type == JTokenType.Float;
        }
    }
}
