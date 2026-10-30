using System;
using System.Collections.Generic;
using Ugokue.Core.Format;
using Ugokue.Core.Rig;
using UnityEngine;
using UnityEngine.Rendering;
using FormatMesh = Ugokue.Core.Format.Mesh;
using UnityMesh = UnityEngine.Mesh;

namespace Ugokue.App.Rig
{
    /// <summary>
    /// 中間形式のモデルを読み込み、パラメータの値で変形したメッシュを、マスクで切り抜きながらパーツごとに描画する。
    /// </summary>
    public sealed class ModelRenderer : MonoBehaviour
    {
        public const string PartShaderName = "Ugokue/UnlitTransparent";

        public const string MaskShaderName = "Ugokue/StencilMask";

        public const string DebugShaderName = "Ugokue/DebugColor";

        private const int MaxStencilValue = 255;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int DebugColorId = Shader.PropertyToID("_DebugColor");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
        private static readonly int StencilCompId = Shader.PropertyToID("_StencilComp");
        private static readonly int StencilPassId = Shader.PropertyToID("_StencilPass");

        [SerializeField] private Material partMaterial;
        [SerializeField] private Material maskMaterial;
        [SerializeField] private Material debugMaterial;
        [SerializeField] private float pixelsPerUnit = 100;
        [SerializeField] private bool fitCamera = true;

        private readonly Dictionary<string, PartView> partViews = new Dictionary<string, PartView>(StringComparer.Ordinal);
        private readonly Dictionary<string, MeshBuffer> meshBuffers = new Dictionary<string, MeshBuffer>(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly List<RendererView> rendererViews = new List<RendererView>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        private MaterialPropertyBlock propertyBlock;
        private Material basePartMaterial;
        private Material baseMaskMaterial;
        private Material baseDebugMaterial;
        private float canvasWidth;
        private float canvasHeight;
        private int applyCount;
        private bool debugView;

        public ModelRig Rig { get; private set; }

        public float PixelsPerUnit => pixelsPerUnit;

        /// <summary>
        /// パーツを描画リストの順位の色で塗り、切り抜かれる部分も表示するデバッグ表示の有効・無効。
        /// </summary>
        public bool DebugView
        {
            get => debugView;
            set
            {
                debugView = value;
                Apply();
            }
        }

        /// <summary>
        /// フォルダのモデルを読み込み、パーツごとの描画用オブジェクトとマスク用のオブジェクトを作り直す。
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
            basePartMaterial = partMaterial != null ? partMaterial : CreateOwnedMaterial(Shader.Find(PartShaderName));
            baseMaskMaterial = maskMaterial != null ? maskMaterial : CreateOwnedMaterial(Shader.Find(MaskShaderName));
            baseDebugMaterial = debugMaterial != null ? debugMaterial : CreateOwnedMaterial(Shader.Find(DebugShaderName));

            foreach (FormatMesh mesh in model.Meshes)
            {
                if (!textures.ContainsKey(mesh.Texture))
                {
                    textures[mesh.Texture] = LoadTexture(mesh.Texture, package.Textures[mesh.Texture]);
                }

                meshBuffers[mesh.Id] = new MeshBuffer(mesh.Id, CreateMesh(mesh), new Vector3[mesh.Vertices.Length], textures[mesh.Texture]);
            }

            int nextStencil = 1;
            foreach (Part part in model.Parts)
            {
                if (part.Mesh == null && !model.Meshes.Exists(m => m.Part == part.Id))
                {
                    continue;
                }

                int maskCount = part.ClipBy?.Count ?? 0;
                if (maskCount == 0)
                {
                    var unclipped = new PartView(part.Id, basePartMaterial, baseDebugMaterial, Array.Empty<Material>());
                    GetInstance(unclipped, 0);
                    partViews[part.Id] = unclipped;
                    continue;
                }

                int lastStencil = nextStencil + maskCount - 1;
                if (lastStencil > MaxStencilValue)
                {
                    throw new InvalidOperationException($"Clipped parts need more than {MaxStencilValue} stencil values.");
                }

                var maskMaterials = new Material[maskCount];
                for (int j = 0; j < maskCount; j++)
                {
                    maskMaterials[j] = j == 0
                        ? CreateStencilMaterial(baseMaskMaterial, nextStencil, CompareFunction.Always, StencilOp.Replace)
                        : CreateStencilMaterial(baseMaskMaterial, nextStencil + j - 1, CompareFunction.Equal, StencilOp.IncrementSaturate);
                }

                var view = new PartView(
                    part.Id,
                    CreateStencilMaterial(basePartMaterial, lastStencil, CompareFunction.Equal, null),
                    CreateStencilMaterial(baseDebugMaterial, lastStencil, CompareFunction.Equal, null),
                    maskMaterials);
                GetInstance(view, 0);
                partViews[part.Id] = view;
                nextStencil = lastStencil + 1;
            }

            FitCamera();
            Apply();
        }

