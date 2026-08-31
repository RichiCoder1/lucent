# Lucent M7 compiler/SDK research

Research basis: Microsoft Learn and the official dotnet/roslyn, dotnet/razor, and
dotnet/sdk repositories (accessed 2026-08-30). Facts and recommendations are
deliberately separated. This is a read-only research artifact; no repository
files were changed.

## Executive recommendation

Use four small deliverables, with one source of truth for language semantics:

```text
src/Lucent.Lui.Compiler/          # parser, binder, model, C# emitter, mapping
src/Lucent.Lui.Generator/         # thin IIncrementalGenerator adapter
src/Lucent.Lui.Sdk/               # NuGet MSBuild SDK: Sdk.props/targets
src/Lucent.Lui.Editor/            # later: project/document semantics + LSP
tests/Lucent.Lui.Compiler.Tests/
tests/Lucent.Lui.Generator.Tests/
```

The compiler library should be ordinary, host-independent .NET code. The
generator should read `AdditionalTextsProvider` items ending in `.lui`, pass
immutable parsed results to the library, and emit C# only with `AddSource`.
The SDK should supply the default `.lui` glob and generator/package wiring;
the editor should eventually consume the same project model and parser, not
reverse-engineer generated C#.

Do not make the generator a file-writing compiler. Roslyn owns generated source
lifetime. If disk artifacts are needed for debugging, use the compiler's
`EmitCompilerGeneratedFiles` facility or an explicitly owned `obj` target, not
the source tree.

## Facts from primary sources

1. **Roslyn input/output boundary (severity: architectural).** An incremental
generator implements `IIncrementalGenerator`; the old `ISourceGenerator` API is
legacy/obsolete in current API documentation. `AdditionalTextsProvider` is an
incremental provider of non-code `AdditionalText` values; `GetText` returns a
`SourceText` and can return null. A generator's supported output operation is
`RegisterSourceOutput` + `SourceProductionContext.AddSource`; it adds files to
the compilation and does not edit existing source files. [IIncrementalGenerator](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.iincrementalgenerator?view=roslyn-dotnet-4.14.0), [AdditionalTextsProvider](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.incrementalgeneratorinitializationcontext.additionaltextsprovider?view=roslyn-dotnet-4.14.0), [AddSource](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.sourceproductioncontext.addsource?view=roslyn-dotnet-4.14.0)

2. **Incrementality (severity: performance/correctness).** Roslyn tracks
additions, edits, and removals in the additional-text provider. Filter first,
parse each file independently, and project to small immutable/equatable result
records. `.Collect()` creates a whole-set dependency and should be used only
for an index requiring every document. Combine global options/configuration
only where needed; cancellation tokens must flow through parsing. Metadata can
be read through `AnalyzerConfigOptionsProvider.GetOptions(file)`. [Incremental
generators](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md),
[cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md)

3. **Diagnostics (severity: user experience).** Diagnostics are reported via
`SourceProductionContext.ReportDiagnostic`. Preserve the original path and
token span in the compiler result, and create a non-source `Location` for the
`.lui` `AdditionalText`; never report every parse failure at `Location.None`.
The diagnostic descriptor should use stable IDs, titles, categories, help
links (if available), and appropriate warning/error severity. [Additional-file
analysis context](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.additionalfileanalysiscontext?view=roslyn-dotnet-4.14.0),
[ReportDiagnostic](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.sourceproductioncontext.reportdiagnostic?view=roslyn-dotnet-4.14.0)

