using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using WpfMcp.Core;
using WpfMcp.Core.Server;

namespace WpfMcp.ExampleApp
{
    /// <summary>
    /// Hosts the MCP activity view and exposes a few tools of its own.
    /// <para>
    /// This is an <i>instance</i> tool collection: the methods below are instance methods, so the
    /// generator adds the implementation to this class and registers the live window via
    /// OnInitialized. That is what lets an MCP client read and change what is on screen.
    /// </para>
    /// </summary>
    [McpToolCollection]
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _model;

        public MainWindow()
        {
            InitializeComponent();

            _model = new MainViewModel(App.EndpointUrl);
            DataContext = _model;

            var app = (App)Application.Current;

            if (app.Server is { } server)
            {
                server.ToolInvocationStarted += OnToolStarted;
                server.ToolProgressReported += OnToolProgress;
                server.ToolInvocationCompleted += OnToolCompleted;
            }
            else
            {
                _model.StatusMessage = app.ServerError ?? "The MCP server is not running.";
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            var server = ((App)Application.Current).Server;
            if (server is not null)
            {
                server.ToolInvocationStarted -= OnToolStarted;
                server.ToolProgressReported -= OnToolProgress;
                server.ToolInvocationCompleted -= OnToolCompleted;
            }

            base.OnClosing(e);
        }

        // ---------------------------------------------------------------------------------------
        // Activity feed. Server events arrive on thread-pool threads, so every handler hops to the
        // dispatcher before touching the view model.
        // ---------------------------------------------------------------------------------------

        private void OnToolStarted(McpToolInvocation invocation)
        {
            Dispatcher.Invoke(() =>
            {
                _model.Activities.Insert(0, new ToolActivity(
                    invocation.Id,
                    invocation.ToolName,
                    FormatArguments(invocation.Arguments)));

                _model.CallCount++;
                _model.RaiseCollectionCounts();
            });
        }

        private void OnToolProgress(McpToolProgressReport report)
        {
            Dispatcher.Invoke(() =>
            {
                var activity = Find(report.Id);
                if (activity is null)
                {
                    return;
                }

                activity.Progress = report.Progress;
                activity.ProgressMaximum = report.Total ?? 0;
                activity.ProgressMessage = report.Message ?? string.Empty;
            });
        }

        private void OnToolCompleted(McpToolCompletion completion)
        {
            Dispatcher.Invoke(() =>
            {
                var activity = Find(completion.Id);
                if (activity is null)
                {
                    return;
                }

                activity.IsRunning = false;
                activity.IsError = completion.IsError;
                activity.Result = completion.Error ?? FormatResult(completion.Result);
                activity.Duration = FormatDuration(completion.Duration);

                if (completion.IsError)
                {
                    _model.ErrorCount++;
                }
            });
        }

        private ToolActivity? Find(Guid id) => _model.Activities.FirstOrDefault(a => a.Id == id);

        private static string FormatArguments(JsonObject? arguments)
        {
            if (arguments is null || arguments.Count == 0)
            {
                return "()";
            }

            var parts = arguments.Select(pair => $"{pair.Key}: {pair.Value?.ToJsonString() ?? "null"}");
            return $"({string.Join(", ", parts)})";
        }

        private static string FormatResult(JsonNode? result)
        {
            if (result is null)
            {
                return "(no result)";
            }

            return result is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : result.ToJsonString();
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalMilliseconds < 1000
                ? $"{duration.TotalMilliseconds:F0} ms"
                : $"{duration.TotalSeconds:F1} s";
        }

        // ---------------------------------------------------------------------------------------
        // Tools. These run on a thread-pool thread, so each one marshals to the dispatcher before
        // touching UI state — the one rule to remember when writing tools on a Window.
        // ---------------------------------------------------------------------------------------

        [McpTool("set_status")]
        [Description("Sets the status message shown in the application window")]
        public string SetStatus([Description("Text to display as the window status")] string message)
        {
            Dispatcher.Invoke(() => _model.StatusMessage = message);
            return $"Status is now: {message}";
        }

        [McpTool("add_note")]
        [Description("Adds a note to the list shown in the application window")]
        public int AddNote([Description("Text of the note to add")] string text)
        {
            return Dispatcher.Invoke(() =>
            {
                _model.Notes.Add(text);
                _model.RaiseCollectionCounts();
                return _model.Notes.Count;
            });
        }

        [McpTool("clear_notes")]
        [Description("Removes every note from the application window")]
        public int ClearNotes()
        {
            return Dispatcher.Invoke(() =>
            {
                var removed = _model.Notes.Count;
                _model.Notes.Clear();
                _model.RaiseCollectionCounts();
                return removed;
            });
        }

        [McpTool("read_notes")]
        [Description("Reads back the notes currently displayed in the application window")]
        public string ReadNotes()
        {
            return Dispatcher.Invoke(() => _model.Notes.Count == 0
                ? "There are no notes."
                : string.Join("; ", _model.Notes));
        }

        // ---------------------------------------------------------------------------------------
        // Editing a real control. The editor is a two-way bound TextBox, so these tools and the
        // person at the keyboard write to the same place: the model can type into the window, and
        // read back whatever the human typed.
        // ---------------------------------------------------------------------------------------

        [McpTool("set_editor_text")]
        [Description("Replaces all text in the editor shown in the application window")]
        public int SetEditorText([Description("The new editor contents")] string text)
        {
            return Dispatcher.Invoke(() =>
            {
                _model.DocumentText = text ?? string.Empty;
                return _model.DocumentText.Length;
            });
        }

        [McpTool("append_editor_text")]
        [Description("Appends text to the end of the editor, leaving existing content in place")]
        public int AppendEditorText([Description("Text to append")] string text)
        {
            return Dispatcher.Invoke(() =>
            {
                _model.DocumentText += text;
                return _model.DocumentText.Length;
            });
        }

        [McpTool("read_editor_text")]
        [Description("Reads the editor contents, including anything the user typed by hand")]
        public string ReadEditorText()
        {
            return Dispatcher.Invoke(() => _model.DocumentText.Length == 0
                ? "The editor is empty."
                : _model.DocumentText);
        }

        [McpTool("clear_editor")]
        [Description("Empties the editor in the application window")]
        public int ClearEditor()
        {
            return Dispatcher.Invoke(() =>
            {
                var cleared = _model.DocumentText.Length;
                _model.DocumentText = string.Empty;
                return cleared;
            });
        }

        [McpTool("set_window_title")]
        [Description("Changes the text shown in the application's title bar")]
        public string SetWindowTitle([Description("The new window title")] string title)
        {
            // Not view-model state — this writes straight to the Window's own property.
            Dispatcher.Invoke(() => Title = title);
            return $"Window title is now: {title}";
        }
    }
}
