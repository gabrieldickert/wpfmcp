# Changelog

All notable changes to this project are documented here. This project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] — 2026-08-08

First release.

### Added

- **MCP server over HTTP.** Implements the Streamable HTTP transport of MCP `2025-06-18`: a single
  endpoint serving `POST` for JSON-RPC and `GET` for a server-to-client SSE stream, with
  `initialize` version negotiation, `ping`, `tools/list` and `tools/call`.
- **Source generator.** `[McpToolCollection]` on a class and `[McpTool]` on its methods generate the
  tool schema, the dispatch, and the registration. Nothing is wired up by hand.
- **Two shapes of tool collection.** Static collections register themselves at module load;
  instance collections on a `partial` WPF class register the live window, so tools can read and
  change what is on screen.
- **Async tools**, with `Task<T>` / `ValueTask<T>` unwrapped automatically.
- **Progress reporting.** Declare an `IMcpProgress` parameter; when a client sends
  `params._meta.progressToken` the response streams as `text/event-stream` carrying
  `notifications/progress` before the result.
- **Cancellation.** Declare a `CancellationToken` parameter; a client's `notifications/cancelled`
  cancels the running tool for real.
- **`notifications/tools/list_changed`**, delivered over the GET stream when the tool set changes.
- **Activity events.** `McpServer` raises `ToolInvocationStarted`, `ToolProgressReported` and
  `ToolInvocationCompleted` so a host application can display live MCP activity without any logging
  code inside the tools.
- **`tools/list` pagination.** Pages at `McpServer.ToolPageSize` (50 by default) and returns
  `nextCursor` while results remain. Cursors are opaque and encode a position by tool name rather
  than by index, so they stay stable while the tool set changes; a malformed cursor is rejected
  with `-32602`.
- **Compile-time diagnostics** MCP001–MCP005 for misuse of the attributes.
- **Origin validation** on every request, as the transport spec requires, to prevent DNS-rebinding
  attacks against the loopback server.

### Notes

- HTTP only. The stdio transport is deliberately not implemented.
- Targets `net6.0-windows`, so .NET 6, 7, 8 and 9 WPF applications can all consume the package.
- Not implemented: sessions (`Mcp-Session-Id`), OAuth authorization, and
  `structuredContent` / `outputSchema`.

[0.1.0]: https://github.com/gabrieldickert/wpfmcp/releases/tag/v0.1.0
