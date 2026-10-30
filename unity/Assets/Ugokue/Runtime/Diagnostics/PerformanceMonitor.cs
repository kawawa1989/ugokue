using System;
using System.IO;
using Ugokue.Core.Diagnostics;
using UnityEngine;

namespace Ugokue.App.Diagnostics
{
    /// <summary>
    /// fps とフレーム時間を画面に表示し、指定した秒数の計測値を CSV に記録する。
    /// </summary>
    public sealed class PerformanceMonitor : MonoBehaviour
    {
        [SerializeField] private KeyCode toggleOverlayKey = KeyCode.F1;
        [SerializeField] private KeyCode startRecordingKey = KeyCode.F9;
        [SerializeField] private double manualRecordSeconds = 10;
        [SerializeField] private bool showOverlay = true;

        private readonly RollingFrameStats rollingStats = new RollingFrameStats(60);
        private readonly FrameTiming[] frameTimings = new FrameTiming[1];
        private FrameRecorder recorder;
        private string outputPath;
        private bool quitAfterRecord;
        private string lastResult = string.Empty;
        private double? latestCpuTimeMs;
        private GUIStyle overlayStyle;

        public bool IsRecording => recorder != null && !recorder.IsComplete;

        /// <summary>
        /// ウォームアップ後に計測を開始する。出力先を省略した場合は永続データフォルダに保存する。
        /// </summary>
        public void StartRecording(double seconds, double warmupSeconds = 0, string path = null)
        {
            recorder = new FrameRecorder(seconds, warmupSeconds);
            outputPath = string.IsNullOrEmpty(path) ? DefaultOutputPath() : path;
            lastResult = $"Recording {seconds:0.#}s";
        }

        private void Awake()
        {
            RecordingOptions options = RecordingOptions.Parse(Environment.GetCommandLineArgs());
            if (options.AutoRecord)
            {
                quitAfterRecord = options.QuitAfterRecord;
                StartRecording(options.RecordSeconds.Value, options.WarmupSeconds, options.OutputPath);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleOverlayKey))
            {
                showOverlay = !showOverlay;
            }

            if (Input.GetKeyDown(startRecordingKey) && !IsRecording)
            {
                StartRecording(manualRecordSeconds);
            }

            double frameTimeMs = Time.unscaledDeltaTime * 1000.0;
            latestCpuTimeMs = ReadCpuFrameTimeMs();
            rollingStats.Add(frameTimeMs);

            if (IsRecording && !recorder.Add(Time.realtimeSinceStartupAsDouble, frameTimeMs, latestCpuTimeMs))
            {
                FinishRecording();
            }
        }

        private void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            overlayStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = Color.white } };
            string cpu = latestCpuTimeMs.HasValue ? $"{latestCpuTimeMs.Value:0.00} ms" : "n/a";
            GUI.Label(
                new Rect(12, 12, 480, 120),
                $"FPS {rollingStats.AverageFps:0.0}\nFrame {rollingStats.AverageFrameTimeMs:0.00} ms\nCPU {cpu}\n{lastResult}",
                overlayStyle);
        }

        private double? ReadCpuFrameTimeMs()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, frameTimings) == 0)
            {
                return null;
            }

            double cpuMs = frameTimings[0].cpuMainThreadFrameTime;
            return cpuMs > 0 ? cpuMs : (double?)null;
        }

        private void FinishRecording()
        {
            FrameSummary summary = FrameSummary.From(recorder.Samples);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
            using (var writer = new StreamWriter(outputPath))
            {
                FrameCsvWriter.WriteSamples(writer, recorder.Samples);
            }

            string summaryPath = Path.ChangeExtension(outputPath, null) + "_summary.csv";
            using (var writer = new StreamWriter(summaryPath))
            {
                FrameCsvWriter.WriteSummary(writer, summary);
            }

            lastResult = $"Saved {summary.FrameCount} frames, avg {summary.AverageFps:0.0} fps";
            Debug.Log($"[PerformanceMonitor] {lastResult} to {outputPath}");

            if (quitAfterRecord)
            {
                Application.Quit();
            }
        }

        private static string DefaultOutputPath()
        {
            return Path.Combine(Application.persistentDataPath, "perf", $"perf_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }
    }
}
