using System;
using Ugokue.Core.Live;
using Xunit;

namespace Ugokue.Core.Tests.Live
{
    public class ReceiveRateMeterTests
    {
        [Fact]
        public void ReturnsNullWithFewerThanTwoFrames()
        {
            var meter = new ReceiveRateMeter();
            Assert.Null(meter.Snapshot());
            meter.Add(1);
            Assert.Null(meter.Snapshot());
        }

        [Fact]
        public void ComputesRateAndIntervals()
        {
            var meter = new ReceiveRateMeter(10);
            double t = 0;
            for (int i = 0; i < 60; i++)
            {
                meter.Add(t);
                t += i % 10 == 9 ? 0.05 : 1.0 / 60;
            }

            ReceiveRateStats stats = meter.Snapshot();

            Assert.InRange(stats.FramesPerSecond, 50, 60);
            Assert.Equal(50, stats.MaxIntervalMs, 6);
            Assert.InRange(stats.P95IntervalMs, 16.6, 50.1);
            Assert.InRange(stats.AverageIntervalMs, 16.6, 20);
        }

        [Fact]
        public void DropsFramesOlderThanTheWindow()
        {
            var meter = new ReceiveRateMeter(1);
            meter.Add(0);
            meter.Add(0.5);
            meter.Add(5);
            meter.Add(5.1);

            ReceiveRateStats stats = meter.Snapshot();

            Assert.Equal(100, stats.MaxIntervalMs, 6);
        }

        [Fact]
        public void RejectsNonPositiveWindow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReceiveRateMeter(0));
        }
    }
}
