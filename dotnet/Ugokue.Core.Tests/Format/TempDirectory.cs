using System;
using System.IO;

namespace Ugokue.Core.Tests.Format
{
    /// <summary>
    /// テストの間だけ使う一時フォルダを作り、破棄時に削除する。
    /// </summary>
    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ugokue-format-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        /// <summary>
        /// 一時フォルダの中のパスを返す。
        /// </summary>
        public string Combine(string relative)
        {
            return System.IO.Path.Combine(Path, relative);
        }

        /// <summary>
        /// 一時フォルダを中身ごと削除する。
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
