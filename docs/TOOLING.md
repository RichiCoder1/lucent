# Tooling and developer experience

Tooling is part of Lucent's language design. A separate `.lui` source format is worthwhile only if the editor can recognize its structure and explain compiler decisions in Lucent terms.

## Shared frontend

The build compiler and language server should use the same parser, syntax tree, semantic model, diagnostics, and symbol representation:

```text
                    Lucent frontend
                   /               \
             build compiler      language server
```

Duplicating the parser or semantic rules would create drift exactly where the project needs trust. The initial frontend APIs should be designed for incremental document analysis even if the first build path is batch-oriented.

## Proof-of-concept language server

The proof of concept is not complete without basic editor support for `.lui`.

Required capabilities:

- document recognition;
- syntax and basic semantic diagnostics;
- component symbol discovery;
- component completion;
- go-to-definition for Lucent components;
- hover information;
- document symbols.

The current server implements project-wide document synchronization, shared compiler
diagnostics, native member and context-valid value completion, native
control/property/event/value hover, and source navigation for project-defined
controls and members. Completion includes writable Avalonia properties,
compatible events, `Class`, enums, booleans, same-type static values, and
compatible `Brushes` values. Property expressions also complete and hover
component state, computed values, ordinary component fields and methods,
keyed-loop locals, event parameters and locals, project types, static members,
methods, and typed member chains. Type,
constructor, field, and expression hover plus project-source definition
navigation work in persistent-member initializers and render expressions, so
`Text: package.Description` resolves through the project's C# model. CSS class
name completion inside `Class:` is intentionally excluded. C# types from the
current namespace and imports are completion candidates, while `(` and `,`
automatically reopen expression completion for arguments. It discovers the
owning `.csproj` from workspace projects or the nearest repository/solution,
including linked `LucentSource` files opened without a workspace root, and uses design-time
MSBuild to obtain C# sources, Lucent sources, and resolved references. Open
`.lui` buffers overlay the disk snapshot. A sibling open, change, or close
rebuilds the project batch and republishes diagnostics for every open project
document, so hover, completion, definition, and diagnostics use one current
component index. Visible components, named arguments, and slots have distinct
semantic symbols, and definitions navigate directly between `.lui` files.
The MSBuild targets also expose the last successful generated component files
as design-time C# compile items, so C# tooling can resolve generated component
types after a build without compiling `.lui` files during every design-time
evaluation.

Async-boundary fallback islands use the same project semantic model. The named
`Exception` catch local participates in completion, hover, and definition only
inside its fallback branch; computed status facets and `Refresh()` use the same
symbol-aware member intelligence as `Value`.

The server also supports deterministic document formatting (line-ending and
trailing-whitespace normalization) and a versioned `lucent/sourceMap` request
for generated-code tooling. The request requires the generated-content hash;
stale generated text intentionally receives no map. Generated C# remains owned
by installed C# tooling rather than this server.

Useful follow-ups include signature help, metadata-as-source, references,
rename, reactive dependency inspection, and richer component navigation.

## Diagnostics

Diagnostics should describe the user's component and style model, not expose internal parser codes or generated C# accidents.

```text
The property `background` expects a Color or ColorToken,
but `spacing.large` is a LengthToken.

Button {
    background: spacing.large;
                ^^^^^^^^^^^^^
}
```

The diagnostic model needs source spans and concepts for components, render methods, slots, context, state members, keys, CSS, and Avalonia property projection. Generated-code diagnostics should map back to the responsible `.lui` or CSS expression whenever possible.

## Formatting and source mapping

The custom format needs a formatter early enough that syntax discussions are not distorted by hand-formatted examples. Formatting must preserve stable output and should be based on the shared syntax tree.

Generated C# should include deterministic names and source mappings. Developers need to debug application behavior without treating generated code as the primary authoring surface, while framework contributors still need generated output that is readable enough to inspect.

## Project cache and benchmark

The shared frontend retains at most eight immutable project Roslyn bases, keyed
by project inputs, global usings, reference identities, and C# source content.
It reuses a base across Lucent edits, evicts least-recently-used bases, and can
never publish a result from a different input generation. The language-server
batch remains project-scoped; open buffers overlay disk sources.

`tools/Lucent.LanguageServer.Benchmarks` runs 500 sequential JSON-RPC completion
requests and 500 edit-then-completion cycles in Release. Its checked-in fixture,
workload, baseline, and final evidence are under `tools/Lucent.LanguageServer.Benchmarks`
and `docs/quality/008-tooling`. Reproduce with:

