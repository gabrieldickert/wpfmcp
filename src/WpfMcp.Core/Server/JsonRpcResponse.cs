using System.Text.Json.Nodes;

namespace WpfMcp.Core.Server
{
    /// <summary>
    /// A JSON-RPC 2.0 response. Carries either a result or an error, never both.
    /// </summary>
    public class JsonRpcResponse : IJsonRpcMessage
    {
        public const string Version = "2.0";

        /// <summary>
        /// Id echoed verbatim from the request — a string or a number — or null when the request
        /// could not be parsed well enough to recover one.
        /// </summary>
        public JsonNode? Id { get; }

        public JsonNode? Result { get; }

        public JsonRpcResponseError? Error { get; }

        private JsonRpcResponse(JsonNode? id, JsonNode? result, JsonRpcResponseError? error)
        {
            Id = id;
            Result = result;
            Error = error;
        }

        public static JsonRpcResponse Success(JsonNode? id, JsonNode? result)
        {
            return new JsonRpcResponse(id, result ?? new JsonObject(), null);
        }

        public static JsonRpcResponse Failure(JsonNode? id, JsonRpcResponseError error)
        {
            return new JsonRpcResponse(id, null, error);
        }

        /// <summary>
        /// Successful MCP tools/call response: the tool's return value wrapped as text content.
        /// </summary>
        public static JsonRpcResponse ToolResult(JsonNode? id, JsonNode? returnValue)
        {
            return Success(id, new JsonObject
            {
                ["content"] = TextContent(ToText(returnValue)),
                ["isError"] = false
            });
        }

        /// <summary>
        /// A tool that was found and invoked but failed. Per MCP this is a successful JSON-RPC
        /// response carrying isError, not a protocol-level error, so the model can see what broke.
        /// </summary>
        public static JsonRpcResponse ToolFailure(JsonNode? id, string message)
        {
            return Success(id, new JsonObject
            {
                ["content"] = TextContent(message),
                ["isError"] = true
            });
        }

        private static JsonArray TextContent(string text)
        {
            return new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = text }
            };
        }

        /// <summary>
        /// Renders a tool's return value as MCP text content. Strings are emitted raw rather than
        /// as quoted JSON, so a tool returning "hello" yields hello and not "hello".
        /// </summary>
        private static string ToText(JsonNode? value)
        {
            if (value is null)
            {
                return string.Empty;
            }

            if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var s))
            {
                return s;
            }

            return value.ToJsonString();
        }

        public JsonObject ToJson()
        {
            var json = new JsonObject
            {
                ["jsonrpc"] = Version,
                ["id"] = Id?.CloneNode()
            };

            if (Error is not null)
            {
                json["error"] = Error.ToJson();
            }
            else
            {
                // Cloning keeps this instance reusable: a JsonNode may only have one parent.
                json["result"] = Result?.CloneNode() ?? new JsonObject();
            }

            return json;
        }

        public string ToJsonString()
        {
            return ToJson().ToJsonString();
        }
    }

    /// <summary>
    /// The error member of a JSON-RPC 2.0 response.
    /// </summary>
    public class JsonRpcResponseError
    {
        public const int ParseErrorCode = -32700;
        public const int InvalidRequestCode = -32600;
        public const int MethodNotFoundCode = -32601;
        public const int InvalidParamsCode = -32602;
        public const int InternalErrorCode = -32603;

        public int Code { get; }

        public string Message { get; }

        public JsonObject? Data { get; }

        public JsonRpcResponseError(int code, string message, JsonObject? data = null)
        {
            Code = code;
            Message = message;
            Data = data;
        }

        public static JsonRpcResponseError ParseError(string details)
        {
            return new JsonRpcResponseError(ParseErrorCode, "Parse error",
                new JsonObject { ["details"] = details });
        }

        public static JsonRpcResponseError InvalidRequest(string details)
        {
            return new JsonRpcResponseError(InvalidRequestCode, "Invalid Request",
                new JsonObject { ["details"] = details });
        }

        public static JsonRpcResponseError MethodNotFound(string? method)
        {
            return new JsonRpcResponseError(MethodNotFoundCode, "Method not found",
                new JsonObject { ["method"] = method });
        }

        public static JsonRpcResponseError InvalidParams(string details)
        {
            return new JsonRpcResponseError(InvalidParamsCode, "Invalid params",
                new JsonObject { ["details"] = details });
        }

        /// <summary>
        /// tools/call naming a tool that is not registered. The method exists, so this is an
        /// invalid-params error rather than method-not-found.
        /// </summary>
        public static JsonRpcResponseError UnknownTool(string? toolName)
        {
            return new JsonRpcResponseError(InvalidParamsCode, "Unknown tool",
                new JsonObject { ["name"] = toolName });
        }

        public static JsonRpcResponseError InternalError(string details)
        {
            return new JsonRpcResponseError(InternalErrorCode, "Internal error",
                new JsonObject { ["details"] = details });
        }

        public JsonObject ToJson()
        {
            var json = new JsonObject
            {
                ["code"] = Code,
                ["message"] = Message
            };

            if (Data is not null)
            {
                json["data"] = Data.CloneNode();
            }

            return json;
        }
    }
}
