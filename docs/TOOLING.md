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

Useful follow-ups include Lucent-to-C# navigation, CSS class/token completion,
signature help, metadata-as-source, document symbols,
references, rename, reactive dependency inspection, and Lucent component
navigation.

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
