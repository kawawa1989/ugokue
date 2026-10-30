using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ugokue.Core.Live.Nizima
{
    /// <summary>
    /// nizima LIVE Plugin API の JSON メッセージを作り、受け取ったメッセージを読み取る。
    /// </summary>
    public static class NizimaProtocol
    {
        public const string ApiVersion = "1.0.0";

        public const string RegisterPlugin = "RegisterPlugin";
        public const string EstablishConnection = "EstablishConnection";
        public const string NotifyEnabledChanged = "NotifyEnabledChanged";
        public const string NotifyFrameUpdated = "NotifyFrameUpdated";
        public const string GetLiveParameters = "GetLiveParameters";
        public const string InvalidToken = "InvalidToken";

        /// <summary>
        /// Request のメッセージを JSON 文字列にする。
        /// </summary>
        public static string Request(string method, string id, JObject data)
        {
            var message = new JObject
            {
                ["nLPlugin"] = ApiVersion,
                ["Timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["Id"] = id,
                ["Type"] = "Request",
                ["Method"] = method,
                ["Data"] = data ?? new JObject(),
            };
            return message.ToString(Formatting.None);
        }

        /// <summary>
        /// 受け取った JSON 文字列を読み取る。JSON として読めない、または必要な項目がなければ null を返す。
        /// </summary>
        public static NizimaMessage Parse(string json)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }

            string type = root.Value<string>("Type");
            string method = root.Value<string>("Method");
            if (type == null || method == null)
            {
                return null;
            }

            long? timestamp = root["Timestamp"]?.Type == JTokenType.Integer ? root.Value<long>("Timestamp") : (long?)null;
            return new NizimaMessage(type, method, root.Value<string>("Id"), root["Data"] as JObject ?? new JObject(), timestamp);
        }

        /// <summary>
        /// NotifyFrameUpdated の Data から、指定したモデル（null なら先頭のモデル）の LiveParameter の値を取り出す。モデルがなければ null を返す。
        /// </summary>
        public static Dictionary<string, double> ReadLiveParameterValues(JObject data, string modelId = null)
        {
            if (!(data["Models"] is JArray models))
            {
                return null;
            }

            foreach (JToken model in models)
            {
                if (modelId != null && model.Value<string>("ModelId") != modelId)
                {
                    continue;
                }

                var values = new Dictionary<string, double>(StringComparer.Ordinal);
                if (model["LiveParameterValues"] is JArray parameters)
                {
                    foreach (JToken parameter in parameters)
                    {
                        string id = parameter.Value<string>("Id");
                        JToken value = parameter["Value"];
                        if (id != null && value != null && (value.Type == JTokenType.Float || value.Type == JTokenType.Integer))
                        {
                            values[id] = value.Value<double>();
                        }
                    }
                }

                return values;
            }

            return null;
        }

        /// <summary>
        /// GetLiveParameters の Response の Data から、LiveParameter の定義の一覧を取り出す。
        /// </summary>
        public static List<LiveParameterInfo> ReadLiveParameters(JObject data)
        {
            var result = new List<LiveParameterInfo>();
            if (!(data["LiveParameters"] is JArray parameters))
            {
                return result;
            }

            foreach (JToken parameter in parameters)
            {
                string id = parameter.Value<string>("Id");
                if (id == null)
                {
                    continue;
                }

                result.Add(new LiveParameterInfo(
                    id,
                    parameter.Value<string>("CreatedBy"),
                    parameter.Value<string>("Group"),
                    ReadNumber(parameter, "Min"),
                    ReadNumber(parameter, "Max"),
                    ReadNumber(parameter, "Base"),
                    parameter.Value<bool?>("Repeat") ?? false));
            }

            return result;
        }

        private static double ReadNumber(JToken token, string key)
        {
            JToken value = token[key];
            return value != null && (value.Type == JTokenType.Float || value.Type == JTokenType.Integer)
                ? value.Value<double>()
                : double.NaN;
        }

        /// <summary>
        /// Request の Id に使う文字列を返す。
        /// </summary>
        public static string RequestId(int sequence)
        {
            return "ugokue-" + sequence.ToString(CultureInfo.InvariantCulture);
        }
    }
}