        /// <summary>
        /// 現在のパラメータの値で変形を計算し、頂点位置、表示、描画順、マスクを描画用オブジェクトに反映する。
        /// </summary>
        public void Apply()
        {
            if (Rig == null)
            {
                return;
            }

            Rig.Update();
            applyCount++;
            IReadOnlyList<DrawItem> drawList = Rig.DrawList;
            int order = 0;
            for (int i = 0; i < drawList.Count; i++)
            {
                DrawItem item = drawList[i];
                PartView part = partViews[item.Part.Id];
                InstanceView view = GetInstance(part, item.Instance);
                MeshBuffer buffer = meshBuffers[item.Mesh.Id];
                float opacity = (float)item.Opacity;

                if (view.Masks.Length > 0)
                {
                    if (debugView)
                    {
                        Show(view.ClippedAway, buffer, order, baseDebugMaterial, ToColor(DebugColors.ClippedAway), opacity);
                    }

                    order++;
                    bool masksReady = true;
                    foreach (ClipMask mask in item.Masks)
                    {
                        masksReady &= mask.Mesh != null;
                    }

                    for (int j = 0; j < view.Masks.Length; j++, order++)
                    {
                        if (masksReady)
                        {
                            Show(view.Masks[j], meshBuffers[item.Masks[j].Mesh.Id], order, part.MaskMaterials[j], null, 1);
                        }
                    }

                    if (!masksReady)
                    {
                        order++;
                        continue;
                    }
                }

                if (debugView)
                {
                    Show(view.Body, buffer, order++, part.DebugMaterial, ToColor(DebugColors.ForRank(i, drawList.Count)), opacity);
                }
                else
                {
                    Show(view.Body, buffer, order++, part.NormalMaterial, null, opacity);
                }
            }

            foreach (RendererView view in rendererViews)
            {
                if (!view.Visible)
                {
                    view.Renderer.enabled = false;
                }

                view.Visible = false;
            }
        }

        /// <summary>
        /// パーツの instance 番目の項目の描画に使う MeshRenderer を返す。メッシュを持たないパーツと、まだ作っていない項目は null を返す。
        /// </summary>
        public MeshRenderer GetPartRenderer(string partId, int instance = 0)
        {
            return FindInstance(partId, instance)?.Body.Renderer;
        }

        /// <summary>
        /// 切り抜かれるパーツの instance 番目の項目で、clipBy の index 番目のマスクを書く MeshRenderer を返す。なければ null を返す。
        /// </summary>
        public MeshRenderer GetMaskRenderer(string partId, int index, int instance = 0)
        {
            InstanceView view = FindInstance(partId, instance);
            return view != null && index >= 0 && index < view.Masks.Length ? view.Masks[index].Renderer : null;
        }

        /// <summary>
        /// デバッグ表示で、切り抜かれるパーツの instance 番目の項目の切り抜く前の形を描く MeshRenderer を返す。切り抜かれないパーツは null を返す。
        /// </summary>
        public MeshRenderer GetClippedAwayRenderer(string partId, int instance = 0)
        {
            return FindInstance(partId, instance)?.ClippedAway?.Renderer;
        }

