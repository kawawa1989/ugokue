using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Ugokue.Core.Format;
using Xunit;

namespace Ugokue.Core.Tests.Format
{
    public class ModelPackageTests
    {
        [Fact]
        public void SaveLoadSaveGivesIdenticalBytes()
        {
            using (var temp = new TempDirectory())
            {
                string first = temp.Combine("first");
                string second = temp.Combine("second");

                TestModels.BuildPackage().Save(first);
                ModelPackage loaded = ModelPackage.Load(first);
                loaded.Save(second);

                Assert.Equal(File.ReadAllBytes(Path.Combine(first, "model.json")), File.ReadAllBytes(Path.Combine(second, "model.json")));
                foreach (string texture in new[] { "textures/head.png", "textures/eyes/eye_l.png" })
                {
                    Assert.Equal(File.ReadAllBytes(Path.Combine(first, texture)), File.ReadAllBytes(Path.Combine(second, texture)));
                }

                Assert.Equal(TestModels.HeadPng, File.ReadAllBytes(Path.Combine(second, "textures/head.png")));
                Assert.Equal(TestModels.EyePng, loaded.Textures["textures/eyes/eye_l.png"]);
                Assert.Equal(2, loaded.Textures.Count);
            }
        }

        [Fact]
        public void LoadedModelKeepsValues()
        {
            using (var temp = new TempDirectory())
            {
                TestModels.BuildPackage().Save(temp.Path);

                Model model = ModelPackage.Load(temp.Path).Model;

                Assert.Equal("ugokue-model", model.Format);
                Assert.Equal("0.1.0", model.Version);
                Assert.Equal(0.85, model.Parameters[1].Default);
                Assert.Null(model.Parts[0].Parent);
                Assert.Null(model.Parts[2].Mesh);
                Assert.Equal(new[] { 256.5, 300.25 }, model.Parts[0].Anchor);
                Assert.Equal(new[] { 0, 2, 3 }, model.Meshes[1].Triangles[1]);
                Assert.Equal(-7.25, model.Views[1].Pitch);
                Assert.Equal("eye_l_side", model.Views[1].Overrides[0].Mesh);
                Assert.Null(model.Views[1].Overrides[0].Visible);
                Assert.False(model.Views[1].Overrides[1].Visible);
                Assert.Equal(30.0 / 7.0, model.Deformation.KeyformGroups[0].Keyforms[4].Offsets[0][0]);
                Assert.Equal(-1e-7, model.Deformation.Procedural[1].OffsetPerUnit[1]);
                Assert.Null(model.Deformation.Procedural[0].OffsetPerUnit);
                Assert.Equal(0.333, model.Deformation.Procedural[0].DegreesPerUnit);
                Assert.Equal(0.35, model.Physics.Pendulums[0].Stiffness);
                Assert.Equal(0.25, model.Tracking.Parameters[0].Smoothing);
            }
        }

        [Fact]
        public void WritesUtf8WithoutBomWithLfAndTrailingNewline()
        {
            using (var temp = new TempDirectory())
            {
                TestModels.BuildPackage().Save(temp.Path);

                byte[] bytes = File.ReadAllBytes(temp.Combine("model.json"));
                string text = Encoding.UTF8.GetString(bytes);

                Assert.Equal((byte)'{', bytes[0]);
                Assert.DoesNotContain('\r', text);
                Assert.EndsWith("}\n", text);
                Assert.StartsWith("{\n  \"format\": \"ugokue-model\",\n  \"version\": \"0.1.0\",\n", text);
            }
        }

        [Fact]
        public void WritesPropertiesInSpecOrder()
        {
            JObject root = JObject.Parse(ModelJson.Write(TestModels.Build()));

            Assert.Equal(
                new[] { "format", "version", "canvas", "parameters", "parts", "meshes", "views", "correspondence", "deformation", "physics", "tracking" },
                root.Properties().Select(p => p.Name));
            Assert.Equal(
                new[] { "id", "role", "parent", "anchor", "drawOrder", "mesh", "clipBy" },
                ((JObject)root["parts"][0]).Properties().Select(p => p.Name));
            Assert.Equal(
                new[] { "id", "part", "texture", "vertices", "uvs", "triangles" },
                ((JObject)root["meshes"][0]).Properties().Select(p => p.Name));
            Assert.Equal(
                new[] { "id", "mesh", "parameters", "keys", "interpolation", "keyforms" },
                ((JObject)root["deformation"]["keyformGroups"][0]).Properties().Select(p => p.Name));
            Assert.Equal(
                new[] { "id", "inputs", "output", "stiffness", "damping", "scale" },
                ((JObject)root["physics"]["pendulums"][0]).Properties().Select(p => p.Name));
        }

