using System;
using Ugokue.Core.Format;

namespace Ugokue.Format.Cli
{
    /// <summary>
    /// 中間形式のモデルを C# の実装で読み書きするコマンドラインツール。
    /// </summary>
    public static class Program
    {
        private const string Usage = "usage: ugokue-format-cs roundtrip <in_dir> <out_dir>";

        /// <summary>
        /// コマンドを実行し、成功なら 0、失敗なら 0 以外を返す。
        /// </summary>
        public static int Main(string[] args)
        {
            if (args.Length != 3 || args[0] != "roundtrip")
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }

            try
            {
                Roundtrip(args[1], args[2]);
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("error: " + e.Message);
                return 1;
            }
        }

        /// <summary>
        /// モデルのフォルダを読み込み、別のフォルダに書き出す。
        /// </summary>
        public static void Roundtrip(string inputDirectory, string outputDirectory)
        {
            ModelPackage.Load(inputDirectory).Save(outputDirectory);
        }
    }
}
