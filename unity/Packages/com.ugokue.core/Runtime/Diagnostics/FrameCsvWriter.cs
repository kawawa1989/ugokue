using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Ugokue.Core.Diagnostics
{
    /// <summary>
    /// フレームの計測値と集計値を CSV 形式で書き出す。
    /// </summary>
    public static class FrameCsvWriter
    {
        public const string SamplesHeader = "frame,timestamp_s,frame_time_ms,cpu_time_ms";

        public const string SummaryHeader = "metric,value";

        /// <summary>
        /// フレームごとの計測値を、1フレーム1行で書き出す。
        /// </summary>
        public static void WriteSamples(TextWriter writer, IEnumerable<FrameSample> samples)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            writer.WriteLine(SamplesHeader);
            foreach (FrameSample sample in samples)
            {
                writer.WriteLine(string.Join(
                    ",",
                    sample.Index.ToString(CultureInfo.InvariantCulture),
                    Format(sample.TimestampSeconds),
                    Format(sample.FrameTimeMs),
                    sample.CpuTimeMs.HasValue ? Format(sample.CpuTimeMs.Value) : string.Empty));
            }
        }

        /// <summary>
        /// 集計値を、1項目1行で書き出す。
        /// </summary>
        public static void WriteSummary(TextWriter writer, FrameSummary summary)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            writer.WriteLine(SummaryHeader);
            writer.WriteLine("frame_count," + summary.FrameCount.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("duration_s," + Format(summary.DurationSeconds));
            writer.WriteLine("average_fps," + Format(summary.AverageFps));
            writer.WriteLine("average_frame_time_ms," + Format(summary.AverageFrameTimeMs));
            writer.WriteLine("p95_frame_time_ms," + Format(summary.P95FrameTimeMs));
            writer.WriteLine("p99_frame_time_ms," + Format(summary.P99FrameTimeMs));
            writer.WriteLine("max_frame_time_ms," + Format(summary.MaxFrameTimeMs));
            writer.WriteLine("average_cpu_time_ms," + (summary.AverageCpuTimeMs.HasValue ? Format(summary.AverageCpuTimeMs.Value) : string.Empty));
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
