# WpfMcp

Turn a WPF application into an [MCP](https://modelcontextprotocol.io) server.

Mark a class `[McpToolCollection]` and its methods `[McpTool]`, and a Roslyn source generator
exposes them as Model Context Protocol tools over HTTP. Tools can be plain static methods, or
instance methods on a live `Window` — which lets a model read and change what is actually on
screen.

```csharp
[McpToolCollection]
public static class MathTools
{
    [McpTool("create_sum")]
    [Description("Adds two numbers together")]
    public static int Sum(
        [Description("First value")] int a,
        [Description("Second value")] int b) => a + b;
}
```

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    _server = new McpServer("http://127.0.0.1:9000/mcp");
    _server.Start();
}
```

That's the whole setup. Nothing registers tools by hand: static collections register at module
load, window-bound collections register as the window initialises.

## Repository layout

| Path | Contents |
|---|---|
| `src/WpfMcp.Core` | Runtime library, the MCP server, and the attributes. This project is the NuGet package. |
| `src/WpfMcp.Generators` | The Roslyn source generator, shipped inside the package as an analyzer. |
| `samples/WpfMcp.ExampleApp` | A WPF app that hosts the server and displays live tool activity. |

## Build and run

```bash
dotnet build WpfMcp.sln
dotnet run --project samples/WpfMcp.ExampleApp
```

The sample listens on `http://127.0.0.1:9000/mcp` and shows every tool call as it happens —
arguments, result, duration, progress, and errors — alongside window state that its own tools
read and change.

Try it with curl:

```bash
curl -X POST http://127.0.0.1:9000/mcp -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'

curl -X POST http://127.0.0.1:9000/mcp -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"create_sum","arguments":{"a":23,"b":42}}}'
```

Note that tool names are **not** JSON-RPC methods — tools are always invoked through `tools/call`.

## Pack the NuGet package

```bash
dotnet pack src/WpfMcp.Core -c Release
```

Produces `WpfMcp.<version>.nupkg` containing the runtime library, the attributes, and the
generator under `analyzers/dotnet/cs`. One `PackageReference` gives a consumer all three.

## Protocol support

HTTP only — the stdio transport is deliberately not implemented.

Implements the Streamable HTTP transport of MCP `2025-06-18`: a single endpoint serving `POST`
for JSON-RPC and `GET` for a server-to-client SSE stream, `initialize` version negotiation,
`ping`, `tools/list`, `tools/call`, notification handling (202, no body), per-request
cancellation via `notifications/cancelled`, progress over SSE, and
`notifications/tools/list_changed` when the tool set changes. `Origin` is validated on every
request to prevent DNS-rebinding attacks against the loopback server.

Not implemented: sessions (`Mcp-Session-Id`), `tools/list` pagination, OAuth authorization, and
`structuredContent` / `outputSchema`.

See [`src/WpfMcp.Core/README.md`](src/WpfMcp.Core/README.md) for the full API walkthrough —
async tools, progress reporting, cancellation, and the activity events.

## Licence

MIT — see [LICENSE.txt](LICENSE.txt).