        /// <summary>
        /// パーツの描画用に作った項目の数を返す。メッシュを持たないパーツは 0 を返す。
        /// </summary>
        public int GetInstanceCount(string partId)
        {
            return partViews.TryGetValue(partId, out PartView view) ? view.Instances.Count : 0;
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

        /// <summary>
        /// 0〜255 の RGB または RGBA の値を Unity の色に変換する。アルファがなければ不透明にする。
        /// </summary>
        public static Color ToColor(byte[] rgba)
        {
            return new Color32(rgba[0], rgba[1], rgba[2], rgba.Length > 3 ? rgba[3] : (byte)255);
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

        private void Show(RendererView view, MeshBuffer buffer, int sortingOrder, Material material, Color? debugColor, float opacity)
        {
            if (buffer.WrittenAt != applyCount)
            {
                WriteVertices(Rig.GetPositions(buffer.MeshId), buffer);
                buffer.WrittenAt = applyCount;
            }

            if (view.Filter.sharedMesh != buffer.Mesh)
            {
                view.Filter.sharedMesh = buffer.Mesh;
            }

            if (view.Renderer.sharedMaterial != material)
            {
                view.Renderer.sharedMaterial = material;
            }

            propertyBlock.Clear();
            propertyBlock.SetTexture(MainTexId, buffer.Texture);
            propertyBlock.SetFloat(OpacityId, opacity);
            if (debugColor.HasValue)
            {
                propertyBlock.SetColor(DebugColorId, debugColor.Value);
            }

            view.Renderer.SetPropertyBlock(propertyBlock);
            view.Renderer.sortingOrder = sortingOrder;
            view.Renderer.enabled = true;
            view.Visible = true;
        }

        private InstanceView FindInstance(string partId, int instance)
        {
            return partViews.TryGetValue(partId, out PartView view) && instance >= 0 && instance < view.Instances.Count
                ? view.Instances[instance]
                : null;
        }

        private InstanceView GetInstance(PartView part, int instance)
        {
            while (part.Instances.Count <= instance)
            {
                int number = part.Instances.Count;
                string name = number == 0 ? part.PartId : $"{part.PartId}#{number}";
                int maskCount = part.MaskMaterials.Length;
                var view = new InstanceView(CreateRendererView(name), new RendererView[maskCount]);
                if (maskCount > 0)
                {
                    view.ClippedAway = CreateRendererView(name + ".clippedAway");
                    for (int j = 0; j < maskCount; j++)
                    {
                        view.Masks[j] = CreateRendererView($"{name}.mask{j}");
                    }
                }

                part.Instances.Add(view);
            }

            return part.Instances[instance];
        }

        private RendererView CreateRendererView(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var filter = child.AddComponent<MeshFilter>();
            var meshRenderer = child.AddComponent<MeshRenderer>();
            meshRenderer.enabled = false;
            var view = new RendererView(filter, meshRenderer);
            rendererViews.Add(view);
            return view;
        }

        private Material CreateOwnedMaterial(Shader shader)
        {
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            ownedMaterials.Add(material);
            return material;
        }

        private Material CreateStencilMaterial(Material source, int stencilRef, CompareFunction comparison, StencilOp? pass)
        {
            var material = new Material(source) { name = source.name + " (Stencil " + stencilRef + ")", hideFlags = HideFlags.DontSave };
            material.SetInt(StencilRefId, stencilRef);
            material.SetInt(StencilCompId, (int)comparison);
            if (pass.HasValue)
            {
                material.SetInt(StencilPassId, (int)pass.Value);
            }

            ownedMaterials.Add(material);
            return material;
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
            foreach (RendererView view in rendererViews)
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

            foreach (Material material in ownedMaterials)
            {
                DestroyOwned(material);
            }

            rendererViews.Clear();
            partViews.Clear();
            meshBuffers.Clear();
            textures.Clear();
            ownedMaterials.Clear();
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
        /// 1つの MeshRenderer と、そのフレームで表示するかどうかを保持する。
        /// </summary>
        private sealed class RendererView
        {
            public RendererView(MeshFilter filter, MeshRenderer renderer)
            {
                Filter = filter;
                Renderer = renderer;
            }

            public MeshFilter Filter { get; }

            public MeshRenderer Renderer { get; }

            public bool Visible { get; set; }
        }

        /// <summary>
        /// パーツ1つの通常とデバッグ表示とマスクのマテリアル、描画リストの項目ごとの描画用オブジェクトを保持する。
        /// </summary>
        private sealed class PartView
        {
            public PartView(string partId, Material normalMaterial, Material debugMaterial, Material[] maskMaterials)
            {
                PartId = partId;
                NormalMaterial = normalMaterial;
                DebugMaterial = debugMaterial;
                MaskMaterials = maskMaterials;
            }

            public string PartId { get; }

            public Material NormalMaterial { get; }

            public Material DebugMaterial { get; }

            public Material[] MaskMaterials { get; }

            public List<InstanceView> Instances { get; } = new List<InstanceView>();
        }

        /// <summary>
        /// 描画リストの1項目を描く、パーツ自身、マスク、切り抜かれる部分のデバッグ表示の描画用オブジェクトを保持する。
        /// </summary>
        private sealed class InstanceView
        {
            public InstanceView(RendererView body, RendererView[] masks)
            {
                Body = body;
                Masks = masks;
            }

            public RendererView Body { get; }

            public RendererView[] Masks { get; }

            public RendererView ClippedAway { get; set; }
        }

        /// <summary>
        /// 中間形式のメッシュ1つに対応する Unity のメッシュ、頂点の書き込み先、テクスチャを保持する。
        /// </summary>
        private sealed class MeshBuffer
        {
            public MeshBuffer(string meshId, UnityMesh mesh, Vector3[] vertices, Texture2D texture)
            {
                MeshId = meshId;
                Mesh = mesh;
                Vertices = vertices;
                Texture = texture;
            }

            public string MeshId { get; }

            public UnityMesh Mesh { get; }

            public Vector3[] Vertices { get; }

            public Texture2D Texture { get; }

            public int WrittenAt { get; set; }
        }
    }
}
