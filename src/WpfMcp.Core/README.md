# WpfMcp

Turn a WPF application into an [MCP](https://modelcontextprotocol.io) server. Mark a class and its
methods with two attributes, and a source generator exposes them as Model Context Protocol tools
over HTTP — including tools bound to a live window, so a model can read and change what is on screen.

## Install

```
dotnet add package WpfMcp
```

The package contains the runtime library, the attributes, and the source generator. One reference
is all you need.

## Write a tool

A static collection, for anything that doesn't touch the UI:

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

`[Description]` on the method and each parameter becomes the tool description and JSON Schema
documentation an MCP client sees.

## Start the server

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

Nothing registers tools by hand — static collections register themselves at module load, and
window-bound collections register as the window initialises.

## Tools bound to a window

Make the tools instance methods on a `partial` class and the generator registers the live
instance, so the tool can touch real UI state. Tools run on a thread-pool thread, so marshal to
the dispatcher:

```csharp
[McpToolCollection]
public partial class MainWindow : Window
{
    [McpTool("set_status")]
    [Description("Sets the status message shown in the application window")]
    public string SetStatus([Description("Text to display")] string message)
    {
        Dispatcher.Invoke(() => StatusText.Text = message);
        return $"Status is now: {message}";
    }
}
```

## Async, progress and cancellation

Declare a `CancellationToken` or an `IMcpProgress` parameter and the framework supplies it — these
never appear in the tool's JSON schema:

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
carrying `notifications/progress` followed by the result. A client's `notifications/cancelled`
cancels the token for real.

## Observing activity

`McpServer` raises `ToolInvocationStarted`, `ToolProgressReported` and `ToolInvocationCompleted`
(correlated by a `Guid`) so a host application can display live MCP activity without any logging
code inside the tools. Handlers run on thread-pool threads.

## Protocol support

HTTP only — the stdio transport is deliberately not implemented.

Implements the Streamable HTTP transport of MCP `2025-06-18`: a single endpoint serving `POST`
for JSON-RPC and `GET` for a server-to-client SSE stream, `initialize` version negotiation,
`ping`, `tools/list`, `tools/call`, notification handling, per-request cancellation, and
`notifications/tools/list_changed` when the tool set changes. `Origin` is validated on every
request to prevent DNS-rebinding attacks against the loopback server.

Not implemented: sessions (`Mcp-Session-Id`), `tools/list` pagination, OAuth authorization, and
`structuredContent` / `outputSchema`.

## Diagnostics

| ID | Severity | Meaning |
|---|---|---|
| MCP001 | Error | `[McpToolCollection]` type with instance tools isn't `partial` |
| MCP002 | Error | Parameter or return type isn't a supported primitive |
| MCP003 | Error | Two tools in one type share a name |
| MCP004 | Warning | `[McpTool]` method in a type without `[McpToolCollection]` |
| MCP005 | Warning | No automatic registration hook; call `RegisterMcpTools()` |

## Licence

MIT
