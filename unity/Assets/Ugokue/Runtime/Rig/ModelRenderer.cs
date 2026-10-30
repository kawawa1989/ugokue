using System;
using System.Collections.Generic;
using Ugokue.Core.Format;
using Ugokue.Core.Rig;
using UnityEngine;
using FormatMesh = Ugokue.Core.Format.Mesh;
using UnityMesh = UnityEngine.Mesh;

namespace Ugokue.App.Rig
{
    /// <summary>
    /// 中間形式のモデルを読み込み、パラメータの値で変形したメッシュをパーツごとに描画する。
    /// </summary>
    public sealed class ModelRenderer : MonoBehaviour
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        [SerializeField] private Material partMaterial;
        [SerializeField] private float pixelsPerUnit = 100;
        [SerializeField] private bool fitCamera = true;

        private readonly Dictionary<string, PartView> partViews = new Dictionary<string, PartView>(StringComparer.Ordinal);
        private readonly Dictionary<string, MeshBuffer> meshBuffers = new Dictionary<string, MeshBuffer>(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private MaterialPropertyBlock propertyBlock;
        private float canvasWidth;
        private float canvasHeight;

        public ModelRig Rig { get; private set; }

        public float PixelsPerUnit => pixelsPerUnit;

        /// <summary>
        /// フォルダのモデルを読み込み、パーツごとの描画用オブジェクトを作り直す。
        /// </summary>
        public void Load(string directory)
        {
            Unload();
            ModelPackage package = ModelPackage.Load(directory);
            Model model = package.Model;
            Rig = new ModelRig(model);
            canvasWidth = model.Canvas.Width;
            canvasHeight = model.Canvas.Height;
            propertyBlock ??= new MaterialPropertyBlock();

            foreach (FormatMesh mesh in model.Meshes)
            {
                if (!textures.ContainsKey(mesh.Texture))
                {
                    textures[mesh.Texture] = LoadTexture(mesh.Texture, package.Textures[mesh.Texture]);
                }

                meshBuffers[mesh.Id] = new MeshBuffer(CreateMesh(mesh), new Vector3[mesh.Vertices.Length], textures[mesh.Texture]);
            }

            foreach (Part part in model.Parts)
            {
                if (part.Mesh == null && !model.Meshes.Exists(m => m.Part == part.Id))
                {
                    continue;
                }

                var child = new GameObject(part.Id);
                child.transform.SetParent(transform, false);
                var filter = child.AddComponent<MeshFilter>();
                var meshRenderer = child.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = partMaterial;
                meshRenderer.enabled = false;
                partViews[part.Id] = new PartView(filter, meshRenderer);
            }

            FitCamera();
            Apply();
        }

        /// <summary>
        /// 現在のパラメータの値で変形を計算し、頂点位置、表示、描画順を描画用オブジェクトに反映する。
        /// </summary>
        public void Apply()
        {
            if (Rig == null)
            {
                return;
            }

            Rig.Update();
            IReadOnlyList<DrawItem> drawList = Rig.DrawList;
            for (int i = 0; i < drawList.Count; i++)
            {
                DrawItem item = drawList[i];
                PartView view = partViews[item.Part.Id];
                MeshBuffer buffer = meshBuffers[item.Mesh.Id];
                WriteVertices(Rig.GetPositions(item.Mesh.Id), buffer);

                if (view.Filter.sharedMesh != buffer.Mesh)
                {
                    view.Filter.sharedMesh = buffer.Mesh;
                    propertyBlock.SetTexture(MainTexId, buffer.Texture);
                    view.Renderer.SetPropertyBlock(propertyBlock);
                }

                view.Renderer.sortingOrder = i;
                view.Renderer.enabled = true;
                view.Visible = true;
            }

            foreach (PartView view in partViews.Values)
            {
                if (!view.Visible)
                {
                    view.Renderer.enabled = false;
                }

                view.Visible = false;
            }
        }

        /// <summary>
        /// パーツの描画に使う MeshRenderer を返す。メッシュを持たないパーツは null を返す。
        /// </summary>
        public MeshRenderer GetPartRenderer(string partId)
        {
            return partViews.TryGetValue(partId, out PartView view) ? view.Renderer : null;
        }

        /// <summary>
        /// 中間形式のメッシュに対応する Unity のメッシュを返す。
        /// </summary>
        public UnityMesh GetUnityMesh(string meshId)
        {
            return meshBuffers.TryGetValue(meshId, out MeshBuffer buffer) ? buffer.Mesh : null;
        }

        /// <summary>
        /// キャンバス座標（左上原点、y 下向き）を、キャンバスの中心を原点とするローカル座標に変換する。
        /// </summary>
        public static Vector3 CanvasToLocal(double x, double y, float width, float height, float pixelsPerUnit)
        {
            return new Vector3((float)((x - width / 2.0) / pixelsPerUnit), (float)((height / 2.0 - y) / pixelsPerUnit), 0);
        }

        private void Start()
        {
            string directory = ModelLocation.Resolve();
            try
            {
                Load(directory);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ModelRenderer] Failed to load model from {directory}: {e}");
            }
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void OnDestroy()
        {
            Unload();
        }

        private void WriteVertices(double[] positions, MeshBuffer buffer)
        {
            Vector3[] vertices = buffer.Vertices;
            for (int v = 0; v < vertices.Length; v++)
            {
                vertices[v] = CanvasToLocal(positions[v * 2], positions[v * 2 + 1], canvasWidth, canvasHeight, pixelsPerUnit);
            }

            buffer.Mesh.SetVertices(vertices);
            buffer.Mesh.RecalculateBounds();
        }

        private UnityMesh CreateMesh(FormatMesh source)
        {
            int count = source.Vertices.Length;
            var vertices = new Vector3[count];
            var uvs = new Vector2[count];
            for (int v = 0; v < count; v++)
            {
                vertices[v] = CanvasToLocal(source.Vertices[v][0], source.Vertices[v][1], canvasWidth, canvasHeight, pixelsPerUnit);
                uvs[v] = new Vector2((float)source.Uvs[v][0], (float)(1.0 - source.Uvs[v][1]));
            }

            var triangles = new int[source.Triangles.Length * 3];
            for (int t = 0; t < source.Triangles.Length; t++)
            {
                triangles[t * 3] = source.Triangles[t][0];
                triangles[t * 3 + 1] = source.Triangles[t][1];
                triangles[t * 3 + 2] = source.Triangles[t][2];
            }

            var mesh = new UnityMesh { name = source.Id, hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Texture2D LoadTexture(string name, byte[] png)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.DontSave,
            };
            if (!texture.LoadImage(png))
            {
                throw new InvalidOperationException($"Texture '{name}' is not a valid PNG.");
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private void FitCamera()
        {
            Camera camera = Camera.main;
            if (!fitCamera || !Application.isPlaying || camera == null || !camera.orthographic)
            {
                return;
            }

            const float margin = 1.05f;
            float halfHeight = canvasHeight / 2f / pixelsPerUnit;
            float halfWidth = canvasWidth / 2f / pixelsPerUnit;
            float aspect = camera.aspect > 0 ? camera.aspect : 1;
            camera.orthographicSize = Mathf.Max(halfHeight, halfWidth / aspect) * margin;
        }

        private void Unload()
        {
            foreach (PartView view in partViews.Values)
            {
                if (view.Filter != null)
                {
                    DestroyOwned(view.Filter.gameObject);
                }
            }

            foreach (MeshBuffer buffer in meshBuffers.Values)
            {
                DestroyOwned(buffer.Mesh);
            }

            foreach (Texture2D texture in textures.Values)
            {
                DestroyOwned(texture);
            }

            partViews.Clear();
            meshBuffers.Clear();
            textures.Clear();
            Rig = null;
        }

        private static void DestroyOwned(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        /// <summary>
        /// パーツの描画用オブジェクトと、そのフレームで表示するかどうかを保持する。
        /// </summary>
        private sealed class PartView
        {
            public PartView(MeshFilter filter, MeshRenderer renderer)
            {
                Filter = filter;
                Renderer = renderer;
            }

            public MeshFilter Filter { get; }

            public MeshRenderer Renderer { get; }

            public bool Visible { get; set; }
        }

        /// <summary>
        /// 中間形式のメッシュ1つに対応する Unity のメッシュ、頂点の書き込み先、テクスチャを保持する。
        /// </summary>
        private sealed class MeshBuffer
        {
            public MeshBuffer(UnityMesh mesh, Vector3[] vertices, Texture2D texture)
            {
                Mesh = mesh;
                Vertices = vertices;
                Texture = texture;
            }

            public UnityMesh Mesh { get; }

            public Vector3[] Vertices { get; }

            public Texture2D Texture { get; }
        }
    }
}
