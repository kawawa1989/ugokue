using System;
using System.IO;
using UnityEngine;

namespace Ugokue.App.Rig
{
    /// <summary>
    /// 起動時に読み込むモデルのフォルダを、コマンドライン引数と実行環境から決める。
    /// </summary>
    public static class ModelLocation
    {
        public const string ModelArgument = "-ugokue-model";

        public const string StreamingAssetsModelPath = "Models/test-face";

        public const string RepositorySamplePath = "format/samples/test-face";

        /// <summary>
        /// 現在のプロセスの引数と実行環境から、モデルのフォルダの絶対パスを返す。
        /// </summary>
        public static string Resolve()
        {
            return Resolve(Environment.GetCommandLineArgs(), Application.isEditor, Application.dataPath, Application.streamingAssetsPath);
        }

        /// <summary>
        /// <c>-ugokue-model</c> の値があればそれを、なければエディタではリポジトリの固定テストモデル、プレイヤーでは StreamingAssets のモデルを返す。
        /// </summary>
        public static string Resolve(string[] args, bool isEditor, string dataPath, string streamingAssetsPath)
        {
            string fromArgs = ArgumentValue(args);
            if (!string.IsNullOrEmpty(fromArgs))
            {
                return Path.GetFullPath(fromArgs);
            }

            return isEditor
                ? RepositorySampleDirectory(dataPath)
                : Path.GetFullPath(Path.Combine(streamingAssetsPath, StreamingAssetsModelPath));
        }

        /// <summary>
        /// Unity プロジェクトの Assets フォルダのパスから、リポジトリにある固定テストモデルのフォルダを返す。
        /// </summary>
        public static string RepositorySampleDirectory(string dataPath)
        {
            return Path.GetFullPath(Path.Combine(dataPath, "..", "..", RepositorySamplePath));
        }

        private static string ArgumentValue(string[] args)
        {
            if (args == null)
            {
                return null;
            }

            int index = Array.IndexOf(args, ModelArgument);
            if (index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            {
                return null;
            }

            return args[index + 1];
        }
    }
}
