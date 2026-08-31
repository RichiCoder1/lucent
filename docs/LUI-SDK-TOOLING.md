# `.lui` SDK and tooling contract

Status: Accepted design for the first `0.2` implementation

## Projects and dependencies

M7 adds three initial build-time deliverables:

```text
Lucent.Lui.Compiler   parser, bound model, formatter, diagnostics, lowering, maps
Lucent.Lui.Generator  thin Roslyn incremental-generator adapter
Lucent.Lui.Sdk        additive MSBuild SDK props/targets and package metadata
```

`Lucent.Lui.LanguageServer` and a thin VS Code extension follow after the Filter Bar compiler slice. All experimental packages version together. The app/runtime has no dependency on Roslyn, the SDK, language server, JSON-RPC, or editor assets.

The compiler is host-independent and targets the smallest practical analyzer-compatible TFM. A load/navigation proof first attempts .NET 10 against the pinned SDK and editor host; only the generator/compiler boundary falls back to `netstandard2.0` if host compatibility requires it. Generated applications remain .NET 10 NativeAOT.

## MSBuild SDK

`Lucent.Lui.Sdk` layers onto `Microsoft.NET.Sdk`; it does not replace restore, C# compilation, references, output, or project semantics. It contributes only:

- an opt-out project-relative `**/*.lui` item excluding `bin`, `obj`, hidden/generated output, and removed files;
- Roslyn `AdditionalFiles` metadata and the generator analyzer asset;
- `<LucentLuiLangVersion>` with SDK default `preview`;
- optional ordinary C# `<Using>`/global-static-using items for author-facing Lucent property groups;
- validation, generated inspection, formatting check, and clean integration.

Projects may disable the default glob and list files explicitly. `.lui` never enters `Compile`. Evaluated items/imports are tested with `dotnet msbuild -preprocess`; broad or duplicate globs fail tests. Build props/targets do not mutate restore-driving framework/package properties.

## Compiler and generator

The compiler has three bounded layers: immutable recoverable syntax nodes with exact spans; a Roslyn-bound semantic model containing resolved symbols/types; and direct C# lowering with map entries. It has no generalized runtime UI IR, serializer, plugin pipeline, optimizer framework, or filesystem discovery.

The generator filters `AdditionalTextsProvider` before parsing, parses documents independently, carries cancellation, and projects small immutable/equatable results. Whole-set collection exists only for the cross-document component index. Build/editor adapters map inputs into one shared immutable project-context model; neither parses project files independently.

The generator emits only through `AddSource`. Removed or invalid `.lui` input cannot leave a persistent source. Optional inspection uses `EmitCompilerGeneratedFiles` under `obj`; clean owns that output. No target writes generated C# into source or glob-deletes user files.

Paths normalize to project-relative logical identity. Files outside the project root require explicit inclusion. Duplicate logical paths/components, generated-input recursion, malformed identifiers/literals, and collisions fail closed. Parsing/lowering never executes user code.

## Source maps and project authority

Build and editor use the actual Roslyn `Compilation`, global usings, analyzer options, references, defines, language version, nullability, and `.lui` items. The editor evaluates the real project through `MSBuildWorkspace`; there is no custom `.csproj` parser or approximate reference resolver.

Generated C# uses enhanced `#line` spans for compiler/debugger mapping and `#line hidden` for scaffolding. The compiler's deterministic map retains source/generated document identity and exact spans in both directions. Tests use `GetMappedLineSpan` and map round trips rather than prose claims.

## Editor and CLI

The first complete editor target is VS Code. A separate .NET 10 LSP process consumes `Lucent.Lui.Compiler` and the shared project model. The extension remains a thin protocol/client layer.

Before `.lui` is preferred, tooling covers every frozen construct: components, parameters, overloads, enums, literals, expression islands, named/default content, styles, tokens, variants, conditionals, keyed loops, namespaces/usings, locals, and XML documentation. It provides completion, hover, diagnostics, semantic navigation, cross-language rename/references, stable formatting, generated navigation, and mapped expression breakpoints/exceptions. Unsafe or ambiguous rename is refused rather than partially applied.

One formatter implementation serves editor document/range formatting, a repository CLI, and optional check-only CI/MSBuild integration. Builds never rewrite source. Hot reload, markup stepping, and a visual designer are follow-ups; initial DevX requires correct incremental build and fast restart.

## Evidence and budgets

Before the first app conversion:

- parser recovery and stable diagnostics pass a bounded malformed-input matrix;
- binding covers symbols, types, nullability, overloads, content, and styles;
- generated C# and bidirectional maps have focused goldens;
- add/change/delete/rename, cleanup, stale-output rejection, and unchanged-input incrementality pass;
- a temporary SDK consumer proves default glob, opt-out, multi-targeting, clean, failure, generated inspection, and evaluated items;
- managed and NativeAOT C#/`.lui` behavior and dumps match.

The Filter Bar prototype establishes an honest editor baseline. Budgets are frozen before optimization for cold project load, warm completion, edit-to-diagnostic, rename, formatting, incremental no-op, and one-file invalidation. The Issue Row/keyed slice must pass those budgets before application cutover.

Review occurs once at each meaningful boundary: runtime primitives; syntax/recovery; binding/lowering/maps; SDK/incrementality; Filter Bar parity; Issue Row parity; editor/cutover. Only blocker/high/medium findings block the boundary. Full runtime/NativeAOT gates run when runtime evidence is invalidated and once at final M7 closure, not after every parser-only edit.

