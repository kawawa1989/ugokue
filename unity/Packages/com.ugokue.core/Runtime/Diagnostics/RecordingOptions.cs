using System;
using System.Globalization;

namespace Ugokue.Core.Diagnostics
{
    /// <summary>
    /// コマンドライン引数から、起動時の自動計測の設定を読み取る。
    /// </summary>
    public sealed class RecordingOptions
    {
        public const string RecordSecondsArgument = "-ugokue-record-seconds";
        public const string OutputPathArgument = "-ugokue-record-out";
        public const string QuitAfterRecordArgument = "-ugokue-quit-after-record";
        public const string WarmupSecondsArgument = "-ugokue-record-warmup";
        public const double DefaultWarmupSeconds = 2;

        private RecordingOptions(double? recordSeconds, double warmupSeconds, string outputPath, bool quitAfterRecord)
        {
            RecordSeconds = recordSeconds;
            WarmupSeconds = warmupSeconds;
            OutputPath = outputPath;
            QuitAfterRecord = quitAfterRecord;
        }

        public double? RecordSeconds { get; }

        public double WarmupSeconds { get; }

        public string OutputPath { get; }

        public bool QuitAfterRecord { get; }

        public bool AutoRecord => RecordSeconds.HasValue;

        /// <summary>
        /// コマンドライン引数を解析して設定を返す。
        /// </summary>
        public static RecordingOptions Parse(string[] args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            double? recordSeconds = null;
            double warmupSeconds = DefaultWarmupSeconds;
            string outputPath = null;
            bool quitAfterRecord = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case RecordSecondsArgument:
                        recordSeconds = ParseSeconds(args, i++, allowZero: false);
                        break;
                    case WarmupSecondsArgument:
                        warmupSeconds = ParseSeconds(args, i++, allowZero: true);
                        break;
                    case OutputPathArgument:
                        outputPath = ValueAt(args, i++);
                        break;
                    case QuitAfterRecordArgument:
                        quitAfterRecord = true;
                        break;
                }
            }

            return new RecordingOptions(recordSeconds, warmupSeconds, outputPath, quitAfterRecord);
        }

        private static double ParseSeconds(string[] args, int optionIndex, bool allowZero)
        {
            string value = ValueAt(args, optionIndex);
            bool parsed = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds);
            if (!parsed || double.IsNaN(seconds) || seconds < 0 || (!allowZero && seconds == 0))
            {
                string requirement = allowZero ? "a non-negative number" : "a positive number";
                throw new ArgumentException($"{args[optionIndex]} requires {requirement}, got '{value}'.");
            }

            return seconds;
        }

        private static string ValueAt(string[] args, int optionIndex)
        {
            if (optionIndex + 1 >= args.Length)
            {
                throw new ArgumentException($"{args[optionIndex]} requires a value.");
            }

            return args[optionIndex + 1];
        }
    }
}
