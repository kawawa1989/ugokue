using System;
using System.Linq;
using Ugokue.Core.Diagnostics;
using Xunit;

namespace Ugokue.Core.Tests.Diagnostics
{
    public class FrameRecorderTests
    {
        [Fact]
        public void RecordsEveryFrameForTheRequestedDuration()
        {
            var recorder = new FrameRecorder(10);
            const double frameSeconds = 1.0 / 60;

            int frame = 0;
            while (recorder.Add(100 + frame * frameSeconds, frameSeconds * 1000, 5))
            {
                frame++;
            }

            Assert.True(recorder.IsComplete);
            Assert.Equal(601, recorder.Samples.Count);
            Assert.Equal(Enumerable.Range(0, 601), recorder.Samples.Select(s => s.Index));
            Assert.Equal(0, recorder.Samples[0].TimestampSeconds);
            Assert.True(recorder.Samples[^1].TimestampSeconds >= 10);
        }

        [Fact]
        public void IgnoresFramesAfterCompletion()
        {
            var recorder = new FrameRecorder(1);
            recorder.Add(0, 16, null);
            recorder.Add(1, 16, null);

            Assert.False(recorder.Add(2, 16, null));
            Assert.Equal(2, recorder.Samples.Count);
        }

        [Fact]
        public void SkipsFramesDuringWarmup()
        {
            var recorder = new FrameRecorder(1, warmupSeconds: 2);
            recorder.Add(10.0, 2000, null);
            recorder.Add(11.0, 1000, null);
            recorder.Add(12.0, 16, null);
            recorder.Add(13.0, 17, null);

            Assert.True(recorder.IsComplete);
            Assert.Equal(new[] { 16.0, 17.0 }, recorder.Samples.Select(s => s.FrameTimeMs));
            Assert.Equal(new[] { 0.0, 1.0 }, recorder.Samples.Select(s => s.TimestampSeconds));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(double.NaN)]
        public void RejectsNonPositiveDuration(double duration)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRecorder(duration));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(double.NaN)]
        public void RejectsNegativeWarmup(double warmup)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRecorder(1, warmup));
        }
    }
}
