using System.Net;
using System.Windows;
using WpfMcp.Core.Server;

namespace WpfMcp.ExampleApp
{
    /// <summary>
    /// Owns the MCP server for the lifetime of the application. Windows are tool collections and
    /// activity views; none of them contain server plumbing.
    /// </summary>
    public partial class App : Application
    {
        public const string EndpointUrl = "http://127.0.0.1:9000/mcp";

        /// <summary>The running server, or null if it could not be started.</summary>
        public McpServer? Server { get; private set; }

        /// <summary>Why the server failed to start, if it did. Shown by the main window.</summary>
        public string? ServerError { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var server = new McpServer(EndpointUrl, serverName: "WpfMcp.ExampleApp");

            try
            {
                // Binding is synchronous, so a port conflict is caught here rather than surfacing
                // later as an unhandled exception on a background thread.
                server.Start();
                Server = server;
            }
            catch (HttpListenerException ex)
            {
                ServerError = $"Could not listen on {EndpointUrl} — {ex.Message}";
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Server?.Stop();
            Server = null;

            base.OnExit(e);
        }
    }
}
