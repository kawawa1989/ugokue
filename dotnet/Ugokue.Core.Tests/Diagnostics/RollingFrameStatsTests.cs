using System;
using Ugokue.Core.Diagnostics;
using Xunit;

namespace Ugokue.Core.Tests.Diagnostics
{
    public class RollingFrameStatsTests
    {
        [Fact]
        public void ReturnsZeroBeforeAnyFrame()
        {
            var stats = new RollingFrameStats(4);

            Assert.Equal(0, stats.AverageFrameTimeMs);
            Assert.Equal(0, stats.AverageFps);
        }

        [Fact]
        public void AveragesOnlyTheMostRecentWindow()
        {
            var stats = new RollingFrameStats(2);
            stats.Add(10);
            stats.Add(20);
            stats.Add(40);

            Assert.Equal(30, stats.AverageFrameTimeMs, 9);
            Assert.Equal(1000.0 / 30, stats.AverageFps, 9);
        }

        [Fact]
        public void RejectsNonPositiveWindow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RollingFrameStats(0));
        }
    }
}
