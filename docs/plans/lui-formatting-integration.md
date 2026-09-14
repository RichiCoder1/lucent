# Formatting implementation and evidence

Execution baseline: `d53b872`, September 14, 2026. This record starts with the
historical F01/#295 integration gate and appends subsequent implementation and
verification evidence for the [approved plan](lui-formatting-and-linting.md).
The [formatting guide](../LUI-FORMATTING.md) is the current usage reference.

## F01 findings and selected integration (historical)

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

## F01 configuration and marker contracts (implemented in F02–F04)

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

## F02 layout delivery

The grouped printer replaces the old structure-only printer, including C# members,
parameter/argument lists, expression wrapping and structural conditions. It shares
indentation, soft width and EOL settings, inserts the mandatory declaration/recipe
separator, preserves grouping and expands multi-assignment inline styles. Complete
adjacent range boundaries include named-style members; surrounding text stays exact.
Reasoned `lui-format-ignore` markers retain their next node's authored slice, including
nested selections. Invalid, unreasoned and dangling markers report `LUI6003`.

Roslyn normalization needed two corrections established by regression tests: spacing
before an invocation's `is` pattern, and wrapping indentation inside method blocks.
Literal tokens and interpolated strings retain their authored contents. Structural
EOL overrides do not rewrite multiline string contents. A maintained test compiles
and executes actual formatter output and compares raw/verbatim/interpolated results.
The preservation key now reuses the input parse and handles LUI text independently
of C# lexing, including URLs. Hover continues to use this key rather than the printer.

The complete compiler suite passed 116 tests with the F02 implementation and the
in-progress configuration/lint contracts. The earlier read-only source/app corpus
accepted all 69 files; final full-repository migration and post-migration performance
measurement remain F05/F06 work. No broad source-format migration is included here.

## F05 corpus and performance characterization

The expanded read-only corpus contains 103 tracked `.lui` files, including in-tree
apps, Core, SDK fixtures and headless/native test fixtures. The first configured
CRLF pass identified six XML-documentation cases where Roslyn includes the line
terminator inside documentation trivia. A failing regression established that
case; the comparison now admits documentation EOL normalization while retaining
comment text and exact literal token contents. All 103 files subsequently passed
the preservation guard, and the post-migration second check was clean.

The same three original LF specimens from `c465350` were retained under an artifact
source root for comparison. `Lucent.Lui.Tooling.Benchmarks format <source-root>`
accepts this optional root so migration does not silently change benchmark inputs.
The measurements below use 100 iterations after ten warmups, on this machine:

| Source | Operation | First call ms | Median ms | p95 ms | Bytes/operation |
| --- | --- | ---: | ---: | ---: | ---: |
| ButtonsExample, 2,462 characters | Whole-file format | 155.88 | 10.15 | 18.39 | 4,230,281 |
| ButtonsExample | Hover key | 1.48 | 1.33 | 3.21 | 969,131 |
| Field, 1,005 characters | Whole-file format | 18.37 | 2.81 | 5.44 | 1,190,446 |
| Field | Hover key | 0.20 | 0.16 | 0.19 | 209,332 |
| ComponentDetail, 4,922 characters | Whole-file format | 25.86 | 13.09 | 17.82 | 13,504,641 |
| ComponentDetail | Hover key | 3.34 | 3.72 | 5.40 | 4,119,664 |

Whole-file formatting now performs C# normalization and width layout that F01 did
not perform; this is not a formatter speedup claim. Hover remains independent of
the printer. Its added source/text masking costs roughly 9 KB per Buttons key and
20 KB per ComponentDetail key, under 1% of their earlier allocations. Buttons p95
rose from 2.17 to 3.21 ms in this characterization; ComponentDetail stayed below
the prior 6 ms investigation point. These local timings include GC/JIT variation
and are observations, not universal latency promises. Preserve the editor's
existing whitespace-hover cache and no-whole-graph-hover regression contracts.

## F03–F06 integration and adoption

The shared linter binds default-content candidates to actual component metadata,
checks the replacement's binding and conversions, and offers only explicit fixes.
CLI and editor resolve the same configuration and severity policy; versioned
editor actions and byte-checked atomic CLI writes reject stale input. The generator
still emits valid source when a lint is promoted to an error, avoiding secondary
missing-symbol diagnostics. Invalid configuration remains an error.

The actual SDK test exposed a distinction that synthetic generator options could
not prove: reserved diagnostic severity settings were unavailable through normal
AdditionalFiles options. The SDK now supplies marked configuration snapshots as
incremental inputs. A second failing package probe established that transport must
run before `GenerateMSBuildEditorConfigFileShouldRun`, ahead of Roslyn's metadata
snapshot. Moving the target earlier made the same consumer pass. The maintained
package test isolates repository targets, places configuration in a LUI-only
subdirectory, and checks build/CLI error-to-none changes without editing source.
Package CI reuses this focused test inside its existing authoring consumer check.

The compiler/generator keep the existing Roslyn syntax dependency boundary. Only
the development-host CLI adopts the LSP's existing MSBuild workspace stack for
semantic linting; its licenses and third-party notices ship with the SDK tools.

Source normalization is isolated in `66ebfe8`. Subsequent content-placement fixes
are separate, symbol-proven changes. The repository formatting wrapper checks all
authored C# and `.lui` files, including in-tree applications and test fixtures, and
runs inside managed CI after the normal solution build. Light Notes uses the same
source policy, with its pure formatting changes isolated in `5964730`; its build
enables the packaged formatter check. Publication and consumer validation records
belong to [F05 #299](https://github.com/RichiCoder1/lucent/issues/299) and
[F06 #300](https://github.com/RichiCoder1/lucent/issues/300).

Focused final runs pass 119 compiler, 35 generator, 35 language-server and eight
CLI tests. The extension contract passes all 15 tests. Combined with earlier
managed verification after migration, 1,161 managed tests pass, with three
intentional Skia skips. The final formatting check covers 103 `.lui` files and
538 authored C# files; the generated icon file is excluded by CSharpier's policy.

The SDK matrix passes its package/configuration cases. Its NativeAOT tail exposed
two intentional retained-reader attribute tests; scoped, reasoned suppressions
preserve those test routes. The tail then passes transition, menu, content,
retained-payload, stateful and async runtime contracts with the exact permitted
runtime inventory. All SDK notice entries and the configuration target are
present in the inspected package. These source/tooling changes require no new
focus-taking UI walkthrough.
