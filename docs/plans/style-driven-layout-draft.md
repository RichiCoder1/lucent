# Style-driven layout and container conditions

Discussion draft, September 8, 2026. This records the follow-up question raised alongside #108; it does not authorize a new public API or a CSS-compatible styling engine.

## Recommendation

Expose a generic `Layout` component over the existing typed layout properties. Keep Row and Column as convenient presets of that same model. A layout-mode change should rearrange retained children, preserving their identity, local state, focus where possible, editor sessions and viewport state. Use conditional composition when ownership really should change; use `Participation.Collapsed` when a retained child should leave layout and semantics temporarily.

The parent selects the layout algorithm. Each child supplies its own sizing and placement metadata. The parent's algorithm reads that metadata; children do not call back into the parent to rearrange siblings. This is already how Lucent's Grid placement and Flex contributions work.

| Concern | Available now | Proposed improvement |
| --- | --- | --- |
| Generic arrangement | Every arranged element resolves `LayoutProperties.Mode` and `Axis`; a Column can be styled as Grid | Neutral `Layout` recipe, avoiding a misleading Column name for a Grid container |
| Parent configuration | Typed tracks, gaps, alignment, padding, wrapping, min/max constraints | Coherent documentation/completion grouping; presets remain optional |
| Child contribution | `GridPlacement`, `MainBasis`, `MainGrow`, `MainShrink`, width/height and min/max | Optional named placement helpers such as row/column/span if they preserve unambiguous precedence |
| Container size | `ResponsiveContainer` publishes logical content bounds through `ResponsiveConstraints` | Optional query scope on `Layout`, plus reusable size conditions in styles |
| Retained hiding | `Participation.Visible`, `Hidden`, `Collapsed` | Query-driven participation using the same semantics |
| Other conditions | Explicit reactive state, typed tokens, theme appearance and variants | Extend a typed condition context only for demonstrated needs, with dependency tracking |

Do not introduce a separate attached-property storage system for Grid placement. Lucent's typed properties already provide the relevant mechanism. Grid-specific properties can remain on a child while its parent switches to Flex, where those properties are inactive. On switching back to Grid, normal placement validation applies. Every visible direct child still needs a valid explicit placement; implicit Grid tracks and auto-placement are separate capabilities.

## Current authoring mechanism

The maintained [StyleDrivenLayout fixture](../../tests/Lucent.Testing.Tests/Fixtures/StyleDrivenLayout.lui) uses existing syntax: a retained responsive container supplies width, a derived `wide` value changes its inner container's `Mode`, and children carry their own `GridPlacement` and participation. Its test checks Grid/Flex geometry, hidden semantics, editor values and stable identities across width/DPI changes. It is an executable reference for the current API, not an implementation of proposed query syntax.

The desired next public surface could replace that inner Column with `Layout`. A later style-condition surface might express the same condition near the style declarations instead of repeating inline conditional values. Choose grammar after validating the runtime contract; do not silently reinterpret existing `when` variant rules or introduce global descendant selectors.

## Query contract

Start with the assigned logical inline width of an explicitly identified containing scope. A component should respond to the space its parent allocates, including a pane resize inside an unchanged window. Width thresholds must be independent of device scale. An explicit scope reference is sufficient initially; named/nearest-ancestor lookup can follow with clear shadowing rules and authoring diagnostics.

Queries apply to contained layout/presentation using an already assigned constraint. The queried axis must have a parent-assigned size or an explicit containment contract; contents cannot determine the same size that decides how those contents lay out. Height/aspect-ratio queries require a similarly stable block constraint and should not be enabled indiscriminately on content-sized containers. Keep finite correction/discovery budgets and report the offending container and condition when feedback is invalid.

The useful lesson from [CSS container queries](https://drafts.csswg.org/css-conditional-5/#container-queries) is querying a containing scope with explicit size-containment rules. Lucent should use its retained typed style and constraint machinery to achieve that behavior, without importing selectors, CSS specificity or a second layout engine. [WPF attached properties](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/properties/attached-properties-overview) illustrate parent-consumed metadata such as `Grid.Row`; Lucent already has the analogous typed child placement value.

Only invalidate elements whose style actually reads the changed condition. Derive breakpoint predicates so resizing within the same range does not reconstruct styles or remount content unnecessarily. Layout still runs when available space changes; unchanged paragraph shaping remains cached. Condition evaluation must remain deterministic and owner-thread bound.

## Delivery slices to consider

1. Finish #108's constrained auto-height correction and preserve current Grid/Flex/responsive guarantees.
2. Add the thin `Layout` recipe and an explicit optional constraint reader. Keep existing Row/Column compatibility, style precedence and shared engine behavior. Provide a `.lui` sample switching Grid/Flex while retaining editor state.
3. Add reusable container conditions with explicit scope, width-first containment, useful diagnostics and editor completion/hover. Prove nested scopes and pane resizing before extending conditions to height, orientation, scroll state or richer environment values.

No need for a full styling language rewrite, a new native dependency, or a runtime reflection-based attached-property registry. The first question to settle before slice 3 is the authoring shape of reusable conditions, including how named style declarations receive an explicit container reference.