        [Fact]
        public void WritesNullParentAndNullPartMesh()
        {
            JObject root = JObject.Parse(ModelJson.Write(TestModels.Build()));

            JObject head = (JObject)root["parts"][0];
            JObject group = (JObject)root["parts"][2];
            Assert.Equal(JTokenType.Null, head.Property("parent").Value.Type);
            Assert.Equal(JTokenType.Null, group.Property("mesh").Value.Type);
        }

        [Fact]
        public void OmitsOptionalFieldsWhenNotSet()
        {
            Model model = TestModels.Build();
            model.Physics = null;
            model.Tracking = null;

            JObject root = JObject.Parse(ModelJson.Write(model));

            Assert.Null(root.Property("physics"));
            Assert.Null(root.Property("tracking"));
            JArray overrides = (JArray)root["views"][1]["overrides"];
            Assert.Equal(new[] { "part", "mesh", "drawOrder" }, ((JObject)overrides[0]).Properties().Select(p => p.Name));
            Assert.Equal(new[] { "part", "visible" }, ((JObject)overrides[1]).Properties().Select(p => p.Name));
            JArray procedural = (JArray)root["deformation"]["procedural"];
            Assert.Equal(new[] { "type", "parameter", "part", "degreesPerUnit" }, ((JObject)procedural[0]).Properties().Select(p => p.Name));
            Assert.Equal(new[] { "type", "parameter", "part", "offsetPerUnit" }, ((JObject)procedural[1]).Properties().Select(p => p.Name));
        }

        [Theory]
        [InlineData("0.2.0")]
        [InlineData("1.1.0")]
        [InlineData("0.1")]
        [InlineData("0.1.0-beta")]
        public void RejectsUnsupportedVersion(string version)
        {
            JObject root = JObject.Parse(ModelJson.Write(TestModels.Build()));
            root["version"] = version;

            var error = Assert.Throws<ModelFormatException>(() => ModelJson.Read(root.ToString()));
            Assert.Contains("version", error.Message);
        }

        [Fact]
        public void AcceptsOtherPatchVersion()
        {
            JObject root = JObject.Parse(ModelJson.Write(TestModels.Build()));
            root["version"] = "0.1.7";

            Assert.Equal("0.1.7", ModelJson.Read(root.ToString()).Version);
        }

        [Fact]
        public void RejectsWrongFormat()
        {
            JObject root = JObject.Parse(ModelJson.Write(TestModels.Build()));
            root["format"] = "other-model";

            var error = Assert.Throws<ModelFormatException>(() => ModelJson.Read(root.ToString()));
            Assert.Contains("format", error.Message);
        }

        [Fact]
        public void RejectsMissingFormat()
        {
            JObject root = JObject.Parse(ModelJson.Write(TestModels.Build()));
            root.Remove("format");

            Assert.Throws<ModelFormatException>(() => ModelJson.Read(root.ToString()));
        }

        [Fact]
        public void RejectsUnknownTopLevelProperty()
        {
            JObject root = JObject.Parse(ModelJson.Write(TestModels.Build()));
            root["extra"] = 1;

            var error = Assert.Throws<ModelFormatException>(() => ModelJson.Read(root.ToString()));
            Assert.Contains("extra", error.Message);
        }

        [Fact]
        public void RejectsUnknownNestedProperty()
        {
            JObject root = JObject.Parse(ModelJson.Write(TestModels.Build()));
            root["views"][1]["overrides"][0]["color"] = "red";

            var error = Assert.Throws<ModelFormatException>(() => ModelJson.Read(root.ToString()));
            Assert.Contains("color", error.Message);
        }

