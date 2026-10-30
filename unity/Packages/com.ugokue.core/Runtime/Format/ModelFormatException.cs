using System;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// 中間形式のモデルが読み書きできないことを表す例外。
    /// </summary>
    public sealed class ModelFormatException : Exception
    {
        public ModelFormatException(string message)
            : base(message)
        {
        }

        public ModelFormatException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
