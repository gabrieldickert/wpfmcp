; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MCP001 | WpfMcp.Generators | Error | [McpToolCollection] type must be declared partial
MCP002 | WpfMcp.Generators | Error | Tool parameter or return type is not a supported primitive
MCP003 | WpfMcp.Generators | Error | Two [McpTool] methods in one type share a tool name
MCP004 | WpfMcp.Generators | Warning | [McpTool] method in a type not marked [McpToolCollection]
MCP005 | WpfMcp.Generators | Warning | Type needs a manual RegisterMcpTools() call
