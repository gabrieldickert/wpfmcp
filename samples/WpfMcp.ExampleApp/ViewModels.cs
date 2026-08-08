using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfMcp.ExampleApp
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected void Raise([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>One row in the activity feed: a single tool call from start to finish.</summary>
    public sealed class ToolActivity : ObservableObject
    {
        private string _result = string.Empty;
        private string _duration = string.Empty;
        private bool _isRunning = true;
        private bool _isError;
        private double _progress;
        private double _progressMaximum;
        private string _progressMessage = string.Empty;

        public ToolActivity(Guid id, string toolName, string arguments)
        {
            Id = id;
            ToolName = toolName;
            Arguments = arguments;
            StartedAt = DateTime.Now.ToString("HH:mm:ss");
        }

        public Guid Id { get; }

        public string ToolName { get; }

        /// <summary>Formatted call arguments, or "()" when the tool takes none.</summary>
        public string Arguments { get; }

        public string StartedAt { get; }

        public string Result
        {
            get => _result;
            set => Set(ref _result, value);
        }

        public string Duration
        {
            get => _duration;
            set => Set(ref _duration, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set => Set(ref _isRunning, value);
        }

        public bool IsError
        {
            get => _isError;
            set => Set(ref _isError, value);
        }

        public double Progress
        {
            get => _progress;
            set => Set(ref _progress, value);
        }

        /// <summary>Zero while no total is known, which the UI shows as an indeterminate bar.</summary>
        public double ProgressMaximum
        {
            get => _progressMaximum;
            set
            {
                Set(ref _progressMaximum, value);
                Raise(nameof(HasDeterminateProgress));
            }
        }

        public bool HasDeterminateProgress => _progressMaximum > 0;

        public string ProgressMessage
        {
            get => _progressMessage;
            set => Set(ref _progressMessage, value);
        }
    }

    /// <summary>
    /// Everything the window displays: the live activity feed plus the application state that
    /// MCP tools are allowed to read and change.
    /// </summary>
    public sealed class MainViewModel : ObservableObject
    {
        private string _statusMessage = "Waiting for an MCP client…";
        private string _documentText = string.Empty;
        private int _callCount;
        private int _errorCount;

        public MainViewModel(string endpointUrl)
        {
            EndpointUrl = endpointUrl;
        }

        public string EndpointUrl { get; }

        /// <summary>Newest first, so the most recent call is always in view.</summary>
        public ObservableCollection<ToolActivity> Activities { get; } = new();

        /// <summary>Notes owned by the UI; tools can add to and clear this list.</summary>
        public ObservableCollection<string> Notes { get; } = new();

        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        }

        /// <summary>
        /// Contents of the editor. Bound two-way, so it holds whatever the user typed as well as
        /// whatever a tool wrote — which is what lets a model read human input back.
        /// </summary>
        public string DocumentText
        {
            get => _documentText;
            set => Set(ref _documentText, value);
        }

        public int CallCount
        {
            get => _callCount;
            set => Set(ref _callCount, value);
        }

        public int ErrorCount
        {
            get => _errorCount;
            set => Set(ref _errorCount, value);
        }

        public bool HasActivity => Activities.Count > 0;

        public bool HasNotes => Notes.Count > 0;

        public void RaiseCollectionCounts()
        {
            Raise(nameof(HasActivity));
            Raise(nameof(HasNotes));
        }
    }
}
