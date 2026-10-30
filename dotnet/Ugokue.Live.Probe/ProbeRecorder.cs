using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ugokue.Core.Live;
using Ugokue.Core.Live.Nizima;

namespace Ugokue.Live.Probe
{
    /// <summary>
    /// nizima LIVE から届いたメッセージをそのまま記録し、受信レートやモデルの有無を集計する。
    /// </summary>
    public sealed class ProbeRecorder
    {
        private readonly object gate = new object();
        private readonly NizimaLiveClient client;
        private readonly string outputDirectory;
        private readonly StreamWriter messages;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DateTime startedAt = DateTime.UtcNow;
        private readonly ReceiveRateMeter rate = new ReceiveRateMeter(24 * 60 * 60);
        private readonly JArray stateChanges = new JArray();
        private readonly SortedSet<string> modelIds = new SortedSet<string>(StringComparer.Ordinal);
        private readonly SortedSet<string> observedIds = new SortedSet<string>(StringComparer.Ordinal);
        private int framesWithModel;
        private int framesWithoutModel;
        private int messageCount;

        /// <summary>
        /// クライアントのイベントを購読し、出力フォルダに messages.jsonl を書き始める。
        /// </summary>
        public ProbeRecorder(NizimaLiveClient client, string outputDirectory)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.outputDirectory = outputDirectory;
            Directory.CreateDirectory(outputDirectory);
            messages = new StreamWriter(Path.Combine(outputDirectory, "messages.jsonl"));
            client.MessageReceived += OnMessage;
            client.StateChanged += OnStateChanged;
        }

        public int FramesWithModel
        {
            get { lock (gate) { return framesWithModel; } }
        }

        public int FramesWithoutModel
        {
            get { lock (gate) { return framesWithoutModel; } }
        }

        public int MessageCount
        {
            get { lock (gate) { return messageCount; } }
        }

        /// <summary>
        /// 記録を終え、集計を summary.json に書き出して返す。
        /// </summary>
        public JObject Finish()
        {
            client.MessageReceived -= OnMessage;
            client.StateChanged -= OnStateChanged;
            JObject summary;
            lock (gate)
            {
                messages.Dispose();
                ReceiveRateStats stats = rate.Snapshot();
                summary = new JObject
                {
                    ["uri"] = client.Options.Uri.ToString(),
                    ["pluginName"] = client.Options.PluginName,
                    ["startedAt"] = startedAt.ToString("o"),
                    ["durationSeconds"] = Math.Round(clock.Elapsed.TotalSeconds, 3),
                    ["connectionCount"] = client.ConnectionCount,
                    ["lastError"] = client.LastError,
                    ["stateChanges"] = stateChanges,
                    ["frames"] = new JObject
                    {
                        ["withModel"] = framesWithModel,
                        ["withoutModel"] = framesWithoutModel,
                        ["modelIds"] = new JArray(modelIds),
                    },
                    ["rate"] = stats == null ? JValue.CreateNull() : new JObject
                    {
                        ["framesPerSecond"] = Math.Round(stats.FramesPerSecond, 2),
                        ["averageIntervalMs"] = Math.Round(stats.AverageIntervalMs, 2),
                        ["p95IntervalMs"] = Math.Round(stats.P95IntervalMs, 2),
                        ["maxIntervalMs"] = Math.Round(stats.MaxIntervalMs, 2),
                    },
                    ["observedLiveParameterIds"] = new JArray(observedIds),
                    ["liveParameters"] = new JArray(client.LiveParameters.Select(p => new JObject
                    {
                        ["Id"] = p.Id,
                        ["CreatedBy"] = p.CreatedBy,
                        ["Group"] = p.Group,
                        ["Min"] = p.Min,
                        ["Max"] = p.Max,
                        ["Base"] = p.Base,
                        ["Repeat"] = p.Repeat,
                    })),
                };
            }

            File.WriteAllText(Path.Combine(outputDirectory, "summary.json"), summary.ToString());
            return summary;
        }

        private void OnMessage(string json)
        {
            double at = clock.Elapsed.TotalSeconds;
            JToken message = TryParse(json);
            lock (gate)
            {
                messageCount++;
                var line = new JObject { ["atSeconds"] = Math.Round(at, 4), ["message"] = message };
                messages.WriteLine(line.ToString(Formatting.None));
                if (message is JObject obj && obj.Value<string>("Method") == NizimaProtocol.NotifyFrameUpdated && obj.Value<string>("Type") == "Event")
                {
                    CountFrame(obj["Data"] as JObject, at);
                }
            }
        }

        private void CountFrame(JObject data, double at)
        {
            Dictionary<string, double> values = data == null ? null : NizimaProtocol.ReadLiveParameterValues(data, client.Options.ModelId);
            if (values == null)
            {
                framesWithoutModel++;
                return;
            }

            framesWithModel++;
            rate.Add(at);
            observedIds.UnionWith(values.Keys);
            foreach (JToken model in (JArray)data["Models"])
            {
                string id = model.Value<string>("ModelId");
                if (id != null)
                {
                    modelIds.Add(id);
                }
            }
        }

        private void OnStateChanged(NizimaConnectionState state)
        {
            lock (gate)
            {
                stateChanges.Add(new JObject { ["atSeconds"] = Math.Round(clock.Elapsed.TotalSeconds, 4), ["state"] = state.ToString() });
            }
        }

        private static JToken TryParse(string json)
        {
            try
            {
                return JToken.Parse(json);
            }
            catch (JsonReaderException)
            {
                return json;
            }
        }
    }
}
