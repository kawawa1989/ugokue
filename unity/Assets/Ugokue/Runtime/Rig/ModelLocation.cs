using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Ugokue.App.Rig
{
    /// <summary>
    /// 起動時に読み込むモデルのフォルダを、コマンドライン引数、エディタでの選択、実行環境から決める。
    /// </summary>
    public static class ModelLocation
    {
        public const string ModelArgument = "-ugokue-model";

        public const string StreamingAssetsModelPath = "Models/test-face";

        public const string RepositorySamplePath = "format/samples/test-face";

        public const string GeneratedModelsPath = "artifacts/keyforms";

        public const string EditorSelectionKey = "Ugokue.ModelDirectory";

        /// <summary>
        /// 現在のプロセスの引数、エディタでの選択、実行環境から、モデルのフォルダの絶対パスを返す。
        /// </summary>
        public static string Resolve()
        {
            return Resolve(Environment.GetCommandLineArgs(), Application.isEditor, Application.dataPath, Application.streamingAssetsPath, EditorSelection);
        }

        /// <summary>
        /// <c>-ugokue-model</c> の値があればそれを、なければエディタでは選んだフォルダ（あれば）かリポジトリの固定テストモデル、プレイヤーでは StreamingAssets のモデルを返す。
        /// </summary>
        public static string Resolve(string[] args, bool isEditor, string dataPath, string streamingAssetsPath, string editorSelection = null)
        {
            string fromArgs = ArgumentValue(args);
            if (!string.IsNullOrEmpty(fromArgs))
            {
                return Path.GetFullPath(fromArgs);
            }

            if (isEditor && !string.IsNullOrEmpty(editorSelection) && Directory.Exists(editorSelection))
            {
                return Path.GetFullPath(editorSelection);
            }

            return isEditor
                ? RepositorySampleDirectory(dataPath)
                : Path.GetFullPath(Path.Combine(streamingAssetsPath, StreamingAssetsModelPath));
        }

        /// <summary>
        /// エディタで選んだモデルのフォルダ（このマシンのエディタの設定）。選んでいなければ空。プレイヤーでは常に空。
        /// </summary>
        public static string EditorSelection
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetString(EditorSelectionKey, string.Empty);
#else
                return string.Empty;
#endif
            }
        }

        /// <summary>
        /// Unity プロジェクトの Assets フォルダのパスから、リポジトリにある固定テストモデルのフォルダを返す。
        /// </summary>
        public static string RepositorySampleDirectory(string dataPath)
        {
            return Path.GetFullPath(Path.Combine(dataPath, "..", "..", RepositorySamplePath));
        }

        /// <summary>
        /// エディタで選べるモデルの名前とフォルダ。固定テストモデルと、<c>ugokue-pipeline keyforms</c> が書いたモデル（model.json があるもの）を名前順に返す。
        /// </summary>
        public static IReadOnlyList<(string Name, string Directory)> Candidates(string dataPath)
        {
            var result = new List<(string Name, string Directory)> { ("test-face", RepositorySampleDirectory(dataPath)) };
            string generated = Path.GetFullPath(Path.Combine(dataPath, "..", "..", GeneratedModelsPath));
            if (Directory.Exists(generated))
            {
                result.AddRange(Directory.GetDirectories(generated)
                    .Select(d => (Name: Path.GetFileName(d), Directory: Path.Combine(d, "model")))
                    .Where(c => File.Exists(Path.Combine(c.Directory, "model.json")))
                    .OrderBy(c => c.Name, StringComparer.Ordinal)
                    .Select(c => (c.Name, Path.GetFullPath(c.Directory))));
            }

            return result;
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
