using System;
using System.Collections.Generic;
using Ugokue.Core.Format;
using Ugokue.Core.Live;
using Ugokue.Core.Rig;
using Xunit;

namespace Ugokue.Core.Tests.Live
{
    public class TrackingAdapterTests
    {
        private const double Tick = 1.0 / 60;

        [Fact]
        public void MapsSourceValuesToModelParameters()
        {
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TestModel(), NoSmoothing());

            adapter.Update(Frame(0, ("Yaw", 12), ("Pitch", -5), ("Roll", 3), ("EyeOpenLeft", 0.25), ("EyeOpenRight", 0.75), ("MouthOpen", 0.5)), 0);

            Assert.Equal(TrackingState.Live, adapter.State);
            Assert.Equal(12, adapter.Get("AngleX").Value, 9);
            Assert.Equal(-5, adapter.Get("AngleY").Value, 9);
            Assert.Equal(3, adapter.Get("AngleZ").Value, 9);
            Assert.Equal(0.25, adapter.Get("EyeOpenL").Value, 9);
            Assert.Equal(0.75, adapter.Get("EyeOpenR").Value, 9);
            Assert.Equal(0.5, adapter.Get("MouthOpenY").Value, 9);
        }

        [Fact]
        public void RemapsInputRangeToOutputRange()
        {
            var profile = new TrackingProfile(null, new[] { new TrackingMapping("In", "AngleX", 0, 1, -30, 30) });
            var adapter = new TrackingAdapter(profile, TestModel(), NoSmoothing());

            adapter.Update(Frame(0, ("In", 0.75)), 0);

            Assert.Equal(15, adapter.Get("AngleX").Value, 9);
        }

        [Theory]
        [InlineData("Yaw", -39.8, "AngleX", -30)]
        [InlineData("Roll", 49, "AngleZ", 30)]
        [InlineData("EyeOpenLeft", 2.07, "EyeOpenL", 1)]
        [InlineData("MouthOpen", -0.05, "MouthOpenY", 0)]
        public void ClampsToTheParameterRange(string source, double value, string target, double expected)
        {
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TestModel(), NoSmoothing());

            adapter.Update(Frame(0, (source, value)), 0);

