# .lui source style, formatting and linting

Status: shared understanding confirmed and implementation handoff authorized, 2026-09-14.
The owner requested a grill with documents to establish early conventions, make human
reviews easier, and design formatter/linter work. All 18 decisions below are settled;
the owner requested that Implementation tackle the approved plan.

## Goal

Make authored .lui files consistent and easy to read and review, with tooling that applies
mechanical layout and explains actionable source problems. Preserve authored behavior,
comments, documentation, meaningful text, reactive intent and declaration/evaluation order.

## Terminology during the interview

The existing glossary defines Style as typed visual/layout assignments. Use source style
for code conventions so that the two meanings remain distinguishable. Formatter and
linter retain their general programming meanings; do not add generic tool definitions to
the domain glossary. Add project-specific glossary terms only when they are resolved.

## Current evidence

Source inspection uses the current main checkout at
138b4aca65a0c1dcad48c7e6c6e1732e025e6773, including active uncommitted Browser/LSP changes.
This design worktree's runtime is older. No runtime edits or verification runs are part
of this interview. Recheck the execution baseline before implementation.

- Source inspection confirms shared formatter entry points in the compiler, CLI
  --check/--write, LSP document/range formatting, and VS Code providers. This is an
  extension and policy-design effort over existing tooling, not a first formatter.
  The preservation and diagnostic findings below qualify that existing behavior.
- .editorconfig currently sets four-space indentation for .cs/.lui, CRLF by default,
  a final newline and trimming of trailing whitespace.
- docs/LUI-LANGUAGE.md, Diagnostics, recovery, and formatting: one deterministic
  formatter is intended to serve document/range/CLI/check surfaces. The initial policy
  preserves C# expression-island token text, comments and runtime-significant text.
  Initial lints are objective. Builds do not rewrite source.
- docs/LUI-SDK-TOOLING.md repeats that shared formatter contract and the initial
  preservation of expression islands. Changing that policy needs an explicit decision.
- Current Browser examples contain long opening tags, inline style expressions, local
  functions and long scalar text. Core Field.lui also demonstrates multiline component
  parameters, documentation comments and content expressions. These make useful review
  specimens; their current incidental layout is not automatically the desired standard.

The initial fact check also found two adoption gaps: the repository formatting wrapper
checks C# files only, and the .lui CLI's check comparison currently treats malformed input
as unchanged because the formatter returns the original source on parser diagnostics.
These are source-inspected behaviors, not new runtime test results. The future check
contract must distinguish a clean document from one it could not safely format.

### Detailed source findings

- Current formatter uses four spaces, same-line opening braces, single-line opening tags
  and parameter lists, and inline single text/expression children. It preserves source
  order but reconstructs blank-line grouping. No line-width wrapping policy exists.
  See LuiFormatter.cs:119, :192 and :328.
- Range formatting selects the largest complete node wholly inside the range, rather
  than all relevant siblings. A parser diagnostic anywhere prevents formatting. See
  LuiFormatter.cs:31 and :65.
- Whole-file formatting first needs a stronger comment/trivia preservation contract:
  LuiToken has no leading/trailing trivia (LuiSyntax.cs:43); parameter extraction uses
  Roslyn Span rather than FullSpan (LuiParser.cs:303); expression extraction trims outer
  whitespace (:1737). Comment loss beside parameter separators and a trailing line-comment
  swallowing a closing brace are source-inferred risks requiring executable probes.
- StatefulSyntaxTests.cs:167 deliberately expects embedded C# text to remain unchanged.
  The accepted whole-file direction requires updating this contract with stronger
  preservation tests, not merely changing the expected formatting string.
- LUI5001 already warns on specific symbol-resolved unstable key APIs; LUI5002 warns on
  unused private styles (LuiCompiler.cs:462). LUI2017 warns on mutable collections inferred
  as read-only derived state (:1034). There is an existing lint foundation.
- Build/editor diagnostic severity and suppression infrastructure exists, with parity
  coverage for LUI2001. The fact check found no dedicated LUI5001/5002 configuration tests,
  standalone lint CLI, source lint-disable/formatter-ignore directives or code actions.
  See LuiGenerator.cs:224, LuiProjectContext.cs:2855 and LanguageServerTests.cs:4153.
- Repository C# formatting uses CSharpier (tools/Verify-Formatting.ps1:11), independently
  of the .lui formatter. Choose the .lui source policy explicitly before choosing how to
  reuse C# formatting machinery; do not assume a formatter can honor every brace policy.
- LSP hover caching also uses LuiFormatter.Format for source canonicalization
  (LuiProjectContext.cs:684 and :2229). A heavier whole-file printer requires separating
  or proving this cache-equivalence path so formatting does not add unexpected edit-time
  work or permit stale hover reuse.

All findings above are read-only source/test inspection at the stated baseline, not
executed regression results. The bounded independent fact check changed no files.

### Default-content follow-up evidence

The added content-placement rule was checked against main
`d53b8726080160e973d6d6f4247a556c6f282fc6`. This is source/test inspection; no new
runtime probes were executed for the rule.

