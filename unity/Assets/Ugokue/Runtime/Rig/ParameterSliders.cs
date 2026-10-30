using Ugokue.Core.Format;
using Ugokue.Core.Rig;
using UnityEngine;

namespace Ugokue.App.Rig
{
    /// <summary>
    /// 画面の右側に、モデルの各パラメータを動かすデバッグ用のスライダーを表示する。
    /// </summary>
    public sealed class ParameterSliders : MonoBehaviour
    {
        private const int WindowId = 0x55474b31;

        [SerializeField] private ModelRenderer target;
        [SerializeField] private KeyCode toggleKey = KeyCode.F2;
        [SerializeField] private bool visible = true;
        [SerializeField] private float windowWidth = 320;

        public bool Visible => visible;

        private void Awake()
        {
            if (target == null)
            {
                target = GetComponent<ModelRenderer>();
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                visible = !visible;
            }
        }

        private void OnGUI()
        {
            if (!visible || target == null || target.Rig == null)
            {
                return;
            }

            var rect = new Rect(Screen.width - windowWidth - 12, 12, windowWidth, 0);
            GUILayout.Window(WindowId, rect, DrawWindow, "Parameters (F2)");
        }

        private void DrawWindow(int id)
        {
            ParameterValues values = target.Rig.Parameters;
            foreach (Parameter parameter in values.Definitions)
            {
                double current = values.Get(parameter.Id);
                GUILayout.Label($"{parameter.Id}  {current:0.00}  ({parameter.Min:0.##}..{parameter.Max:0.##})");
                float next = GUILayout.HorizontalSlider((float)current, (float)parameter.Min, (float)parameter.Max);
                if (!Mathf.Approximately(next, (float)current))
                {
                    values.Set(parameter.Id, next);
                }
            }

            if (GUILayout.Button("Reset"))
            {
                values.ResetToDefaults();
            }
        }
    }
}
