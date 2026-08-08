using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WpfMcp.Generators
{
    [Generator]
    public class McpToolGenerator : IIncrementalGenerator
    {
        private const string McpToolAttributeFullName = "WpfMcp.Core.McpToolAttribute";
        private const string McpToolCollectionAttributeFullName = "WpfMcp.Core.McpToolCollectionAttribute";
        private const string DescriptionAttributeFullName = "System.ComponentModel.DescriptionAttribute";
        private const string FrameworkElementFullName = "System.Windows.FrameworkElement";
        private const string CancellationTokenFullName = "System.Threading.CancellationToken";
        private const string CancellationTokenSourceFullName = "System.Threading.CancellationTokenSource";
        private const string McpProgressFullName = "WpfMcp.Core.Server.IMcpProgress";

        private static readonly DiagnosticDescriptor NotPartialDiagnostic = new(
            id: "MCP001",
            title: "MCP tool collection must be partial",
            messageFormat: "Type '{0}' is marked [McpToolCollection] but is not declared 'partial'; the generator adds the tool implementation to the class itself, which requires a partial class",
            category: "WpfMcp.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor UnsupportedTypeDiagnostic = new(
            id: "MCP002",
            title: "Unsupported MCP tool member type",
            messageFormat: "Member '{0}' on tool '{1}' has unsupported type '{2}'; only primitive types are supported",
            category: "WpfMcp.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor DuplicateToolNameDiagnostic = new(
            id: "MCP003",
            title: "Duplicate MCP tool name",
            messageFormat: "Tool name '{0}' is used by more than one [McpTool] method in type '{1}'",
            category: "WpfMcp.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor OrphanToolDiagnostic = new(
            id: "MCP004",
            title: "McpTool method outside a tool collection",
            messageFormat: "Method '{0}' is marked [McpTool] but its containing type '{1}' is not marked [McpToolCollection], so it will be ignored",
            category: "WpfMcp.Generators",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ManualRegistrationDiagnostic = new(
            id: "MCP005",
            title: "MCP tool collection needs a manual registration call",
            messageFormat: "Type '{0}' cannot be registered automatically; call RegisterMcpTools() from its constructor, or make its [McpTool] methods static",
            category: "WpfMcp.Generators",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var collections = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    McpToolCollectionAttributeFullName,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, _) => GetCollectionInfo(ctx))
                .Where(static c => c is not null)
                .Select(static (c, _) => c!);

            context.RegisterSourceOutput(collections.Collect(), static (spc, items) => Execute(items, spc));

            // Warn about [McpTool] methods whose containing type is not a tool collection.
            var orphans = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    McpToolAttributeFullName,
                    predicate: static (node, _) => node is MethodDeclarationSyntax,
                    transform: static (ctx, _) => GetOrphanDiagnostic(ctx))
                .Where(static d => d is not null)
                .Select(static (d, _) => d!);

            context.RegisterSourceOutput(orphans, static (spc, diagnostic) => spc.ReportDiagnostic(diagnostic));
        }

        private static Diagnostic? GetOrphanDiagnostic(GeneratorAttributeSyntaxContext ctx)
        {
            var method = (IMethodSymbol)ctx.TargetSymbol;
            var containingType = method.ContainingType;

            bool isCollection = containingType.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == McpToolCollectionAttributeFullName);

            if (isCollection)
            {
                return null;
            }

            return Diagnostic.Create(OrphanToolDiagnostic, method.Locations.FirstOrDefault(),
                method.Name, containingType.ToDisplayString());
        }

        private static ToolCollectionInfo? GetCollectionInfo(GeneratorAttributeSyntaxContext ctx)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            {
                return null;
            }

            var diagnostics = new List<Diagnostic>();
            var tools = new List<ToolMethodInfo>();

            foreach (var method in typeSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                var toolAttr = method.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString() == McpToolAttributeFullName);

                if (toolAttr is null)
                {
                    continue;
                }

                string toolName = toolAttr.ConstructorArguments.Length > 0 && toolAttr.ConstructorArguments[0].Value is string s
                    ? s
                    : method.Name;

                bool isValid = true;

                var returnInfo = AnalyzeReturnType(method.ReturnType);
                if (returnInfo is null)
                {
                    isValid = false;
                    diagnostics.Add(Diagnostic.Create(UnsupportedTypeDiagnostic, method.Locations.FirstOrDefault(),
                        "return value", toolName, method.ReturnType.ToDisplayString()));
                    returnInfo = new ReturnInfo(false, null);
                }

                var parameters = new List<ToolParameterInfo>();
                foreach (var p in method.Parameters)
                {
                    var injected = ClassifyInjectedParameter(p.Type);
                    if (injected != InjectedKind.None)
                    {
                        parameters.Add(ToolParameterInfo.Inject(p.Name, injected));
                        continue;
                    }

                    var schemaType = MapJsonSchemaType(p.Type);
                    if (schemaType is null)
                    {
                        isValid = false;
                        diagnostics.Add(Diagnostic.Create(UnsupportedTypeDiagnostic,
                            p.Locations.FirstOrDefault() ?? method.Locations.FirstOrDefault(),
                            p.Name, toolName, p.Type.ToDisplayString()));
                        continue;
                    }

                    parameters.Add(ToolParameterInfo.Value(
                        p.Name,
                        p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        schemaType,
                        GetDescription(p.GetAttributes()),
                        p.HasExplicitDefaultValue,
                        p.HasExplicitDefaultValue ? FormatDefaultValue(p.ExplicitDefaultValue) : null));
                }

                if (!isValid)
                {
                    continue;
                }

                tools.Add(new ToolMethodInfo(
                    method.Name,
                    toolName,
                    GetDescription(method.GetAttributes()),
                    returnInfo.IsAwaitable,
                    returnInfo.ResultSchemaType,
                    method.IsStatic,
                    parameters));
            }

            return new ToolCollectionInfo(typeSymbol, tools, DetermineRegistrationMode(typeSymbol, tools), diagnostics);
        }

        /// <summary>
        /// Picks how the generated code hooks itself into McpToolRegistry without any hand-written call.
        /// </summary>
        private static RegistrationMode DetermineRegistrationMode(INamedTypeSymbol type, List<ToolMethodInfo> tools)
        {
            // A collection whose tools are all static needs no instance at all, so it can be
            // registered once at module load through a generated companion adapter. This is also
            // the only option for a `static class`, which cannot implement an interface.
            if (tools.Count > 0 && tools.All(t => t.IsStatic))
            {
                return RegistrationMode.StaticAdapter;
            }

            if (type.IsStatic)
            {
                // Static class with instance tools is impossible, but a static class with no valid
                // tools can land here; nothing to generate.
                return RegistrationMode.StaticAdapter;
            }

            bool derivesFromFrameworkElement = false;
            for (var t = type.BaseType; t is not null; t = t.BaseType)
            {
                if (t.ToDisplayString() == FrameworkElementFullName)
                {
                    derivesFromFrameworkElement = true;
                    break;
                }
            }

            bool declaresOnInitialized = type
                .GetMembers("OnInitialized")
                .OfType<IMethodSymbol>()
                .Any();

            if (derivesFromFrameworkElement && !declaresOnInitialized)
            {
                return RegistrationMode.OnInitializedOverride;
            }

            bool declaresConstructor = type.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared);
            if (!declaresConstructor && !type.IsAbstract)
            {
                return RegistrationMode.GeneratedConstructor;
            }

            return RegistrationMode.Manual;
        }

        private static InjectedKind ClassifyInjectedParameter(ITypeSymbol type)
        {
            return type.ToDisplayString() switch
            {
                CancellationTokenFullName => InjectedKind.CancellationToken,
                CancellationTokenSourceFullName => InjectedKind.CancellationTokenSource,
                McpProgressFullName => InjectedKind.Progress,
                _ => InjectedKind.None,
            };
        }

        /// <summary>
        /// Unwraps Task/ValueTask so async tool methods are described by the value they produce.
        /// Returns null when the (unwrapped) type cannot be represented in JSON.
        /// </summary>
        private static ReturnInfo? AnalyzeReturnType(ITypeSymbol returnType)
        {
            if (returnType.SpecialType == SpecialType.System_Void)
            {
                return new ReturnInfo(false, null);
            }

            string name = returnType.OriginalDefinition.ToDisplayString();

            if (name == "System.Threading.Tasks.Task" || name == "System.Threading.Tasks.ValueTask")
            {
                return new ReturnInfo(true, null);
            }

            if (name == "System.Threading.Tasks.Task<TResult>" || name == "System.Threading.Tasks.ValueTask<TResult>")
            {
                if (returnType is not INamedTypeSymbol named || named.TypeArguments.Length != 1)
                {
                    return null;
                }

                var inner = MapJsonSchemaType(named.TypeArguments[0]);
                return inner is null ? null : new ReturnInfo(true, inner);
            }

            var schema = MapJsonSchemaType(returnType);
            return schema is null ? null : new ReturnInfo(false, schema);
        }

        private static string? GetDescription(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attr in attributes)
            {
                if (attr.AttributeClass?.ToDisplayString() == DescriptionAttributeFullName &&
                    attr.ConstructorArguments.Length > 0 &&
                    attr.ConstructorArguments[0].Value is string d)
                {
                    return d;
                }
            }
            return null;
        }

        private static void Execute(ImmutableArray<ToolCollectionInfo> collections, SourceProductionContext context)
        {
            if (collections.IsDefaultOrEmpty)
            {
                return;
            }

            var seen = new HashSet<string>();

            foreach (var collection in collections)
            {
                foreach (var diagnostic in collection.Diagnostics)
                {
                    context.ReportDiagnostic(diagnostic);
                }

                var type = collection.ContainingType;
                string key = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!seen.Add(key))
                {
                    continue;
                }

                if (collection.Tools.Count == 0)
                {
                    continue;
                }

                bool hasDuplicate = false;
                foreach (var dup in collection.Tools.GroupBy(t => t.ToolName).Where(g => g.Count() > 1))
                {
                    hasDuplicate = true;
                    context.ReportDiagnostic(Diagnostic.Create(DuplicateToolNameDiagnostic,
                        type.Locations.FirstOrDefault(), dup.Key, type.Name));
                }
                if (hasDuplicate)
                {
                    continue;
                }

                string source;
                string hintName = key.Replace("global::", "").Replace(".", "_") + ".McpTools.g.cs";

                if (collection.RegistrationMode == RegistrationMode.StaticAdapter)
                {
                    // A companion type is generated alongside the class, so the class itself is
                    // never modified and does not need to be partial.
                    source = GenerateStaticAdapterSource(type, collection.Tools);
                }
                else
                {
                    // The implementation is added to the class itself, which requires partial.
                    if (!IsPartialAllTheWayUp(type, out var offendingType))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(NotPartialDiagnostic,
                            type.Locations.FirstOrDefault(), offendingType!.Name));
                        continue;
                    }

                    if (collection.RegistrationMode == RegistrationMode.Manual)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(ManualRegistrationDiagnostic,
                            type.Locations.FirstOrDefault(), type.Name));
                    }

                    source = GenerateInPlaceSource(type, collection.Tools, collection.RegistrationMode);
                }

                context.AddSource(hintName, source);
            }
        }

        private static bool IsPartialAllTheWayUp(INamedTypeSymbol type, out INamedTypeSymbol? offendingType)
        {
            for (var t = type; t is not null; t = t.ContainingType)
            {
                bool isPartial = t.DeclaringSyntaxReferences.Any(r =>
                    r.GetSyntax() is ClassDeclarationSyntax cds && cds.Modifiers.Any(SyntaxKind.PartialKeyword));

                if (!isPartial)
                {
                    offendingType = t;
                    return false;
                }
            }
            offendingType = null;
            return true;
        }

        private static string? MapJsonSchemaType(ITypeSymbol type)
        {
            return type.SpecialType switch
            {
                SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64
                    or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64
                    or SpecialType.System_Byte or SpecialType.System_SByte => "integer",
                SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal => "number",
                SpecialType.System_Boolean => "boolean",
                SpecialType.System_String => "string",
                _ => null,
            };
        }

        private static string FormatDefaultValue(object? value)
        {
            switch (value)
            {
                case null:
                    return "default";
                case string s:
                    return SyntaxFactory.Literal(s).ToString();
                case bool b:
                    return b ? "true" : "false";
                case float f:
                    return f.ToString("R", CultureInfo.InvariantCulture) + "f";
                case double d:
                    return d.ToString("R", CultureInfo.InvariantCulture) + "d";
                case decimal m:
                    return m.ToString(CultureInfo.InvariantCulture) + "m";
                default:
                    return System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "default";
            }
        }

        private static string QuoteLiteral(string s) => SyntaxFactory.Literal(s).ToString();

        /// <summary>
        /// Generates the IMcpTool implementation directly into the (partial) tool class.
        /// </summary>
        private static string GenerateInPlaceSource(INamedTypeSymbol containingType, List<ToolMethodInfo> tools, RegistrationMode mode)
        {
            var sb = new StringBuilder();
            AppendHeader(sb);

            string ns = GetNamespace(containingType);
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
            }

            var chain = new List<INamedTypeSymbol>();
            for (var t = containingType; t is not null; t = t.ContainingType)
            {
                chain.Insert(0, t);
            }

            for (int i = 0; i < chain.Count; i++)
            {
                bool isLast = i == chain.Count - 1;
                sb.AppendLine(isLast
                    ? $"partial class {chain[i].Name} : global::WpfMcp.Core.Server.IMcpTool"
                    : $"partial class {chain[i].Name}");
                sb.AppendLine("{");
            }

            switch (mode)
            {
                case RegistrationMode.OnInitializedOverride:
                    sb.AppendLine("    protected override void OnInitialized(global::System.EventArgs e)");
                    sb.AppendLine("    {");
                    sb.AppendLine("        base.OnInitialized(e);");
                    sb.AppendLine("        RegisterMcpTools();");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                    break;

                case RegistrationMode.GeneratedConstructor:
                    sb.AppendLine($"    public {containingType.Name}()");
                    sb.AppendLine("    {");
                    sb.AppendLine("        RegisterMcpTools();");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                    break;
            }

            sb.AppendLine("    /// <summary>Registers this instance's [McpTool] methods with the McpToolRegistry.</summary>");
            sb.AppendLine("    public void RegisterMcpTools()");
            sb.AppendLine("    {");
            sb.AppendLine("        global::WpfMcp.Core.Server.McpToolRegistry.Register(this);");
            sb.AppendLine("    }");
            sb.AppendLine();

            AppendGetToolDefinitions(sb, tools, "    ");
            sb.AppendLine();
            AppendInvokeTool(sb, tools, "    ", targetPrefix: "this.", staticTypeName: null);

            for (int i = 0; i < chain.Count; i++)
            {
                sb.AppendLine("}");
            }

            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generates a companion adapter for a collection whose tools are all static. The tool class
        /// itself is untouched (so it may be a `static class`), and a module initializer registers a
        /// strongly rooted singleton so the tools are available without anyone creating an instance.
        /// </summary>
        private static string GenerateStaticAdapterSource(INamedTypeSymbol containingType, List<ToolMethodInfo> tools)
        {
            var sb = new StringBuilder();
            AppendHeader(sb);

            string ns = GetNamespace(containingType);
            string flatName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", "").Replace(".", "_");
            string adapterName = $"__{flatName}_McpAdapter";
            string registrationName = $"__{flatName}_McpRegistration";
            string staticTypeName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
            }

            sb.AppendLine($"internal sealed class {adapterName} : global::WpfMcp.Core.Server.IMcpTool");
            sb.AppendLine("{");
            AppendGetToolDefinitions(sb, tools, "    ");
            sb.AppendLine();
            AppendInvokeTool(sb, tools, "    ", targetPrefix: null, staticTypeName: staticTypeName);
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine($"internal static class {registrationName}");
            sb.AppendLine("{");
            sb.AppendLine($"    // Static field keeps the adapter alive; McpToolRegistry holds only weak references.");
            sb.AppendLine($"    private static readonly {adapterName} __instance = new {adapterName}();");
            sb.AppendLine();
            sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            sb.AppendLine("    internal static void Initialize()");
            sb.AppendLine("    {");
            sb.AppendLine("        global::WpfMcp.Core.Server.McpToolRegistry.Register(__instance);");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static void AppendHeader(StringBuilder sb)
        {
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
        }

        private static string GetNamespace(INamedTypeSymbol type)
        {
            return type.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? ns.ToDisplayString()
                : string.Empty;
        }

        private static void AppendGetToolDefinitions(StringBuilder sb, List<ToolMethodInfo> tools, string indent)
        {
            sb.AppendLine($"{indent}global::System.Text.Json.Nodes.JsonArray global::WpfMcp.Core.Server.IMcpTool.GetToolDefinitions()");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    var __tools = new global::System.Text.Json.Nodes.JsonArray();");

            foreach (var tool in tools)
            {
                var schemaParams = tool.Parameters.Where(p => p.Injected == InjectedKind.None).ToList();

                sb.AppendLine($"{indent}    __tools.Add(new global::System.Text.Json.Nodes.JsonObject");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        [\"name\"] = {QuoteLiteral(tool.ToolName)},");
                sb.AppendLine($"{indent}        [\"description\"] = {QuoteLiteral(tool.Description ?? string.Empty)},");
                sb.AppendLine($"{indent}        [\"inputSchema\"] = new global::System.Text.Json.Nodes.JsonObject");
                sb.AppendLine($"{indent}        {{");
                sb.AppendLine($"{indent}            [\"type\"] = \"object\",");
                sb.AppendLine($"{indent}            [\"properties\"] = new global::System.Text.Json.Nodes.JsonObject");
                sb.AppendLine($"{indent}            {{");
                foreach (var p in schemaParams)
                {
                    string descPart = p.Description is null ? "" : $", [\"description\"] = {QuoteLiteral(p.Description)}";
                    sb.AppendLine($"{indent}                [{QuoteLiteral(p.Name)}] = new global::System.Text.Json.Nodes.JsonObject {{ [\"type\"] = \"{p.SchemaType}\"{descPart} }},");
                }
                sb.AppendLine($"{indent}            }},");
                string requiredItems = string.Join(", ", schemaParams.Where(p => !p.HasDefault).Select(p => QuoteLiteral(p.Name)));
                sb.AppendLine($"{indent}            [\"required\"] = new global::System.Text.Json.Nodes.JsonArray {{ {requiredItems} }}");
                sb.AppendLine($"{indent}        }}");
                sb.AppendLine($"{indent}    }});");
            }

            sb.AppendLine($"{indent}    return __tools;");
            sb.AppendLine($"{indent}}}");
        }

        private static void AppendInvokeTool(StringBuilder sb, List<ToolMethodInfo> tools, string indent,
            string? targetPrefix, string? staticTypeName)
        {
            sb.AppendLine($"{indent}async global::System.Threading.Tasks.Task<global::System.Text.Json.Nodes.JsonNode?> global::WpfMcp.Core.Server.IMcpTool.InvokeToolAsync(string name, global::System.Text.Json.Nodes.JsonObject? arguments, global::WpfMcp.Core.Server.IMcpProgress progress, global::System.Threading.CancellationToken cancellationToken)");
            sb.AppendLine($"{indent}{{");
            // Guarantees at least one await so the method compiles without CS1998 when no tool is async.
            sb.AppendLine($"{indent}    await global::System.Threading.Tasks.Task.CompletedTask;");
            sb.AppendLine($"{indent}    switch (name)");
            sb.AppendLine($"{indent}    {{");

            foreach (var tool in tools)
            {
                sb.AppendLine($"{indent}        case {QuoteLiteral(tool.ToolName)}:");
                sb.AppendLine($"{indent}        {{");

                var argExpressions = new List<string>();
                int valueIndex = 0;
                int ctsIndex = 0;

                foreach (var p in tool.Parameters)
                {
                    switch (p.Injected)
                    {
                        case InjectedKind.CancellationToken:
                            argExpressions.Add("cancellationToken");
                            break;

                        case InjectedKind.CancellationTokenSource:
                            string ctsVar = $"__cts{ctsIndex++}";
                            sb.AppendLine($"{indent}            using var {ctsVar} = global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);");
                            argExpressions.Add(ctsVar);
                            break;

                        case InjectedKind.Progress:
                            argExpressions.Add("progress");
                            break;

                        default:
                            string argVar = $"__arg{valueIndex}";
                            string valueVar = $"__v{valueIndex}";
                            valueIndex++;
                            // default! keeps a missing reference-typed argument from tripping
                            // nullable warnings in the consuming project.
                            string defaultExpr = p.HasDefault ? p.DefaultValueLiteral! : "default!";
                            sb.AppendLine($"{indent}            {p.TypeName} {argVar} = arguments is not null && arguments.TryGetPropertyValue({QuoteLiteral(p.Name)}, out var {valueVar}) && {valueVar} is not null ? {valueVar}.GetValue<{p.TypeName}>() : {defaultExpr};");
                            argExpressions.Add(argVar);
                            break;
                    }
                }

                string target = tool.IsStatic
                    ? $"{staticTypeName ?? "this"}.{tool.MethodName}"
                    : $"{targetPrefix}{tool.MethodName}";

                string call = $"{target}({string.Join(", ", argExpressions)})";
                string awaited = tool.IsAwaitable ? $"await {call}" : call;

                if (tool.ResultSchemaType is null)
                {
                    sb.AppendLine($"{indent}            {awaited};");
                    sb.AppendLine($"{indent}            return null;");
                }
                else
                {
                    sb.AppendLine($"{indent}            var __result = {awaited};");
                    sb.AppendLine($"{indent}            return global::System.Text.Json.Nodes.JsonValue.Create(__result);");
                }

                sb.AppendLine($"{indent}        }}");
            }

            sb.AppendLine($"{indent}        default:");
            sb.AppendLine($"{indent}            return null;");
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}}}");
        }

        private enum RegistrationMode
        {
            /// <summary>WPF element: hook the FrameworkElement initialization callback.</summary>
            OnInitializedOverride,

            /// <summary>Plain class with no hand-written constructor: generate one that registers.</summary>
            GeneratedConstructor,

            /// <summary>All tools are static: register a companion singleton at module load.</summary>
            StaticAdapter,

            /// <summary>No safe automatic hook available; the user must call RegisterMcpTools().</summary>
            Manual,
        }

        private enum InjectedKind
        {
            None,
            CancellationToken,
            CancellationTokenSource,
            Progress,
        }

        private sealed class ReturnInfo
        {
            public ReturnInfo(bool isAwaitable, string? resultSchemaType)
            {
                IsAwaitable = isAwaitable;
                ResultSchemaType = resultSchemaType;
            }

            public bool IsAwaitable { get; }
            public string? ResultSchemaType { get; }
        }

        private sealed class ToolCollectionInfo
        {
            public ToolCollectionInfo(INamedTypeSymbol containingType, List<ToolMethodInfo> tools,
                RegistrationMode registrationMode, List<Diagnostic> diagnostics)
            {
                ContainingType = containingType;
                Tools = tools;
                RegistrationMode = registrationMode;
                Diagnostics = diagnostics;
            }

            public INamedTypeSymbol ContainingType { get; }
            public List<ToolMethodInfo> Tools { get; }
            public RegistrationMode RegistrationMode { get; }
            public List<Diagnostic> Diagnostics { get; }
        }

        private sealed class ToolMethodInfo
        {
            public ToolMethodInfo(string methodName, string toolName, string? description,
                bool isAwaitable, string? resultSchemaType, bool isStatic, List<ToolParameterInfo> parameters)
            {
                MethodName = methodName;
                ToolName = toolName;
                Description = description;
                IsAwaitable = isAwaitable;
                ResultSchemaType = resultSchemaType;
                IsStatic = isStatic;
                Parameters = parameters;
            }

            public string MethodName { get; }
            public string ToolName { get; }
            public string? Description { get; }
            public bool IsAwaitable { get; }
            public string? ResultSchemaType { get; }
            public bool IsStatic { get; }
            public List<ToolParameterInfo> Parameters { get; }
        }

        private sealed class ToolParameterInfo
        {
            private ToolParameterInfo(string name, string typeName, string schemaType, string? description,
                bool hasDefault, string? defaultValueLiteral, InjectedKind injected)
            {
                Name = name;
                TypeName = typeName;
                SchemaType = schemaType;
                Description = description;
                HasDefault = hasDefault;
                DefaultValueLiteral = defaultValueLiteral;
                Injected = injected;
            }

            public static ToolParameterInfo Value(string name, string typeName, string schemaType,
                string? description, bool hasDefault, string? defaultValueLiteral)
                => new(name, typeName, schemaType, description, hasDefault, defaultValueLiteral, InjectedKind.None);

            public static ToolParameterInfo Inject(string name, InjectedKind kind)
                => new(name, string.Empty, string.Empty, null, false, null, kind);

            public string Name { get; }
            public string TypeName { get; }
            public string SchemaType { get; }
            public string? Description { get; }
            public bool HasDefault { get; }
            public string? DefaultValueLiteral { get; }
            public InjectedKind Injected { get; }
        }
    }
}
