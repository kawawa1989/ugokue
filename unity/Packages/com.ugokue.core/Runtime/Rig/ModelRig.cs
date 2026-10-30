using System;
using System.Collections.Generic;
using Ugokue.Core.Format;

namespace Ugokue.Core.Rig
{
    /// <summary>
    /// パラメータの値から、各メッシュの変形後の頂点位置、使うビュー、描画リストを求める。
    /// </summary>
    public sealed class ModelRig
    {
        private readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        private readonly Dictionary<string, double[]> positions = new Dictionary<string, double[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ProceduralMotion>> motionsByMesh = new Dictionary<string, List<ProceduralMotion>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Part> parts = new Dictionary<string, Part>(StringComparer.Ordinal);
        private readonly List<KeyformInterpolator> interpolators = new List<KeyformInterpolator>();
        private readonly List<DrawItem> drawList = new List<DrawItem>();
        private readonly Dictionary<string, int> partIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<ViewWeight> viewWeights = new List<ViewWeight>();
        private readonly List<PartState> partStates = new List<PartState>();

        /// <summary>
        /// ビューを切り替えるときに見た目を混ぜる幅の初期値（度）。
        /// </summary>
        public const double DefaultViewTransitionDegrees = 8;

        public ModelRig(Model model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Parameters = new ParameterValues(model);
            for (int i = 0; i < model.Parts.Count; i++)
            {
                parts[model.Parts[i].Id] = model.Parts[i];
                partIndex[model.Parts[i].Id] = i;
            }

            foreach (Mesh mesh in model.Meshes)
            {
                meshes[mesh.Id] = mesh;
                positions[mesh.Id] = new double[mesh.Vertices.Length * 2];
                motionsByMesh[mesh.Id] = MotionsFor(mesh.Part);
            }

            foreach (KeyformGroup group in model.Deformation.KeyformGroups)
            {
                interpolators.Add(new KeyformInterpolator(group, meshes[group.Mesh].Vertices.Length));
            }

            Update();
        }

        public Model Model { get; }

        public ParameterValues Parameters { get; }

        /// <summary>
        /// 現在の (AngleX, AngleY) に最も近いビュー。
        /// </summary>
        public View CurrentView { get; private set; }

        /// <summary>
        /// ビューを切り替えるときに見た目を混ぜる幅（度）。最も近いビューとの距離の差がこの値未満のビューを混ぜる。0 なら混ぜない。
        /// </summary>
        public double ViewTransitionDegrees { get; set; } = DefaultViewTransitionDegrees;

        /// <summary>
        /// 現在の角度で見た目に寄与するビューと、その重み（合計 1）。
        /// </summary>
        public IReadOnlyList<ViewWeight> ViewWeights => viewWeights;

        /// <summary>
        /// 描画順の小さいものから並べた、表示するパーツの一覧。ビューの切り替えの途中では、同じパーツが不透明度の違う複数の項目になる。
        /// </summary>
        public IReadOnlyList<DrawItem> DrawList => drawList;

        /// <summary>
        /// メッシュの変形後の頂点位置（キャンバス座標）を、x と y を交互に並べた配列で返す。
        /// </summary>
        public double[] GetPositions(string meshId)
        {
            return positions[meshId];
        }

        /// <summary>
        /// 現在のパラメータの値で、頂点位置、ビュー、描画リストを計算し直す。
        /// </summary>
        public void Update()
        {
            foreach (Mesh mesh in Model.Meshes)
            {
                double[] target = positions[mesh.Id];
                for (int v = 0; v < mesh.Vertices.Length; v++)
                {
                    target[v * 2] = mesh.Vertices[v][0];
                    target[v * 2 + 1] = mesh.Vertices[v][1];
                }
            }

            foreach (KeyformInterpolator interpolator in interpolators)
            {
                interpolator.AddOffsets(Parameters, positions[interpolator.MeshId]);
            }

            foreach (Mesh mesh in Model.Meshes)
            {
                foreach (ProceduralMotion motion in motionsByMesh[mesh.Id])
                {
                    Apply(motion, positions[mesh.Id]);
                }
            }

            double angleX = Parameters.Get("AngleX");
            double angleY = Parameters.Get("AngleY");
            CurrentView = SelectView(Model.Views, angleX, angleY);
            ComputeViewWeights(angleX, angleY);
            BuildDrawList();
        }

        /// <summary>
        /// (yaw, pitch) とのユークリッド距離が最も小さいビューを返す。距離が同じなら先にあるものを返す。
        /// </summary>
        public static View SelectView(IReadOnlyList<View> views, double angleX, double angleY)
        {
            View best = null;
            double bestDistance = double.PositiveInfinity;
            foreach (View view in views)
            {
                double dx = view.Yaw - angleX;
                double dy = view.Pitch - angleY;
                double distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    best = view;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private List<ProceduralMotion> MotionsFor(string partId)
        {
            var result = new List<ProceduralMotion>();
            for (string current = partId; current != null; current = parts[current].Parent)
            {
                foreach (ProceduralMotion motion in Model.Deformation.Procedural)
                {
                    if (motion.Part == current)
                    {
                        result.Add(motion);
                    }
                }
            }

            return result;
        }

        private void Apply(ProceduralMotion motion, double[] target)
        {
            double value = Parameters.Get(motion.Parameter);
            if (motion.Type == "rotate")
            {
                double radians = motion.DegreesPerUnit.GetValueOrDefault() * value * Math.PI / 180;
                if (radians == 0)
                {
                    return;
                }

                double cos = Math.Cos(radians);
                double sin = Math.Sin(radians);
                double[] anchor = parts[motion.Part].Anchor;
                for (int i = 0; i < target.Length; i += 2)
                {
                    double dx = target[i] - anchor[0];
                    double dy = target[i + 1] - anchor[1];
                    target[i] = anchor[0] + dx * cos - dy * sin;
                    target[i + 1] = anchor[1] + dx * sin + dy * cos;
                }
            }
            else if (motion.Type == "translate")
            {
                double ox = motion.OffsetPerUnit[0] * value;
                double oy = motion.OffsetPerUnit[1] * value;
                for (int i = 0; i < target.Length; i += 2)
                {
                    target[i] += ox;
                    target[i + 1] += oy;
                }
            }
        }

        private ClipMask[] MasksFor(Part part)
        {
            if (part.ClipBy == null || part.ClipBy.Count == 0)
            {
                return null;
            }

            var masks = new ClipMask[part.ClipBy.Count];
            for (int i = 0; i < masks.Length; i++)
            {
                Part maskPart = parts[part.ClipBy[i]];
                string meshId = CurrentView?.Overrides.Find(o => o.Part == maskPart.Id)?.Mesh ?? maskPart.Mesh;
                masks[i] = new ClipMask(maskPart, meshId == null ? null : meshes[meshId]);
            }

            return masks;
        }

        private void ComputeViewWeights(double angleX, double angleY)
        {
            viewWeights.Clear();
            if (CurrentView == null)
            {
                return;
            }

            double nearest = Distance(CurrentView, angleX, angleY);
            double total = 0;
            foreach (View view in Model.Views)
            {
                double raw = view == CurrentView
                    ? 1
                    : ViewTransitionDegrees > 0 ? Math.Max(0, 1 - (Distance(view, angleX, angleY) - nearest) / ViewTransitionDegrees) : 0;
                if (raw > 0)
                {
                    viewWeights.Add(new ViewWeight(view, raw));
                    total += raw;
                }
            }

            for (int i = 0; i < viewWeights.Count; i++)
            {
                viewWeights[i] = new ViewWeight(viewWeights[i].View, viewWeights[i].Weight / total);
            }
        }

        private static double Distance(View view, double angleX, double angleY)
        {
            double dx = view.Yaw - angleX;
            double dy = view.Pitch - angleY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void BuildDrawList()
        {
            drawList.Clear();
            foreach (Part part in Model.Parts)
            {
                partStates.Clear();
                double visibleWeight = 0;
                foreach (ViewWeight viewWeight in viewWeights)
                {
                    ViewOverride viewOverride = viewWeight.View.Overrides.Find(o => o.Part == part.Id);
                    string meshId = viewOverride?.Mesh ?? part.Mesh;
                    if (meshId == null || viewOverride?.Visible == false)
                    {
                        continue;
                    }

                    int drawOrder = viewOverride?.DrawOrder ?? part.DrawOrder;
                    visibleWeight += viewWeight.Weight;
                    int existing = partStates.FindIndex(state => state.MeshId == meshId && state.DrawOrder == drawOrder);
                    if (existing >= 0)
                    {
                        partStates[existing] = new PartState(meshId, drawOrder, partStates[existing].Weight + viewWeight.Weight);
                    }
                    else
                    {
                        partStates.Add(new PartState(meshId, drawOrder, viewWeight.Weight));
                    }
                }

                if (partStates.Count == 0)
                {
                    continue;
                }

                partStates.Sort((a, b) => a.DrawOrder != b.DrawOrder ? a.DrawOrder.CompareTo(b.DrawOrder) : b.Weight.CompareTo(a.Weight));
                ClipMask[] masks = MasksFor(part);
                for (int i = 0; i < partStates.Count; i++)
                {
                    double opacity = i == 0 ? Math.Min(1, visibleWeight) : partStates[i].Weight;
                    drawList.Add(new DrawItem(part, meshes[partStates[i].MeshId], partStates[i].DrawOrder, masks, opacity, i));
                }
            }

            drawList.Sort((a, b) =>
            {
                int order = a.DrawOrder.CompareTo(b.DrawOrder);
                if (order != 0)
                {
                    return order;
                }

                int part = partIndex[a.Part.Id].CompareTo(partIndex[b.Part.Id]);
                return part != 0 ? part : a.Instance.CompareTo(b.Instance);
            });
        }

        private readonly struct PartState
        {
            public PartState(string meshId, int drawOrder, double weight)
            {
                MeshId = meshId;
                DrawOrder = drawOrder;
                Weight = weight;
            }

            public string MeshId { get; }

            public int DrawOrder { get; }

            public double Weight { get; }
        }
    }
}
