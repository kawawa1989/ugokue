using System.IO;
using Ugokue.App.Rig;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ugokue.Editor
{
    /// <summary>
    /// ビルドの前に固定テストモデルを StreamingAssets にコピーし、ビルドの後に取り除く。
    /// </summary>
    public sealed class StreamingModelBuildHook : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        /// <summary>
        /// プロジェクトの StreamingAssets フォルダの絶対パスを返す。
        /// </summary>
        public static string StreamingAssetsRoot => Path.Combine(Application.dataPath, "StreamingAssets");

        /// <summary>
        /// ビルドの前に、リポジトリの固定テストモデルを StreamingAssets にコピーする。
        /// </summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            RemoveModel(StreamingAssetsRoot);
            CopyModel(ModelLocation.RepositorySampleDirectory(Application.dataPath), StreamingAssetsRoot);
        }

        /// <summary>
        /// ビルドの後に、コピーしたモデルを StreamingAssets から取り除く。
        /// </summary>
        public void OnPostprocessBuild(BuildReport report)
        {
            RemoveModel(StreamingAssetsRoot);
        }

        /// <summary>
        /// モデルのフォルダから model.json と textures の PNG を、<paramref name="streamingAssetsRoot"/> の下のモデル用フォルダにコピーする。
        /// </summary>
        public static void CopyModel(string sourceDirectory, string streamingAssetsRoot)
        {
            string destination = Path.Combine(streamingAssetsRoot, ModelLocation.StreamingAssetsModelPath);
            string textures = Path.Combine(destination, "textures");
            Directory.CreateDirectory(textures);
            File.Copy(Path.Combine(sourceDirectory, "model.json"), Path.Combine(destination, "model.json"), true);
            foreach (string png in Directory.GetFiles(Path.Combine(sourceDirectory, "textures"), "*.png"))
            {
                File.Copy(png, Path.Combine(textures, Path.GetFileName(png)), true);
            }
        }

        /// <summary>
        /// コピーしたモデルのフォルダと .meta を削除し、空になった親フォルダも .meta ごと削除する。
        /// </summary>
        public static void RemoveModel(string streamingAssetsRoot)
        {
            string modelDirectory = Path.Combine(streamingAssetsRoot, ModelLocation.StreamingAssetsModelPath);
            DeleteDirectoryWithMeta(modelDirectory, true);
            for (string parent = Path.GetDirectoryName(modelDirectory);
                parent != null && parent.Length >= streamingAssetsRoot.TrimEnd('/', '\\').Length;
                parent = Path.GetDirectoryName(parent))
            {
                if (Directory.Exists(parent) && Directory.GetFileSystemEntries(parent).Length > 0)
                {
                    break;
                }

                DeleteDirectoryWithMeta(parent, false);
            }
        }

        private static void DeleteDirectoryWithMeta(string directory, bool recursive)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive);
            }

            string meta = directory.TrimEnd('/', '\\') + ".meta";
            if (File.Exists(meta))
            {
                File.Delete(meta);
            }
        }
    }
}
