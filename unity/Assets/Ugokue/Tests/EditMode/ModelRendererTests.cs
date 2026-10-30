using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ugokue.App.Rig;
using Ugokue.Core.Rig;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ugokue.Tests.EditMode
{
    public class ModelRendererTests
    {
        private const double Tolerance = 1e-4;

        private static readonly string[] ClippedParts = { "iris_r", "iris_l", "teeth" };

        private GameObject gameObject;
        private ModelRenderer modelRenderer;

        [SetUp]
        public void SetUp()
        {
            gameObject = new GameObject("ModelRendererTest");
            modelRenderer = gameObject.AddComponent<ModelRenderer>();
            modelRenderer.Load(ModelLocation.RepositorySampleDirectory(Application.dataPath));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void CreatesOneRendererPerPartWithMeshPlusMaskAndClippedAwayRenderers()
        {
            var partsWithMesh = modelRenderer.Rig.Model.Parts
                .Where(p => p.Mesh != null || modelRenderer.Rig.Model.Meshes.Any(m => m.Part == p.Id))
                .ToList();
            int masks = partsWithMesh.Sum(p => p.ClipBy?.Count ?? 0);
            int clipped = partsWithMesh.Count(p => p.ClipBy != null && p.ClipBy.Count > 0);

            Assert.That(clipped, Is.EqualTo(ClippedParts.Length));
            Assert.That(
                gameObject.GetComponentsInChildren<MeshRenderer>(true),
                Has.Length.EqualTo(partsWithMesh.Count + masks + clipped));
            Assert.That(modelRenderer.GetPartRenderer("head"), Is.Null);
            foreach (var part in partsWithMesh)
            {
                Assert.That(modelRenderer.GetPartRenderer(part.Id), Is.Not.Null, part.Id);
            }
        }

        [Test]
        public void VerticesFollowRigPositionsAfterTurningRight()
        {
            modelRenderer.Rig.Parameters.Set("AngleX", 30);
            modelRenderer.Apply();

            var canvas = modelRenderer.Rig.Model.Canvas;
            foreach (DrawItem item in modelRenderer.Rig.DrawList)
            {
                double[] positions = modelRenderer.Rig.GetPositions(item.Mesh.Id);
                MeshFilter filter = modelRenderer.GetPartRenderer(item.Part.Id).GetComponent<MeshFilter>();
                Assert.That(filter.sharedMesh, Is.SameAs(modelRenderer.GetUnityMesh(item.Mesh.Id)), item.Part.Id);
                Vector3[] vertices = filter.sharedMesh.vertices;
                Assert.That(vertices.Length * 2, Is.EqualTo(positions.Length), item.Mesh.Id);
                for (int v = 0; v < vertices.Length; v++)
                {
                    double x = (positions[v * 2] - canvas.Width / 2.0) / modelRenderer.PixelsPerUnit;
                    double y = (canvas.Height / 2.0 - positions[v * 2 + 1]) / modelRenderer.PixelsPerUnit;
                    Assert.That(vertices[v].x, Is.EqualTo(x).Within(Tolerance), $"{item.Mesh.Id}[{v}].x");
                    Assert.That(vertices[v].y, Is.EqualTo(y).Within(Tolerance), $"{item.Mesh.Id}[{v}].y");
                    Assert.That(vertices[v].z, Is.EqualTo(0));
                }
            }

            Assert.That(modelRenderer.GetPartRenderer("ear_l").enabled, Is.False);
            Assert.That(modelRenderer.GetPartRenderer("ear_r").enabled, Is.True);
        }

        [TestCase(0)]
        [TestCase(15)]
        [TestCase(30)]
        [TestCase(-30)]
        public void SortingOrderFollowsDrawListWithMasksBeforeTheirPart(double angleX)
        {
            modelRenderer.Rig.Parameters.Set("AngleX", angleX);
            modelRenderer.Apply();

            var drawList = modelRenderer.Rig.DrawList;
            int previous = int.MinValue;
            int expectedEnabled = 0;
            for (int i = 0; i < drawList.Count; i++)
            {
                DrawItem item = drawList[i];
                for (int j = 0; j < item.Masks.Count; j++)
                {
                    MeshRenderer maskRenderer = modelRenderer.GetMaskRenderer(item.Part.Id, j, item.Instance);
                    Assert.That(maskRenderer.enabled, Is.True, $"{item.Part.Id}.mask{j}");
                    Assert.That(maskRenderer.sortingOrder, Is.GreaterThan(previous), $"{item.Part.Id}.mask{j}");
                    previous = maskRenderer.sortingOrder;
                    expectedEnabled++;
                }

                MeshRenderer partRenderer = modelRenderer.GetPartRenderer(item.Part.Id, item.Instance);
                Assert.That(partRenderer.enabled, Is.True, item.Part.Id);
                Assert.That(partRenderer.sortingOrder, Is.GreaterThan(previous), item.Part.Id);
                previous = partRenderer.sortingOrder;
                expectedEnabled++;
            }

            int enabled = gameObject.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.enabled);
            Assert.That(enabled, Is.EqualTo(expectedEnabled));
        }

        [Test]
        public void ClippedPartsHaveOneMaskRendererUsingTheMaskPartMesh()
        {
            foreach (string partId in ClippedParts)
            {
                DrawItem item = modelRenderer.Rig.DrawList.Single(d => d.Part.Id == partId);
                Assert.That(item.Masks.Count, Is.EqualTo(1), partId);
                MeshRenderer maskRenderer = modelRenderer.GetMaskRenderer(partId, 0);
                Assert.That(maskRenderer, Is.Not.Null, partId);
                Assert.That(maskRenderer.name, Is.EqualTo(partId + ".mask0"));
                Assert.That(modelRenderer.GetMaskRenderer(partId, 1), Is.Null, partId);
                Mesh maskMesh = maskRenderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(maskMesh, Is.SameAs(modelRenderer.GetUnityMesh(item.Masks[0].Mesh.Id)), partId);
                MeshRenderer maskPartRenderer = modelRenderer.GetPartRenderer(item.Masks[0].Part.Id);
                Assert.That(maskMesh, Is.SameAs(maskPartRenderer.GetComponent<MeshFilter>().sharedMesh), partId);
                Assert.That(maskRenderer.sharedMaterial.shader.name, Is.EqualTo(ModelRenderer.MaskShaderName), partId);
                Assert.That(maskRenderer.sortingOrder, Is.LessThan(modelRenderer.GetPartRenderer(partId).sortingOrder), partId);
            }
        }

        [Test]
        public void MaskVerticesFollowTheMaskPartAfterMoving()
        {
            modelRenderer.Rig.Parameters.Set("AngleX", 30);
            modelRenderer.Rig.Parameters.Set("AngleY", 20);
            modelRenderer.Apply();

            var canvas = modelRenderer.Rig.Model.Canvas;
            foreach (string partId in ClippedParts)
            {
                ClipMask mask = modelRenderer.Rig.DrawList.Single(d => d.Part.Id == partId).Masks[0];
                double[] positions = modelRenderer.Rig.GetPositions(mask.Mesh.Id);
                Vector3[] vertices = modelRenderer.GetMaskRenderer(partId, 0).GetComponent<MeshFilter>().sharedMesh.vertices;
                for (int v = 0; v < vertices.Length; v++)
                {
                    double x = (positions[v * 2] - canvas.Width / 2.0) / modelRenderer.PixelsPerUnit;
                    double y = (canvas.Height / 2.0 - positions[v * 2 + 1]) / modelRenderer.PixelsPerUnit;
                    Assert.That(vertices[v].x, Is.EqualTo(x).Within(Tolerance), $"{mask.Mesh.Id}[{v}].x");
                    Assert.That(vertices[v].y, Is.EqualTo(y).Within(Tolerance), $"{mask.Mesh.Id}[{v}].y");
                }
            }
        }

        [Test]
        public void StencilValuesOfClippedPartsDoNotOverlap()
        {
            var used = new Dictionary<int, string>();
            foreach (string partId in ClippedParts)
            {
                Material mask = modelRenderer.GetMaskRenderer(partId, 0).sharedMaterial;
                Material part = modelRenderer.GetPartRenderer(partId).sharedMaterial;
                int maskRef = mask.GetInt("_StencilRef");

                Assert.That(maskRef, Is.InRange(1, 255), partId);
                Assert.That(mask.GetInt("_StencilComp"), Is.EqualTo((int)CompareFunction.Always), partId);
                Assert.That(mask.GetInt("_StencilPass"), Is.EqualTo((int)StencilOp.Replace), partId);
                Assert.That(part.shader.name, Is.EqualTo(ModelRenderer.PartShaderName), partId);
                Assert.That(part.GetInt("_StencilComp"), Is.EqualTo((int)CompareFunction.Equal), partId);
                Assert.That(part.GetInt("_StencilRef"), Is.EqualTo(maskRef), partId);
                Assert.That(used.ContainsKey(maskRef), Is.False, $"{partId} reuses stencil {maskRef}");
                used[maskRef] = partId;
            }
        }

        [Test]
        public void UnclippedPartsDoNotUseTheStencil()
        {
            foreach (DrawItem item in modelRenderer.Rig.DrawList.Where(d => d.Masks.Count == 0))
            {
                Material material = modelRenderer.GetPartRenderer(item.Part.Id).sharedMaterial;
                Assert.That(material.shader.name, Is.EqualTo(ModelRenderer.PartShaderName), item.Part.Id);
                Assert.That(material.GetInt("_StencilComp"), Is.EqualTo((int)CompareFunction.Always), item.Part.Id);
                Assert.That(modelRenderer.GetMaskRenderer(item.Part.Id, 0), Is.Null, item.Part.Id);
                Assert.That(modelRenderer.GetClippedAwayRenderer(item.Part.Id), Is.Null, item.Part.Id);
            }
        }

        [Test]
        public void DebugViewColorsPartsByRankAndShowsClippedAwayShapes()
        {
            var normalMaterials = modelRenderer.Rig.DrawList
                .ToDictionary(d => d.Part.Id, d => modelRenderer.GetPartRenderer(d.Part.Id).sharedMaterial);
            foreach (string partId in ClippedParts)
            {
                Assert.That(modelRenderer.GetClippedAwayRenderer(partId).enabled, Is.False, partId);
            }

            modelRenderer.DebugView = true;

            var drawList = modelRenderer.Rig.DrawList;
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < drawList.Count; i++)
            {
                DrawItem item = drawList[i];
                MeshRenderer partRenderer = modelRenderer.GetPartRenderer(item.Part.Id);
                Assert.That(partRenderer.sharedMaterial.shader.name, Is.EqualTo(ModelRenderer.DebugShaderName), item.Part.Id);
                partRenderer.GetPropertyBlock(block);
                AssertColor(block.GetColor("_DebugColor"), DebugColors.ForRank(i, drawList.Count), item.Part.Id);
                Assert.That(block.GetFloat("_Opacity"), Is.EqualTo(item.Opacity).Within(Tolerance), item.Part.Id);
                int expectedComp = item.Masks.Count > 0 ? (int)CompareFunction.Equal : (int)CompareFunction.Always;
                Assert.That(partRenderer.sharedMaterial.GetInt("_StencilComp"), Is.EqualTo(expectedComp), item.Part.Id);
            }

            foreach (string partId in ClippedParts)
            {
                MeshRenderer clippedAway = modelRenderer.GetClippedAwayRenderer(partId);
                MeshRenderer maskRenderer = modelRenderer.GetMaskRenderer(partId, 0);
                Assert.That(clippedAway.enabled, Is.True, partId);
                Assert.That(clippedAway.sharedMaterial.shader.name, Is.EqualTo(ModelRenderer.DebugShaderName), partId);
                Assert.That(clippedAway.sharedMaterial.GetInt("_StencilComp"), Is.EqualTo((int)CompareFunction.Always), partId);
                Assert.That(
                    clippedAway.GetComponent<MeshFilter>().sharedMesh,
                    Is.SameAs(modelRenderer.GetPartRenderer(partId).GetComponent<MeshFilter>().sharedMesh),
                    partId);
                Assert.That(clippedAway.sortingOrder, Is.LessThan(maskRenderer.sortingOrder), partId);
                clippedAway.GetPropertyBlock(block);
                AssertColor(block.GetColor("_DebugColor"), DebugColors.ClippedAway, partId);
            }

            modelRenderer.DebugView = false;

            foreach (DrawItem item in modelRenderer.Rig.DrawList)
            {
                Assert.That(
                    modelRenderer.GetPartRenderer(item.Part.Id).sharedMaterial,
                    Is.SameAs(normalMaterials[item.Part.Id]),
                    item.Part.Id);
            }

            foreach (string partId in ClippedParts)
            {
                Assert.That(modelRenderer.GetClippedAwayRenderer(partId).enabled, Is.False, partId);
            }
        }

        [Test]
        public void BlendsInstancesOfAPartAtTheViewBoundary()
        {
            modelRenderer.Rig.Parameters.Set("AngleX", 15);
            modelRenderer.Apply();

            var drawList = modelRenderer.Rig.DrawList.ToList();
            var hair = drawList.Where(d => d.Part.Id == "hair_side_l").OrderBy(d => d.Instance).ToList();
            Assert.That(hair, Has.Count.EqualTo(2));
            Assert.That(hair[0].Opacity, Is.EqualTo(1).Within(Tolerance));
            Assert.That(hair[1].Opacity, Is.EqualTo(0.5).Within(Tolerance));
            Assert.That(hair[0].DrawOrder, Is.LessThan(hair[1].DrawOrder));

            MeshRenderer back = modelRenderer.GetPartRenderer("hair_side_l", 0);
            MeshRenderer front = modelRenderer.GetPartRenderer("hair_side_l", 1);
            Assert.That(back.name, Is.EqualTo("hair_side_l"));
            Assert.That(front.name, Is.EqualTo("hair_side_l#1"));
            Assert.That(back.enabled, Is.True);
            Assert.That(front.enabled, Is.True);
            Assert.That(Opacity(back), Is.EqualTo(hair[0].Opacity).Within(Tolerance));
            Assert.That(Opacity(front), Is.EqualTo(hair[1].Opacity).Within(Tolerance));
            Assert.That(back.sortingOrder, Is.LessThan(front.sortingOrder));

            int face = drawList.FindIndex(d => d.Part.Id == "face");
            Assert.That(drawList.IndexOf(hair[0]), Is.LessThan(face));
            Assert.That(drawList.IndexOf(hair[1]), Is.GreaterThan(face));
            int faceOrder = modelRenderer.GetPartRenderer("face").sortingOrder;
            Assert.That(back.sortingOrder, Is.LessThan(faceOrder));
            Assert.That(front.sortingOrder, Is.GreaterThan(faceOrder));

            DrawItem ear = drawList.Single(d => d.Part.Id == "ear_l");
            Assert.That(ear.Opacity, Is.EqualTo(0.5).Within(Tolerance));
            MeshRenderer earRenderer = modelRenderer.GetPartRenderer("ear_l");
            Assert.That(earRenderer.enabled, Is.True);
            Assert.That(Opacity(earRenderer), Is.EqualTo(ear.Opacity).Within(Tolerance));
        }

        [TestCase(0)]
        [TestCase(30)]
        public void AwayFromViewBoundariesEachPartHasOneOpaqueInstance(double angleX)
        {
            modelRenderer.Rig.Parameters.Set("AngleX", angleX);
            modelRenderer.Apply();

            var drawList = modelRenderer.Rig.DrawList;
            Assert.That(drawList.Select(d => d.Part.Id).Distinct().Count(), Is.EqualTo(drawList.Count));
            foreach (DrawItem item in drawList)
            {
                Assert.That(item.Instance, Is.EqualTo(0), item.Part.Id);
                Assert.That(item.Opacity, Is.EqualTo(1), item.Part.Id);
            }

            foreach (MeshRenderer meshRenderer in gameObject.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled))
            {
                Assert.That(meshRenderer.name, Does.Not.Contain("#"));
                Assert.That(Opacity(meshRenderer), Is.EqualTo(1), meshRenderer.name);
            }
        }

        [Test]
        public void SweepingAcrossTheViewBoundaryLeavesNoStrayInstances()
        {
            for (int angle = 0; angle <= 30; angle++)
            {
                modelRenderer.Rig.Parameters.Set("AngleX", angle);
                modelRenderer.Apply();
            }

            for (int angle = 30; angle >= 0; angle--)
            {
                modelRenderer.Rig.Parameters.Set("AngleX", angle);
                modelRenderer.Apply();
            }

            Assert.That(modelRenderer.GetInstanceCount("hair_side_l"), Is.GreaterThanOrEqualTo(2));
            var drawList = modelRenderer.Rig.DrawList;
            int expectedEnabled = drawList.Count + drawList.Sum(d => d.Masks.Count);
            var enabled = gameObject.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled).ToList();
            Assert.That(enabled.Where(r => r.name.Contains("#")).Select(r => r.name), Is.Empty);
            Assert.That(enabled, Has.Count.EqualTo(expectedEnabled));
            Assert.That(modelRenderer.GetPartRenderer("hair_side_l", 1).enabled, Is.False);
        }

        [Test]
        public void DebugViewColorsInstancesByRankWithTheirOpacity()
        {
            modelRenderer.Rig.Parameters.Set("AngleX", 15);
            modelRenderer.DebugView = true;

            var drawList = modelRenderer.Rig.DrawList;
            Assert.That(drawList.Any(d => d.Instance > 0), Is.True);
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < drawList.Count; i++)
            {
                DrawItem item = drawList[i];
                string label = $"{item.Part.Id}#{item.Instance}";
                MeshRenderer partRenderer = modelRenderer.GetPartRenderer(item.Part.Id, item.Instance);
                Assert.That(partRenderer.enabled, Is.True, label);
                Assert.That(partRenderer.sharedMaterial.shader.name, Is.EqualTo(ModelRenderer.DebugShaderName), label);
                partRenderer.GetPropertyBlock(block);
                AssertColor(block.GetColor("_DebugColor"), DebugColors.ForRank(i, drawList.Count), label);
                Assert.That(block.GetFloat("_Opacity"), Is.EqualTo(item.Opacity).Within(Tolerance), label);
                if (item.Masks.Count > 0)
                {
                    MeshRenderer clippedAway = modelRenderer.GetClippedAwayRenderer(item.Part.Id, item.Instance);
                    Assert.That(clippedAway.enabled, Is.True, label);
                    clippedAway.GetPropertyBlock(block);
                    Assert.That(block.GetFloat("_Opacity"), Is.EqualTo(item.Opacity).Within(Tolerance), label);
                }
            }

            modelRenderer.DebugView = false;
        }

        private static float Opacity(MeshRenderer meshRenderer)
        {
            var block = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(block);
            return block.GetFloat("_Opacity");
        }

        private static void AssertColor(Color actual, byte[] expected, string message)
        {
            Assert.That(actual.r * 255, Is.EqualTo(expected[0]).Within(0.5), message + " r");
            Assert.That(actual.g * 255, Is.EqualTo(expected[1]).Within(0.5), message + " g");
            Assert.That(actual.b * 255, Is.EqualTo(expected[2]).Within(0.5), message + " b");
            Assert.That(actual.a * 255, Is.EqualTo(expected.Length > 3 ? expected[3] : 255).Within(0.5), message + " a");
        }

        [Test]
        public void UvsAreFlippedToBottomLeftOrigin()
        {
            foreach (var mesh in modelRenderer.Rig.Model.Meshes)
            {
                Vector2[] uvs = modelRenderer.GetUnityMesh(mesh.Id).uv;
                for (int v = 0; v < uvs.Length; v++)
                {
                    Assert.That(uvs[v].x, Is.EqualTo(mesh.Uvs[v][0]).Within(Tolerance), mesh.Id);
                    Assert.That(uvs[v].y, Is.EqualTo(1 - mesh.Uvs[v][1]).Within(Tolerance), mesh.Id);
                }
            }
        }
    }
}