        [Fact]
        public void RejectsMissingNullableRequiredProperty()
        {
            JObject root = JObject.Parse(ModelJson.Write(TestModels.Build()));
            ((JObject)root["parts"][2]).Remove("mesh");

            Assert.Throws<ModelFormatException>(() => ModelJson.Read(root.ToString()));
        }

        [Fact]
        public void KeepsDateLikeStringsAsWritten()
        {
            Model model = TestModels.Build();
            model.Parts[0].Role = "2024-01-02T03:04:05Z";

            Assert.Equal("2024-01-02T03:04:05Z", ModelJson.Read(ModelJson.Write(model)).Parts[0].Role);
        }

        [Theory]
        [InlineData("../outside.png")]
        [InlineData("textures/../../outside.png")]
        [InlineData("/tmp/absolute.png")]
        [InlineData("textures\\head.png")]
        [InlineData("C:/absolute.png")]
        public void LoadRejectsTexturePathOutsideFolder(string texture)
        {
            using (var temp = new TempDirectory())
            {
                string modelDir = temp.Combine("model");
                TestModels.BuildPackage().Save(modelDir);
                JObject root = JObject.Parse(File.ReadAllText(Path.Combine(modelDir, "model.json")));
                root["meshes"][0]["texture"] = texture;
                File.WriteAllText(Path.Combine(modelDir, "model.json"), root.ToString());
                File.WriteAllBytes(temp.Combine("outside.png"), TestModels.HeadPng);

                Assert.Throws<ModelFormatException>(() => ModelPackage.Load(modelDir));
            }
        }

        [Fact]
        public void SaveRejectsTexturePathOutsideFolder()
        {
            using (var temp = new TempDirectory())
            {
                ModelPackage package = TestModels.BuildPackage();
                package.Model.Meshes[0].Texture = "../outside.png";
                package.Textures.Remove("textures/head.png");
                package.Textures["../outside.png"] = TestModels.HeadPng;

                Assert.Throws<ModelFormatException>(() => package.Save(temp.Combine("model")));
                Assert.False(File.Exists(temp.Combine("outside.png")));
            }
        }

        [Fact]
        public void LoadRejectsMissingTexture()
        {
            using (var temp = new TempDirectory())
            {
                TestModels.BuildPackage().Save(temp.Path);
                File.Delete(temp.Combine("textures/head.png"));

                Assert.Throws<ModelFormatException>(() => ModelPackage.Load(temp.Path));
            }
        }

        [Fact]
        public void SaveRejectsMissingOrUnreferencedTexture()
        {
            using (var temp = new TempDirectory())
            {
                ModelPackage missing = TestModels.BuildPackage();
                missing.Textures.Remove("textures/head.png");
                Assert.Throws<ModelFormatException>(() => missing.Save(temp.Combine("missing")));

                ModelPackage extra = TestModels.BuildPackage();
                extra.Textures["textures/unused.png"] = TestModels.HeadPng;
                Assert.Throws<ModelFormatException>(() => extra.Save(temp.Combine("extra")));
            }
        }

        [Fact]
        public void NumbersAreCultureInvariant()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                using (var temp = new TempDirectory())
                {
                    TestModels.BuildPackage().Save(temp.Path);
                    string text = File.ReadAllText(temp.Combine("model.json"));

                    Assert.Contains("\"default\": 0.85", text);
                    Assert.Contains("256.5,", text);
                    Assert.DoesNotContain("0,85", text);

                    Model model = ModelPackage.Load(temp.Path).Model;
                    Assert.Equal(0.85, model.Parameters[1].Default);
                    Assert.Equal(22.5, model.Views[1].Yaw);
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }

            using (var temp = new TempDirectory())
            {
                TestModels.BuildPackage().Save(temp.Path);
                string invariantText = File.ReadAllText(temp.Combine("model.json"));
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                try
                {
                    Assert.Equal(invariantText, ModelJson.Write(TestModels.Build()));
                }
                finally
                {
                    Thread.CurrentThread.CurrentCulture = original;
                }
            }
        }
    }
}
