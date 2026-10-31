using System;
using System.Collections.Generic;
using Ugokue.Core.Format;
using Ugokue.Core.Rig;

namespace Ugokue.Core.Live
{
    /// <summary>
    /// 入力アダプターの状態。
    /// </summary>
    public enum TrackingState
    {
        /// <summary>まだ有効なフレームを受け取っていない。パラメータには書き込まない。</summary>
        NoInput,

        /// <summary>有効なフレームが届いていて、その値に追従している。</summary>
        Live,

        /// <summary>入力が途切れたので、最後の有効な値を保持している。</summary>
        Holding,

        /// <summary>途切れが長く続いたので、パラメータの初期値へゆっくり戻している。</summary>
        Returning,
    }

    /// <summary>
    /// 入力元のトラッキング値を、校正・範囲の置き換え・上限・平滑化・途切れの処理を通して、モデルのパラメータの値にする。
    /// </summary>
    public sealed class TrackingAdapter
    {
        private readonly TrackingProfile profile;
        private readonly TrackingAdapterOptions options;
        private readonly List<Channel> channels = new List<Channel>();
        private readonly Dictionary<string, Channel> channelsByTarget = new Dictionary<string, Channel>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> neutral = new Dictionary<string, double>(StringComparer.Ordinal);
        private IReadOnlyDictionary<string, double> lastValidValues;
        private TrackingFrame lastFrame;
        private double lastValidAt;
        private double? lastUpdateAt;
        private double recoveryStartedAt = double.NegativeInfinity;

        /// <summary>
        /// 変換表と、モデルのパラメータの定義から作る。モデルにないパラメータへの変換規則は使わない。
        /// </summary>
        public TrackingAdapter(TrackingProfile profile, Model model, TrackingAdapterOptions options = null)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.options = options ?? new TrackingAdapterOptions();
            var parameters = new Dictionary<string, Parameter>(StringComparer.Ordinal);
            foreach (Parameter parameter in model.Parameters)
            {
                parameters[parameter.Id] = parameter;
            }

            foreach (TrackingMapping mapping in profile.Mappings)
            {
                if (parameters.TryGetValue(mapping.Target, out Parameter parameter) && !channelsByTarget.ContainsKey(mapping.Target))
                {
                    var channel = new Channel(mapping, parameter, SmoothingFor(model, mapping.Target, this.options.DefaultSmoothing));
                    channels.Add(channel);
                    channelsByTarget[mapping.Target] = channel;
                }
            }
        }

        public TrackingState State { get; private set; } = TrackingState.NoInput;

        /// <summary>
        /// 校正で正面とした、入力元の値。校正していなければ空。
        /// </summary>
        public IReadOnlyDictionary<string, double> Neutral => neutral;

        /// <summary>
        /// 最新のフレームと今の時刻（<see cref="TrackingClock"/> と同じ時計の秒数）から、出力の値を1フレーム分進める。
        /// </summary>
        public void Update(TrackingFrame latest, double now)
        {
            double dt = lastUpdateAt.HasValue ? Math.Max(0, now - lastUpdateAt.Value) : 0;
            lastUpdateAt = now;

            if (latest != null && !ReferenceEquals(latest, lastFrame))
            {
                lastFrame = latest;
                if (IsValid(latest))
                {
                    lastValidValues = latest.Values;
                    lastValidAt = latest.ReceivedAtSeconds;
                    UpdateTargets();
                    if (State == TrackingState.NoInput)
                    {
                        foreach (Channel channel in channels)
                        {
                            channel.Output = channel.Target;
                        }

                        State = TrackingState.Live;
                    }
                }
            }

            if (State == TrackingState.NoInput)
            {
                return;
            }

            double since = now - lastValidAt;
            TrackingState next = since <= options.StaleSeconds ? TrackingState.Live
                : since <= options.StaleSeconds + options.HoldSeconds ? TrackingState.Holding
                : TrackingState.Returning;
            if (next == TrackingState.Live && State != TrackingState.Live)
            {
                recoveryStartedAt = now;
            }

            State = next;
            bool recovering = State == TrackingState.Live && now - recoveryStartedAt < options.RecoverySeconds;
            foreach (Channel channel in channels)
            {
                double goal;
                double alpha;
                if (State == TrackingState.Returning)
                {
                    goal = channel.Parameter.Default;
                    alpha = ExponentialAlpha(dt, options.ReturnTimeConstant);
                }
                else
                {
                    goal = channel.Target;
                    alpha = 1 - Math.Pow(channel.Smoothing, 60 * dt);
                    if (recovering)
                    {
                        alpha = Math.Min(alpha, ExponentialAlpha(dt, options.RecoveryTimeConstant));
                    }
                }

                channel.Output += (goal - channel.Output) * alpha;
            }
        }

