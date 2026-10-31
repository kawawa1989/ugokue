using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Ugokue.Core.Format;
using Ugokue.Core.Imaging;
using Ugokue.Core.Rig;

namespace Ugokue.Format.Cli
{
    /// <summary>
    /// 中間形式のモデルを C# の実装で読み書きするコマンドラインツール。
    /// </summary>
    public static class Program
    {
        private const string Usage = "usage: ugokue-format-cs roundtrip <in_dir> <out_dir>\n       ugokue-format-cs render <model_dir> <out.png> [<parameter>=<value> ...]";

        private static readonly byte[] Background = { 255, 255, 255, 255 };

        /// <summary>
        /// コマンドを実行し、成功なら 0、失敗なら 0 以外を返す。
        /// </summary>
        public static int Main(string[] args)
        {
            bool roundtrip = args.Length == 3 && args[0] == "roundtrip";
            bool render = args.Length >= 3 && args[0] == "render";
            if (!roundtrip && !render)
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }

            try
            {
                if (roundtrip)
                {
                    Roundtrip(args[1], args[2]);
                }
                else
                {
                    Render(args[1], args[2], args.Skip(3).ToArray());
                }

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

        /// <summary>
        /// モデルを、指定したパラメータの値で参照レンダラーで描き、白い背景の PNG に書き出す。値は「名前=数値」で渡す。
        /// </summary>
        public static void Render(string modelDirectory, string outputPath, string[] parameters)
        {
            ModelPackage package = ModelPackage.Load(modelDirectory);
            var rig = new ModelRig(package.Model);
            foreach (string parameter in parameters)
            {
                string[] pair = parameter.Split('=');
                if (pair.Length != 2)
                {
                    throw new ArgumentException("parameter must be <name>=<value>: " + parameter);
                }

                rig.Parameters.Set(pair[0], double.Parse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture));
            }

            rig.Update();
            var textures = package.Textures.ToDictionary(t => t.Key, t => Png.Decode(t.Value));
            RgbaImage image = ReferenceRenderer.Render(rig, textures, Background);
            File.WriteAllBytes(outputPath, Png.Encode(image));
        }
    }
}
