# POC 0003: Shared compiler and tooling foundation

This slice replaces the Counter-shaped compiler path with one shared frontend,
recursive binder, and emitter used by every adapter:

```text
                         +-> lucentc generate / verify
.lui -> LucentCompiler --+-> MSBuild task -> obj/Lucent/*.g.cs -> Roslyn
                         +-> language server -> LSP diagnostics -> VS Code
```

`LucentCompiler.Compile(sourceText, sourcePath)` remains the small external
interface. Parsing, bounded C# validation, semantic control projection,
reactive state rewriting, and generated source mapping stay behind that seam.

## Generalized lowering

The semantic model now represents controls recursively instead of recognizing a
fixed Counter tree. The initial projection registry remains deliberately small:

| Lucent node | Avalonia control | Supported members |
| --- | --- | --- |
| `Column` | `StackPanel` | `class`, children |
| `Text` | `TextBlock` | `class`, `text` |
| `Button` | `Button` | `class`, `text`, `onClick` |

Nested columns and arbitrary ordering or counts of the supported controls lower
through the same path. Duplicate and unknown properties receive Lucent semantic
diagnostics rather than escaping as emitter or binder exceptions.

State-backed property expressions are emitted into one `UpdateBindings` method.
State updates rewrite to generated setters, which update the stored value and
then update the existing controls. This is still a synchronous proof of concept,
not the final dependency graph or scheduler.

## Bounded C# islands and source mapping

Property values are scanned as balanced expression islands, while event blocks
are scanned as balanced statement islands. The scanner accounts for nested
delimiters, comments, ordinary strings, verbatim and interpolated strings, and
raw strings before asking Roslyn to parse the isolated fragment.

Roslyn syntax diagnostics are translated from fragment-relative UTF-16 spans to
absolute `.lui` spans and reported as `LUC3001`. Generated property expressions
and event bodies also receive `#line` directives so later C# diagnostics name the
authoring file rather than only the intermediate `.g.cs` file.

Roslyn is currently used for syntax validation, not full semantic binding. An
island can therefore be syntactically valid while referring to an unavailable
symbol or wrong target type; those errors are caught when the generated C# is
compiled.

## MSBuild adapter

`Lucent.Compiler.MSBuild` is an in-process task built against the MSBuild 17.14
line used by the .NET 9 SDK. `build/Lucent.Compiler.props` supplies defaults and
`build/Lucent.Compiler.targets`:

- accepts `@(LucentSource)` items;
- generates under `$(IntermediateOutputPath)Lucent` before `CoreCompile`;
- explicitly includes generated files in `@(Compile)` even when generation is
  skipped as up to date;
- reports Lucent diagnostics through the MSBuild logger;
- avoids rewriting unchanged bytes and preserves prior outputs on failure;
- registers outputs in `FileWrites` for normal cleaning.

The POC consumes `Counter.lui` through this adapter. The checked-in generated
Counter remains a CLI determinism snapshot, but it is excluded from the POC's
`Compile` items so it cannot conflict with the `obj` output.

This is repository-local build integration, not yet a packaged SDK or NuGet
experience. The consuming project currently references the task project and
imports the props and targets explicitly.

## Language server and VS Code extension

`Lucent.LanguageServer` is a dependency-free stdio JSON-RPC adapter over the
same compiler interface. The first protocol surface supports:

- `initialize`, `initialized`, `shutdown`, and `exit`;
- full and ranged `didOpen`, `didChange`, and `didClose` synchronization;
- push diagnostics with zero-based UTF-16 LSP ranges.

`editors/vscode` registers `.lui`, supplies a TextMate grammar and language
configuration, starts the server with `vscode-languageclient`, and includes a
script that publishes the .NET 9 server into the extension's `server` folder.

Completion, hover, symbols, navigation, formatting, semantic tokens, incremental
analysis, cancellation of superseded analysis, and cross-file symbols remain
future language-server work.

## Verification commands

```powershell
dotnet restore Lucent.sln
dotnet build Lucent.sln -c Debug --no-restore
dotnet test Lucent.sln -c Debug --no-build

dotnet run --project src/Lucent.Compiler.Cli/Lucent.Compiler.Cli.csproj --no-build -- verify --input examples/counter/Counter.lui --output src/Lucent.Poc/Generated/CounterComponent.g.cs --diagnostics-format msbuild
dotnet run --project src/Lucent.Poc/Lucent.Poc.csproj --no-build -- --smoke-test

Push-Location editors/vscode
npm install
npm test
npm run prepare-server
Pop-Location
```

## Findings and next limits

- The compiler, build, and editor paths can share one deep module interface; no
  adapter needs its own Lucent parser.
- C# fragment scanning still belongs to Lucent because Roslyn cannot determine
  where a C# island ends inside Lucent syntax.
- Syntax-only Roslyn parsing is a checkpoint. Type-aware island binding needs a
  generated semantic context and source-map-aware diagnostic translation.
- Reactive dependency recognition and state rewriting are intentionally narrow.
  They must move from textual recognition to Roslyn syntax or semantic symbols
  before arbitrary expressions are promised.
- The control projection is a small descriptor table, not reflection. Adding
  controls now requires explicit properties, events, child policy, and lowering.
- The repository-local MSBuild task proves ordering and incremental output, but
  packaging, design-time builds, multi-targeting, and task dependency isolation
  still need dedicated work.
- TextMate highlighting is heuristic. Embedded C# semantic highlighting should
  eventually come from the language server rather than an ever-larger regex
  grammar.