        /// <summary>
        /// 出力の値を返す。まだ有効なフレームを受け取っていないか、モデルにないパラメータなら null。
        /// </summary>
        public double? Get(string parameter)
        {
            if (State == TrackingState.NoInput || !channelsByTarget.TryGetValue(parameter, out Channel channel))
            {
                return null;
            }

            return channel.Output;
        }

        /// <summary>
        /// 出力の値をモデルのパラメータに書き込む。有効なフレームを受け取るまでは何もしない。
        /// </summary>
        public void ApplyTo(ParameterValues values)
        {
            if (State == TrackingState.NoInput)
            {
                return;
            }

            foreach (Channel channel in channels)
            {
                values.Set(channel.Mapping.Target, channel.Output);
            }
        }

        /// <summary>
        /// 最後の有効なフレームの値を正面として覚える。有効なフレームがまだなければ false を返す。
        /// </summary>
        public bool Calibrate()
        {
            if (lastValidValues == null)
            {
                return false;
            }

            foreach (Channel channel in channels)
            {
                if (channel.Mapping.Calibrate && lastValidValues.TryGetValue(channel.Mapping.Source, out double value))
                {
                    neutral[channel.Mapping.Source] = value;
                }
            }

            UpdateTargets();
            return true;
        }

        /// <summary>
        /// 校正を取り消す。
        /// </summary>
        public void ResetCalibration()
        {
            neutral.Clear();
            if (lastValidValues != null)
            {
                UpdateTargets();
            }
        }

        private static double SmoothingFor(Model model, string parameter, double fallback)
        {
            if (model.Tracking != null)
            {
                foreach (TrackingParameter setting in model.Tracking.Parameters)
                {
                    if (setting.Parameter == parameter)
                    {
                        return Math.Min(Math.Max(setting.Smoothing, 0), 0.999);
                    }
                }
            }

            return Math.Min(Math.Max(fallback, 0), 0.999);
        }

        private static double ExponentialAlpha(double dt, double timeConstant)
        {
            return timeConstant <= 0 ? 1 : 1 - Math.Exp(-dt / timeConstant);
        }

        private bool IsValid(TrackingFrame frame)
        {
            return profile.PresenceSource == null
                || !frame.Values.TryGetValue(profile.PresenceSource, out double presence)
                || presence >= options.PresenceThreshold;
        }

        private void UpdateTargets()
        {
            foreach (Channel channel in channels)
            {
                TrackingMapping mapping = channel.Mapping;
                if (!lastValidValues.TryGetValue(mapping.Source, out double value) || double.IsNaN(value))
                {
                    continue;
                }

                if (mapping.Calibrate && neutral.TryGetValue(mapping.Source, out double offset))
                {
                    value -= offset;
                }

                channel.Target = Math.Min(Math.Max(mapping.Map(value), channel.Parameter.Min), channel.Parameter.Max);
            }
        }

        private sealed class Channel
        {
            public Channel(TrackingMapping mapping, Parameter parameter, double smoothing)
            {
                Mapping = mapping;
                Parameter = parameter;
                Smoothing = smoothing;
                Target = parameter.Default;
                Output = parameter.Default;
            }

            public TrackingMapping Mapping { get; }

            public Parameter Parameter { get; }

            public double Smoothing { get; }

            public double Target { get; set; }

            public double Output { get; set; }
        }
    }
}
