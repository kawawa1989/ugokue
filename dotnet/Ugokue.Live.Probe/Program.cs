using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Ugokue.Core.Live;
using Ugokue.Core.Live.Nizima;

namespace Ugokue.Live.Probe
{
    /// <summary>
    /// nizima LIVE の Plugin API に接続し、届いた値と受信レートを記録する確認用ツール。
    /// </summary>
    public static class Program
    {
        private const string Usage =
            "usage: ugokue-nizima-probe [--seconds N] [--out DIR] [--uri ws://127.0.0.1:22022/] [--model ID] [--token-file PATH]";

        /// <summary>
        /// 引数を読み、指定秒数（Ctrl+C でも止まる）記録して、成功なら 0 を返す。
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            var options = new NizimaLiveClientOptions { PluginName = "ugokue probe", Developer = "ugokue", PluginVersion = "0.1.0" };
            double seconds = 60;
            string output = Path.Combine("artifacts", "nizima-probe", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            string tokenFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ugokue", "nizima-probe-token");
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string value = i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("missing value for " + args[i]);
                    switch (args[i])
                    {
                        case "--seconds": seconds = double.Parse(value, CultureInfo.InvariantCulture); break;
                        case "--out": output = value; break;
                        case "--uri": options.Uri = new Uri(value); break;
                        case "--model": options.ModelId = value; break;
                        case "--token-file": tokenFile = value; break;
                        default: throw new ArgumentException("unknown option " + args[i]);
                    }

                    i++;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is UriFormatException)
            {
                Console.Error.WriteLine("error: " + e.Message);
                Console.Error.WriteLine(Usage);
                return 2;
            }

            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stop.Cancel();
            };

            using var client = new NizimaLiveClient(options, new FileTokenStore(tokenFile));
            var recorder = new ProbeRecorder(client, output);
            NizimaConnectionState? shown = null;
            client.StateChanged += state =>
            {
                if (state != NizimaConnectionState.Connecting && state != shown)
                {
                    shown = state;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {state}{Hint(state)}");
                }
            };
            Console.WriteLine($"connecting to {options.Uri} as \"{options.PluginName}\" for {seconds}s (Ctrl+C to stop)");
            client.Start();

            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, stop.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (client.State == NizimaConnectionState.Streaming)
                {
                    ReceiveRateStats stats = client.RateMeter.Snapshot();
                    string rate = stats == null ? "no frames" : $"{stats.FramesPerSecond:F1} fps, p95 {stats.P95IntervalMs:F1} ms";
                    string model = recorder.FramesWithoutModel > 0 && !client.LastFrameHadModel ? " (frames have no model)" : string.Empty;
                    Console.WriteLine($"  {rate}, frames {recorder.FramesWithModel}+{recorder.FramesWithoutModel}{model}");
                }
            }

            await client.StopAsync();
            JObject summary = recorder.Finish();
            Console.WriteLine($"wrote {Path.GetFullPath(output)}");
            Console.WriteLine(summary["frames"].ToString());
            Console.WriteLine(summary["rate"].ToString());
            return 0;
        }

        private static string Hint(NizimaConnectionState state)
        {
            switch (state)
            {
                case NizimaConnectionState.WaitingForRegistration:
                    return " - nizima LIVE 側でプラグインの登録を許可してください";
                case NizimaConnectionState.WaitingForEnable:
                    return " - nizima LIVE のプラグイン一覧で \"ugokue probe\" を有効にしてください";
                case NizimaConnectionState.Disconnected:
                    return " - nizima LIVE が起動していて、プラグイン API が有効か確認してください";
                default:
                    return string.Empty;
            }
        }
    }
}
