# LUI formatting and linting

The [source-style guide](LUI-SOURCE-STYLE.md) defines one layout for `.lui` structure
and embedded C#. Formatting preserves source order, comments, literal contents and
meaningful text. It does not move default content or apply lint fixes.

## CLI

Build the tooling executable with the repository's pinned .NET SDK:

```powershell
dotnet build src/Lucent.Lui.Tooling -c Release
$tool = 'src/Lucent.Lui.Tooling/bin/Release/net10.0/Lucent.Lui.Tooling.dll'
dotnet $tool path/to/Example.lui
dotnet $tool --check path/to/Example.lui
dotnet $tool --write path/to/Example.lui
dotnet $tool --lint --project path/to/App.csproj path/to/Example.lui
dotnet $tool --lint --project path/to/App.csproj --fix path/to/Example.lui
```

The default mode writes formatted text to standard output. `--check` and `--write`
accept multiple paths. Lint mode also accepts multiple paths; omit `--project` only
when walking upward from each file finds an unambiguous owning project. Project
evaluation is required for semantic lints, but not for formatting. Evaluate only
projects you trust, using the same trust boundary as a normal build.

| Operation | Exit 0 | Exit 1 | Exit 2 |
| --- | --- | --- | --- |
| Formatting check | All files clean | Formatting drift | Invalid source/configuration, unavailable preservation, or operation failure |
| Formatting write | Requested writes completed | Not used | At least one file could not be safely written |
| Lint / lint fix | Complete analysis with no effective errors | Not used | Effective errors, incomplete analysis, or operation failure |

Failure takes precedence over drift in a batch. Each file is replaced atomically
after comparing its current bytes with the read snapshot; detected edits are not
overwritten. Writes preserve the original supported encoding and BOM. A batch is
not a transaction: earlier successful files remain updated if a later file fails.
Review the reported failures and repository diff before retrying.

Malformed documents and unsupported preservation cases remain unchanged. An
unchanged file with an unavailable result is not a clean check. The existing asset
generation command remains independent of these formatting/lint modes.

## Shared configuration

CLI, editor and build lint policy read `.editorconfig`. Formatting uses canonical
defaults unless an applicable project setting overrides one of these dimensions:

```editorconfig
[*.lui]
indent_style = space
indent_size = 4
max_line_length = 100
end_of_line = crlf

# Optional; no declaration-order rule is enabled by default.
lucent_lui_declaration_order = component_first

# Normal diagnostic severity settings apply to enabled lint rules.
dotnet_diagnostic.LUI5003.severity = warning
```

Indentation accepts spaces or tabs, `indent_size` from 1 through 16 or `tab`, and
`tab_width` from 1 through 16. Width accepts an integer of at least 20 or `off`.
EOL accepts `lf`, `crlf` or `cr`. Without an EOL override, existing document
newlines are preserved; source with no newline uses LF. Literal contents retain
their original characters even when structural EOL is overridden.

Ancestor files apply from outer to inner, stopping at `root = true`. Matching
sections apply in source order. `unset` removes an inherited setting. Standard
Roslyn EditorConfig matching handles paths and glob patterns. Invalid explicit
values report their configuration file and line. Editor-global indentation does
not override this project policy. Braces, wrapping, sorting and declaration
movement do not acquire additional formatter switches.

The SDK supplies applicable configuration files as incremental generator inputs,
including configurations in directories containing only `.lui` source. The
generator does not read arbitrary project files behind Roslyn's cache. Changing
configuration updates subsequent build diagnostics without requiring a source edit.

## Lints and explicit fixes

| ID | Predicate | Automatic fix |
| --- | --- | --- |
| LUI5001 | A keyed iteration uses a known symbol-resolved unstable identity | None; choose the correct identity |
| LUI5002 | A private named style has no bound use | None; initialization can have effects |
| LUI5003 | A bound default-content attribute has a proven equivalent body form | Move that content, then rebind |
| LUI5004 | Component/style order violates an explicitly configured convention | None |
| LUI5005 | A lint suppression is malformed, unknown or cannot attach | None |
| LUI2017 | A mutable collection is inferred as read-only derived state | Existing compiler warning; no duplicated lint |

The source lints in the table default to warnings and honor effective project
strictness. Compiler errors retain their original IDs and are reported once.
`LUI5006` means semantic analysis is incomplete; `LUI5007` means it failed. These
two analysis-status diagnostics are errors, and neither is a clean result. Declaration
order accepts `none`, `component_first` or `styles_first`, with `none` as default.

The content rule follows `[DefaultContent]` metadata, not a parameter's name.
Eligible fixes preserve the selected method and conversions, evaluation order,
explicit readers, comments and values. Direct `ComponentContent` forwarding,
target-typed collections, explicit null/empty semantics and uncertain conversions
are not rewritten merely to satisfy a visual convention. `--fix` applies only
independently proven edits and re-analyzes the result. It never invents keys,
deletes styles or supplies suppression reasons.

## Scoped exceptions

```csharp
// lui-format-ignore: Keep this example aligned with the external table.
<Text>{value}</Text>

// lui-lint-disable-next LUI5002: Referenced by the documentation specimen.
style DocumentedStyle { Padding: 12; }
```

Each marker attaches through leading comments to the next complete construct in
its scope and requires a reason. Formatter ignores retain that node's authored
slice. They do not suppress lints. Lint suppressions name one supported rule and
cannot hide parser or binder errors. Unknown, unreasoned and dangling markers
are diagnosed; broad formatter-off regions and wildcard suppressions are unsupported.

## Editor

The matching VS Code extension and server provide document/range formatting and
explicit quick fixes. Formatting works without a selected project; semantic
features require `lucentLui.projectPath`. Format-on-save follows VS Code's own
setting. Builds report diagnostics and never rewrite source.

Range formatting selects complete supported boundaries, including adjacent
siblings, and leaves surrounding source intact. Requests and edits use UTF-16
coordinates. The client rejects cancelled or obsolete formatting results. Quick
fixes are resolved against the current project and document before exposing
versioned edits; changed snapshots require a fresh action.

## Repository checks

Run `./tools/Verify-Formatting.ps1` before committing. It checks authored C# with
the pinned CSharpier tool and `.lui` with this formatter. The managed CI suite runs
the same check after building tooling. Use `-NoBuild` only when Release tooling
already matches the current checkout. Generated and ignored build outputs are
outside the authored source list.

For SDK consumers, `-p:LucentLuiFormatCheck=true` enables the packaged formatter's
check before compilation. It never writes files and propagates both formatting
drift and unavailable/error results as build failures.
