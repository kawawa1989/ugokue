using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ugokue.Core.Format;
using Ugokue.Core.Live;
using Xunit;

namespace Ugokue.Core.Tests.Live
{
    /// <summary>
    /// 実機の nizima LIVE で記録した入力を 60 fps で再生し、入力アダプターの出力を確かめるテスト。
    /// </summary>
    public class TrackingPlaybackTests
    {
        private const double Tick = 1.0 / 60;

        private static readonly string[] Angles = { "AngleX", "AngleY", "AngleZ" };

        [Fact]
        public void LoadsRecordedFramesInOrder()
        {
            RecordedTrackingSource source = Load("playback-short-loss");

            Assert.Equal(120, source.Frames.Count);
            Assert.True(source.Frames.Zip(source.Frames.Skip(1), (a, b) => b.ReceivedAtSeconds > a.ReceivedAtSeconds).All(x => x));
            Assert.NotNull(source.Frames[0].SourceTimestampUnixMs);
            Assert.Null(source.LatestFrame);

            source.Seek(source.Frames[10].ReceivedAtSeconds + 0.001);
            Assert.Same(source.Frames[10], source.LatestFrame);
        }

        [Fact]
        public void ShortFaceLossHoldsThePoseAndRecoversSmoothly()
        {
            List<Sample> samples = Play("playback-short-loss");
            TrackingFrame steadyFrame = Load("playback-short-loss").Frames.Last(f => f.ReceivedAtSeconds < 3.75);

            Sample beforeLoss = samples.Last(s => s.Time < 1.85);
            Sample[] lost = samples.Where(s => s.Time > 2.05 && s.Time < 2.68).ToArray();
            Assert.All(lost, s => Assert.Equal(TrackingState.Holding, s.State));
            Assert.All(lost, s => Assert.InRange(s.Values["EyeOpenL"], beforeLoss.Values["EyeOpenL"] - 0.05, beforeLoss.Values["EyeOpenL"] + 0.05));
            Assert.All(lost, s => Assert.InRange(s.Values["MouthOpenY"], beforeLoss.Values["MouthOpenY"] - 0.1, beforeLoss.Values["MouthOpenY"] + 0.1));

            Assert.True(MaxStep(samples, "AngleZ") < 3, $"step {MaxStep(samples, "AngleZ")}");
            Sample steady = samples.Last(s => s.Time < 3.75);
            Assert.Equal(TrackingState.Live, steady.State);
            Assert.InRange(steady.Values["AngleZ"], steadyFrame.Values["Roll"] - 0.5, steadyFrame.Values["Roll"] + 0.5);
        }

        [Fact]
        public void RawInputJumpsWhereTheAdapterDoesNot()
        {
            RecordedTrackingSource source = Load("playback-short-loss");

            double rawMaxStep = source.Frames.Zip(source.Frames.Skip(1), (a, b) => Math.Abs(b.Values["Roll"] - a.Values["Roll"])).Max();

            Assert.True(rawMaxStep > 5, $"raw step {rawMaxStep}");
            Assert.Contains(source.Frames, f => f.Values["FaceFound"] < 0.1 && f.Values["EyeOpenLeft"] > 0.6);
        }

        [Fact]
        public void LongFaceLossReturnsToTheFrontWithoutJumps()
        {
            List<Sample> samples = Play("playback-long-loss");

            Assert.Contains(samples, s => s.State == TrackingState.Holding);
            Sample last = samples.Last();
            Assert.Equal(TrackingState.Returning, last.State);
            Assert.All(Angles, a => Assert.InRange(last.Values[a], -0.5, 0.5));
            Assert.InRange(last.Values["EyeOpenL"], 0.95, 1);
            Assert.All(Angles, a => Assert.True(MaxStep(samples, a) < 3, $"{a} step {MaxStep(samples, a)}"));
        }

        [Fact]
        public void OutOfRangeAnglesStopAtTheParameterLimit()
        {
            RecordedTrackingSource source = Load("playback-out-of-range");
            Assert.Contains(source.Frames, f => f.Values["Yaw"] < -39);

            List<Sample> samples = Play("playback-out-of-range");

            Assert.All(samples, s => Assert.InRange(s.Values["AngleX"], -30, 30));
            Assert.InRange(samples.Min(s => s.Values["AngleX"]), -30, -29.9);
        }

        [Fact]
        public void CalibrationOnRecordedDataMakesThatPoseFront()
        {
            RecordedTrackingSource source = Load("playback-out-of-range");
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TrackingAdapterTests.TestModel());
            double t = 0;
            for (; t < 0.5; t += Tick)
            {
                source.Seek(t);
                adapter.Update(source.LatestFrame, t);
            }

            TrackingFrame calibrated = source.LatestFrame;
            Assert.True(adapter.Calibrate());
            for (int i = 0; i < 30; i++, t += Tick)
            {
                adapter.Update(calibrated, t);
            }

            Assert.All(Angles, a => Assert.InRange(adapter.Get(a).Value, -0.01, 0.01));
        }

        private static List<Sample> Play(string name)
        {
            RecordedTrackingSource source = Load(name);
            Model model = TrackingAdapterTests.TestModel();
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, model);
            var samples = new List<Sample>();
            double end = source.Frames[source.Frames.Count - 1].ReceivedAtSeconds;
            for (double t = 0; t <= end; t += Tick)
            {
                source.Seek(t);
                adapter.Update(source.LatestFrame, t);
                if (adapter.State != TrackingState.NoInput)
                {
                    samples.Add(new Sample(t, adapter.State, model.Parameters.ToDictionary(p => p.Id, p => adapter.Get(p.Id) ?? p.Default)));
                }
            }

            return samples;
        }

        private static double MaxStep(List<Sample> samples, string parameter)
        {
            return samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b.Values[parameter] - a.Values[parameter])).Max();
        }

        private static RecordedTrackingSource Load(string name)
        {
            return RecordedTrackingSource.Load(Path.Combine(TestPaths.RepositoryRoot, "dotnet", "Ugokue.Core.Tests", "Live", "Fixtures", name + ".jsonl"));
        }

        private sealed class Sample
        {
            public Sample(double time, TrackingState state, Dictionary<string, double> values)
            {
                Time = time;
                State = state;
                Values = values;
            }

            public double Time { get; }

            public TrackingState State { get; }

            public Dictionary<string, double> Values { get; }
        }
    }
}
