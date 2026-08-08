using System.Text.Json.Nodes;

namespace WpfMcp.Core.Server
{
    /// <summary>
    /// A JSON-RPC 2.0 notification — a message with a method but no id, so no response is expected.
    /// </summary>
    public class JsonRpcNotification : IJsonRpcMessage
    {
        public const string ProgressMethod = "notifications/progress";

        public string Method { get; }

        public JsonObject? Params { get; }

        public JsonRpcNotification(string method, JsonObject? @params = null)
        {
            Method = method;
            Params = @params;
        }

        /// <summary>
        /// Builds an MCP notifications/progress message. The token is echoed from the request's
        /// params._meta.progressToken and may be a string or a number.
        /// </summary>
        public static JsonRpcNotification Progress(JsonNode progressToken, double progress, double? total, string? message)
        {
            var parameters = new JsonObject
            {
                ["progressToken"] = progressToken.CloneNode(),
                ["progress"] = progress
            };

            if (total.HasValue)
            {
                parameters["total"] = total.Value;
            }

            if (!string.IsNullOrEmpty(message))
            {
                parameters["message"] = message;
            }

            return new JsonRpcNotification(ProgressMethod, parameters);
        }

        public JsonObject ToJson()
        {
            var json = new JsonObject
            {
                ["jsonrpc"] = JsonRpcResponse.Version,
                ["method"] = Method
            };

            if (Params is not null)
            {
                json["params"] = Params.CloneNode();
            }

            return json;
        }

        public string ToJsonString()
        {
            return ToJson().ToJsonString();
        }
    }
}
