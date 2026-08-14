# POC 0002: Counter compiler

> [!NOTE]
> This document records the original Counter-only checkpoint. [POC 0003](0003-shared-compiler-tooling.md) supersedes its current-state limitations with the generalized binder, Roslyn islands, source mapping, MSBuild adapter, and editor foundation.

This slice implements the first executable Lucent compiler path:

```text
Counter.lui
    -> Lucent lexer
    -> Lucent parser and syntax tree
    -> Counter semantic validation
    -> deterministic C# emitter
    -> CounterComponent.g.cs
    -> Roslyn and Avalonia
```

The compiler is intentionally narrow. It proves the module seams, diagnostics, generated-code workflow, and end-to-end desktop result before expanding the language.

## Projects

| Project | Responsibility |
| --- | --- |
| `src/Lucent.Compiler` | Side-effect-free compiler module: syntax, diagnostics, parsing, binding, and C# emission |
| `src/Lucent.Compiler.Cli` | File-system adapter providing `generate` and `verify` commands |
| `tests/Lucent.Compiler.Tests` | Parser, diagnostic, deterministic output, and CLI tests |
| `src/Lucent.Poc` | Avalonia host that compiles and runs the checked-in generated result |

The compiler module's public interface is:

```csharp
CompilationResult LucentCompiler.Compile(
    string sourceText,
    string sourcePath = "<memory>");
```

It returns the syntax tree, source-based diagnostics, and generated C# without reading or writing files. The CLI owns file I/O, atomic replacement, stale-output verification, and MSBuild-formatted diagnostic output.

## Supported source

The first grammar accepts the forms used by `examples/counter/Counter.lui`:

- one file-scoped namespace;
- one parameterless component;
- one `private readonly State<int>` member with an integer initializer;
- exactly one block-bodied `Fragment Render()` method;
- one returned `Column` containing one `Text` followed by one `Button`;
- literal and interpolated `text` values;
- literal `class` values;
- the event block form `onClick: { state.Update(state.Value + integer); }`.

The event block is deliberately accepted because it is the canonical fixture syntax. It is a bounded event-value form, not permission for arbitrary block-valued properties.

The initial Avalonia projection is fixed:

```text
Column  -> StackPanel
Text    -> TextBlock
Button  -> Button
class   -> Classes.Add(...)
text    -> TextBlock.Text or Button.Content
onClick -> Button.Click
```

## Generate and verify

Generate the checked-in C#:

```powershell
dotnet run --project src/Lucent.Compiler.Cli/Lucent.Compiler.Cli.csproj -- \
  generate \
  --input examples/counter/Counter.lui \
  --output src/Lucent.Poc/Generated/CounterComponent.g.cs \
  --diagnostics-format msbuild
```

Verify that the checked-in output is current without writing:

```powershell
dotnet run --project src/Lucent.Compiler.Cli/Lucent.Compiler.Cli.csproj -- \
  verify \
  --input examples/counter/Counter.lui \
  --output src/Lucent.Poc/Generated/CounterComponent.g.cs \
  --diagnostics-format msbuild
```

The generated file is checked in so clean POC builds do not need a nested compiler build. A future MSBuild adapter should generate under `obj`; this slice does not invoke `dotnet run` recursively from a pre-build target.

CLI exit codes are stable:

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | Invalid command-line usage |
| 2 | Lucent compilation diagnostics |
| 3 | Checked-in generated output is stale |
| 4 | I/O failure |

## Verification

The slice is verified by:

- compiler and solution builds with zero warnings and zero errors;
- nine automated parser, recovery, semantic, deterministic-output, and CLI tests;
- exact `lucentc verify` comparison against the checked-in generated C#;
- a native Avalonia smoke run that opens the window, raises the generated button's routed click event, observes `Count: 1` on the existing text control, closes the window, and exits cleanly.

## Insights and open issues

- Interpolated strings must be lexed as complete tokens so their braces never become Lucent UI braces.
- Generated C# uses the component source namespace and a deterministic `ComponentNameComponent` type name. It contains no timestamps, absolute paths, or GUIDs.
- The compiler emits only behavior represented in `.lui`. Window presentation and values from `Counter.css` are not invented by the emitter.
- Generated `.g.cs` files require explicit `#nullable enable`; generated files use stable UTF-8 without a BOM and LF line endings.
- Checked-in output plus `verify` is the current build integration. It avoids a compiler/application cycle but means a normal POC build alone does not detect stale `.lui`; tests and verification do.
- The lexer preserves a narrowly bounded event body, but embedded C# is not yet parsed or type-checked by Roslyn. The shared frontend milestone should introduce explicit C#-island parsing and translate Roslyn diagnostics back to absolute `.lui` spans.
- The semantic binder currently recognizes only the Counter shape. General control descriptors, property/event resolution, multiple nodes, component parameters, and multiple components are unsupported rather than silently guessed.
- Duplicate properties are rejected with source diagnostics; malformed user input must not escape the compiler as a binder exception.
- The parser provides deterministic source diagnostics and basic recovery, but missing-token nodes, skipped syntax, richer recovery, and cancellation remain for the shared frontend.
- Generated code does not yet emit `#line` mappings. Compiler diagnostics point into `.lui`, while later Roslyn diagnostics still point into `.g.cs`.
- State lowering is synchronous and UI-thread-local. Scheduling, equality, batching, background updates, post-disposal work, and Avalonia property priority remain runtime work.
