using System;
using System.Collections.Generic;
using Ugokue.Core.Format;

namespace Ugokue.Core.Rig
{
    /// <summary>
    /// モデルのパラメータの現在値を、各パラメータの範囲に収めて保持する。
    /// </summary>
    public sealed class ParameterValues
    {
        private readonly Dictionary<string, Parameter> parameters = new Dictionary<string, Parameter>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> values = new Dictionary<string, double>(StringComparer.Ordinal);

        public ParameterValues(Model model)
        {
            foreach (Parameter parameter in model.Parameters)
            {
                parameters[parameter.Id] = parameter;
                values[parameter.Id] = parameter.Default;
            }

            Definitions = model.Parameters;
        }

        public IReadOnlyList<Parameter> Definitions { get; }

        /// <summary>
        /// パラメータの値を返す。モデルにないパラメータは 0 を返す。
        /// </summary>
        public double Get(string id)
        {
            return values.TryGetValue(id, out double value) ? value : 0;
        }

        /// <summary>
        /// パラメータの値を範囲に収めて設定する。モデルにないパラメータは無視する。
        /// </summary>
        public void Set(string id, double value)
        {
            if (!parameters.TryGetValue(id, out Parameter parameter) || double.IsNaN(value))
            {
                return;
            }

            values[id] = Math.Min(Math.Max(value, parameter.Min), parameter.Max);
        }

        /// <summary>
        /// すべてのパラメータを初期値に戻す。
        /// </summary>
        public void ResetToDefaults()
        {
            foreach (Parameter parameter in parameters.Values)
            {
                values[parameter.Id] = parameter.Default;
            }
        }
    }
}