- Default-content binding follows metadata, not parameter names; unannotated content
  parameters do not gain that role. See LuiCompiler.cs:2008 and CompilerTests.cs:2176.
- Both attributes and body expressions support live-reader inference. Do not use
  content={value} versus {value} as a general snapshot/live counterexample. Quoted
  attributes and literal body text can select reader/scalar overloads differently;
  body text explicitly prefers a compatible non-reader default. See LuiCompiler.cs:1783,
  :2051 and :4344. The exact Text transformation is source-inferred, not a new tested claim.
- A typed ComponentContent body expression is supported, but forwards through collection
  expansion ([.. existing]) rather than passing the same collection directly. Ordered
  recipe references survive; collection identity/allocation can differ. Raw target-typed
  collection expressions cannot be assumed to work as equivalent body contributions.
  See LuiCompiler.cs:1906 and :3961, Core/Recipes.cs:518, and CompilerTests.cs:2457 and :2555.
- Attribute arguments emit in authored order; body content is appended after them.
  Moving an earlier eager argument can reorder side effects. Empty scalar bodies omit
  the argument; they do not mean an explicit empty string. Text bodies trim outer spaces.
  Null and typed-null collection spreads require their own behavior proof. See
  LuiCompiler.cs:3829 and :3869 and LuiParser.cs:1932.

The owner accepted enforcement with semantic exceptions after these differences were
explained. Keep direct forwarding where it matters and offer only proven explicit fixes.

## Design tree

First-round decisions, accepted on 2026-09-14:

1. Authority: one canonical opinionated source layout versus a configurable style engine.
   Decision: one canonical layout, with a small number of project compatibility
   settings. The language style guide and formatter should agree.
2. Coverage: whole-file source formatting, including embedded C#, versus retaining the
   existing structure-only policy. Decision: target coherent whole-file formatting,
   subject to syntax/comment/semantic-preservation feasibility. This deliberately evolves
   the documented initial expression-island policy.
3. Lint scope: objective correctness and readability diagnostics versus broader enforced
   authoring/design conventions. Decision: objective diagnostics by default;
   subjective extraction/complexity guidance starts as documented advice or optional hints.

Round two, accepted:

4. Whitespace baseline: accepted four spaces and a soft 100-column target. Alternatives
   of 120 or 80 columns were declined. Content-preservation exceptions may exceed the target.
5. Braces: accepted the existing same-line .lui braces consistently for component/style
   declarations, control flow and local C# functions. New-line braces were declined.
6. Wrapping: accepted keeping short lists compact and switching to one attribute or
   parameter per line when a list wraps, with a separate closing delimiter. Alternatives
   are always-expanded lists or packing multiple entries onto wrapped lines.
7. Simple content: accepted inline short single-text/expression-child elements. The
   alternative puts all nonempty element content on separate lines.

Accepted wrapping shape; exact semantic-preserving output still requires fixture proof:

```csharp
<Button onInvoke={Save}>Save</Button>

<Button
    onInvoke={Save}
    style={ActionStyle}
    name="document.save"
>
    Save document
</Button>
```

Source order is preserved. Formatting must not silently sort attributes, style
assignments, declarations or any other potentially meaningful evaluation/precedence order.

Round three, accepted:

8. Inline style blocks: accepted compact single assignments and one assignment per
   line for multiple assignments, including blocks that would otherwise fit the width.
9. Grouping: accepted preserving at most one intentional blank line inside bodies and
   consistent separators between top-level declarations; preserve source order.
10. Configuration: accepted .editorconfig overrides for indentation, print width and
    line endings only. Braces and wrapping remain canonical.
11. Exceptions: accepted a formatter-ignore marker for one complete node and scoped,
    rule-specific lint suppressions with a reason. General formatter-off/on regions are
    deferred from the first release.
12. File organization: accepted after its source prerequisite was checked:
    recommend namespace/imports, component, then named styles, but enable no default
    declaration-order enforcement. The owner refined this to require configurable,
    enforceable component-first/styles-first policies. Implement that as an opt-in lint
    policy, separate from mechanical formatter options. Ordinary formatting preserves
    order; any organizing fix is explicit and preserves style order and comment attachment.

Both style-first and component-first documents, including forward style references, are
supported by current parser/binder/tests (LuiParser.cs:124; LuiCompiler.cs:3039;
CompilerTests.cs:1542). Do not generalize this into arbitrary sorting: relative order of
nonparameterized styles can affect eager static initialization. Keep their relative order
and comment attachment intact (LuiCompiler.cs:4369 and :4679).

Final tooling round, accepted:

13. Incomplete source: accepted leaving malformed documents unchanged, reporting
    formatting unavailable and failing check mode distinctly from clean source. Safe
    partial formatting would be a later feature.
14. Severity: accepted objective lint warnings by default, subject to existing project
    diagnostic configuration and warnings-as-errors policy. Optional design rules stay off.
15. Fixes: accepted a small set of proven behavior-preserving editor/CLI fixes, separate
    from formatting. Unstable-key diagnostics must not invent a replacement key.
