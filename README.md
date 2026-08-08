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

### Letting a client drive the UI

The sample's right-hand panel has a real editor. These tools write into it, read it back, and
rename the window itself:

```bash
# type into the editor from outside the app
curl -X POST http://127.0.0.1:9000/mcp -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"set_editor_text","arguments":{"text":"Written by the model.\n"}}}'

# now type something into the editor yourself, then read the whole thing back
curl -X POST http://127.0.0.1:9000/mcp -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"read_editor_text","arguments":{}}}'

# change the actual title bar
curl -X POST http://127.0.0.1:9000/mcp -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"set_window_title","arguments":{"title":"Retitled by an MCP client"}}}'
```

The editor is two-way bound, so the model and the person at the keyboard are editing the same
text — which is what makes `read_editor_text` useful as an input channel, not just an echo.

### Everything else

```bash
curl -X POST http://127.0.0.1:9000/mcp -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'

curl -X POST http://127.0.0.1:9000/mcp -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"create_sum","arguments":{"a":23,"b":42}}}'
```

Note that tool names are **not** JSON-RPC methods — tools are always invoked through `tools/call`.

## Requirements

| | Minimum | Notes |
|---|---|---|
| Consuming app | `net6.0-windows` | .NET 6, 7, 8 and 9 WPF apps all work. `net6.0` is the floor because `System.Text.Json`'s `JsonNode` arrived in .NET 6. |
| SDK / Visual Studio to build against it | .NET SDK 6.0.400 / VS 2022 17.3 | Set by the generator's Roslyn 4.3 reference — the analyzer will not load on an older toolchain. |
| Platform | Windows | It's WPF. |

## Pack the NuGet package

```bash
dotnet pack src/WpfMcp.Core -c Release
```

Produces `WpfMcp.<version>.nupkg` containing the runtime library, the attributes, and the
generator under `analyzers/dotnet/cs`, plus a `.snupkg` of symbols. One `PackageReference` gives a
consumer all three, and SourceLink lets them step into this source under the debugger.

## Releasing

1. Bump `<Version>` in `src/WpfMcp.Core/WpfMcp.Core.csproj` and add a `CHANGELOG.md` entry.
2. Build the release artefacts deterministically:

   ```bash
   dotnet pack src/WpfMcp.Core -c Release -p:ContinuousIntegrationBuild=true
   ```

3. Push both packages (nuget.org picks up the `.snupkg` alongside the `.nupkg`):

   ```bash
   dotnet nuget push src/WpfMcp.Core/bin/Release/WpfMcp.<version>.nupkg \
     --source https://api.nuget.org/v3/index.json --api-key <YOUR_KEY>
   ```

4. Tag the commit: `git tag v<version> && git push origin v<version>`.

> A published version number can never be reused, even after unlisting. When testing a package
> locally, bump the version or delete `~/.nuget/packages/wpfmcp/<version>` first — otherwise NuGet
> silently serves the cached copy and you test the wrong bits.

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
