# Give .lui one whole-file source-formatting policy

Status: accepted design direction, 2026-09-14. The [source-style guide](../LUI-SOURCE-STYLE.md)
records the settled rules; the [implementation plan](../plans/lui-formatting-and-linting.md)
has owner-confirmed shared understanding and is approved for handoff to Implementation.
Implementation follows in #295–300. The [formatting guide](../LUI-FORMATTING.md)
documents the shared compiler, CLI, editor and build contract; the
[integration record](../plans/lui-formatting-integration.md) preserves the
original gate and subsequent executable evidence.

Lucent will define one canonical source layout with few configuration options, covering
.lui structure and embedded C# declarations, statements and expressions. Consistent
authored files should let human reviews focus on behavior and structure. This deliberately
extends the initial expression-text-preservation policy documented in LUI-LANGUAGE.md and
LUI-SDK-TOOLING.md; formatting must preserve behavior, comments, documentation and string
or meaningful text content. Syntax and preservation evidence are required before adopting
the new formatter behavior.

Lint defaults will cover objective problems and explicitly accepted canonical authoring
rules. General subjective authoring/design advice remains optional. A highly
configurable formatting engine and default enforcement of subjective design conventions
were considered and rejected for this initial direction. Existing shared editor/CLI
formatting architecture remains the starting point.

Teams may explicitly configure and enforce subjective conventions such as component-first
or styles-first declaration placement. No declaration-order policy is enforced by default;
the guide's recommendation is distinct from a project's selected lint policy. Ordinary
formatting preserves order, while any reordering fix is an explicit operation.

The owner subsequently selected default content between tags as an enforced authoring
rule, with semantic exceptions for binding, evaluation and forwarding differences. This
uses resolved default-content metadata and proven explicit fixes; it does not ban an
attribute spelling, rewrite source during formatting or change the language's semantics.
