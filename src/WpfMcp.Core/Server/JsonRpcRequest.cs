using System.Text.Json;
using System.Text.Json.Nodes;

namespace WpfMcp.Core.Server
{
    /// <summary>
    /// An inbound JSON-RPC 2.0 message. A message with no id is a notification, which must never
    /// be answered.
    /// </summary>
    public class JsonRpcRequest : IJsonRpcMessage
    {
        /// <summary>
        /// JSON-RPC 2.0 requires the literal string "2.0" — not the number 2.0.
        /// </summary>
        public const string Version = JsonRpcResponse.Version;

        public string Method { get; }

        /// <summary>The 'jsonrpc' value as sent by the caller; null when omitted.</summary>
        public string? JsonRpc { get; }

        public JsonObject? Params { get; }

        /// <summary>
        /// The request id. MCP allows a string or a number, and forbids null, so this is kept as a
        /// raw node and echoed back verbatim rather than coerced to a specific CLR type.
        /// </summary>
        public JsonNode? Id { get; }

        public JsonRpcRequest(string method, JsonObject? @params = null, JsonNode? id = null, string? jsonrpc = null)
        {
            Method = method;
            Params = @params;
            Id = id;
            JsonRpc = jsonrpc;
        }

        /// <summary>A message without an id: one-way, and the receiver must not respond.</summary>
        public bool IsNotification => Id is null;

        /// <summary>True when the caller omitted 'jsonrpc' or sent the required "2.0".</summary>
        public bool HasValidVersion => JsonRpc is null || JsonRpc == Version;

        /// <summary>
        /// The progress token from params._meta.progressToken, or null if the caller did not ask
        /// for progress notifications. May be a string or a number.
        /// </summary>
        public JsonNode? ProgressToken => Params?["_meta"]?["progressToken"];

        /// <summary>
        /// Parses a JSON-RPC message. Returns false with a populated <paramref name="error"/> for
        /// malformed JSON or a body that is not a JSON-RPC message shape.
        /// </summary>
        public static bool TryParse(string json, out JsonRpcRequest? message, out JsonRpcResponseError? error)
        {
            message = null;
            error = null;

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (JsonException ex)
            {
                error = JsonRpcResponseError.ParseError(ex.Message);
                return false;
            }

            if (root is not JsonObject obj)
            {
                error = JsonRpcResponseError.InvalidRequest("Expected a JSON-RPC object");
                return false;
            }

            var method = ReadString(obj, "method");
            if (method is null)
            {
                error = JsonRpcResponseError.InvalidRequest("Missing required member 'method'");
                return false;
            }

            // An explicit null id is invalid per the spec; treating it as absent means the message
            // is handled as a notification rather than answered with a null-id response.
            var id = obj["id"];
            if (id is not null && id.GetValueKind() == JsonValueKind.Null)
            {
                id = null;
            }

            message = new JsonRpcRequest(method, obj["params"] as JsonObject, id?.DeepClone(), ReadString(obj, "jsonrpc"));
            return true;
        }

        private static string? ReadString(JsonObject obj, string property)
        {
            return obj[property] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
        }

        public JsonObject ToJson()
        {
            var json = new JsonObject
            {
                ["jsonrpc"] = Version,
                ["method"] = Method
            };

            if (Id is not null)
            {
                json["id"] = Id.DeepClone();
            }

            if (Params is not null)
            {
                json["params"] = Params.DeepClone();
            }

            return json;
        }

        public string ToJsonString()
        {
            return ToJson().ToJsonString();
        }
    }
}
