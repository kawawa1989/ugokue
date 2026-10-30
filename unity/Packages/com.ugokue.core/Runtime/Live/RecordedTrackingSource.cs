using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ugokue.Core.Live.Nizima;

namespace Ugokue.Core.Live
{
    /// <summary>
    /// ugokue-nizima-probe で記録した messages.jsonl を、時刻を指定して再生する入力元。
    /// </summary>
    public sealed class RecordedTrackingSource : ITrackingSource
    {
        private readonly List<TrackingFrame> frames;
        private int position = -1;

        public RecordedTrackingSource(IEnumerable<TrackingFrame> frames)
        {
            this.frames = new List<TrackingFrame>(frames ?? throw new ArgumentNullException(nameof(frames)));
            this.frames.Sort((a, b) => a.ReceivedAtSeconds.CompareTo(b.ReceivedAtSeconds));
        }

        /// <summary>
        /// 記録したフレーム。受け取った時刻は、記録を始めてからの秒数。
        /// </summary>
        public IReadOnlyList<TrackingFrame> Frames => frames;

        public TrackingFrame LatestFrame => position < 0 ? null : frames[position];

        /// <summary>
        /// 記録のファイルを読む。値の通知（モデルを含む NotifyFrameUpdated の Event）以外の行は読み飛ばす。
        /// </summary>
        public static RecordedTrackingSource Load(string path)
        {
            var frames = new List<TrackingFrame>();
            foreach (string line in File.ReadLines(path))
            {
                TrackingFrame frame = ReadLine(line);
                if (frame != null)
                {
                    frames.Add(frame);
                }
            }

            return new RecordedTrackingSource(frames);
        }

        /// <summary>
        /// 再生の位置を、指定した秒数の時点に移す。その時点までに受け取った最後のフレームが LatestFrame になる。
        /// </summary>
        public void Seek(double seconds)
        {
            if (position >= 0 && frames[position].ReceivedAtSeconds > seconds)
            {
                position = -1;
            }

            while (position + 1 < frames.Count && frames[position + 1].ReceivedAtSeconds <= seconds)
            {
                position++;
            }
        }

        private static TrackingFrame ReadLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            JObject record;
            try
            {
                record = JObject.Parse(line);
            }
            catch (JsonException)
            {
                return null;
            }

            if (!(record["message"] is JObject message) || !(message["Data"] is JObject data)
                || message.Value<string>("Type") != "Event" || message.Value<string>("Method") != NizimaProtocol.NotifyFrameUpdated)
            {
                return null;
            }

            Dictionary<string, double> values = NizimaProtocol.ReadLiveParameterValues(data);
            double? at = record.Value<double?>("atSeconds");
            return values == null || at == null ? null : new TrackingFrame(at.Value, values, message.Value<long?>("Timestamp"));
        }
    }
}
