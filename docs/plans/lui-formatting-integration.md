# Formatting integration gate

Execution baseline: `d53b872`, September 14, 2026. This is F01/#295 of the
[approved plan](lui-formatting-and-linting.md). It does not claim whole-file layout,
lint actions or repository adoption are complete.

## Findings and selected integration

The original CLI returned 0 for `internal component Broken() { <Text>` in check
mode. The original printer also removed a comment following a parameter comma
from a valid declaration. Both cases are reproduced in maintained tests. The
explicit result gate now reports malformed and unsupported preservation as
unavailable, retains the input and emits no edits. CLI check returns 2 rather
than certifying the file clean. Ordinary drift remains 1 and clean/write success
remains 0; failure takes precedence over drift in a batch.

`LuiFormattingResult` exposes clean, changed, unavailable and failed outcomes.
`LUI6000` describes an operation failure, `LUI6001` unsupported preservation, and
`LUI6002` a range without a supported complete boundary. Parser diagnostics retain
their existing IDs. The legacy string-returning methods remain compatible and
return unchanged source when an edit is unavailable. They are not check APIs.
Cancellation propagates; it cannot publish a replacement.

Use Roslyn's existing syntax/token/trivia APIs and a shared grouped document
printer. No new package is selected. Compiler and generator retain their existing
`Microsoft.CodeAnalysis.CSharp` 5.0.0 boundary; Workspaces and CSharpier are not
added to it. The CLI and editor use the same printer. CSharpier continues to own
ordinary `.cs` formatting separately.

The C# experiment normalizes syntax spacing and changes only block/type brace
trivia. It compiles and executes both forms with raw, verbatim and interpolated
strings. A deliberately overbroad brace implementation changed interpolation
values and failed that test; syntax-scoped braces pass. This rules out textual
brace substitution. Literal token contents remain protected. The grouped printer
demonstrates flat/wrapped argument lists, separate closing delimiters, nested
compact groups and forced comment breaks. Its cached flat widths avoid searching
combinations of layout alternatives. F02 must integrate these mechanisms across
all supported C# islands and preserve their original boundaries.

The initial preservation gate compares token boundaries, exact literal/comment
content and parsed meaningful scalar text before allowing an edit. It is
deliberately conservative and can report valid input unavailable. This is a
temporary safety boundary, not permission to skip such input during migration.
F02 must resolve the source/trivia gaps and validate actual lowered/runtime values
as well as this lexical comparison.

## Configuration and marker contracts for subsequent implementation

One file-path-based `.editorconfig` resolver walks ancestors until `root = true`,
then applies matching sections in outer-to-inner/source order. `unset` removes
the inherited value and returns to the canonical fallback. Unsupported explicit
values are errors, not fallback triggers. Host-global tab preferences do not
override project or canonical settings.

- `indent_style`: `space` or `tab`, default `space`.
- `indent_size`: positive integer 1–16, or `tab` to use `tab_width`.
- `tab_width`: positive integer 1–16; defaults to numeric `indent_size`, otherwise 4.
- `max_line_length`: positive integer at least 20, or `off` for unlimited soft width;
  default 100. Literal/text preservation still takes priority.
- `end_of_line`: `lf`, `crlf` or `cr`; unset preserves the existing structural
  convention, with LF when none exists. Literal content never undergoes EOL conversion.
- `lucent_lui_declaration_order`: `none`, `component_first`, `styles_first`;
  default/unset is `none`. This is lint policy, not a formatting option.

Use `// lui-format-ignore: reason` and
`// lui-lint-disable-next RULE: reason`. Attach to the next complete supported
element/declaration, passing through its leading comment/documentation group.
The marker and attached comments move as one group. A nested marker targets its
own following construct; multiple markers before one construct retain source
order. A missing reason/target, unknown rule or wildcard is invalid. Format-ignore
preserves that construct's exact source; it does not suppress diagnostics. Lint
suppression covers only the named rule in the attached construct, never parse or
binder errors. There are no formatter-off regions. These contracts are not yet
implemented by the F01 result repair.

Preserve default/stdout, `--check`, `--write` and `--generate-assets`. Add
`--lint [--project project.csproj] [--fix] file.lui [...]`; formatting modes,
lint mode and asset generation are mutually exclusive. `--fix` requires lint
mode. Formatting does not apply fixes. Semantic checks that cannot run report
incomplete analysis and fail instead of falling back to name-only predicates.
F04 owns atomic writes, source-race checks, effective strictness and final editor
action integration.

## Evidence and performance baseline

The compiler suite passed 95 tests after the result gate, followed by focused
adapter/grouped-layout tests. CLI malformed/unsupported/write/batch tests and the
existing structure/whitespace-hover contract pass. Logs remain under
`artifacts/f01-*`.

`LuiSourceComparison` now supplies hover's narrow lexical key independently of
the formatter. Cached tooltip text can survive layout whitespace; source maps,
diagnostics and compilation results still invalidate. Identifiers, literal
contents, comments/documentation and meaningful text are not ignored.

Local characterization from `Lucent.Lui.Tooling.Benchmarks format`, 100 warm
iterations after ten warmups (milliseconds; not a machine-independent benchmark):

| Source | Operation | First call | Median | p95 | Bytes/operation |
| --- | --- | ---: | ---: | ---: | ---: |
| ButtonsExample.lui, 2,462 characters | F01 guarded format | 221.87 | 6.73 | 19.45 | 3,792,975 |
| ButtonsExample.lui | Hover key | 1.28 | 1.05 | 2.17 | 959,984 |
| Field.lui, 1,005 characters | F01 guarded format | 13.23 | 0.80 | 1.18 | 790,018 |
| Field.lui | Hover key | 0.23 | 0.22 | 0.29 | 204,357 |
| ComponentDetail.lui, 4,922 characters | F01 guarded format | 21.04 | 12.57 | 17.04 | 16,308,932 |
| ComponentDetail.lui | Hover key | 3.96 | 3.55 | 5.77 | 4,099,405 |

The first measurement includes process-first Roslyn/JIT work; later first calls
share that warm process. The safety wrapper currently reparses source and is not
an optimization claim. F02 should reuse parsed boundaries and avoid repeated
parsing; F05 must remeasure these specimens. ComponentDetail is the largest tracked
repository `.lui` file at this checkpoint. Preserve the editor's existing
no-whole-graph-hover contract, and investigate warm key regressions beyond the
observed approximately 2 ms p95 for Buttons and 6 ms for ComponentDetail before adoption.
