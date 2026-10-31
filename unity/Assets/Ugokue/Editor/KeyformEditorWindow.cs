using System;
using System.Collections.Generic;
using System.Linq;
using Ugokue.App.Rig;
using Ugokue.Core.Format;
using Ugokue.Core.Rig;
using UnityEditor;
using UnityEngine;
using FormatMesh = Ugokue.Core.Format.Mesh;

namespace Ugokue.Editor
{
    /// <summary>
    /// 再生中のモデルの顔の向きのキーフォームを、格子点ごとにシーンビューで頂点をドラッグして手で修正するウィンドウ。修正は &lt;メッシュ&gt;_angle_edit のグループに持つ。
    /// </summary>
    public sealed class KeyformEditorWindow : EditorWindow
    {
        private const int MaxUndo = 50;

        private static readonly Color WireColor = new Color(0.2f, 0.9f, 1f, 0.6f);
        private static readonly Color VertexColor = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color BrushColor = new Color(1f, 0.6f, 0.1f, 0.9f);
        private static readonly Color ResetBrushColor = new Color(1f, 0.3f, 0.3f, 0.9f);

        private readonly List<(string MeshId, double X, double Y, double[][] Offsets)> undo = new List<(string, double, double, double[][])>();
        private bool editing;
        private int meshIndex;
        private int xIndex;
        private int yIndex;
        private float radius = 40;
        private bool resetBrush;
        private bool dirty;
        private bool dragging;
        private Vector2 lastCanvas;
        private double[] dragWeights;
        private ModelRenderer target;

        /// <summary>
        /// ウィンドウを開く。
        /// </summary>
        [MenuItem("Ugokue/Keyform Editor")]
        public static void Open()
        {
            GetWindow<KeyformEditorWindow>("Keyform Editor");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            StopEditing();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode && dirty)
            {
                Debug.LogWarning("[KeyformEditor] 保存していないキーフォームの修正は、再生を止めると消えます。");
            }

            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                editing = false;
                dirty = false;
                undo.Clear();
                target = null;
            }

