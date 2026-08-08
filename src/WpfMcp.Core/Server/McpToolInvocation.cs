using System;
using System.Text.Json.Nodes;

namespace WpfMcp.Core.Server
{
    /// <summary>
    /// Raised when a tool call begins. <see cref="Id"/> correlates this with the progress reports
    /// and the completion for the same call.
    /// </summary>
    public sealed class McpToolInvocation
    {
        public McpToolInvocation(Guid id, string toolName, JsonObject? arguments)
        {
            Id = id;
            ToolName = toolName;
            Arguments = arguments;
            StartedAt = DateTimeOffset.Now;
        }

        public Guid Id { get; }

        public string ToolName { get; }

        public JsonObject? Arguments { get; }

        public DateTimeOffset StartedAt { get; }
    }

    /// <summary>A progress report from a running tool, correlated by <see cref="Id"/>.</summary>
    public sealed class McpToolProgressReport
    {
        public McpToolProgressReport(Guid id, string toolName, double progress, double? total, string? message)
        {
            Id = id;
            ToolName = toolName;
            Progress = progress;
            Total = total;
            Message = message;
        }

        public Guid Id { get; }

        public string ToolName { get; }

        public double Progress { get; }

        public double? Total { get; }

        public string? Message { get; }
    }

    /// <summary>
    /// Raised when a tool call finishes, successfully or otherwise. <see cref="Error"/> is null on
    /// success; <see cref="Result"/> is null when the tool failed or returned nothing.
    /// </summary>
    public sealed class McpToolCompletion
    {
        public McpToolCompletion(Guid id, string toolName, JsonNode? result, string? error, TimeSpan duration)
        {
            Id = id;
            ToolName = toolName;
            Result = result;
            Error = error;
            Duration = duration;
        }

        public Guid Id { get; }

        public string ToolName { get; }

        public JsonNode? Result { get; }

        public string? Error { get; }

        public TimeSpan Duration { get; }

        public bool IsError => Error is not null;
    }
}
