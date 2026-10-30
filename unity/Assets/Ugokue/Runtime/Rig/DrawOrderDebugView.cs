using System.Collections.Generic;
using Ugokue.Core.Rig;
using UnityEngine;

namespace Ugokue.App.Rig
{
    /// <summary>
    /// キー操作でモデルのデバッグ表示を切り替え、有効なときは描画リストの凡例を画面の左下に表示する。
    /// </summary>
    public sealed class DrawOrderDebugView : MonoBehaviour
    {
        private const int WindowId = 0x55474b33;
        private const float RowHeight = 18;
        private const float Padding = 8;
        private const float TitleHeight = 20;

        [SerializeField] private ModelRenderer target;
        [SerializeField] private KeyCode toggleKey = KeyCode.F3;
        [SerializeField] private float windowWidth = 360;

        private void Awake()
        {
            if (target == null)
            {
                target = GetComponent<ModelRenderer>();
            }
        }

        private void Update()
        {
            if (target != null && Input.GetKeyDown(toggleKey))
            {
                target.DebugView = !target.DebugView;
            }
        }

        private void OnGUI()
        {
            if (target == null || target.Rig == null || !target.DebugView)
            {
                return;
            }

            int rows = target.Rig.DrawList.Count + 1;
            float height = TitleHeight + rows * RowHeight + Padding * 2;
            var rect = new Rect(12, Screen.height - height - 12, windowWidth, height);
            GUI.Window(WindowId, rect, DrawWindow, "Draw order (F3)");
        }

        private void DrawWindow(int id)
        {
            IReadOnlyList<DrawItem> drawList = target.Rig.DrawList;
            float y = TitleHeight + Padding;
            DrawRow(y, ModelRenderer.ToColor(DebugColors.ClippedAway), "clipped away");
            for (int i = 0; i < drawList.Count; i++)
            {
                DrawItem item = drawList[i];
                y += RowHeight;
                string label = $"{item.Part.Id}  order {item.DrawOrder}";
                if (item.Masks.Count > 0)
                {
                    var maskIds = new string[item.Masks.Count];
                    for (int j = 0; j < maskIds.Length; j++)
                    {
                        maskIds[j] = item.Masks[j].Part.Id;
                    }

                    label += "  clip: " + string.Join(", ", maskIds);
                }

                DrawRow(y, ModelRenderer.ToColor(DebugColors.ForRank(i, drawList.Count)), label);
            }
        }

        private void DrawRow(float y, Color color, string label)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(Padding, y + 3, 12, 12), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(Padding + 18, y, windowWidth - Padding * 2 - 18, RowHeight), label);
        }
    }
}
