using System.Text.Json.Nodes;

namespace WpfMcp.Core.Server
{
    internal static class JsonNodeExtensions
    {
        /// <summary>
        /// Detaches a copy of a node so it can be added to another parent.
        /// <para>
        /// A <see cref="JsonNode"/> may only have one parent, so a node taken from an incoming
        /// request cannot be placed straight into an outgoing one. .NET 8 added
        /// <c>JsonNode.DeepClone()</c> for this; round-tripping through JSON does the same job and
        /// works on every framework this library targets.
        /// </para>
        /// </summary>
        public static JsonNode? CloneNode(this JsonNode? node)
        {
            return node is null ? null : JsonNode.Parse(node.ToJsonString());
        }
    }
}
