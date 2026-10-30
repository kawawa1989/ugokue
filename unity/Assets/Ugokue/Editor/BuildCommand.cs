using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Ugokue.Editor
{
    /// <summary>
    /// コマンドラインからスタンドアロン版をビルドする。
    /// </summary>
    public static class BuildCommand
    {
        private const string OutputArgument = "-ugokue-build-out";

        /// <summary>
        /// macOS 向けにビルドする。
        /// </summary>
        public static void BuildMacOS()
        {
            Build(BuildTarget.StandaloneOSX, "Builds/macOS/ugokue.app");
        }

        /// <summary>
        /// Windows 向けにビルドする。
        /// </summary>
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, "Builds/Windows/ugokue.exe");
        }

        private static void Build(BuildTarget target, string defaultOutput)
        {
            string output = OutputPathFromArgs() ?? defaultOutput;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectBootstrap.MainScenePath },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new Exception($"Build failed: {report.summary.result}, {report.summary.totalErrors} errors");
            }
        }

        private static string OutputPathFromArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, OutputArgument);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