            Repaint();
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("再生中のモデルを修正します。再生してください（読み込むモデルは Ugokue > Model Selector で選ぶ）。", MessageType.Info);
                return;
            }

            target = target != null ? target : FindFirstObjectByType<ModelRenderer>();
            if (target == null || target.Rig == null)
            {
                EditorGUILayout.HelpBox("モデルが読み込まれていません。", MessageType.Warning);
                return;
            }

            Model model = target.Rig.Model;
            string[] meshIds = model.Meshes.Select(m => m.Id).ToArray();
            double[][] grid = KeyformEditing.AngleGrid(model, meshIds.FirstOrDefault());
            if (grid == null)
            {
                EditorGUILayout.HelpBox("このモデルには顔の向き（AngleX × AngleY）のキーフォームがありません。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(target.ModelDirectory, EditorStyles.miniLabel);
            bool nowEditing = EditorGUILayout.ToggleLeft("修正する（角度を下の格子点に固定し、シーンビューでドラッグ）", editing);
            if (nowEditing != editing)
            {
                editing = nowEditing;
                if (!editing)
                {
                    StopEditing();
                }
            }

            meshIndex = Mathf.Clamp(EditorGUILayout.Popup("メッシュ", meshIndex, meshIds), 0, meshIds.Length - 1);
            xIndex = Mathf.Clamp(EditorGUILayout.Popup("AngleX", xIndex, grid[0].Select(v => v.ToString("0.##")).ToArray()), 0, grid[0].Length - 1);
            yIndex = Mathf.Clamp(EditorGUILayout.Popup("AngleY", yIndex, grid[1].Select(v => v.ToString("0.##")).ToArray()), 0, grid[1].Length - 1);
            radius = EditorGUILayout.Slider("影響範囲（画素）", radius, 2, 300);
            resetBrush = EditorGUILayout.ToggleLeft("戻すブラシ（ドラッグした範囲を自動の位置に戻す）", resetBrush);

            if (editing)
            {
                target.PinnedValues["AngleX"] = grid[0][xIndex];
                target.PinnedValues["AngleY"] = grid[1][yIndex];
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(undo.Count == 0))
                {
                    if (GUILayout.Button($"元に戻す（{undo.Count}）"))
                    {
                        UndoLast();
                    }
                }

                if (GUILayout.Button("この格子点の修正を消す"))
                {
                    Keyform keyform = SelectedKeyform(model, meshIds[meshIndex], grid);
                    Remember(meshIds[meshIndex], grid, keyform);
                    KeyformEditing.Reset(keyform, Enumerable.Repeat(1.0, keyform.Offsets.Length).ToArray());
                    Changed();
                }
            }

            using (new EditorGUI.DisabledScope(!dirty))
            {
                if (GUILayout.Button(dirty ? "保存（model.json に書き出す）" : "保存済み"))
                {
                    Save();
                }
            }

            EditorGUILayout.HelpBox(
                "シーンビューで、選んだメッシュの線と頂点を表示する。ドラッグすると、押した位置から影響範囲の内側の頂点が、近いほど大きく動く。" +
                "修正は自動のキーフォームに足し合わされ、間の角度では補間される。ugokue-pipeline keyforms で作り直しても、メッシュと格子が同じなら残る。",
                MessageType.None);
        }

        private void OnSceneGUI(SceneView view)
        {
            if (!editing || !EditorApplication.isPlaying || target == null || target.Rig == null)
            {
                return;
            }

            Model model = target.Rig.Model;
            if (meshIndex >= model.Meshes.Count)
            {
                return;
            }

            FormatMesh mesh = model.Meshes[meshIndex];
            double[][] grid = KeyformEditing.AngleGrid(model, mesh.Id);
            double[] positions = target.Rig.GetPositions(mesh.Id);
            DrawMesh(mesh, positions);

            Event current = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!new Plane(target.transform.forward, target.transform.position).Raycast(ray, out float distance))
            {
                return;
            }

            Vector2 canvas = target.WorldToCanvas(ray.GetPoint(distance));
            Handles.color = resetBrush ? ResetBrushColor : BrushColor;
            Handles.DrawWireDisc(target.CanvasToWorld(canvas.x, canvas.y), Vector3.forward, radius / target.PixelsPerUnit * target.transform.lossyScale.x);

            switch (current.GetTypeForControl(control))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(control);
                    break;
                case EventType.MouseDown when current.button == 0 && !current.alt:
                    GUIUtility.hotControl = control;
                    dragging = true;
                    lastCanvas = canvas;
                    dragWeights = KeyformEditing.Weights(positions, canvas.x, canvas.y, radius);
                    Keyform keyform = SelectedKeyform(model, mesh.Id, grid);
                    Remember(mesh.Id, grid, keyform);
                    if (resetBrush)
                    {
                        KeyformEditing.Reset(keyform, dragWeights);
                        Changed();
                    }

                    current.Use();
                    break;
                case EventType.MouseDrag when dragging && GUIUtility.hotControl == control:
                    if (!resetBrush)
                    {
                        KeyformEditing.Move(SelectedKeyform(model, mesh.Id, grid), dragWeights, canvas.x - lastCanvas.x, canvas.y - lastCanvas.y);
                        Changed();
                    }

                    lastCanvas = canvas;
                    current.Use();
                    break;
                case EventType.MouseUp when dragging && GUIUtility.hotControl == control:
                    dragging = false;
                    GUIUtility.hotControl = 0;
                    current.Use();
                    break;
                case EventType.MouseMove:
                    view.Repaint();
                    break;
            }
        }

        private Keyform SelectedKeyform(Model model, string meshId, double[][] grid)
        {
            return KeyformEditing.KeyformAt(KeyformEditing.EditGroup(model, meshId), grid[0][xIndex], grid[1][yIndex]);
        }

        private void DrawMesh(FormatMesh mesh, double[] positions)
        {
            Handles.color = WireColor;
            foreach (int[] triangle in mesh.Triangles)
            {
                Vector3 a = Point(positions, triangle[0]);
                Vector3 b = Point(positions, triangle[1]);
                Vector3 c = Point(positions, triangle[2]);
                Handles.DrawLine(a, b);
                Handles.DrawLine(b, c);
                Handles.DrawLine(c, a);
            }

            Handles.color = VertexColor;
            float size = 2f / target.PixelsPerUnit * target.transform.lossyScale.x;
            for (int v = 0; v < mesh.Vertices.Length; v++)
            {
                Handles.DrawSolidDisc(Point(positions, v), Vector3.forward, size);
            }
        }

        private Vector3 Point(double[] positions, int vertex)
        {
            return target.CanvasToWorld(positions[vertex * 2], positions[vertex * 2 + 1]);
        }

        private void Remember(string meshId, double[][] grid, Keyform keyform)
        {
            undo.Add((meshId, grid[0][xIndex], grid[1][yIndex], keyform.Offsets.Select(o => (double[])o.Clone()).ToArray()));
            if (undo.Count > MaxUndo)
            {
                undo.RemoveAt(0);
            }
        }

        private void UndoLast()
        {
            (string meshId, double x, double y, double[][] offsets) = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            KeyformEditing.KeyformAt(KeyformEditing.EditGroup(target.Rig.Model, meshId), x, y).Offsets = offsets;
            Changed();
        }

        private void Changed()
        {
            dirty = true;
            target.RebuildRig();
            SceneView.RepaintAll();
            Repaint();
        }

        private void Save()
        {
            try
            {
                target.SaveModel();
                dirty = false;
                Debug.Log($"[KeyformEditor] Saved {target.ModelDirectory}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[KeyformEditor] Failed to save: {e}");
            }
        }

        private void StopEditing()
        {
            if (target != null)
            {
                target.PinnedValues.Remove("AngleX");
                target.PinnedValues.Remove("AngleY");
            }

            dragging = false;
        }
    }
}
