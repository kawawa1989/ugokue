using System.Linq;
using NUnit.Framework;
using Ugokue.App.Rig;
using Ugokue.Core.Rig;
using UnityEngine;

namespace Ugokue.Tests.EditMode
{
    public class ModelRendererTests
    {
        private const double Tolerance = 1e-4;

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
        public void CreatesOneRendererPerPartWithMesh()
        {
            int partsWithMesh = modelRenderer.Rig.Model.Parts
                .Count(p => p.Mesh != null || modelRenderer.Rig.Model.Meshes.Any(m => m.Part == p.Id));

            Assert.That(gameObject.GetComponentsInChildren<MeshRenderer>(true), Has.Length.EqualTo(partsWithMesh));
            Assert.That(modelRenderer.GetPartRenderer("head"), Is.Null);
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

        [Test]
        public void SortingOrderFollowsDrawList()
        {
            modelRenderer.Rig.Parameters.Set("AngleX", 30);
            modelRenderer.Apply();

            var drawList = modelRenderer.Rig.DrawList;
            for (int i = 0; i < drawList.Count; i++)
            {
                MeshRenderer partRenderer = modelRenderer.GetPartRenderer(drawList[i].Part.Id);
                Assert.That(partRenderer.enabled, Is.True, drawList[i].Part.Id);
                Assert.That(partRenderer.sortingOrder, Is.EqualTo(i), drawList[i].Part.Id);
            }

            int enabled = gameObject.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.enabled);
            Assert.That(enabled, Is.EqualTo(drawList.Count));
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