4. **Source mapping/navigation (severity: functional).** Emit precise C#
`#line` mappings around generated expressions/statements and restore with
`#line default`; use `#line hidden` for scaffolding. Enhanced `#line
(startLine,startColumn)-(endLine,endColumn)` supports columns/spans. Roslyn's
`SyntaxTree.GetMappedLineSpan` is the direct verification API. Razor's current
cohosting direction uses compiler mappings rather than a separate span-mapping
service and treats template files as workspace AdditionalDocuments. [Enhanced
line directives](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-10.0/enhanced-line-directives),
[GetMappedLineSpan](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.syntaxtree.getmappedlinespan?view=roslyn-dotnet-4.14.0),
[Razor cohosting](https://github.com/dotnet/razor/issues/9519)

5. **SDK evaluation and default items (severity: packaging).** SDK-style
projects implicitly import `Sdk.props` early and `Sdk.targets` late. The .NET
SDK supplies default globs (including `Compile` and `None`) and excludes `bin`
and `obj`; duplicate broad globs produce `NETSDK1022`. A NuGet project SDK is
selected as `<Project Sdk="Lucent.Lui.Sdk/1.0.0">` (or centrally in
`global.json`) and conventionally packages `Sdk/Sdk.props` and `Sdk/Sdk.targets`.
The SDK can add `AdditionalFiles Include="**/*.lui"` with a narrow
`DefaultItemExcludes`/root convention, plus analyzer/generator assets. Inspect
the actual evaluated project with `dotnet msbuild -preprocess:output.xml`.
[Project SDK overview](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/overview),
[NuGet SDK resolver](https://learn.microsoft.com/en-us/visualstudio/msbuild/how-to-use-project-sdk?view=visualstudio)

6. **Package build assets (severity: packaging).** If a full project SDK is
unnecessary, a normal package can use `build/<PackageId>.props` and
`build/<PackageId>.targets`; `buildMultiTargeting` handles the outer build and
`buildTransitive` deliberately flows assets to consumers. Package props/targets
must not mutate restore-driving properties such as `TargetFramework` or
`PackageReference`. [NuGet MSBuild props and targets](https://learn.microsoft.com/en-us/nuget/concepts/msbuild-props-and-targets)

7. **Razor evidence (severity: design precedent, not dependency).** Razor SDK
uses item types such as `RazorGenerate`/`RazorComponent`, can use source
generation, and writes inspectable generated files under `obj` when
`EmitCompilerGeneratedFiles` is enabled. It is a useful precedent, not a
reason to depend on Razor or copy its private implementation. [Razor SDK](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/sdk?view=aspnetcore-10.0)

## Minimal package/project shape

Recommended package split (avoid a runtime dependency from the app on Roslyn):

```text
Lucent.Lui.Compiler       # netstandard2.0 if practical; public syntax/model API
Lucent.Lui.Generator      # analyzer package, Roslyn references private/build-only
Lucent.Lui.Sdk            # Sdk/Sdk.props, Sdk/Sdk.targets, package metadata
Lucent.Lui.Editor         # later, editor/LSP host (not required for M7)
```

`Sdk.props` should define opt-out/configuration properties and add the default
`.lui` AdditionalFiles item once. `Sdk.targets` should add the analyzer package
or analyzer DLL and only minimal validation/clean integration. Do not add
`.lui` to `Compile` (C# cannot compile it); do not add a second `**/*` or
`**/*.cs` glob. Expose an opt-out such as `LucentEnableLui=false`, and use
item metadata for logical document identity. Multi-targeted projects should
avoid producing duplicate global outputs: either per-document generated names
are stable in every inner build, or generation is explicitly scoped.

## Strict stale-output cleanup

The safest M7 design emits no persistent generated files: `AddSource` output is
recomputed by the compiler, so removed `.lui` AdditionalFiles cannot leave a
stale source in the next build. For optional on-disk inspection, direct it to
`$(CompilerGeneratedFilesOutputPath)`/`obj`, exclude that directory from inputs,
and let clean delete it. If the SDK owns any other generated directory, its
target must delete the complete known output directory (or a manifest of its
own files) before generation and on `Clean`; never glob-delete user source.
Run generation after inputs are evaluated and ensure failed generation does
not preserve old files. **Severity: high** if persistent output is introduced:
MSBuild target ordering and cancellation can otherwise publish stale code.

## Generator limits and editor plan

The generator cannot provide live editor parsing, mutate `.lui` files, or
reliably use arbitrary filesystem discovery outside declared inputs. Keep
project semantics in a reusable model: root, enabled flag, logical document
identity, options, references, and deterministic path normalization. Build
adapter maps MSBuild items to this model; editor adapter maps workspace
documents/project properties to it. The editor can then offer diagnostics,
navigation, completion, and generated-document views using the same parser and
source spans. Generated C# `#line` mappings provide Roslyn navigation fallback,
but editor tooling should navigate `.lui` syntax directly where possible.

## Testing and acceptance checks

* Compiler unit tests: parse/bind/emission snapshots or structural assertions,
  malformed syntax, duplicate IDs, path normalization, cancellation, and
  exact source-span mapping.
* Generator tests: `GeneratorDriver` (adapt an incremental generator with
  `AsSourceGenerator` if the harness requires it), assert generated hint names,
  diagnostics and `.lui` locations, and test add/change/remove plus unchanged
  input behavior. Include two files to catch accidental whole-set recompute.
* Build integration: temporary SDK consumer project; verify default glob,
  opt-out, multi-targeting, clean, failed parse, `NETSDK1022` avoidance, and
  `EmitCompilerGeneratedFiles` output under `obj`. `dotnet msbuild
  -preprocess` is required evidence for evaluated imports/items.
* Mapping check: inspect generated syntax and assert
  `GetMappedLineSpan` points to the expected `.lui` path, line, and columns.

## Pitfalls (file path / severity)

* `src/Lucent.Lui.Generator/*` — **high**: collecting all files before parsing
  or using path-only keys causes unnecessary work or stale identity behavior.
* `src/Lucent.Lui.Sdk/Sdk.props` — **high**: broad globs include `obj`, sample
  trees, or generated output; constrain `DefaultItemExcludes` and test the
  evaluated item list.
* `src/Lucent.Lui.Sdk/Sdk.targets` — **high**: disk writes without clean/failed
  build handling leave stale generated code; prefer no disk writes.
* `src/Lucent.Lui.Compiler/*` — **high**: generated C# without `#line` spans
  makes diagnostics/navigation point at `.g.cs`, not the document.
* `src/Lucent.Lui.Editor/*` — **medium**: duplicating project semantics causes
  build/editor disagreement; consume the shared model instead.
* package layout — **medium**: exposing Roslyn as an application dependency
  or mixing SDK and transitive build assets creates version/load conflicts;
  keep analyzer references private and pin tested versions.

## CREDITS.md ledger (exact references to record before adoption)

These are the references used for this brief, not a claim that Lucent should
vendor them. Record URL, exact version/commit, and MIT license in `CREDITS.md`:

* **Microsoft.CodeAnalysis.CSharp 4.14.0**, NuGet, MIT, published/updated
  2025-05-15: https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/4.14.0
* **dotnet/roslyn**, commit
  `e79586494f629704a0fd18b7afb840144fd5e673` (`main`, observed
  2026-08-30), MIT: https://github.com/dotnet/roslyn/tree/e79586494f629704a0fd18b7afb840144fd5e673
* **dotnet/razor**, commit
  `58ec96978ef4e5823b54e960b9fd64cff45d7e68` (`main`, observed
  2026-08-30), MIT: https://github.com/dotnet/razor/tree/58ec96978ef4e5823b54e960b9fd64cff45d7e68
* **.NET SDK 10.0.100** (SDK/toolset reference; install via official
  distribution), MIT: https://github.com/dotnet/sdk/tree/v10.0.100

Pin package versions in the repository's package-management file when
implementation starts; do not use floating `main` commits as build inputs.
License facts should be rechecked against the package/repository at the exact
version actually shipped.

## Gaps

Microsoft documents the Roslyn APIs and SDK mechanics, but not Lucent's `.lui`
grammar, runtime document identity, or an official turnkey “generated document
navigation” contract for arbitrary custom generators. Those are Lucent design
work. Validate them with a small end-to-end project before committing public
package APIs.
