using System.IO;
using System.Text;

namespace Ugokue.Core.Live.Nizima
{
    /// <summary>
    /// nizima LIVE から受け取ったプラグインのトークンを保存・読み込みする。
    /// </summary>
    public interface ITokenStore
    {
        /// <summary>
        /// 保存したトークンを返す。なければ null を返す。
        /// </summary>
        string Load();

        void Save(string token);

        void Clear();
    }

    /// <summary>
    /// トークンを1つのテキストファイルに保存する。
    /// </summary>
    public sealed class FileTokenStore : ITokenStore
    {
        public FileTokenStore(string path)
        {
            Path = path;
        }

        public string Path { get; }

        /// <summary>
        /// ファイルのトークンを返す。ファイルがない、または空なら null を返す。
        /// </summary>
        public string Load()
        {
            if (!File.Exists(Path))
            {
                return null;
            }

            string token = File.ReadAllText(Path, Encoding.UTF8).Trim();
            return token.Length == 0 ? null : token;
        }

        /// <summary>
        /// トークンをファイルに書き、親フォルダがなければ作る。
        /// </summary>
        public void Save(string token)
        {
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path, token, new UTF8Encoding(false));
        }

        /// <summary>
        /// トークンのファイルを消す。
        /// </summary>
        public void Clear()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }

    /// <summary>
    /// トークンをメモリだけに持つ。テストと一時的な接続に使う。
    /// </summary>
    public sealed class MemoryTokenStore : ITokenStore
    {
        private string token;

        public MemoryTokenStore(string token = null)
        {
            this.token = token;
        }

        public string Load()
        {
            return token;
        }

        public void Save(string value)
        {
            token = value;
        }

        public void Clear()
        {
            token = null;
        }
    }
}
