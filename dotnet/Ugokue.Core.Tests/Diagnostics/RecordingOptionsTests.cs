using System;
using Ugokue.Core.Diagnostics;
using Xunit;

namespace Ugokue.Core.Tests.Diagnostics
{
    public class RecordingOptionsTests
    {
        [Fact]
        public void DisablesAutoRecordWithoutArguments()
        {
            RecordingOptions options = RecordingOptions.Parse(new[] { "-batchmode" });

            Assert.False(options.AutoRecord);
            Assert.Null(options.OutputPath);
            Assert.False(options.QuitAfterRecord);
            Assert.Equal(RecordingOptions.DefaultWarmupSeconds, options.WarmupSeconds);
        }

        [Fact]
        public void ReadsAllRecordingArguments()
        {
            RecordingOptions options = RecordingOptions.Parse(new[]
            {
                "-ugokue-record-seconds", "30.5",
                "-ugokue-record-warmup", "0",
                "-ugokue-record-out", "/tmp/perf.csv",
                "-ugokue-quit-after-record",
            });

            Assert.True(options.AutoRecord);
            Assert.Equal(30.5, options.RecordSeconds);
            Assert.Equal(0, options.WarmupSeconds);
            Assert.Equal("/tmp/perf.csv", options.OutputPath);
            Assert.True(options.QuitAfterRecord);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("abc")]
        public void RejectsInvalidSeconds(string value)
        {
            Assert.Throws<ArgumentException>(() => RecordingOptions.Parse(new[] { "-ugokue-record-seconds", value }));
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("NaN")]
        public void RejectsInvalidWarmup(string value)
        {
            Assert.Throws<ArgumentException>(() => RecordingOptions.Parse(new[] { "-ugokue-record-warmup", value }));
        }

        [Fact]
        public void RejectsMissingValue()
        {
            Assert.Throws<ArgumentException>(() => RecordingOptions.Parse(new[] { "-ugokue-record-out" }));
        }
    }
}
