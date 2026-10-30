using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ugokue.Core.Tests.Live
{
    /// <summary>
    /// テスト用に nizima LIVE の Plugin API の応答をまねる WebSocket サーバー。
    /// </summary>
    internal sealed class FakeNizimaLiveServer : IDisposable
    {
        private readonly ConcurrentQueue<JObject> requests = new ConcurrentQueue<JObject>();
        private readonly object gate = new object();
        private TcpListener listener;
        private CancellationTokenSource cancellation;
        private WebSocket current;
        private int connections;

        public FakeNizimaLiveServer(int port = 0)
        {
            Port = port;
        }

        public int Port { get; private set; }

        public Uri Uri => new Uri($"ws://127.0.0.1:{Port}/");

        /// <summary>
        /// EstablishConnection を受け付けるトークン。RegisterPlugin の応答でもこの値を返す。
        /// </summary>
        public string ValidToken { get; set; } = "issued-token";

        /// <summary>
        /// EstablishConnection に成功したとき、Enabled を true で返すかどうか。
        /// </summary>
        public bool EnabledOnEstablish { get; set; } = true;

        public JArray LiveParameters { get; set; } = new JArray();

        public int Connections => Volatile.Read(ref connections);

        public IReadOnlyCollection<JObject> Requests => requests.ToArray();

        /// <summary>
        /// 待ち受けを始める。ポートが 0 なら空いているポートを使い、2回目以降は同じポートを使う。
        /// </summary>
        public void Start()
        {
            listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            Task.Run(() => AcceptLoopAsync(token));
        }

        /// <summary>
        /// 待ち受けと今の接続を止める（nizima LIVE の終了をまねる）。
        /// </summary>
        public void Stop()
        {
            cancellation?.Cancel();
            listener?.Stop();
            lock (gate)
            {
                current?.Abort();
                current = null;
            }
        }

        public bool HasRequest(string method)
        {
            foreach (JObject request in requests)
            {
                if (request.Value<string>("Method") == method)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 今の接続に Event を送る。
        /// </summary>
        public Task SendEventAsync(string method, JObject data)
        {
            return SendAsync(new JObject
            {
                ["nLPlugin"] = "1.0.0",
                ["Type"] = "Event",
                ["Method"] = method,
                ["Data"] = data,
            });
        }

        /// <summary>
        /// 今の接続に、JSON 文字列をそのまま送る。
        /// </summary>
        public Task SendRawAsync(string json)
        {
            WebSocket socket;
            lock (gate)
            {
                socket = current;
            }

            if (socket == null)
            {
                throw new InvalidOperationException("No client is connected.");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public void Dispose()
        {
            Stop();
        }

        private Task SendAsync(JObject message)
        {
            return SendRawAsync(message.ToString());
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }

                _ = Task.Run(() => ServeAsync(client, cancellationToken));
            }
        }

        private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                if (!await HandshakeAsync(stream))
                {
                    return;
                }

                using (WebSocket socket = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30)))
                {
                    lock (gate)
                    {
                        current = socket;
                    }

                    Interlocked.Increment(ref connections);
                    var buffer = new byte[64 * 1024];
                    try
                    {
                        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                        {
                            var text = new MemoryStream();
                            WebSocketReceiveResult result;
                            do
                            {
                                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                                if (result.MessageType == WebSocketMessageType.Close)
                                {
                                    return;
                                }

                                text.Write(buffer, 0, result.Count);
                            }
                            while (!result.EndOfMessage);

                            JObject request = JObject.Parse(Encoding.UTF8.GetString(text.ToArray()));
                            requests.Enqueue(request);
                            await RespondAsync(request);
                        }
                    }
                    catch (Exception) when (cancellationToken.IsCancellationRequested)
                    {
                    }
                    catch (WebSocketException)
                    {
                    }
                }
            }
        }

        private async Task RespondAsync(JObject request)
        {
            string method = request.Value<string>("Method");
            JObject data = request["Data"] as JObject ?? new JObject();
            var response = new JObject
            {
                ["nLPlugin"] = "1.0.0",
                ["Id"] = request["Id"],
                ["Type"] = "Response",
                ["Method"] = method,
                ["Data"] = new JObject(),
            };

            switch (method)
            {
                case "RegisterPlugin":
                    response["Data"] = new JObject { ["Token"] = ValidToken };
                    break;
                case "EstablishConnection":
                    if (data.Value<string>("Token") == ValidToken)
                    {
                        response["Data"] = new JObject { ["Enabled"] = EnabledOnEstablish };
                    }
                    else
                    {
                        response["Type"] = "Error";
                        response["Data"] = new JObject { ["ErrorType"] = "InvalidToken" };
                    }

                    break;
                case "GetLiveParameters":
                    response["Data"] = new JObject { ["LiveParameters"] = LiveParameters };
                    break;
            }

            await SendAsync(response);
        }

        private static async Task<bool> HandshakeAsync(NetworkStream stream)
        {
            var header = new StringBuilder();
            var one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(one, 0, 1) == 0)
                {
                    return false;
                }

                header.Append((char)one[0]);
            }

            string key = null;
            foreach (string line in header.ToString().Split("\r\n"))
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    key = line.Substring("Sec-WebSocket-Key:".Length).Trim();
                }
            }

            if (key == null)
            {
                return false;
            }

            string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            byte[] reply = Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
            await stream.WriteAsync(reply, 0, reply.Length);
            return true;
        }
    }
}
