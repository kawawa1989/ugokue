using System;
using Ugokue.Core.Live;
using Xunit;

namespace Ugokue.Core.Tests.Live
{
    public class LatencyMeterTests
    {
        [Fact]
        public void ReturnsNullBeforeAnySample()
        {
            Assert.Null(new LatencyMeter().Snapshot());
        }

        [Fact]
        public void ComputesAverageP95AndMaximumOfRecentSamples()
        {
            var meter = new LatencyMeter(100);
            for (int i = 1; i <= 100; i++)
            {
                meter.Add(i);
            }

            LatencyStats stats = meter.Snapshot();

            Assert.Equal(50.5, stats.AverageMs, 9);
            Assert.Equal(95, stats.P95Ms, 9);
            Assert.Equal(100, stats.MaxMs, 9);
            Assert.Equal(100, stats.Count);
        }

        [Fact]
        public void KeepsOnlyTheMostRecentSamples()
        {
            var meter = new LatencyMeter(3);
            foreach (double ms in new double[] { 100, 1, 2, 3 })
            {
                meter.Add(ms);
            }

            Assert.Equal(3, meter.Snapshot().MaxMs, 9);
        }

        [Fact]
        public void IgnoresInvalidSamples()
        {
            var meter = new LatencyMeter();
            meter.Add(double.NaN);
            meter.Add(-5);

            Assert.Null(meter.Snapshot());
        }

        [Fact]
        public void RejectsNonPositiveCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LatencyMeter(0));
        }
    }
}
