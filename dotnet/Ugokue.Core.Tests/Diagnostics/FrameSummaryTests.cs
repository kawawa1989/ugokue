using System;
using System.Collections.Generic;
using System.Linq;
using Ugokue.Core.Diagnostics;
using Xunit;

namespace Ugokue.Core.Tests.Diagnostics
{
    public class FrameSummaryTests
    {
        [Fact]
        public void ComputesAveragesPercentilesAndMaximum()
        {
            List<FrameSample> samples = Enumerable.Range(1, 100)
                .Select(i => new FrameSample(i - 1, (i - 1) * 0.01, i, i % 2 == 0 ? 4.0 : (double?)null))
                .ToList();

            FrameSummary summary = FrameSummary.From(samples);

            Assert.Equal(100, summary.FrameCount);
            Assert.Equal(0.99, summary.DurationSeconds, 9);
            Assert.Equal(50.5, summary.AverageFrameTimeMs, 9);
            Assert.Equal(1000 / 50.5, summary.AverageFps, 9);
            Assert.Equal(95, summary.P95FrameTimeMs);
            Assert.Equal(99, summary.P99FrameTimeMs);
            Assert.Equal(100, summary.MaxFrameTimeMs);
            Assert.Equal(4.0, summary.AverageCpuTimeMs);
        }

        [Fact]
        public void LeavesCpuTimeEmptyWhenNoSampleHasIt()
        {
            FrameSummary summary = FrameSummary.From(new[] { new FrameSample(0, 0, 16, null) });

            Assert.Null(summary.AverageCpuTimeMs);
            Assert.Equal(16, summary.P95FrameTimeMs);
        }

        [Fact]
        public void RejectsEmptySamples()
        {
            Assert.Throws<ArgumentException>(() => FrameSummary.From(Array.Empty<FrameSample>()));
        }
    }
}
