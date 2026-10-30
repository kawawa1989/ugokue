using Newtonsoft.Json.Linq;

namespace Ugokue.Core.Live.Nizima
{
    /// <summary>
    /// nizima LIVE から受け取ったメッセージ1件。
    /// </summary>
    public sealed class NizimaMessage
    {
        public NizimaMessage(string type, string method, string id, JObject data)
        {
            Type = type;
            Method = method;
            Id = id;
            Data = data;
        }

        /// <summary>
        /// "Request"、"Response"、"Event"、"Error" のいずれか。
        /// </summary>
        public string Type { get; }

        public string Method { get; }

        public string Id { get; }

        public JObject Data { get; }

        public bool IsResponse => Type == "Response";

        public bool IsEvent => Type == "Event";

        public bool IsError => Type == "Error";

        /// <summary>
        /// Error のときの ErrorType。それ以外は null。
        /// </summary>
        public string ErrorType => IsError ? Data.Value<string>("ErrorType") : null;
    }
}
