using System;
using System.Collections.Generic;

namespace WpfMcp.Core.Server
{
    /// <summary>
    /// Global registry that generated [McpToolCollection] classes register themselves into during
    /// construction. Entries are held weakly, so a closed Window or discarded view model drops out
    /// on its own without an explicit unregister call.
    /// </summary>
    public static class McpToolRegistry
    {
        private static readonly object _lock = new();
        private static readonly List<WeakReference<IMcpTool>> _tools = new();

        /// <summary>
        /// Raised when the set of registered tools changes, so a running server can emit
        /// notifications/tools/list_changed. Raised outside the registry lock.
        /// </summary>
        public static event Action? ToolsChanged;

        public static void Register(IMcpTool tool)
        {
            bool added = false;

            lock (_lock)
            {
                Prune();

                bool alreadyPresent = false;
                foreach (var entry in _tools)
                {
                    if (entry.TryGetTarget(out var existing) && ReferenceEquals(existing, tool))
                    {
                        alreadyPresent = true;
                        break;
                    }
                }

                if (!alreadyPresent)
                {
                    _tools.Add(new WeakReference<IMcpTool>(tool));
                    added = true;
                }
            }

            if (added)
            {
                ToolsChanged?.Invoke();
            }
        }

        public static void Unregister(IMcpTool tool)
        {
            int removed;

            lock (_lock)
            {
                removed = _tools.RemoveAll(entry => !entry.TryGetTarget(out var existing) || ReferenceEquals(existing, tool));
            }

            if (removed > 0)
            {
                ToolsChanged?.Invoke();
            }
        }

        public static IReadOnlyList<IMcpTool> Tools
        {
            get
            {
                lock (_lock)
                {
                    var alive = new List<IMcpTool>(_tools.Count);
                    foreach (var entry in _tools)
                    {
                        if (entry.TryGetTarget(out var tool))
                        {
                            alive.Add(tool);
                        }
                    }
                    return alive;
                }
            }
        }

        private static void Prune()
        {
            _tools.RemoveAll(entry => !entry.TryGetTarget(out _));
        }
    }
}
