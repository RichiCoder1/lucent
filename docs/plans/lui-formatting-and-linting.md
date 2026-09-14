# .lui formatting and linting implementation plan

Status: implemented, 2026-09-14. The owner-approved source policy includes
content-placement semantic exceptions. The whole-file formatter, shared semantic
lint policy, configuration resolver, CLI, editor actions and repository checks are
implemented and verified locally. F05/F06 tickets record CI publication and the
independent Light Notes package adoption.

Execution checkout: `d53b8726080160e973d6d6f4247a556c6f282fc6`. Execution tickets
are [F01 #295](https://github.com/RichiCoder1/lucent/issues/295),
[F02 #296](https://github.com/RichiCoder1/lucent/issues/296),
[F03 #297](https://github.com/RichiCoder1/lucent/issues/297),
[F04 #298](https://github.com/RichiCoder1/lucent/issues/298),
[F05 #299](https://github.com/RichiCoder1/lucent/issues/299), and
[F06 #300](https://github.com/RichiCoder1/lucent/issues/300).
The owner's subsequent scope extension includes all authored Lucent source and
in-tree apps plus Light Notes in F06, under both the existing C# policy and the new
`.lui` policy. Generated/vendor sources and unrelated work remain outside migration.
The [integration gate](lui-formatting-integration.md) records executable findings
and the selected technical contracts. The [formatting guide](../LUI-FORMATTING.md)
describes the implemented authoring and automation surface.

## Outcome and scope

Give authors one predictable source layout across .lui and its embedded C#, with the same
results in VS Code, the CLI and CI. Provide objective diagnostics and a small set of
explicit, proven fixes. Make reviews easier without changing application behavior.

The [source-style guide](../LUI-SOURCE-STYLE.md) is the policy authority. The
[interview record](lui-style-and-tooling.md) captures all accepted decisions;
[ADR 0010](../adr/0010-lui-source-formatting-policy.md) records the whole-file direction.
[Primary-source research](lui-formatting-references.md) explains the Prettier/Vue/JSX,
XAML and C# formatting references and their limits.

The first release covers the guide, shared formatter/linter, document/range formatting,
editor code actions, CLI format/check/lint/fix, and staged repository adoption. Partial
formatting of malformed documents, general formatter-off regions, speculative refactorings
and general subjective design enforcement remain outside this release. The owner-selected
default-content placement rule is included with its accepted semantic exceptions.

## Execution baseline

The investigation inspected the main checkout at
`138b4aca65a0c1dcad48c7e6c6e1732e025e6773`, including active uncommitted editor/sample work.
This design worktree has older runtime code. Refresh the execution baseline and coordinate
with active implementation before changing source. Findings below are source inspection,
not executed regression results.

- `LuiFormatter` already serves compiler/tooling, LSP and VS Code document/range formatting.
  Extend this shared policy; do not introduce independent editor and CLI printers.
- The tooling executable currently accepts `[--check|--write] file.lui [...]`; there is
  no format subcommand. Preserve those entry points and the existing asset-generation mode.
- Formatting currently returns the input on parser diagnostics. Consequently, check mode
  can mistake unavailable formatting for clean source. Make that state explicit first.
- Token/trivia storage and embedded-source extraction are not fully lossless. Existing
  stateful tests intentionally preserve C# island text; whole-file formatting needs a new
  preservation contract, not only new golden strings.
- Existing diagnostics include symbol-resolved unstable keys (`LUI5001`), unused private
  styles (`LUI5002`) and a compiler warning for mutable collections inferred as read-only
  derived state (`LUI2017`). Reuse their predicates and configuration infrastructure.
- The repository formatting wrapper currently checks authored C# files only. Add .lui
  enforcement after its migration, without changing the .cs formatting policy.
- LSP hover caching also calls `LuiFormatter.Format` to canonicalize source, including on
  document changes. A heavier formatter must not silently broaden cache equivalence or add
  unmeasured work to this path.

## Shared architecture

Use a source-preserving .lui representation with precise C# island boundaries, a grouped
layout printer, and one options resolver. Keep formatting independent of project loading,
builds and runtime execution. The linter may use the existing project-aware compiler
context when a rule requires symbols.

Retain original spans, delimiter ownership, comments and documentation through parsing.
Distinguish layout whitespace from meaningful scalar text and literal contents. Roslyn
syntax/trivia is a candidate for C# island analysis and spacing; a coordinated printer is
still needed for width-aware layout. Wrappers used to parse isolated C# must map edits
exactly back to authored source and must never escape into output.

The first work package chooses and proves the C# integration. CSharpier is a useful
reference, but its documented public configuration does not provide the selected brace
policy. Do not post-process formatted C# with regular-expression brace substitutions.
Roslyn Workspaces is a candidate rather than an approved dependency: check compatibility,
distribution and the dependency graph before selecting it. Keep any host-only formatting
adapter shared by CLI/LSP and outside the analyzer/generator package closure. Pure syntax
and policy helpers can remain in compiler tooling where compatible.

No formatter, Node process or browser engine belongs in published Lucent applications.
Record adopted dependencies or copied inspiration in CREDITS before adoption.

### Formatting result and edits

Introduce an explicit result that distinguishes clean, changed, unavailable and failed
formatting; exact type names are implementation details. Include useful source diagnostics
and edits only when the whole requested operation is safe. Parse errors or unsupported
preservation cases leave the document unchanged and cannot produce a successful check.
Report unsupported valid input distinctly so it cannot be silently excluded from adoption.

For valid documents, range formatting operates on complete supported syntax boundaries,
preserving surrounding text. Define selection of adjacent complete siblings and indentation
against the containing document; do not retain the current largest-node-only limitation
accidentally. If a requested range has no safe boundary, report no applicable edit. A syntax
error anywhere still prevents formatting in the first release.

Editor edits use the current document version and UTF-16 positions. Discard stale results
and honor cancellation. CLI writes preflight input, replace each file atomically where
supported, and verify it has not changed since reading. Report write failures and completed
writes accurately; do not imply a transaction across multiple files.

Decouple hover-cache structural equivalence from user-facing formatting, or demonstrate
equivalent narrow reuse and acceptable cost. Changed identifiers, literals, comments used
as documentation, bindings and source positions must invalidate or refresh the appropriate
hover data. Formatter normalization is not evidence of semantic equivalence.

### Layout contract

Implement every rule in the guide, including four-space/100-column defaults, same-line
braces, compact short lists, one item per line after wrapping, separate closing delimiters,
compact simple children, and expanded multi-assignment inline styles. Keep opening-tag,
child-content and embedded-expression layout decisions coordinated without forcing every
descendant to break together. Comments can force a break before a closing delimiter.

Preserve one intentional grouping blank line and normalize excess. Independently of
authored grouping, require **exactly one blank line between component properties/state/
methods and the UI recipe**. A structural conditional or keyed region may begin that
recipe. Put the separator before comments attached to the recipe. A component containing
only a recipe does not gain an empty declaration section.

Preserve declaration, attribute, assignment and evaluation order. Do not sort imports,
styles or parameters. Preserve meaningful text and string values even when they exceed
the width target. Literal-sensitive indentation and line endings take precedence over
cosmetic normalization; document any intentional width exceptions in fixtures.

### Configuration

Use one .editorconfig resolver with the same file path and ancestor precedence in every
host. Proposed keys use standard names for formatting and one Lucent lint-policy key:

```editorconfig
[*.lui]
indent_style = space
indent_size = 4
max_line_length = 100
end_of_line = crlf

# Optional example; omitted means no declaration-order enforcement.
lucent_lui_declaration_order = component_first
```

Formatting overrides are limited to indentation, line width and line endings. Support
spaces/tabs and their indentation size through those standard settings; do not expose
brace, wrapping or attribute-sorting switches. Invalid explicit values produce a clear
configuration diagnostic instead of silently choosing another policy. Specify handling
of standard sentinel values, tab width and nested/root configs in the first contract gate.

When no project setting applies, use four spaces and width 100. Preserve the document's
existing structural line-ending convention using the current preserve policy, with LF
for input having no line ending; an explicit end_of_line overrides layout newlines only.
Never normalize literal content merely to satisfy an EOL setting. CLI and editor must
agree: editor-global tab settings must not silently override the project/canonical policy.

Declaration-order values are proposed as `none`, `component_first` and `styles_first`,
with unset equivalent to `none`. This is lint configuration, separate from the three
formatter options. Diagnostic severity continues through standard .editorconfig diagnostic
settings and project warnings-as-errors policy. Reserve any new diagnostic ID only after
checking the live catalog; this plan does not claim an ID already exists.

### Lints, local exceptions and fixes

Each lint has a stable ID, exact predicate, source span, default severity, configuration
contract and documented false-positive exclusions. Surface compiler diagnostics once;
do not relabel binder errors as lints or duplicate LUI2017. Keep LUI5001 symbol-aware so
unrelated APIs with matching names do not warn. Do not imply it detects every unstable key.

Objective rules default to warnings and respect effective project strictness. Optional
design guidance is off. The declaration-order rule emits only when an order is selected:
component_first places the component before named styles; styles_first places it after
them. Namespace/import placement remains governed by the language. Preserve relative
style order, since eager style initialization can be observable.

Default-content placement is an additional, explicitly selected canonical authoring rule,
enabled as a warning by default and subject to the same severity/suppression controls.
Require children/default content between tags wherever that preserves meaning. Identify
the bound parameter marked as default content; never match only the name content. A
renamed default parameter is covered, while an ordinary parameter with that name and
other named slots are excluded. Existing duplicate attribute/body errors remain compiler
diagnostics and must not acquire a conflicting move-content action.

The owner explicitly chose semantic exceptions over changing the language to make every
attribute/body form equivalent. Current differences make a blanket rewrite unsafe:
quoted attributes and literal bodies can select reader/scalar overloads differently;
body arguments can move after other argument evaluations; and forwarding a ComponentContent
through body expansion can
rebuild the collection instead of passing the same value directly. Target-typed collection
expressions and explicit null/empty values also require dedicated coverage. Preserve
snapshot/live intent, resolved overload/conversions, evaluation order, collection identity
where observable, comments and literal/text values. Both attribute expressions and body
expressions support live-reader inference; do not assume that an attribute is a snapshot.
Do not silently add or remove lambdas.

Define a conservative, executable eligibility predicate before emitting the placement
warning. Genuine semantic exceptions do not warn; uncertain equivalence never receives
an automatic fix. Missing symbols/project context must not fall back to name-only checks.
Offer an explicit editor/CLI content-to-body fix only for proven cases, rebind the edited
document, and check the lowering/evaluation contract as well as the selected symbol.
Ordinary formatting only lays out the authored form. This work does not ban attributes
in the grammar or change binding/forwarding behavior to manufacture equivalence.

Proposed source markers, finalized with attachment tests in the first work package:

```csharp
// lui-format-ignore: Keep this specimen aligned with the external format.
// lui-lint-disable-next LUI5002: Referenced by the documentation specimen.
```

These illustrate marker syntax separately, not a requirement to pair markers. The first
preserves the next complete element/declaration; the second suppresses only its named
lint in the next complete construct. Both require a nonempty reason. Define attachment
through leading documentation/comments and retain that attachment during formatting.
Reject missing targets, unknown IDs and malformed directives clearly; no wildcard or
general formatter-off/on region is introduced. A formatter ignore does not suppress
compiler/lint diagnostics; a lint suppression cannot hide parse/binder failures. Nested
scopes and overlapping markers need deterministic coverage tests.

The initial explicit fix set can include proven default-content-to-body conversions,
configured declaration-order organization once preservation is proved, and a suppression
code action that requests an authored reason.
CLI --fix applies only independently proven automatic edits and does not invent reasons.
Do not delete arbitrary unused styles: initializers may have effects. Do not invent keys
for LUI5001. Decline fixes when directives, comment ownership or semantics make movement
uncertain, and explain the limitation. Ordinary formatting never performs lint fixes.

### CLI and editor behavior

Preserve today's formatting command shape. Propose `--lint`, optional `--project` for
project selection, and explicit `--fix` under lint mode in the existing tooling executable;
finalize mutually exclusive modes and usage in the integration gate. Package installation
and onboarding distribution remain with the existing onboarding effort.

For format check, retain exit 0 for clean and 1 for formatting drift; use a distinct failure
exit, proposed as 2, for unavailable formatting, invalid input/configuration or tool failure.
Unavailable/error takes precedence over drift in a batch. Document these statuses and
ensure wrappers preserve them. Lint reports warnings normally and fails for effective
errors, including warnings promoted by project strictness, or incomplete analysis.

Syntax-only formatting requires no project evaluation. Symbol-aware linting uses an
available, trusted project context; if required analysis cannot run, report unavailable
checks rather than a false clean result. Reuse project selection, cancellation and trust
boundaries already owned by compiler/editor tooling.

VS Code uses the same result/options model for document/range formatting and lint actions.
Format-on-save respects the user's setting. Builds report diagnostics but never rewrite
files. Code actions identify their rule and preview edits; source formatting and lint fixes
remain distinct actions. Verify parity with build, push and pull diagnostics.

## Sequenced work packages

These package boundaries are published as #295–300 above. Each package names its
responsibility and completion evidence; adjust paths to the refreshed execution baseline.

| Package | Responsibility | Depends on | Completion evidence |
| --- | --- | --- | --- |
| F01 Preservation and integration gate | Compiler/tooling fixtures, explicit results, C# adapter experiment, config/marker/CLI contracts, dependency and hover-cache audit | None | Reproduce current check gap; prove comment/literal/island boundaries and chosen C# brace/width strategy; record package graph and measured editor baseline |
| F02 Source-preserving layout | Parser/syntax trivia as needed, shared grouped .lui printer, embedded C# integration, complete layout policy | F01 | Deterministic/idempotent output; semantic preservation corpus; mandatory members/recipe separator; every currently supported construct covered or explicitly blocked before adoption |
| F03 Lint policy and safe edits | Shared lint catalog, severity/configuration, default-content placement with semantic exceptions, declaration-order policy, scoped markers and fix engine | F01, F02 | Existing rule predicates retained; content rule follows default-content metadata and preserves binding/forwarding; opt-in order enforcement; no implicit reordering; comment/style-order preservation and suppression coverage |
| F04 Tooling surfaces | CLI modes/statuses, LSP document/range/actions, VS Code integration, hover equivalence separation | F02, F03 | Editor/CLI/build parity, stale-edit cancellation, malformed check failure, effective strictness, trusted project analysis and honest batch-write reporting |
| F05 Integration proof and author docs | Compiler/generator/LSP/extension and package-boundary checks, guide/examples, language/SDK documentation | F04 | Real corpus and performance evidence; packaging compatibility; documented syntax/options/errors agree with implemented behavior |
| F06 Repository adoption and enforcement | Isolated formatting-only migration, formatting wrapper and CI, contributor instructions | F05 | Migration preserves behavior; second format is empty; required .lui check runs in CI and propagates failures |

F01 may fix the malformed-check result contract early as a contained regression repair,
but cannot declare whole-file formatting ready. If the selected C# path cannot satisfy
preservation, document the blocker and revise the technical approach before proceeding.
Do not weaken an accepted policy silently to make an adapter fit.

## Verification and rollout

Start with fixtures that can falsify the preservation claims: comments beside parameter
separators, trailing // inside expressions, attached XML documentation, directives where
the language supports them, multiline/interpolated/raw strings, significant scalar spaces,
nested inline styles, reactive expressions, and declarations with effects. Compare parsed
and bound meaning plus representative runtime values; token snapshots alone cannot prove
string or text preservation. Formatting twice must equal formatting once.

Test width boundaries, nested groups, forced comment breaks, CRLF/LF, tabs, Unicode source
positions, blank-line normalization and scoped ignores. The mandatory members/recipe
separator needs missing/excess separator fixtures, recipe-attached comments, structural
recipe starts and components with no declarations. Test both configured declaration
orders, unset policy, existing severity overrides and source suppressions across hosts.

Content-placement fixtures must cover scalar literals, explicit live readers, snapshot
values and competing scalar/Func overloads, renamed default parameters, unrelated content
parameters, other slots, direct ComponentContent forwarding versus body expansion, typed
and target-typed collections, null/empty/omitted values, argument side effects and attached
comments. Assert both the diagnostic's exceptions and the fix's behavioral limits; the
existing language tests remain the source of truth for supported syntax.

Exercise malformed and valid-but-unavailable inputs in document/range/check/write paths,
including mixed batches; none may be falsely certified clean. Check cancellation and
concurrent edits. Measure formatter time/allocation and hover/change latency on the real
small and large .lui corpus, with cold/warm cases. Set regression budgets from measured
baselines and the editor's existing budgets, not an invented performance claim.

Select affected checks under [TESTING](../TESTING.md) and the
[verification policy](../agents/verification.md). Expected focused entry points, to
reconfirm in the execution checkout:

```powershell
./tools/Test-Repository.ps1 -Project Lucent.Lui.Compiler.Tests,Lucent.Lui.Generator.Tests,Lucent.Lui.LanguageServer.Tests
node --test extensions/lucent-lui-vscode/extension.test.cjs
./tools/Test-Repository.ps1 -Suite Sdk
./tools/Verify-Formatting.ps1
git diff --check
```

Run SDK/package checks when the affected package boundary warrants them; prove published
NativeAOT consumer compatibility if dependency changes could affect applications. Add
affected headless sample checks for the migration. Pure formatting does not require
focus-stealing desktop tests or a release-wide suite absent additional risk.

Land tooling separately from source normalization. Migrate selected repository .lui files
in isolated formatting-only changes after preservation proof and coordination with active
sample work. Do not combine reordering/lint fixes with that migration. Update existing
language/SDK docs from the initial verbatim-C# policy only when the replacement is real.
Enable the .lui CI check after the migrated tree passes, and ensure unsupported files or
tool failures cannot be skipped as successes. Consumer-package adoption follows the
existing publication workflow. The owner's subsequent scope extension explicitly
authorizes the Light Notes formatting migration; other unrelated checkouts remain out of scope.
