using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace WpfMcp.Core.Server
{
    /// <summary>
    /// An MCP server speaking the Streamable HTTP transport. Exposes a single endpoint that accepts
    /// POSTed JSON-RPC messages and serves a GET SSE stream for server-initiated notifications.
    /// This server is HTTP-only by design; the stdio transport is deliberately not implemented.
    /// </summary>
    public class McpServer
    {
        /// <summary>The protocol revision this server implements.</summary>
        public const string ProtocolVersion = "2025-06-18";

        /// <summary>
        /// Revisions accepted in the MCP-Protocol-Version header and in initialize. 2025-03-26 is
        /// included because the transport spec says to assume it when the header is absent.
        /// </summary>
        private static readonly HashSet<string> SupportedProtocolVersions = new()
        {
            ProtocolVersion,
            "2025-03-26",
        };

        public const string DefaultServerUrl = "http://127.0.0.1:9000/mcp";

        private readonly CancellationTokenSource _cts = new();
        private readonly HttpListener _listener;
        private readonly string _endpointPath;

        // Long-lived GET streams, used to push server-initiated notifications.
        private readonly ConcurrentDictionary<Guid, SseStream> _listeningStreams = new();

        // Cancellation sources for in-flight requests, keyed by their JSON-RPC id.
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlight = new();

        private readonly string _serverName;
        private readonly string _serverVersion;

        /// <summary>
        /// Raised when a tool call starts, progresses and finishes. Intended for host applications
        /// that want to surface MCP activity — a WPF app can bind these to a live view without any
        /// logging code inside the tools themselves.
        /// <para>
        /// Handlers run on a thread-pool thread, so a UI handler must marshal to its dispatcher.
        /// </para>
        /// </summary>
        public event Action<McpToolInvocation>? ToolInvocationStarted;

        /// <inheritdoc cref="ToolInvocationStarted"/>
        public event Action<McpToolProgressReport>? ToolProgressReported;

        /// <inheritdoc cref="ToolInvocationStarted"/>
        public event Action<McpToolCompletion>? ToolInvocationCompleted;

        /// <param name="serverUrl">
        /// Endpoint to serve, e.g. http://127.0.0.1:9000/mcp. Empty or null uses
        /// <see cref="DefaultServerUrl"/>.
        /// </param>
        /// <param name="serverName">Name reported to clients in the initialize result.</param>
        /// <param name="serverVersion">Version reported to clients in the initialize result.</param>
        public McpServer(string? serverUrl = null, string serverName = "WpfMcp", string serverVersion = "1.0.0")
        {
            var uri = new Uri(string.IsNullOrWhiteSpace(serverUrl) ? DefaultServerUrl : serverUrl);

            _serverName = serverName;
            _serverVersion = serverVersion;
            _endpointPath = uri.AbsolutePath.TrimEnd('/');

            if (_endpointPath.Length == 0)
            {
                _endpointPath = "/";
            }

            _listener = new HttpListener();

            // Listen at the authority root and route by path, so unknown paths can be answered with
            // a 404 rather than being refused by the listener.
            _listener.Prefixes.Add($"{uri.Scheme}://{uri.Authority}/");
        }

        /// <summary>
        /// Binds the listener and begins accepting connections. Binding happens synchronously, so a
        /// failure (a port already in use, most often) surfaces to the caller instead of being lost
        /// on a background thread.
        /// </summary>
        public void Start()
        {
            _listener.Start();

            McpToolRegistry.ToolsChanged += OnToolsChanged;

            _ = Task.Run(AcceptLoopAsync);
        }

        public void Stop()
        {
            McpToolRegistry.ToolsChanged -= OnToolsChanged;

            _cts.Cancel();
            _listener.Stop();
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    HttpListenerContext context;

                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch (HttpListenerException)
                    {
                        // Listener stopped while awaiting a connection.
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    _ = Task.Run(() => HandleRequestSafeAsync(context));
                }
            }
            finally
            {
                _listener.Close();
            }
        }

        private async Task HandleRequestSafeAsync(HttpListenerContext context)
        {
            try
            {
                await HandleRequestAsync(context);
            }
            catch (Exception)
            {
                // Never let a handler fault take down the accept loop or leave a socket hanging.
                try
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }
                catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
                {
                    // Response already committed; nothing further to say.
                }
            }
            finally
            {
                try
                {
                    context.Response.Close();
                }
                catch (Exception e) when (e is ObjectDisposedException or HttpListenerException)
                {
                    // Client already gone.
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;

            // The transport spec requires validating Origin on every connection: without it a web
            // page the user visits could drive this local server via DNS rebinding.
            if (!IsOriginAllowed(request))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }

            if (!IsProtocolVersionSupported(request, out var rejected))
            {
                await WriteStatusAsync(context, HttpStatusCode.BadRequest,
                    $"Unsupported MCP-Protocol-Version '{rejected}'");
                return;
            }

            var path = request.Url?.AbsolutePath.TrimEnd('/');
            if (path is null or "")
            {
                path = "/";
            }

            if (!string.Equals(path, _endpointPath, StringComparison.Ordinal))
            {
                await WriteStatusAsync(context, HttpStatusCode.NotFound,
                    $"No MCP endpoint at '{path}'. The endpoint is '{_endpointPath}'.");
                return;
            }

            switch (request.HttpMethod)
            {
                case "POST":
                    await HandlePostAsync(context);
                    break;

                case "GET":
                    await HandleListeningStreamAsync(context);
                    break;

                default:
                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    break;
            }
        }

        /// <summary>
        /// Handles a POSTed JSON-RPC message. A notification is acknowledged with 202 and no body;
        /// a request produces either a single JSON object or an SSE stream.
        /// </summary>
        private async Task HandlePostAsync(HttpListenerContext context)
        {
            var body = await ReadBodyAsync(context.Request);

            if (string.IsNullOrWhiteSpace(body))
            {
                await WriteJsonRpcAsync(context,
                    JsonRpcResponse.Failure(null, JsonRpcResponseError.InvalidRequest("Empty request body")));
                return;
            }

            if (!JsonRpcRequest.TryParse(body, out var msg, out var parseError))
            {
                await WriteJsonRpcAsync(context, JsonRpcResponse.Failure(null, parseError!));
                return;
            }

            if (!msg!.HasValidVersion)
            {
                await WriteJsonRpcAsync(context, JsonRpcResponse.Failure(msg.Id,
                    JsonRpcResponseError.InvalidRequest(
                        $"Unsupported jsonrpc version '{msg.JsonRpc}'; expected \"{JsonRpcRequest.Version}\"")));
                return;
            }

            if (msg.IsNotification)
            {
                HandleNotification(msg);

                // A notification must not be answered; the transport specifies a bare 202.
                context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return;
            }

            // tools/call is the only method that can run long enough to report progress, so it is
            // the only one that may answer over SSE.
            if (msg.Method == "tools/call" && msg.ProgressToken is not null)
            {
                var stream = new SseStream(context.Response, msg.ProgressToken);
                var streamed = await DispatchAsync(msg, stream);
                await stream.SendAsync(streamed);
                return;
            }

            await WriteJsonRpcAsync(context, await DispatchAsync(msg, NullMcpProgress.Instance));
        }

        private void HandleNotification(JsonRpcRequest msg)
        {
            switch (msg.Method)
            {
                case "notifications/initialized":
                    // Client is ready. Nothing to do: this server holds no per-session state.
                    break;

                case "notifications/cancelled":
                    CancelInFlight(msg.Params?["requestId"]);
                    break;
            }
        }

        private async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest msg, IMcpProgress progress)
        {
            switch (msg.Method)
            {
                case "initialize":
                    return JsonRpcResponse.Success(msg.Id, Initialize(msg));

                case "ping":
                    // Liveness check: an empty result is the whole contract.
                    return JsonRpcResponse.Success(msg.Id, new JsonObject());

                case "tools/list":
                    return JsonRpcResponse.Success(msg.Id, new JsonObject { ["tools"] = ListTools() });

                case "tools/call":
                    return await CallToolAsync(msg, progress);

                default:
                    return JsonRpcResponse.Failure(msg.Id, JsonRpcResponseError.MethodNotFound(msg.Method));
            }
        }

        /// <summary>
        /// Builds the InitializeResult. The client's requested version is echoed when supported,
        /// otherwise this server's own version is offered and the client decides whether to proceed.
        /// </summary>
        private JsonObject Initialize(JsonRpcRequest msg)
        {
            var requested = msg.Params?["protocolVersion"] is JsonValue v && v.TryGetValue<string>(out var r) ? r : null;

            var negotiated = requested is not null && SupportedProtocolVersions.Contains(requested)
                ? requested
                : ProtocolVersion;

            return new JsonObject
            {
                ["protocolVersion"] = negotiated,
                ["capabilities"] = new JsonObject
                {
                    // Truthful now that GET streams carry notifications/tools/list_changed.
                    ["tools"] = new JsonObject { ["listChanged"] = true }
                },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = _serverName,
                    ["version"] = _serverVersion
                },
                ["instructions"] = "Tools are provided by a running WPF application and may change as windows open and close."
            };
        }

        private static JsonArray ListTools()
        {
            var tools = new JsonArray();

            foreach (var tool in McpToolRegistry.Tools)
            {
                foreach (var def in tool.GetToolDefinitions())
                {
                    tools.Add(def!.CloneNode());
                }
            }

            return tools;
        }

        private async Task<JsonRpcResponse> CallToolAsync(JsonRpcRequest msg, IMcpProgress progress)
        {
            var toolName = msg.Params?["name"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var n) ? n : null;

            if (string.IsNullOrEmpty(toolName))
            {
                return JsonRpcResponse.Failure(msg.Id, JsonRpcResponseError.InvalidParams("Missing required parameter 'name'"));
            }

            var target = FindTool(toolName!);
            if (target is null)
            {
                return JsonRpcResponse.Failure(msg.Id, JsonRpcResponseError.UnknownTool(toolName));
            }

            var arguments = msg.Params?["arguments"] as JsonObject;

            // Linked to the server token so shutdown still cancels, but cancellable on its own so a
            // client's notifications/cancelled actually stops this call.
            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var key = RequestKey(msg.Id);

            if (key is not null)
            {
                _inFlight[key] = requestCts;
            }

            var invocation = new McpToolInvocation(Guid.NewGuid(), toolName!, arguments);
            var started = Stopwatch.StartNew();

            ToolInvocationStarted?.Invoke(invocation);

            // Tees progress to the host application as well as to the client's SSE stream, so a UI
            // can follow a running tool without the tool knowing anything about the UI.
            var relay = new ProgressRelay(progress, (p, t, m) =>
                ToolProgressReported?.Invoke(new McpToolProgressReport(invocation.Id, toolName!, p, t, m)));

            JsonNode? result = null;
            string? error = null;

            try
            {
                result = await target.InvokeToolAsync(toolName!, arguments, relay, requestCts.Token);
                return JsonRpcResponse.ToolResult(msg.Id, result);
            }
            catch (OperationCanceledException)
            {
                error = $"Tool '{toolName}' was cancelled";
                return JsonRpcResponse.ToolFailure(msg.Id, error);
            }
            catch (Exception ex)
            {
                // A throwing tool is reported to the caller rather than killing the request.
                error = $"Tool '{toolName}' failed: {ex.Message}";
                return JsonRpcResponse.ToolFailure(msg.Id, error);
            }
            finally
            {
                if (key is not null)
                {
                    _inFlight.TryRemove(key, out _);
                }

                ToolInvocationCompleted?.Invoke(
                    new McpToolCompletion(invocation.Id, toolName!, result, error, started.Elapsed));
            }
        }

        /// <summary>
        /// Forwards progress to the client's stream and to the host application. Host handlers run
        /// inline, so a slow or throwing handler must not disrupt the tool: exceptions are swallowed.
        /// </summary>
        private sealed class ProgressRelay : IMcpProgress
        {
            private readonly IMcpProgress _inner;
            private readonly Action<double, double?, string?> _observer;

            public ProgressRelay(IMcpProgress inner, Action<double, double?, string?> observer)
            {
                _inner = inner;
                _observer = observer;
            }

            public Task ReportAsync(double progress, double? total = null, string? message = null)
            {
                try
                {
                    _observer(progress, total, message);
                }
                catch (Exception)
                {
                    // A misbehaving UI handler must not fail the tool call.
                }

                return _inner.ReportAsync(progress, total, message);
            }
        }

        private void CancelInFlight(JsonNode? requestId)
        {
            var key = RequestKey(requestId);

            if (key is not null && _inFlight.TryGetValue(key, out var cts))
            {
                cts.Cancel();
            }
        }

        /// <summary>Ids may be strings or numbers, so their JSON form is the lookup key.</summary>
        private static string? RequestKey(JsonNode? id) => id?.ToJsonString();

        /// <summary>
        /// Serves the GET SSE stream the transport uses for server-initiated messages. Stays open,
        /// heartbeating, until the client disconnects or the server stops.
        /// </summary>
        private async Task HandleListeningStreamAsync(HttpListenerContext context)
        {
            var stream = new SseStream(context.Response);
            var id = Guid.NewGuid();
            _listeningStreams[id] = stream;

            try
            {
                while (!_cts.IsCancellationRequested && stream.IsOpen)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(15), _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    await stream.SendHeartbeatAsync();
                }
            }
            finally
            {
                _listeningStreams.TryRemove(id, out _);
            }
        }

        private void OnToolsChanged()
        {
            _ = BroadcastAsync(new JsonRpcNotification("notifications/tools/list_changed"));
        }

        /// <summary>
        /// Sends a notification to every open listening stream. Without session management each GET
        /// stream is treated as a separate client, which is why this is a fan-out rather than a
        /// pick-one-stream-per-session delivery.
        /// </summary>
        private async Task BroadcastAsync(IJsonRpcMessage message)
        {
            foreach (var pair in _listeningStreams)
            {
                if (!await pair.Value.SendAsync(message))
                {
                    _listeningStreams.TryRemove(pair.Key, out _);
                }
            }
        }

        /// <summary>
        /// Allows same-origin and no-Origin callers (native MCP clients send no Origin) and rejects
        /// browser origins that are not this loopback server.
        /// </summary>
        private static bool IsOriginAllowed(HttpListenerRequest request)
        {
            var origin = request.Headers["Origin"];

            if (string.IsNullOrEmpty(origin))
            {
                return true;
            }

            return Uri.TryCreate(origin, UriKind.Absolute, out var parsed)
                && parsed.IsLoopback
                && parsed.Port == request.Url?.Port;
        }

        private static bool IsProtocolVersionSupported(HttpListenerRequest request, out string? rejected)
        {
            rejected = request.Headers["MCP-Protocol-Version"];

            // Absent means "assume 2025-03-26" per the transport spec, which this server supports.
            if (string.IsNullOrEmpty(rejected))
            {
                rejected = null;
                return true;
            }

            if (SupportedProtocolVersions.Contains(rejected!))
            {
                rejected = null;
                return true;
            }

            return false;
        }

        private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
        {
            using var mem = new MemoryStream();
            await request.InputStream.CopyToAsync(mem);
            return Encoding.UTF8.GetString(mem.ToArray());
        }

        private static IMcpTool? FindTool(string toolName)
        {
            foreach (var tool in McpToolRegistry.Tools)
            {
                foreach (var def in tool.GetToolDefinitions())
                {
                    if (def?["name"]?.GetValue<string>() == toolName)
                    {
                        return tool;
                    }
                }
            }

            return null;
        }

        private static async Task WriteJsonRpcAsync(HttpListenerContext context, IJsonRpcMessage message)
        {
            var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static async Task WriteStatusAsync(HttpListenerContext context, HttpStatusCode status, string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message);

            context.Response.StatusCode = (int)status;
            context.Response.ContentType = "text/plain";
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }
    }
}