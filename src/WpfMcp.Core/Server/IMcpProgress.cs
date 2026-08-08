using System.Threading.Tasks;

namespace WpfMcp.Core.Server
{
    /// <summary>
    /// Reports progress for a long-running tool call. Declare a parameter of this type on an
    /// [McpTool] method and the generator supplies it — it never appears in the tool's JSON schema.
    /// <para>
    /// Reports are delivered as MCP notifications/progress messages, and only reach the client when
    /// it asked for them by sending params._meta.progressToken. Otherwise reporting is a no-op, so
    /// a tool can always report without checking.
    /// </para>
    /// </summary>
    public interface IMcpProgress
    {
        /// <param name="progress">
        /// Work done so far. Per the MCP spec this must increase with each report; a report that
        /// does not advance past the previous one is dropped rather than sent.
        /// </param>
        /// <param name="total">Optional total, when known.</param>
        /// <param name="message">Optional human-readable status.</param>
        Task ReportAsync(double progress, double? total = null, string? message = null);
    }

    /// <summary>
    /// Used when the client did not request progress. Discards every report.
    /// </summary>
    public sealed class NullMcpProgress : IMcpProgress
    {
        public static readonly NullMcpProgress Instance = new();

        private NullMcpProgress()
        {
        }

        public Task ReportAsync(double progress, double? total = null, string? message = null)
        {
            return Task.CompletedTask;
        }
    }
}
