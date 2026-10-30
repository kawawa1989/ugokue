using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ugokue.Core.Format
{
    /// <summary>
    /// model.json の文字列と <see cref="Model"/> を相互に変換する。
    /// </summary>
    public static class ModelJson
    {
        private static readonly Regex SupportedVersion = new Regex(@"^0\.1\.[0-9]+$", RegexOptions.CultureInvariant);

        /// <summary>
        /// model.json の文字列を読み、形式名とバージョンを確かめてから <see cref="Model"/> に変換する。
        /// </summary>
        public static Model Read(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            JObject root = Parse(json);
            CheckHeader(root);

            try
            {
                return root.ToObject<Model>(CreateSerializer());
            }
            catch (JsonException e)
            {
                throw new ModelFormatException("model.json does not match the ugokue model format: " + e.Message, e);
            }
        }

        /// <summary>
        /// <see cref="Model"/> を、2 文字の字下げと LF の改行で、末尾に改行を付けた JSON 文字列にする。
        /// </summary>
        public static string Write(Model model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            var builder = new StringBuilder();
            using (var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture) { NewLine = "\n" })
            using (var jsonWriter = new JsonTextWriter(stringWriter) { Formatting = Formatting.Indented, Indentation = 2, IndentChar = ' ' })
            {
                CreateSerializer().Serialize(jsonWriter, model);
            }

            builder.Append('\n');
            return builder.ToString();
        }

        private static JObject Parse(string json)
        {
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                {
                    JToken token = JToken.ReadFrom(reader);
                    if (reader.Read())
                    {
                        throw new ModelFormatException("model.json has content after the top-level object.");
                    }

                    if (!(token is JObject root))
                    {
                        throw new ModelFormatException("model.json must contain a JSON object at the top level.");
                    }

                    return root;
                }
            }
            catch (JsonReaderException e)
            {
                throw new ModelFormatException("model.json is not valid JSON: " + e.Message, e);
            }
        }

        private static void CheckHeader(JObject root)
        {
            JToken format = root["format"];
            if (format == null || format.Type != JTokenType.String || (string)format != Model.FormatName)
            {
                throw new ModelFormatException(
                    "Unsupported format: expected \"" + Model.FormatName + "\" but found " + Describe(format) + ".");
            }

            JToken version = root["version"];
            if (version == null || version.Type != JTokenType.String || !SupportedVersion.IsMatch((string)version))
            {
                throw new ModelFormatException(
                    "Unsupported version: expected 0.1.x but found " + Describe(version) + ".");
            }
        }

        private static string Describe(JToken token)
        {
            return token == null ? "nothing" : token.ToString(Formatting.None);
        }

        private static JsonSerializer CreateSerializer()
        {
            return JsonSerializer.Create(new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                Culture = CultureInfo.InvariantCulture,
            });
        }
    }
}
