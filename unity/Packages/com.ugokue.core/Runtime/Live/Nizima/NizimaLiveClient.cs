using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ugokue.Core.Live.Nizima
{
    /// <summary>
    /// nizima LIVE の接続の状態。
    /// </summary>
    public enum NizimaConnectionState
    {
        Disconnected,
        Connecting,
        WaitingForRegistration,
        WaitingForEnable,
        Streaming,
    }

    /// <summary>
    /// nizima LIVE にプラグインとして接続し、描画の更新ごとの LiveParameter の値を受け取る。切断されたら再接続を続ける。
    /// </summary>
    public sealed class NizimaLiveClient : IDisposable
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        private readonly NizimaLiveClientOptions options;
        private readonly ITokenStore tokenStore;
        private readonly object gate = new object();
        private CancellationTokenSource cancellation;
        private Task loop;
        private int requestSequence;
        private TrackingFrame latestFrame;
        private IReadOnlyList<LiveParameterInfo> liveParameters = new LiveParameterInfo[0];
        private NizimaConnectionState state = NizimaConnectionState.Disconnected;
        private string lastError;
        private bool lastFrameHadModel;
        private int connectionCount;

        public NizimaLiveClient(NizimaLiveClientOptions options, ITokenStore tokenStore)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        }

        /// <summary>
        /// 状態が変わったときに、受信用のスレッドから呼ばれる。
        /// </summary>
        public event Action<NizimaConnectionState> StateChanged;

        /// <summary>
        /// フレームを受け取ったときに、受信用のスレッドから呼ばれる。
        /// </summary>
        public event Action<TrackingFrame> FrameReceived;

        /// <summary>
        /// nizima LIVE から受け取ったメッセージの JSON 文字列を、受信用のスレッドから渡す。記録に使う。
        /// </summary>
        public event Action<string> MessageReceived;

        public NizimaLiveClientOptions Options => options;

        public ReceiveRateMeter RateMeter { get; } = new ReceiveRateMeter();

        public NizimaConnectionState State
        {
            get { lock (gate) { return state; } }
        }

        /// <summary>
        /// 最後に受け取ったフレーム。まだなければ null。
        /// </summary>
        public TrackingFrame LatestFrame
        {
            get { lock (gate) { return latestFrame; } }
        }

        /// <summary>
        /// 最後に GetLiveParameters で取得した LiveParameter の一覧。
        /// </summary>
        public IReadOnlyList<LiveParameterInfo> LiveParameters
        {
            get { lock (gate) { return liveParameters; } }
        }

        /// <summary>
        /// 最後に受け取った NotifyFrameUpdated に、モデルの値が含まれていたかどうか。
        /// </summary>
        public bool LastFrameHadModel
        {
            get { lock (gate) { return lastFrameHadModel; } }
        }

        /// <summary>
        /// 最後に起きた接続や受信のエラーの説明。なければ null。
        /// </summary>
        public string LastError
        {
            get { lock (gate) { return lastError; } }
        }

        /// <summary>
        /// WebSocket の接続に成功した回数。
        /// </summary>
        public int ConnectionCount
        {
            get { lock (gate) { return connectionCount; } }
        }

        /// <summary>
        /// 接続と受信をバックグラウンドで始める。すでに始めていれば何もしない。
        /// </summary>
        public void Start()
        {
            lock (gate)
            {
                if (loop != null)
                {
                    return;
                }

                cancellation = new CancellationTokenSource();
                CancellationToken token = cancellation.Token;
                loop = Task.Run(() => RunAsync(token));
            }
        }

        /// <summary>
        /// 接続と受信を止め、バックグラウンドの処理が終わるのを待つ。
        /// </summary>
        public async Task StopAsync()
        {
            Task running;
            lock (gate)
            {
                running = loop;
                loop = null;
                cancellation?.Cancel();
            }

            if (running != null)
            {
                await running.ConfigureAwait(false);
            }

            SetState(NizimaConnectionState.Disconnected);
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            cancellation?.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            TimeSpan delay = options.MinReconnectDelay;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using (var socket = new ClientWebSocket())
                    {
                        SetState(NizimaConnectionState.Connecting);
                        await socket.ConnectAsync(options.Uri, cancellationToken).ConfigureAwait(false);
                        lock (gate)
                        {
                            connectionCount++;
                            lastError = null;
                        }

                        delay = options.MinReconnectDelay;
                        await RunSessionAsync(socket, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e) when (e is WebSocketException || e is IOException || e is InvalidOperationException || e is ObjectDisposedException)
                {
                    lock (gate)
                    {
                        lastError = e.Message;
                    }
                }

                SetState(NizimaConnectionState.Disconnected);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.MaxReconnectDelay.Ticks));
            }
        }

        private async Task RunSessionAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            string token = tokenStore.Load();
            if (token != null)
            {
                await SendAsync(socket, NizimaProtocol.EstablishConnection, EstablishData(token), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RegisterAsync(socket, cancellationToken).ConfigureAwait(false);
            }

            var buffer = new byte[64 * 1024];
            var text = new MemoryStream();
            while (socket.State == WebSocketState.Open)
            {
                text.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    text.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                string json = Encoding.UTF8.GetString(text.GetBuffer(), 0, (int)text.Length);
                MessageReceived?.Invoke(json);
                NizimaMessage message = NizimaProtocol.Parse(json);
                if (message != null)
                {
                    await HandleAsync(socket, message, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task HandleAsync(ClientWebSocket socket, NizimaMessage message, CancellationToken cancellationToken)
        {
            switch (message.Method)
            {
                case NizimaProtocol.RegisterPlugin when message.IsResponse:
                    string token = message.Data.Value<string>("Token");
                    if (!string.IsNullOrEmpty(token))
                    {
                        tokenStore.Save(token);
                    }

                    SetState(NizimaConnectionState.WaitingForEnable);
                    break;

                case NizimaProtocol.EstablishConnection when message.IsResponse:
                    if (message.Data.Value<bool?>("Enabled") == true)
                    {
                        await OnEnabledAsync(socket, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        SetState(NizimaConnectionState.WaitingForEnable);
                    }

                    break;

                case NizimaProtocol.EstablishConnection when message.IsError && message.ErrorType == NizimaProtocol.InvalidToken:
                    tokenStore.Clear();
                    await RegisterAsync(socket, cancellationToken).ConfigureAwait(false);
                    break;

                case NizimaProtocol.NotifyEnabledChanged when message.IsEvent:
                    if (message.Data.Value<bool?>("Enabled") == true)
                    {
                        await OnEnabledAsync(socket, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        SetState(NizimaConnectionState.WaitingForEnable);
                    }

                    break;

                case NizimaProtocol.GetLiveParameters when message.IsResponse:
                    List<LiveParameterInfo> parameters = NizimaProtocol.ReadLiveParameters(message.Data);
                    lock (gate)
                    {
                        liveParameters = parameters;
                    }

                    break;

                case NizimaProtocol.NotifyFrameUpdated when message.IsEvent:
                    OnFrame(message.Data);
                    break;

                default:
                    if (message.IsError)
                    {
                        lock (gate)
                        {
                            lastError = $"{message.Method}: {message.ErrorType}";
                        }
                    }

                    break;
            }
        }

        private void OnFrame(JObject data)
        {
            Dictionary<string, double> values = NizimaProtocol.ReadLiveParameterValues(data, options.ModelId);
            double now = Clock.Elapsed.TotalSeconds;
            TrackingFrame frame = values == null ? null : new TrackingFrame(now, values);
            lock (gate)
            {
                lastFrameHadModel = values != null;
                if (frame != null)
                {
                    latestFrame = frame;
                }
            }

            if (frame != null)
            {
                RateMeter.Add(now);
                FrameReceived?.Invoke(frame);
            }
        }

        private async Task OnEnabledAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            await SendAsync(socket, NizimaProtocol.NotifyFrameUpdated, new JObject { ["Enabled"] = true }, cancellationToken).ConfigureAwait(false);
            await SendAsync(socket, NizimaProtocol.GetLiveParameters, new JObject(), cancellationToken).ConfigureAwait(false);
            SetState(NizimaConnectionState.Streaming);
        }

        private async Task RegisterAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var data = new JObject { ["Name"] = options.PluginName };
            if (options.Developer != null)
            {
                data["Developer"] = options.Developer;
            }

            if (options.PluginVersion != null)
            {
                data["Version"] = options.PluginVersion;
            }

            SetState(NizimaConnectionState.WaitingForRegistration);
            await SendAsync(socket, NizimaProtocol.RegisterPlugin, data, cancellationToken).ConfigureAwait(false);
        }

        private JObject EstablishData(string token)
        {
            var data = new JObject { ["Name"] = options.PluginName, ["Token"] = token };
            if (options.PluginVersion != null)
            {
                data["Version"] = options.PluginVersion;
            }

            return data;
        }

        private Task SendAsync(ClientWebSocket socket, string method, JObject data, CancellationToken cancellationToken)
        {
            string id = NizimaProtocol.RequestId(Interlocked.Increment(ref requestSequence));
            byte[] bytes = Encoding.UTF8.GetBytes(NizimaProtocol.Request(method, id, data));
            return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }

        private void SetState(NizimaConnectionState next)
        {
            bool changed;
            lock (gate)
            {
                changed = state != next;
                state = next;
            }

            if (changed)
            {
                StateChanged?.Invoke(next);
            }
        }
    }

    /// <summary>
    /// nizima LIVE への接続の設定。
    /// </summary>
    public sealed class NizimaLiveClientOptions
    {
        public const int DefaultPort = 22022;

        /// <summary>
        /// この PC の nizima LIVE の接続先を返す。nizima LIVE は IPv4 でだけ待ち受けるため、localhost ではなく 127.0.0.1 を使う。
        /// </summary>
        public static Uri LocalUri(int port)
        {
            return new Uri("ws://127.0.0.1:" + port + "/");
        }

        /// <summary>
        /// 接続先。既定は ws://127.0.0.1:22022/。
        /// </summary>
        public Uri Uri { get; set; } = LocalUri(DefaultPort);

        /// <summary>
        /// プラグインマネージャーに表示される名前。トークンはこの名前に結び付く。
        /// </summary>
        public string PluginName { get; set; } = "ugokue";

        public string Developer { get; set; }

        public string PluginVersion { get; set; }

        /// <summary>
        /// 値を読むモデルの ModelId。null なら先頭のモデルを使う。
        /// </summary>
        public string ModelId { get; set; }

        public TimeSpan MinReconnectDelay { get; set; } = TimeSpan.FromSeconds(0.5);

        public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
    }
}
