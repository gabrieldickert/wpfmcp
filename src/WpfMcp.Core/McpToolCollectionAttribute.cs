using System;

namespace WpfMcp.Core
{
    /// <summary>
    /// Marks a class as a collection of MCP tools. The source generator scans types carrying this
    /// attribute for <see cref="McpToolAttribute"/> methods and generates the IMcpTool
    /// implementation plus automatic registration into the McpToolRegistry.
    /// <para>
    /// A collection with instance tools must be declared <c>partial</c>: the generator adds the
    /// implementation to the class itself, which C# only allows across partial declarations.
    /// Marking a non-partial class reports MCP001. A collection whose tools are all <c>static</c>
    /// is never modified in place, so it needs no <c>partial</c> and may be a <c>static class</c>.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class McpToolCollectionAttribute : Attribute
    {
    }
}
