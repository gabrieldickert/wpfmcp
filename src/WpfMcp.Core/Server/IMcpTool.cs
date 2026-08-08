using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace WpfMcp.Core.Server
{
    /// <summary>
    /// Implemented by generated code for types that declare [McpTool] methods.
    /// </summary>
    public interface IMcpTool
    {
        JsonArray GetToolDefinitions();

        Task<JsonNode?> InvokeToolAsync(string name, JsonObject? arguments, IMcpProgress progress, CancellationToken cancellationToken);
    }
}
