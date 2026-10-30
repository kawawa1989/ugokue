using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Ugokue.Core.Live;
using Ugokue.Core.Live.Nizima;
using UnityEngine;

namespace Ugokue.App.Live
{
    /// <summary>
    /// nizima LIVE にプラグインとして接続してトラッキング値を受け取り、接続状態と値を画面に表示する。
    /// </summary>
    public sealed class NizimaLiveReceiver : MonoBehaviour
    {
        private const int WindowId = 0x55474b34;

        [SerializeField] private int port = NizimaLiveClientOptions.DefaultPort;
        [SerializeField] private KeyCode toggleKey = KeyCode.F4;
        [SerializeField] private bool visible = true;
        [SerializeField] private float windowWidth = 300;

        private NizimaLiveClient client;
        private Vector2 scroll;

        /// <summary>
        /// 登録で受け取ったトークンを保存するファイル。次回からは許可の操作なしで接続できる。
        /// </summary>
        public static string TokenPath => Path.Combine(Application.persistentDataPath, "nizima-live-token");

        public NizimaConnectionState State => client?.State ?? NizimaConnectionState.Disconnected;

        /// <summary>
        /// 最後に受け取ったフレーム。まだ受け取っていなければ null。
        /// </summary>
        public TrackingFrame LatestFrame => client?.LatestFrame;

        /// <summary>
        /// 接続状態と受信レートを1つの文字列にまとめる。値が届くまでは、次にする操作の案内を付ける。
        /// </summary>
        public static string DescribeStatus(NizimaConnectionState state, ReceiveRateStats rate, bool lastFrameHadModel, string lastError)
        {
            string text = "nizima LIVE: " + state;
            switch (state)
            {
                case NizimaConnectionState.Disconnected:
                case NizimaConnectionState.Connecting:
                    text += "\nnizima LIVE を起動し、プラグイン API を有効にしてください";
                    if (!string.IsNullOrEmpty(lastError))
                    {
                        text += "\n" + lastError;
                    }

                    break;
                case NizimaConnectionState.WaitingForRegistration:
                    text += "\nnizima LIVE 側でプラグインの登録を許可してください";
                    break;
                case NizimaConnectionState.WaitingForEnable:
                    text += "\nnizima LIVE のプラグイン一覧で ugokue を有効にしてください";
                    break;
                case NizimaConnectionState.Streaming:
                    text += rate == null
                        ? "\nwaiting for frames"
                        : string.Format(CultureInfo.InvariantCulture, "\n{0:0.0} fps, p95 {1:0.0} ms, max {2:0.0} ms", rate.FramesPerSecond, rate.P95IntervalMs, rate.MaxIntervalMs);
                    if (!lastFrameHadModel)
                    {
                        text += "\nnizima LIVE でモデルが読み込まれていないため、値が届いていません";
                    }

                    break;
            }

            return text;
        }

        /// <summary>
        /// フレームの値を、パラメータ名の順に「名前  値」の行にする。
        /// </summary>
        public static IEnumerable<string> DescribeValues(TrackingFrame frame)
        {
            if (frame == null)
            {
                return Enumerable.Empty<string>();
            }

            return frame.Values
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "  " + pair.Value.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private void OnEnable()
        {
            var options = new NizimaLiveClientOptions
            {
                Uri = new Uri("ws://localhost:" + port + "/"),
                PluginName = "ugokue",
                Developer = "ugokue",
                PluginVersion = Application.version,
            };
            client = new NizimaLiveClient(options, new FileTokenStore(TokenPath));
            client.Start();
        }

        private void OnDisable()
        {
            client?.Dispose();
            client = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                visible = !visible;
            }
        }

        private void OnGUI()
        {
            if (!visible || client == null)
            {
                return;
            }

            var rect = new Rect(12, Screen.height * 0.4f, windowWidth, 0);
            GUILayout.Window(WindowId, rect, DrawWindow, "Tracking (F4)");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label(DescribeStatus(client.State, client.RateMeter.Snapshot(), client.LastFrameHadModel, client.LastError));
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Screen.height * 0.4f));
            foreach (string line in DescribeValues(client.LatestFrame))
            {
                GUILayout.Label(line);
            }

            GUILayout.EndScrollView();
        }
    }
}