```powershell
$env:LUCENT_DISABLE_BASE_CACHE = "1"
$env:LUCENT_DISABLE_INCREMENTAL_REBIND = "1"
dotnet run --project tools/Lucent.LanguageServer.Benchmarks -c Release -- docs/quality/008-tooling/baseline.json
Remove-Item Env:LUCENT_DISABLE_BASE_CACHE
Remove-Item Env:LUCENT_DISABLE_INCREMENTAL_REBIND
dotnet run --project tools/Lucent.LanguageServer.Benchmarks -c Release -- docs/quality/008-tooling/final.json
```

The environment switch exists only to capture a comparable pre-cache baseline;
normal server execution always uses the bounded cache.

## Referenced module metadata

During project-generation construction, the shared compiler reads Lucent
reference manifests from embedded PE resources with `PEReader` and
`MetadataReader`. It validates PE/manifest identity, keys immutable results by
identity and PE fingerprint, bounds them to that generation, and reports an
invalid manifest at most once per project generation. CSS selector completion
consumes published referenced class entries (with no package-source definition);
`Class:` values remain excluded. Completion does not open files, load or execute assemblies, use a
network, or read a second theme/global/utility manifest format. Missing
manifests are silent.

This internal package/tooling seam does not replace live project sources and
open buffers. A local PE manifest is checked against independently available
live source text during generation and stale metadata is not merged into live indexes.
Metadata is neither a runtime style registry nor component
activation or invocation.

## Native-C# quality matrix

`NativeCSharpQualityMatrixTests.Supported_native_csharp_quality_cells_execute_protocol_assertions`
is the bounded parity matrix for Lucent's supported authoring seams, not a claim
of full C# language-service parity. It opens real documents and asserts LSP
completion shape and hover output for each matrix cell.
`NativeCSharpQualityMatrixTests.Matrix_executes_malformed_utf16_unsaved_overlay_and_generation_freshness`
owns malformed, astral UTF-16, open-overlay, and newest-generation behavior.
`NativeCSharpQualityMatrixTests.Project_member_completion_exposes_native_csharp_quality_indicators`
owns deterministic sort/filter text, generic signatures, nullable displays,
obsolete tags, XML documentation, hover, and source definitions.
`NativeCSharpQualityMatrixTests.Project_xml_documentation_is_rendered_without_generated_qualification`
owns the complete project XML-doc rendering contract. Together they exercise
the protocol assertions named above. CSS, source maps, trigger characters,
diagnostics, and component-only rename are separately owned by explicit
`LanguageServerProtocolTests` fixtures. All asserted native display text is
clean of generated `global::` qualification. Request execution remains serial
to protect project generations, while a dedicated reader recognizes
`$/cancelRequest` concurrently and cancels only the matching request. Cancelled
requests return JSON-RPC/LSP error `-32800` and cannot publish a stale result.
The VS Code client also watches C#, project, props/targets, Lucent, and adjacent
CSS files. A watched change invalidates and rebuilds the affected open project
generation before the next completion request; completion itself performs no
filesystem or project-loading work.

CSS selector/resource/token completion and navigation use the shared compiler
catalog. Deliberately, CSS class *values* in `Class:` have no completion: a
class name is authored in Lucent, while selector semantics are provided in its
adjacent CSS file.

## Hot reload

Hot reload is desirable after the core proof of concept:

```text
edit component
    -> compile affected region
    -> patch implementation
    -> retain compatible component state
    -> update UI
```

Production-grade hot reload is not an initial requirement. The early component identity, state layout, generated-code boundaries, and source mapping should avoid making it impossible later.

## Native member intelligence

Member-header completion recognizes qualified attached-property owners (for
example `Grid.`) and uses the same Roslyn-backed setter/property validation as
compilation. Attached-property references are reported as
`NativeAttachedProperty` symbols and bind their values with the setter's value
type. Mount-only collection elements remain ordinary C# expression islands.

## Native root interop and lifetime diagnostics

Components with exactly one direct native root expose a generated, exact-type
`MountRoot()` method in addition to `Mount(): Fragment`:

```csharp
using var dialog = new SettingsDialogComponent();
await dialogHost.ShowDialogAsync(owner, dialog.MountRoot());
```

Zero-, multi-root, structural, and component-indirect roots do not receive an
approximate method. Generated component types carry the standard
`GeneratedCodeAttribute` with tool name `Lucent.Compiler`; the optional
`Lucent.Analyzers` Roslyn analyzer recognizes only that exact marker. Its
bounded intra-procedural control-flow analysis reports dropped temporary
mounts, repeated mounts only when one execution path can reach both mounts,
mounted locals not disposed on every exit, and roots escaping lexical disposal
(including an assigned root returned later). Its code fix only offers safe
lexical `using var` transformations and preserves `Mount()` versus
`MountRoot()`. Native window close, ownership transfer, fields, unknown calls,
and event-driven lifetimes remain application decisions rather than proven
ownership transfers.
