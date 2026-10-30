using System;
using System.IO;

namespace Ugokue.Core.Tests
{
    /// <summary>
    /// テストが使うリポジトリ内のフォルダの場所を返す。
    /// </summary>
    internal static class TestPaths
    {
        private static readonly Lazy<string> Root = new Lazy<string>(FindRepositoryRoot);

        public static string RepositoryRoot => Root.Value;

        public static string TestFaceDirectory => Path.Combine(RepositoryRoot, "format", "samples", "test-face");

        private static string FindRepositoryRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")) && Directory.Exists(Path.Combine(dir.FullName, "format", "samples")))
                {
                    return dir.FullName;
                }
            }

            throw new DirectoryNotFoundException("Repository root not found from " + AppContext.BaseDirectory);
        }
    }
}
