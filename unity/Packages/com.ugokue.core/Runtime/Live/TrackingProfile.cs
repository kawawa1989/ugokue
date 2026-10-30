using System;
using System.Collections.Generic;

namespace Ugokue.Core.Live
{
    /// <summary>
    /// 入力元ごとの変換表。顔を捉えているかを表す値の名前と、パラメータの変換規則を持つ。
    /// </summary>
    public sealed class TrackingProfile
    {
        public TrackingProfile(string presenceSource, IReadOnlyList<TrackingMapping> mappings)
        {
            PresenceSource = presenceSource;
            Mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
        }

        /// <summary>
        /// nizima LIVE の LiveParameter を、固定テストモデルと同じ名前のパラメータに変換する表。
        /// </summary>
        public static TrackingProfile NizimaLive { get; } = new TrackingProfile(
            "FaceFound",
            new[]
            {
                new TrackingMapping("Yaw", "AngleX", -30, 30, -30, 30, calibrate: true),
                new TrackingMapping("Pitch", "AngleY", -30, 30, -30, 30, calibrate: true),
                new TrackingMapping("Roll", "AngleZ", -30, 30, -30, 30, calibrate: true),
                new TrackingMapping("EyeOpenLeft", "EyeOpenL", 0, 1, 0, 1),
                new TrackingMapping("EyeOpenRight", "EyeOpenR", 0, 1, 0, 1),
                new TrackingMapping("MouthOpen", "MouthOpenY", 0, 1, 0, 1),
            });

        /// <summary>
        /// 顔を捉えているかを表す入力元の値の名前。null なら、届いたフレームをすべて有効とする。
        /// </summary>
        public string PresenceSource { get; }

        public IReadOnlyList<TrackingMapping> Mappings { get; }
    }
}
