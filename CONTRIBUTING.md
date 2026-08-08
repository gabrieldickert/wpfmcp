# Contributing

Notes for working on WpfMcp itself. If you only want to *use* the package, the
[README](README.md) is the place to look.

## Repository layout

| Path | Contents |
|---|---|
| `src/WpfMcp.Core` | Runtime library, the MCP server, and the attributes. This project is the NuGet package. |
| `src/WpfMcp.Generators` | The Roslyn source generator, shipped inside the package as an analyzer. |
| `samples/WpfMcp.ExampleApp` | A WPF app that hosts the server and displays live tool activity. |

## Build and run

```bash
dotnet build WpfMcp.sln
dotnet run --project samples/WpfMcp.ExampleApp
```

To inspect what the generator produced, build with `-p:EmitCompilerGeneratedFiles=true` and look
under `obj/…/generated/`. The sample project has this enabled already.

## Compatibility floors

Both are deliberately low. Raising either drops consumers, so change them only with a reason.

- **`net6.0-windows`** for `WpfMcp.Core`, so .NET 6 through 9 WPF apps can all consume the package.
  `net6.0` is the hard floor: `System.Text.Json`'s `JsonNode` arrived there. This rules out .NET 8
  APIs — `JsonNode.DeepClone()` is why `JsonNodeExtensions.CloneNode()` exists.
- **Roslyn 4.3.1** in the generator, the version that introduced `ForAttributeWithMetadataName`.
  A newer reference will not load on an older toolchain, so this sets the SDK 6.0.400 / VS 17.3
  build requirement.

## Packaging

```bash
dotnet pack src/WpfMcp.Core -c Release
```

Produces `WpfMcp.<version>.nupkg` plus a `.snupkg` of symbols. The package must contain:

```
lib/net6.0-windows7.0/WpfMcp.Core.dll   runtime library and attributes
analyzers/dotnet/cs/WpfMcp.Generators.dll   the generator — never under lib/
```

`PrivateAssets="all"` on the Roslyn references is load-bearing: without it every consumer inherits
a `Microsoft.CodeAnalysis` dependency. The shipped nuspec should declare **no** dependencies.

To verify a package properly, install it rather than trusting `pack`: copy the `.nupkg` to a local
folder feed, `dotnet new wpf`, `dotnet add package WpfMcp --source <feed>`, build with
`-p:EmitCompilerGeneratedFiles=true`, confirm a `*.McpTools.g.cs` appears, then run it and call
`tools/list`.

> If you did not bump the version, delete `~/.nuget/packages/wpfmcp/<version>` first. NuGet prefers
> the cached copy over your local feed, so otherwise you test the previous build without noticing.

## Releasing

1. Bump `<Version>` in `src/WpfMcp.Core/WpfMcp.Core.csproj` and add a `CHANGELOG.md` entry.
2. Build the artefacts deterministically:

   ```bash
   dotnet pack src/WpfMcp.Core -c Release -p:ContinuousIntegrationBuild=true
   ```

3. Push (nuget.org picks up the `.snupkg` alongside the `.nupkg`):

   ```bash
   dotnet nuget push src/WpfMcp.Core/bin/Release/WpfMcp.<version>.nupkg \
     --source https://api.nuget.org/v3/index.json --api-key <YOUR_KEY>
   ```

4. Tag the commit: `git tag v<version> && git push origin v<version>`.

A published version number can never be reused, even after unlisting.

## Protocol changes

Check behaviour against the spec at <https://modelcontextprotocol.io/specification/> rather than
from memory — the transport, lifecycle, pagination and progress pages each pin down details that
are easy to get subtly wrong.

If you add a diagnostic, add it to `AnalyzerReleases.Unshipped.md` too, or RS2008 fails the build.
