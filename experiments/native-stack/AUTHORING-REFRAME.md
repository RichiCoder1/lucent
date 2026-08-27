# Lucent Native authoring reframe

## Why this attempt exists

The first issue-browser comparison correctly measured commit `3d0da02`, but it
measured an integration probe rather than the product hypothesis described in
the Native README. `BrowserRenderer` drew application geometry, color, and text
directly; the application manually synchronized a computed list into
virtualization; and input, focus, semantics, and rendering were assembled in
the application file. Meanwhile the typed style and retained-element modules
existed mostly as isolated proofs.

The resulting STOP remains evidence. It is not a fair final test of whether a
small reactive C# DSL and typed styling model can outperform XAML-era authoring
because that interface had not been built or used.

## Product interface under test

Application code should read approximately like this:

```csharp
var query = Signal("");
var filtered = Computed(() => Filter(issues, query.Value));

Column(
    TextField(query).Placeholder("Search issues"),
    VirtualList(filtered, issue => IssueRow(issue)
        .Selected(() => issue.Id == selected.Value)
        .OnPress(() => selected.Value = issue.Id)),
    Show(() => selected.Value is not null,
        () => IssueDetails(selected.Value!)))
    .Gap(3)
    .Bg(Tokens.Background)
    .Fg(Tokens.Foreground);
```

The exact names are provisional. The required shape is not:

- composition returns stable retained elements;
- reactive callbacks update only the property or structural region that read
  them;
- keyed and virtualized collections consume reactive data directly;
- fluent style calls create typed immutable values and compose in source order;
- controls add reusable behavior and semantics without inheritance;
- one generic projection owns layout, paint, input, semantics, and disposal.

## Deep modules

### Element composition

Its interface is the finite set of layout, text, control, and structural
constructors plus typed modifiers. Its implementation owns stable identity,
children, scopes, dirty facets, and disposal. Callers do not construct scene
commands or semantic snapshots.

### Reactive properties and collections

Its interface accepts constants or explicit reactive callbacks. Its
implementation tracks dependencies and updates stable element facets. A
virtualized list accepts reactive keyed items; there is no application `Sync`,
`SetItems`, notification interface, or collection reconciliation loop.

### Typed styles

Its interface is concise assignment/chaining over immutable typed values,
semantic tokens, finite variants, and bounded transitions. Its implementation
resolves theme/state precedence and invalidates the correct facet. There is no
runtime CSS, selector matching, specificity, or string property bag.

The issue-browser-required surface is deliberately finite: row/column sizing,
padding/gap, alignment, typography, foreground/background, border, radius,
shadow, opacity, transform, focus ring, light/dark tokens, hover/pressed/
selected/focus-visible/invalid/disabled variants, and reduced-motion-aware
opacity/transform transitions.

### Behaviors

Its interface composes press, edit, selection, focus, and semantic behavior
onto elements. Its implementation owns input registration, focus restoration,
portable semantics, capture, and cleanup. Applications provide intent callbacks
and accessible names, not routing tables or duplicate semantic trees.

### Projection and diagnostics

The generic projection consumes elements and resolved facets, performs layout,
creates retained scene commands, routes input, and emits semantic snapshots.
The same seam exposes deterministic tree, layout, resolved-style, reactive, and
semantic dumps. The issue browser cannot supply a renderer adapter.

## Boundaries

This attempt does not add `.lui`, CSS, a virtual DOM, general reconciliation,
runtime reflection binding, arbitrary animation, variable-height
virtualization, or new controls beyond the issue-browser contract.

The implementation may rename or replace the existing probe classes. No
compatibility layer is required for this unreleased experiment.

## Acceptance

Milestone 2A must pass its absolute authoring-interface gate before any new
Avalonia comparison is scored. Milestone 2B then uses a newly frozen rubric
focused on the hypothesis categories. The old `GAUNTLET.md` contract and
`evidence/issue-13/` STOP remain immutable historical evidence.
