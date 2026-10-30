using System;
using System.Globalization;
using System.IO;
using Ugokue.App.Rig;
using Ugokue.Core.Live;
using UnityEngine;

namespace Ugokue.App.Live
{
    /// <summary>
    /// 入力元のトラッキング値を入力アダプターで変換し、モデルのパラメータを毎フレーム動かす。校正のボタンと遅延の表示を持つ。
    /// </summary>
    public sealed class TrackingDriver : MonoBehaviour
    {
        public const string PlaybackArgument = "-ugokue-tracking-playback";

        private const int WindowId = 0x55474b35;

        [SerializeField] private ModelRenderer target;
        [SerializeField] private NizimaLiveReceiver receiver;
        [SerializeField] private KeyCode toggleKey = KeyCode.F4;
        [SerializeField] private KeyCode calibrateKey = KeyCode.C;
        [SerializeField] private KeyCode markerKey = KeyCode.F5;
        [SerializeField] private bool visible = true;
        [SerializeField] private float windowWidth = 300;

        private readonly LatencyMeter latency = new LatencyMeter();
        private RecordedTrackingSource playback;
        private double playbackStartedAt = double.NaN;
        private object adaptedRig;
        private TrackingFrame lastFrame;
        private bool showMarker;
        private Texture2D markerTexture;

        public TrackingAdapter Adapter { get; private set; }

        public TrackingState State => Adapter?.State ?? TrackingState.NoInput;

        public bool Calibrated => Adapter != null && Adapter.Neutral.Count > 0;

        /// <summary>
        /// 起動引数 <c>-ugokue-tracking-playback</c> の値（記録したファイルのパス）を返す。なければ null。
        /// </summary>
        public static string PlaybackPath(string[] args)
        {
            if (args == null)
            {
                return null;
            }

            int index = Array.IndexOf(args, PlaybackArgument);
            if (index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            {
                return null;
            }

            return Path.GetFullPath(args[index + 1]);
        }

        /// <summary>
        /// 入力アダプターの状態、校正の有無、遅延を1つの文字列にまとめる。
        /// </summary>
        public static string DescribeState(TrackingState state, bool calibrated, LatencyStats latency)
        {
            string text;
            switch (state)
            {
                case TrackingState.Live:
                    text = "顔に追従中";
                    break;
                case TrackingState.Holding:
                    text = "顔を見失ったため、直前の値を保持しています";
                    break;
                case TrackingState.Returning:
                    text = "顔を見失ったため、正面へ戻しています";
                    break;
                default:
                    text = "トラッキング値を待っています";
                    break;
            }

            text += calibrated ? "\n校正済み（C で校正し直す）" : "\nC で今の向きを正面にする";
            if (latency != null)
            {
                text += string.Format(CultureInfo.InvariantCulture, "\nnizima LIVE → Unity {0:0.0} ms（p95 {1:0.0} ms）", latency.AverageMs, latency.P95Ms);
            }

            return text;
        }

        /// <summary>
        /// 遅延の計測用の目印を点けるかどうか。口を半分より大きく開けたら点ける。
        /// </summary>
        public static bool MarkerLit(double? mouthOpen)
        {
            return mouthOpen.HasValue && mouthOpen.Value > 0.5;
        }

        /// <summary>
        /// nizima LIVE の代わりに、記録したデータを再生する。再生の時刻は、次に <see cref="Step"/> を呼んだときを 0 とする。
        /// </summary>
        public void UsePlayback(RecordedTrackingSource source)
        {
            playback = source;
            playbackStartedAt = double.NaN;
            Adapter = null;
        }

        /// <summary>
        /// 1フレーム分、入力アダプターを進めてモデルのパラメータに書き込む。now は <see cref="TrackingClock"/> と同じ時計の秒数。
        /// </summary>
        public void Step(double now)
        {
            if (target == null || target.Rig == null)
            {
                return;
            }

            if (Adapter == null || !ReferenceEquals(adaptedRig, target.Rig))
            {
                Adapter = new TrackingAdapter(TrackingProfile.NizimaLive, target.Rig.Model);
                adaptedRig = target.Rig;
            }

            TrackingFrame frame;
            double time = now;
            if (playback != null)
            {
                if (double.IsNaN(playbackStartedAt))
                {
                    playbackStartedAt = now;
                }

                time = now - playbackStartedAt;
                playback.Seek(time);
                frame = playback.LatestFrame;
            }
            else
            {
                frame = receiver != null ? receiver.LatestFrame : null;
                if (frame != null && !ReferenceEquals(frame, lastFrame) && frame.SourceTimestampUnixMs.HasValue)
                {
                    latency.Add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - frame.SourceTimestampUnixMs.Value);
                }
            }

            lastFrame = frame;
            Adapter.Update(frame, time);
            Adapter.ApplyTo(target.Rig.Parameters);
        }

        /// <summary>
        /// 今の顔の向きを正面にする。有効なトラッキング値がまだなければ false を返す。
        /// </summary>
        public bool Calibrate()
        {
            return Adapter != null && Adapter.Calibrate();
        }

        private void Awake()
        {
            if (target == null)
            {
                target = GetComponent<ModelRenderer>();
            }

            string path = PlaybackPath(Environment.GetCommandLineArgs());
            if (path != null)
            {
                UsePlayback(RecordedTrackingSource.Load(path));
                Debug.Log($"[TrackingDriver] Playing back {path}");
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                visible = !visible;
            }

            if (Input.GetKeyDown(markerKey))
            {
                showMarker = !showMarker;
            }

            if (Input.GetKeyDown(calibrateKey))
            {
                Calibrate();
            }

            Step(TrackingClock.Now);
        }

        private void OnDestroy()
        {
            if (markerTexture != null)
            {
                Destroy(markerTexture);
            }
        }

        private void OnGUI()
        {
            if (showMarker)
            {
                DrawMarker();
            }

            if (!visible)
            {
                return;
            }

            var rect = new Rect(12, 140, windowWidth, 0);
            GUILayout.Window(WindowId, rect, DrawWindow, "Tracking adapter (F4)");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label(DescribeState(State, Calibrated, latency.Snapshot()));
            GUI.enabled = State != TrackingState.NoInput;
            if (GUILayout.Button("校正（今の向きを正面にする）"))
            {
                Calibrate();
            }

            GUI.enabled = Calibrated;
            if (GUILayout.Button("校正を取り消す"))
            {
                Adapter.ResetCalibration();
            }

            GUI.enabled = true;
            GUILayout.Label(playback != null ? "記録したデータを再生中" : "F5: 遅延計測用の目印");
        }

        private void DrawMarker()
        {
            if (markerTexture == null)
            {
                markerTexture = new Texture2D(1, 1);
            }

            bool lit = MarkerLit(Adapter?.Get("MouthOpenY"));
            markerTexture.SetPixel(0, 0, lit ? Color.white : Color.black);
            markerTexture.Apply();
            float size = Mathf.Min(Screen.width, Screen.height) * 0.25f;
            GUI.DrawTexture(new Rect(Screen.width - size - 12, Screen.height - size - 12, size, size), markerTexture);
        }
    }
}
