using System;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace WpfMcp.Core.Server
{
    /// <summary>
    /// Writes JSON-RPC messages to an HTTP response as Server-Sent Events, and acts as the
    /// progress sink for the request being streamed.
    /// <para>
    /// Used in two shapes: a POST stream that carries notifications then the JSON-RPC response for
    /// the originating request before closing, and a long-lived GET stream that carries
    /// server-initiated notifications until the client goes away.
    /// </para>
    /// </summary>
    public sealed class SseStream : IMcpProgress
    {
        private readonly HttpListenerResponse _response;
        private readonly JsonNode? _progressToken;

        // Serializes writes: a tool may report progress from any thread, and the heartbeat loop
        // writes concurrently with broadcast notifications on the GET stream.
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        private double? _lastProgress;

        public SseStream(HttpListenerResponse response, JsonNode? progressToken = null)
        {
            _response = response;
            _progressToken = progressToken;

            _response.StatusCode = (int)HttpStatusCode.OK;
            _response.ContentType = "text/event-stream";
            _response.Headers.Add("Cache-Control", "no-cache");
            _response.SendChunked = true;
        }

        /// <summary>False once a write has failed, i.e. the client has disconnected.</summary>
        public bool IsOpen { get; private set; } = true;

        public async Task ReportAsync(double progress, double? total = null, string? message = null)
        {
            if (_progressToken is null)
            {
                return;
            }

            // The spec requires progress to increase with each notification, so a report that
            // doesn't advance is dropped rather than put on the wire.
            if (_lastProgress.HasValue && progress <= _lastProgress.Value)
            {
                return;
            }

            _lastProgress = progress;

            await SendAsync(JsonRpcNotification.Progress(_progressToken, progress, total, message));
        }

        public Task<bool> SendAsync(IJsonRpcMessage message)
        {
            return WriteAsync($"data: {message.ToJsonString()}\n\n");
        }

        /// <summary>
        /// Writes an SSE comment. Keeps intermediaries from timing the connection out, and is how a
        /// disconnected client is detected on an otherwise idle stream.
        /// </summary>
        public Task<bool> SendHeartbeatAsync()
        {
            return WriteAsync(": heartbeat\n\n");
        }

        private async Task<bool> WriteAsync(string payload)
        {
            if (!IsOpen)
            {
                return false;
            }

            var bytes = Encoding.UTF8.GetBytes(payload);

            await _writeLock.WaitAsync();
            try
            {
                await _response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                await _response.OutputStream.FlushAsync();
                return true;
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or System.IO.IOException)
            {
                // Client hung up mid-stream; the caller drops this stream rather than faulting.
                IsOpen = false;
                return false;
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}
