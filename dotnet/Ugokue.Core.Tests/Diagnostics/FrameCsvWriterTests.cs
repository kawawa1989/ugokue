using System.Globalization;
using System.IO;
using System.Threading;
using Ugokue.Core.Diagnostics;
using Xunit;

namespace Ugokue.Core.Tests.Diagnostics
{
    public class FrameCsvWriterTests
    {
        [Fact]
        public void WritesOneRowPerSampleWithInvariantNumbers()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var writer = new StringWriter { NewLine = "\n" };
                FrameCsvWriter.WriteSamples(writer, new[]
                {
                    new FrameSample(0, 0, 16.6667, 4.5),
                    new FrameSample(1, 0.0166667, 17.25, null),
                });

                Assert.Equal(
                    "frame,timestamp_s,frame_time_ms,cpu_time_ms\n" +
                    "0,0,16.667,4.5\n" +
                    "1,0.017,17.25,\n",
                    writer.ToString());
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Fact]
        public void WritesSummaryMetrics()
        {
            var writer = new StringWriter { NewLine = "\n" };
            FrameSummary summary = FrameSummary.From(new[]
            {
                new FrameSample(0, 0, 10, null),
                new FrameSample(1, 0.5, 20, null),
            });

            FrameCsvWriter.WriteSummary(writer, summary);

            Assert.Equal(
                "metric,value\n" +
                "frame_count,2\n" +
                "duration_s,0.5\n" +
                "average_fps,66.667\n" +
                "average_frame_time_ms,15\n" +
                "p95_frame_time_ms,20\n" +
                "p99_frame_time_ms,20\n" +
                "max_frame_time_ms,20\n" +
                "average_cpu_time_ms,\n",
                writer.ToString());
        }
    }
}