            Assert.Equal(expected, adapter.Get(target).Value, 9);
        }

        [Fact]
        public void CalibrationMakesTheCurrentPoseFront()
        {
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TestModel(), NoSmoothing());
            adapter.Update(Frame(0, ("Yaw", 12), ("Pitch", -8), ("EyeOpenLeft", 0.6)), 0);

            Assert.True(adapter.Calibrate());
            adapter.Update(Frame(Tick, ("Yaw", 22), ("Pitch", -8), ("EyeOpenLeft", 0.6)), Tick);

            Assert.Equal(10, adapter.Get("AngleX").Value, 9);
            Assert.Equal(0, adapter.Get("AngleY").Value, 9);
            Assert.Equal(0.6, adapter.Get("EyeOpenL").Value, 9);

            adapter.ResetCalibration();
            adapter.Update(Frame(2 * Tick, ("Yaw", 22)), 2 * Tick);
            Assert.Equal(22, adapter.Get("AngleX").Value, 9);
        }

        [Fact]
        public void CalibrationNeedsAValidFrame()
        {
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TestModel());

            Assert.False(adapter.Calibrate());
            adapter.Update(Frame(0, ("FaceFound", 0), ("Yaw", 12)), 0);
            Assert.False(adapter.Calibrate());
        }

        [Fact]
        public void SmoothingFollowsAtTheSameSpeedAtAnyFrameRate()
        {
            double at60 = StepResponse(60, 0.1);
            double at144 = StepResponse(144, 0.1);
            double at30 = StepResponse(30, 0.1);

            Assert.InRange(at60, 9, 10);
            Assert.Equal(at60, at144, 1);
            Assert.Equal(at60, at30, 1);
        }

        [Fact]
        public void DefaultSmoothingKeepsHalfOfThePreviousValuePerSixtiethSecond()
        {
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TestModel());
            adapter.Update(Frame(0, ("Yaw", 0)), 0);

            adapter.Update(Frame(Tick, ("Yaw", 10)), Tick);

            Assert.Equal(5, adapter.Get("AngleX").Value, 6);
        }

        [Fact]
        public void ModelSmoothingOverridesTheDefault()
        {
            Model model = TestModel();
            model.Tracking = new Tracking { Parameters = { new TrackingParameter { Parameter = "AngleX", Smoothing = 0 } } };
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, model);
            adapter.Update(Frame(0, ("Yaw", 0), ("Pitch", 0)), 0);

            adapter.Update(Frame(Tick, ("Yaw", 10), ("Pitch", 10)), Tick);

            Assert.Equal(10, adapter.Get("AngleX").Value, 9);
            Assert.Equal(5, adapter.Get("AngleY").Value, 6);
        }

        [Fact]
        public void HoldsTheLastValuesWhileTheFaceIsLost()
        {
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TestModel(), NoSmoothing());
            adapter.Update(Frame(0, ("FaceFound", 1), ("Yaw", 20), ("EyeOpenLeft", 0.1)), 0);

            double t = 0;
            while (t < 0.9)
            {
                t += Tick;
                adapter.Update(Frame(t, ("FaceFound", 0), ("Yaw", 0), ("EyeOpenLeft", 1)), t);
            }

            Assert.Equal(TrackingState.Holding, adapter.State);
            Assert.Equal(20, adapter.Get("AngleX").Value, 9);
            Assert.Equal(0.1, adapter.Get("EyeOpenL").Value, 9);
        }

        [Fact]
        public void HoldsWhenFramesStopArriving()
        {
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TestModel(), NoSmoothing());
            TrackingFrame last = Frame(0, ("Yaw", 20));
            adapter.Update(last, 0);

            adapter.Update(last, 0.1);
            Assert.Equal(TrackingState.Live, adapter.State);
            adapter.Update(last, 0.5);

            Assert.Equal(TrackingState.Holding, adapter.State);
            Assert.Equal(20, adapter.Get("AngleX").Value, 9);
        }

        [Fact]
        public void ReturnsSlowlyToDefaultsAfterTheHold()
        {
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TestModel(), NoSmoothing());
            TrackingFrame last = Frame(0, ("Yaw", 20), ("EyeOpenLeft", 0.1));
            adapter.Update(last, 0);

            double previous = 20;
            double maxStep = 0;
            for (double t = Tick; t < 4; t += Tick)
            {
                adapter.Update(last, t);
                maxStep = Math.Max(maxStep, Math.Abs(adapter.Get("AngleX").Value - previous));
                previous = adapter.Get("AngleX").Value;
            }

            Assert.Equal(TrackingState.Returning, adapter.State);
            Assert.InRange(adapter.Get("AngleX").Value, -0.1, 0.1);
            Assert.InRange(adapter.Get("EyeOpenL").Value, 0.99, 1);
            Assert.True(maxStep < 1, $"step {maxStep}");
        }

        [Fact]
        public void RecoversWithoutAJump()
        {
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TestModel());
            double t = 0;
            for (; t < 0.5; t += Tick)
            {
                adapter.Update(Frame(t, ("Yaw", 0)), t);
            }

            for (; t < 1.3; t += Tick)
            {
                adapter.Update(Frame(t, ("FaceFound", 0), ("Yaw", 0)), t);
            }

            double previous = adapter.Get("AngleX").Value;
            double maxStep = 0;
            double recoveredAt = t;
            for (; t < recoveredAt + 1; t += Tick)
            {
                adapter.Update(Frame(t, ("Yaw", 20)), t);
                maxStep = Math.Max(maxStep, Math.Abs(adapter.Get("AngleX").Value - previous));
                previous = adapter.Get("AngleX").Value;
            }

            Assert.Equal(TrackingState.Live, adapter.State);
            Assert.True(maxStep < 3, $"step {maxStep}");
            Assert.InRange(adapter.Get("AngleX").Value, 19.9, 20);
        }

        [Fact]
        public void WithoutRecoveryHandlingTheSameInputWouldJump()
        {
            var options = new TrackingAdapterOptions { RecoverySeconds = 0 };
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TestModel(), options);
            adapter.Update(Frame(0, ("Yaw", 0)), 0);
            adapter.Update(Frame(1.3, ("FaceFound", 0), ("Yaw", 0)), 1.3);

            adapter.Update(Frame(1.3 + Tick, ("Yaw", 20)), 1.3 + Tick);

            Assert.True(adapter.Get("AngleX") >= 3);
        }

        [Fact]
        public void DoesNotWriteParametersBeforeTheFirstValidFrame()
        {
            Model model = TestModel();
            var values = new ParameterValues(model);
            values.Set("AngleX", 7);
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, model);

            adapter.Update(null, 0);
            adapter.ApplyTo(values);
            adapter.Update(Frame(0.1, ("FaceFound", 0), ("Yaw", 20)), 0.1);
            adapter.ApplyTo(values);

            Assert.Equal(TrackingState.NoInput, adapter.State);
            Assert.Null(adapter.Get("AngleX"));
            Assert.Equal(7, values.Get("AngleX"));
        }

        [Fact]
        public void WritesOnlyMappedParameters()
        {
            Model model = TestModel();
            var values = new ParameterValues(model);
            values.Set("Breath", 0.4);
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, model, NoSmoothing());

            adapter.Update(Frame(0, ("Yaw", 12)), 0);
            adapter.ApplyTo(values);

            Assert.Equal(12, values.Get("AngleX"));
            Assert.Equal(0.4, values.Get("Breath"));
        }

        [Fact]
        public void IgnoresMappingsToParametersTheModelDoesNotHave()
        {
            var model = new Model { Parameters = { new Parameter { Id = "AngleX", Min = -30, Max = 30, Default = 0 } } };
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, model, NoSmoothing());

            adapter.Update(Frame(0, ("Yaw", 12), ("MouthOpen", 1)), 0);

            Assert.Equal(12, adapter.Get("AngleX").Value, 9);
            Assert.Null(adapter.Get("MouthOpenY"));
        }

        [Fact]
        public void MissingSourceValuesKeepTheParameterDefault()
        {
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TestModel(), NoSmoothing());

            adapter.Update(Frame(0, ("Yaw", 12)), 0);

            Assert.Equal(1, adapter.Get("EyeOpenL").Value, 9);
        }

        internal static Model TestModel()
        {
            return new Model
            {
                Parameters =
                {
                    new Parameter { Id = "AngleX", Min = -30, Max = 30, Default = 0 },
                    new Parameter { Id = "AngleY", Min = -30, Max = 30, Default = 0 },
                    new Parameter { Id = "AngleZ", Min = -30, Max = 30, Default = 0 },
                    new Parameter { Id = "EyeOpenL", Min = 0, Max = 1, Default = 1 },
                    new Parameter { Id = "EyeOpenR", Min = 0, Max = 1, Default = 1 },
                    new Parameter { Id = "MouthOpenY", Min = 0, Max = 1, Default = 0 },
                    new Parameter { Id = "Breath", Min = 0, Max = 1, Default = 0 },
                },
            };
        }

        internal static TrackingAdapterOptions NoSmoothing()
        {
            return new TrackingAdapterOptions { DefaultSmoothing = 0, RecoverySeconds = 0 };
        }

        private static TrackingFrame Frame(double at, params (string Id, double Value)[] values)
        {
            var dictionary = new Dictionary<string, double> { ["FaceFound"] = 1 };
            foreach ((string id, double value) in values)
            {
                dictionary[id] = value;
            }

            return new TrackingFrame(at, dictionary);
        }

        private static double StepResponse(double fps, double seconds)
        {
            var adapter = new TrackingAdapter(TrackingProfile.NizimaLive, TestModel());
            adapter.Update(Frame(0, ("Yaw", 0)), 0);
            TrackingFrame step = Frame(1.0 / fps, ("Yaw", 10));
            for (int i = 1; i <= (int)Math.Round(seconds * fps); i++)
            {
                adapter.Update(step, i / fps);
            }

            return adapter.Get("AngleX").Value;
        }
    }
}
