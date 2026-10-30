using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// 1つのモデルのフォルダ（model.json とテクスチャ）を読み書きする。
    /// </summary>
    public sealed class ModelPackage
    {
        public const string ModelFileName = "model.json";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false, true);

        public ModelPackage(Model model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
        }

        public Model Model { get; }

        public Dictionary<string, byte[]> Textures { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>
        /// フォルダから model.json と、メッシュが参照するすべてのテクスチャを読み込む。
        /// </summary>
        public static ModelPackage Load(string directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            string root = Path.GetFullPath(directory);
            string modelPath = Path.Combine(root, ModelFileName);
            if (!File.Exists(modelPath))
            {
                throw new ModelFormatException("model.json was not found: " + modelPath);
            }

            string json;
            try
            {
                json = Utf8NoBom.GetString(File.ReadAllBytes(modelPath));
            }
            catch (DecoderFallbackException e)
            {
                throw new ModelFormatException("model.json is not valid UTF-8.", e);
            }

            if (json.Length > 0 && json[0] == '﻿')
            {
                json = json.Substring(1);
            }

            var package = new ModelPackage(ModelJson.Read(json));
            foreach (string texture in ReferencedTextures(package.Model))
            {
                string texturePath = ResolveTexturePath(root, texture);
                if (!File.Exists(texturePath))
                {
                    throw new ModelFormatException("Texture file was not found: " + texture);
                }

                package.Textures[texture] = File.ReadAllBytes(texturePath);
            }

            return package;
        }

        /// <summary>
        /// フォルダに model.json（UTF-8、BOM なし）とテクスチャを書き出す。フォルダとサブフォルダがなければ作る。
        /// </summary>
        public void Save(string directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            string root = Path.GetFullPath(directory);
            List<string> referenced = ReferencedTextures(Model);
            var texturePaths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string texture in referenced)
            {
                if (!Textures.ContainsKey(texture))
                {
                    throw new ModelFormatException("Texture data is missing for: " + texture);
                }

                texturePaths[texture] = ResolveTexturePath(root, texture);
            }

            string unreferenced = Textures.Keys.FirstOrDefault(key => !texturePaths.ContainsKey(key));
            if (unreferenced != null)
            {
                throw new ModelFormatException("Texture is not referenced by any mesh: " + unreferenced);
            }

            byte[] json = Utf8NoBom.GetBytes(ModelJson.Write(Model));
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, ModelFileName), json);
            foreach (KeyValuePair<string, string> entry in texturePaths)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(entry.Value));
                File.WriteAllBytes(entry.Value, Textures[entry.Key]);
            }
        }

        private static List<string> ReferencedTextures(Model model)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (model.Meshes == null)
            {
                return result;
            }

            foreach (Mesh mesh in model.Meshes)
            {
                if (mesh != null && mesh.Texture != null && seen.Add(mesh.Texture))
                {
                    result.Add(mesh.Texture);
                }
            }

            return result;
        }

        private static string ResolveTexturePath(string root, string texture)
        {
            if (texture.Length == 0
                || texture.IndexOf('\\') >= 0
                || texture[0] == '/'
                || (texture.Length >= 2 && texture[1] == ':')
                || Path.IsPathRooted(texture))
            {
                throw new ModelFormatException("Texture path must be a relative path separated by '/': " + texture);
            }

            if (texture.Split('/').Any(segment => segment.Length == 0 || segment == ".."))
            {
                throw new ModelFormatException("Texture path must stay inside the model folder: " + texture);
            }

            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(root, texture.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            {
                throw new ModelFormatException("Texture path must stay inside the model folder: " + texture);
            }

            return fullPath;
        }
    }
}
