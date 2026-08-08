using System;

namespace WpfMcp.Core
{
    /// <summary>
    /// Marks a method as an MCP tool. The containing type must be marked
    /// <see cref="McpToolCollectionAttribute"/>, or the method is ignored (MCP004).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class McpToolAttribute : Attribute
    {
        /// <param name="methodname">The tool name exposed to MCP clients.</param>
        public McpToolAttribute(string methodname)
        {
            MethodName = methodname;
        }

        public string MethodName { get; }
    }
}