16. Rollout: accepted the guide, shared formatter/linter, editor and CLI/check surfaces,
    followed by isolated formatting-only migration and CI enforcement. Builds do not
    rewrite source; format-on-save follows the user's editor setting.
17. Owner addition: enforce exactly one blank line between component declarations/methods
    and the UI recipe. This is a mandatory formatter rule, not merely retained author
    grouping. Treat a structural conditional/keyed region as a possible recipe start and
    preserve attached comments. A component without declarations does not gain an empty
    declaration section.
18. Owner addition, with the semantic scope confirmed: children/default content belong
    between tags instead of being supplied as the default-content attribute wherever
    that preserves meaning. Enforce through a default warning and explicit proven fixes;
    retain attributes where moving them changes binding, live/snapshot behavior,
    evaluation order or forwarding. Follow resolved default-content metadata, not the
    name content. The owner chose these exceptions over additional language work to make
    both forms universally equivalent. Ordinary formatting does not apply the rewrite.

Consolidated deliverables:

- [Source-style guide](../LUI-SOURCE-STYLE.md): accepted author-facing rules and exceptions.
- [Implementation plan](lui-formatting-and-linting.md): architecture, rule/configuration
  contracts, verification and staged adoption. Proposed technical spellings remain design
  artifacts until the first feasibility gate compiles the actual integration.
- [ADR 0010](../adr/0010-lui-source-formatting-policy.md): durable policy and the intentional
  distinction between canonical formatting and optional organizational enforcement.
- [Primary-source references](lui-formatting-references.md): Prettier/Vue/JSX, XAML and C#
  formatting evidence, with .lui-specific limits.

All product decisions above are settled. The implementation plan identifies technical
feasibility work separately; its proposed API and command spellings do not reopen the
accepted source-style policy. Routine formatting rules live in the guide and the durable
whole-file tradeoff is recorded in ADR 0010.

## Required feasibility evidence

Before adopting whole-file formatting, prove preservation for comments adjacent to
parameter separators, end-of-line comments inside expression islands, documentation
attachment, multiline/interpolated/raw strings, preprocessor directives where supported,
and scalar body text with meaningful internal spaces. Formatting must be deterministic
and idempotent and preserve parsed/bound meaning; define incomplete-input behavior without
discarding unsupported regions or falsely certifying a formatting check as successful.

Use one shared options/policy path for editor, range, CLI and check surfaces. Existing
formatter strings and .lui AST reconstruction are insufficient evidence for lossless
whole-file output. Test real .lui constructs alongside C# islands, including .lui inline
style expressions; a C# formatter cannot be given arbitrary .lui as if it were C#.

Linter expansion must name exact rule predicates and false-positive cases, retain useful
source spans, agree across build/editor/CLI, and use the existing severity/suppression
infrastructure. Safe mechanical fixes and behavior-changing refactorings need separate
contracts. The accepted boundaries and proposed concrete implementation are in the plan.

## Decision log

- Round one: the owner accepted canonical formatting with few options, whole-file C#
  coverage, and objective lint defaults with optional design advice. Recorded in
  [ADR 0010](../adr/0010-lui-source-formatting-policy.md).
- Round two, Q4-Q5: the owner accepted four spaces, a soft 100-column target and
  same-line braces throughout .lui.
- Round two, Q6-Q7: the owner accepted one item per line after wrapping and compact
  short simple content. The owner explicitly requested XAML/React and Vue/Prettier as
  comparative examples, including inspection of Prettier's formatting code if useful.
  The bounded primary-source review is recorded in
  [lui-formatting-references.md](lui-formatting-references.md). Borrowing rules must
  preserve .lui semantics; this review adds no package dependency or copied source.
- Round three, Q8-Q9: the owner accepted expanded multi-assignment inline styles and
  preservation of single intentional grouping breaks.
- Round three, Q10/Q12: the owner accepted only indentation/width/line-ending overrides
  and component-first source organization as guidance.
- Owner refinement: component/style placement must be configurable and enforceable,
  with no enforced default. Component-first remains a guide recommendation; styles-first
  is equally supported when selected. This adds an opt-in lint policy rather than silent
  sorting during formatting.
- Round three, Q11: the owner accepted one-node formatter ignores and rule-specific
  suppressions with a reason. General formatter-off/on regions are outside the first release.
- Final tooling round: the owner accepted conservative malformed-input handling, warning
  defaults with project strictness, proven explicit fixes, and complete staged adoption.
  No product-choice frontier remains. The subsequent shared-understanding confirmation
  authorizes the implementation handoff.
- Owner addition: mandatory blank-line separation between component declarations/methods
  and the UI recipe, including when the original file omitted it.
- Owner addition: enforce children/default content between tags, with explicitly accepted
  semantic exceptions. This adds a selected canonical authoring rule to the objective lint
  baseline; general subjective design guidance remains optional. The language continues
  to support explicit attributes, and the formatter does not silently move their values.
- Final confirmation: the owner stated that understanding is aligned and requested that
  the approved work be passed to Implementation to tackle.
