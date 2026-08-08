# WpfMcp

Turn a WPF application into an [MCP](https://modelcontextprotocol.io) server.

Mark a class `[McpToolCollection]` and its methods `[McpTool]`, and a Roslyn source generator
exposes them as Model Context Protocol tools over HTTP. Tools can be plain static methods, or
instance methods on a live `Window` — which lets a model read and change what is actually on
screen.

```
dotnet add package WpfMcp
```

One reference brings the runtime library, the attributes, and the generator.

## Quick start

Write a tool:

```csharp
using System.ComponentModel;
using WpfMcp.Core;

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

Start the server:

```csharp
public partial class App : Application
{
    private McpServer? _server;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _server = new McpServer("http://127.0.0.1:9000/mcp");
        _server.Start();
    }

    protected override void OnExit(ExitEventArgs e) => _server?.Stop();
}
```

That's the whole setup. Nothing registers tools by hand: static collections register at module
load, window-bound collections register as the window initialises.

`[Description]` on the method and on each parameter becomes the description and JSON Schema
documentation an MCP client sees, so write them for the model.

## Tools bound to a window

Make the tools instance methods on a `partial` class and the generator registers the live
instance, so a tool can touch real UI. Tools run on a thread-pool thread, so marshal to the
dispatcher:

```csharp
[McpToolCollection]
public partial class MainWindow : Window
{
    [McpTool("set_editor_text")]
    [Description("Replaces all text in the editor shown in the application window")]
    public int SetEditorText([Description("The new editor contents")] string text)
    {
        return Dispatcher.Invoke(() =>
        {
            Editor.Text = text ?? string.Empty;
            return Editor.Text.Length;
        });
    }

    [McpTool("read_editor_text")]
    [Description("Reads the editor contents, including anything the user typed by hand")]
    public string ReadEditorText() => Dispatcher.Invoke(() => Editor.Text);
}
```

Because the model and the person at the keyboard write to the same control, `read_editor_text` is
an input channel rather than an echo of the model's own writes.

Anything on the window works the same way — this changes the real title bar:

```csharp
[McpTool("set_window_title")]
[Description("Changes the text shown in the application's title bar")]
public string SetWindowTitle([Description("The new window title")] string title)
{
    Dispatcher.Invoke(() => Title = title);
    return $"Window title is now: {title}";
}
```

## Async, progress and cancellation

Declare a `CancellationToken` or an `IMcpProgress` parameter and the framework supplies it. Neither
appears in the tool's JSON schema:

```csharp
[McpTool("count_slowly")]
[Description("Counts up to a number, reporting progress along the way")]
public static async Task<int> CountSlowly(
    [Description("How high to count")] int steps,
    IMcpProgress progress,
    CancellationToken cancellationToken)
{
    for (int i = 1; i <= steps; i++)
    {
        await Task.Delay(300, cancellationToken);
        await progress.ReportAsync(i, steps, $"Step {i} of {steps}");
    }

    return steps;
}
```

When a client sends `params._meta.progressToken`, the response streams as `text/event-stream`
carrying `notifications/progress` before the result. A client's `notifications/cancelled` cancels
the token for real. Reporting progress is safe even when nobody asked for it — it becomes a no-op.

## Watching what the model does

`McpServer` raises `ToolInvocationStarted`, `ToolProgressReported` and `ToolInvocationCompleted`,
correlated by a `Guid`, so an application can show live MCP activity without any logging code
inside the tools. Handlers run on thread-pool threads, so marshal before touching UI.

## Calling the server

```bash
curl -X POST http://127.0.0.1:9000/mcp -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'

curl -X POST http://127.0.0.1:9000/mcp -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"create_sum","arguments":{"a":23,"b":42}}}'
```

Tool names are **not** JSON-RPC methods — a tool is always invoked through `tools/call`, with its
name in `params.name` and its arguments in `params.arguments`.

## Try the sample

[`samples/WpfMcp.ExampleApp`](samples/WpfMcp.ExampleApp) is a WPF app that hosts the server and
shows every tool call as it happens — arguments, result, duration, progress and errors — next to
window state its own tools read and change, including an editor you and the model can both type
into.

```bash
dotnet run --project samples/WpfMcp.ExampleApp
```

## Requirements

| | Minimum | Notes |
|---|---|---|
| Your app | `net6.0-windows` | .NET 6, 7, 8 and 9 WPF apps all work. |
| SDK / Visual Studio | .NET SDK 6.0.400 / VS 2022 17.3 | Needed to load the generator. |
| Platform | Windows | It's WPF. |

## Protocol support

HTTP only — the stdio transport is deliberately not implemented.

Implements the Streamable HTTP transport of MCP `2025-06-18`: one endpoint serving `POST` for
JSON-RPC and `GET` for a server-to-client SSE stream, with `initialize` version negotiation,
`ping`, `tools/list` (paginated), `tools/call`, notification handling, per-request cancellation,
progress over SSE, and `notifications/tools/list_changed` when the tool set changes. `Origin` is
validated on every request to prevent DNS-rebinding attacks against the loopback server.

`tools/list` returns `McpServer.ToolPageSize` tools per page (50 by default) and a `nextCursor`
while more remain; pass it back as `params.cursor`, and treat a missing `nextCursor` as the end.

Not implemented: sessions (`Mcp-Session-Id`), OAuth authorization, and
`structuredContent` / `outputSchema`.

## Diagnostics

The generator reports mistakes at compile time rather than leaving a tool silently missing:

| ID | Severity | Meaning |
|---|---|---|
| MCP001 | Error | `[McpToolCollection]` type with instance tools isn't `partial` |
| MCP002 | Error | Parameter or return type isn't a supported primitive |
| MCP003 | Error | Two tools in one type share a name |
| MCP004 | Warning | `[McpTool]` method in a type without `[McpToolCollection]` |
| MCP005 | Warning | No automatic registration hook; call `RegisterMcpTools()` |

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## Contributing

Building the repo, packing and releasing are covered in [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

MIT — see [LICENSE.txt](LICENSE.txt).
