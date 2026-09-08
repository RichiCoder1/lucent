# Style-driven layout, parameterized styles and window breakpoints

Accepted September 8, 2026. Implementation follows #108 and ADR 0006. Lucent, Issue Browser and Light Notes are delivered together: the framework capability must remove real layout decisions from both consumers, not merely add a demonstration API. The user explicitly deferred container queries and selected named window breakpoints for this slice; parameterized styles and general reactive conditions remain included.

## Intended result

An application author declares one retained content tree and expresses its arrangement through named `.lui` styles. Styles choose the layout strategy, tracks, direction, spacing, constraints and child placement. Reactive conditions select responsive participation and presentation. Switching from Grid to Flex, or showing one pane on a narrow surface, preserves pane/control mounts, editor sessions, selection and viewport state. Virtualized rows retain their ordinary realization lifetime; resizing may change which rows are mounted.

All application layout logic should be style driven. Application code continues to own data, commands, route intent, focus requests and preferred splitter extent. Layout algorithms necessarily remain framework or custom-strategy code; styles select and configure them. Data-dependent composition such as creating a row per issue remains composition, not style. Visibility changes that are only responsive use `Participation`, not width-dependent `if` branches that mount duplicate controls.

Issue Browser remains an example of stock Lucent presentation: no application color or theme-token overrides. Light Notes retains its established theme, SQLite data, draft recovery and autosave behavior.

## Authoring contract

### Parameterized named styles

```csharp
style Workspace(WindowBreakpoints breakpoints) {
    Axis: LayoutAxis.Column;
    MainGrow: 1;
    MinWidth: 0;
    MinHeight: 0;

    when (breakpoints.IsActive(BrowserBreakpoints.Wide)) {
        Mode: LayoutMode.Grid;
        Columns: GridTracks.Create(GridTrack.Fixed(320), GridTrack.Fraction());
        Rows: GridTracks.Create(GridTrack.Fraction());
        ColumnGap: 8;
    }
}

style DetailPane(WindowBreakpoints breakpoints, BrowserViewState view) {
    GridPlacement: new GridPlacement(0, 1);
    MainGrow: 1;

    when (!breakpoints.IsActive(BrowserBreakpoints.Wide) && !view.ShowDetails) {
        Participation: ElementParticipation.Collapsed;
    }
}
```

The example is illustrative; consumer type names and pane sizing follow the actual application. `BrowserBreakpoints.Wide` is a single typed declaration, `new Breakpoint("wide", 820)`, included in a `BreakpointSet`. The component invokes a parameterized style as an ordinary typed expression, `style={Workspace(view.Breakpoints)}`. Its retained root registers that owner-scoped reader with `breakpoints={view.Breakpoints}` on `Layout`.

Existing `style Name { ... }` declarations remain static style values. Declarations with a parameter list lower to typed style factory methods. Factories capture parameter objects, not snapshots of their changing values. Assignments in a parameterized style lower through the existing reactive binding/token machinery and are evaluated within the applying element's owned scope. Callers should pass state/read capabilities when values must stay live, not an already-evaluated scalar snapshot. Factories perform no subscriptions until the style is applied. Public visibility or cross-file style imports are separate existing language concerns and do not require a new module system in this slice.

### Conditional groups

`when (expression)` accepts a typed Boolean C# expression. It lowers to `Style.When(Func<bool>, Style)`. Existing `when Hover`, `when Selected | FocusVisible`, and other documented interaction variants retain their meaning. Parentheses distinguish reactive conditions from variant names. Reactive and variant groups can nest; enclosing conditions combine with AND and inactive branches stop observing their assignment dependencies. Malformed conditions and non-Boolean expressions produce diagnostics mapped to the original `.lui` span.

Conditions filter eligible assignments; they do not introduce CSS specificity. Existing default/component/author and interaction-variant precedence remains intact. Within the same precedence tier, later eligible assignments win. When a condition becomes false its assignments cease to participate, exposing the earlier eligible value. Mixed theme tokens and concrete values retain normal `StyleValue` behavior.

A condition is evaluated once per applied group and reactive invalidation, not independently for every assignment. Repeated resizing within one predicate range must not remount content or rebuild the style. Style reuse across mounts creates independent condition subscriptions and owned resources. Predicate evaluation is pure, owner-thread bound and subject to the same mutation guards as other reactive reads.

### Generic container and child contributions

`Layout` is the neutral retained content owner. It defaults to Flex/Column without implicit growth and uses the same typed property resolution and projection as other elements. Row and Column remain convenient compatible presets. `LayoutProperties.Algorithm` selects `LayoutAlgorithms.Flex`, `LayoutAlgorithms.Grid`, or a custom `LayoutAlgorithm`. An explicit Algorithm takes precedence over the legacy Mode selector; null restores Mode-based selection. `Layout(content, constraints: ..., style: ..., breakpoints: ...)` supports the existing assigned-size reader for compatibility, but window breakpoints are the responsive surface exercised in this slice.

Children provide `GridPlacement`, basis/grow/shrink and size/min/max constraints in their own styles. Custom strategies may consume typed child property values through a restricted read interface. There is no second attached-property storage registry. Grid metadata can remain present while Flex is selected; Grid validates participating children when selected. Existing explicit-track/placement rules remain: no implicit tracks, auto-placement, or arbitrary selector cascade is introduced.

## Runtime and performance contract

`LayoutAlgorithm.Layout(LayoutAlgorithmContext)` returns an immutable `LayoutAlgorithmResult` containing desired size and child placements. Its context provides constraints, ordered participating children and their typed metadata, `MeasureChild`, and `GetOrCreateState` for container-owned algorithm state. Typed child property reads support custom layout hints without exposing mutable elements, composition, native handles, or the application input router. Shared algorithm objects must not hold state belonging to a particular container. Per-container state is released on algorithm change or container disposal. Any retained measurement cache is invalidated with the relevant children, style, typography, width and scale; stale geometry cannot survive a mode switch.

Custom output must cover the participating children exactly once and contain valid finite nonnegative sizes. Invalid indexes, duplicate/missing placements, negative sizes, nonfinite coordinates and use of an expired context fail explicitly before scene publication. Finite negative offsets are permitted for intentional placement outside the content origin; ordinary clipping still applies. The framework controls text shaping, clipping, rounding and final scene generation. Each evaluation permits at most two distinct measurements per child, including its initial unconstrained desired size; one additional constraint pair is available through `MeasureChild`, and identical requests reuse cached results. Custom layout cannot mutate composition while a scene is projected or escape the owner thread.

This is a nonvirtualizing extension. A virtualized viewport remains an intrinsic-measurement boundary, and custom arrangement of that viewport must not realize its data source. Existing fixed-row realization, assigned viewport bounds, paragraph caches and the bounded responsive discovery/correction phases remain in force. The design does not permit an unbounded settle-until-stable loop.

### Named window breakpoints

`Breakpoint` is an immutable named minimum logical width. `BreakpointSet.Create(...)` copies an ascending set and rejects invalid/default descriptors, duplicate names or thresholds, and nonfinite/negative widths. `WindowBreakpoints(owner, set, name)` is an owner-scoped reader with `IsActive(Breakpoint)` and a read-only diagnostic Width. Asking about a descriptor outside the set fails explicitly. A breakpoint is inclusive at its minimum; below the first threshold the application is in its base presentation. For Light Notes, the base is compact, Medium begins at 840 and Wide at 1060. Rules are cumulative, so a later Wide group can override Medium values. An exclusive medium range can test Medium and not Wide.

`Layout` attaches the reader for its mount lifetime. A reader has one established mount and must use the same reactive graph; separate windows cannot accidentally share the same registration. Before arrangement, `SceneLayout.Project` assigns the current logical viewport width and drains bounded reactive work. All readers in that window see the same source width, including readers attached below the root. Their source is not the layout container's padded content width. DPI changes alone do not cross a breakpoint. No global process-wide window state is introduced.

If an existing assigned-size responsive branch mounts a reader, discovery assigns it before publishing the corrected layout. Window-reader and responsive-container structural discovery share one eight-round budget; their combination does not introduce nested independent convergence limits. The final registration and responsive geometry guards remain active.

General style predicates derive from that reader and publish only changed Boolean results to their assignments. The runtime still updates Width on each resize for accurate diagnostics. This avoids duplicating numeric thresholds in models, markup or tests while retaining ordinary typed C# condition expressions. App declarations own their responsive design; Lucent does not impose web-inspired device categories or universal threshold values.

Container queries, parent-size breakpoints, nearest/named container lookup, height/aspect query APIs and new containment rules are deferred. Existing ResponsiveConstraints remains supported under its current feedback guard. Parameterized styles do not by themselves create a container-query feature; their inputs determine what a condition means.

## Proving consumers

### Issue Browser

- Declare the 820 threshold once in an app-owned BreakpointSet and replace width-dependent wide/narrow composition with one retained workspace and named parameterized styles.
- Retain list, detail, search/filter editors and viewports while transitioning across the 820 logical-pixel boundary.
- Keep splitter preferred extent as interaction state; expose slot styles on SplitPane so its first pane, second pane and handle can adapt without remounting either slot.
- In compact layout, route intent selects the visible pane through style participation. Back navigation and focus remain behavior; breakpoint arithmetic leaves the view model.
- Move filter arrangement, density-dependent geometry and responsive visibility into named styles. Pane resizing still exercises ordinary constrained layout; it does not change window breakpoint state.
- Preserve stock-theme colors/tokens, menu behavior, 10,000-row virtualization and narrow keyboard/accessibility navigation.

### Light Notes

- Declare 840/1060 once in an app-owned BreakpointSet; move pane geometry and participation out of NoteWorkspace and inline markup into named styles consuming WindowBreakpoints.
- Keep collection/editor route intent independent of current width so a later resize can present the same navigation state. Focus follows explicit commands and availability rather than duplicating breakpoint knowledge in the data model.
- Replace layout-only Row/Column wrappers with neutral Layout where this clarifies style ownership; directions belong in styles. Framework control internals and semantic components keep their own defaults.
- Keep capture/search/title/body editor sessions, selection, pending autosave, invalid-address recovery drafts, per-collection scroll/query and menus stable across width changes.
- Use immutable Lucent prerelease packages for the delivered app, update lockfiles and local tooling revision, and preserve the user's data and repository identity.

## Tooling and diagnostics

Parser, lowering, formatter, source maps and editor features are one deliverable. Cover parameter declarations and references, style factory calls, reactive condition expressions, nested groups, typed child properties, and ordinary malformed/incomplete editing states. Existing variants and static styles must remain compatible. No nested style group may be silently dropped. Hover, completion and generated diagnostic projection must resolve against the consumer's actual project and package version.

## Verification and delivery order

1. [Lucent #109](https://github.com/RichiCoder1/lucent/issues/109): Core container/strategy/conditional-style/window-breakpoint contracts, including independent mounts/windows, disposal, exact threshold changes, stale contexts and invalid strategy output.
2. [Lucent #110](https://github.com/RichiCoder1/lucent/issues/110): `.lui` parameterized styles and recursive conditional groups with compiler, formatter, source-map and editor tests.
3. [Lucent #111](https://github.com/RichiCoder1/lucent/issues/111): Issue Browser retained workspace migration and focused headless geometry/interaction/virtualization tests.
4. [Light Notes #3](https://github.com/RichiCoder1/light-notes/issues/3): pack and consume the framework; migrate Light Notes and test real temporary SQLite/draft/editor/route behavior.
5. Run affected managed suites, architecture/formatting checks, NativeAOT/package consumer proofs and focused published resize/input/accessibility checks when the desktop permits. Record any desktop obstruction as unverified evidence, not a product pass. Commit/push both repositories, verify CI/package publication and close the implementation tickets with exact source identities.

Geometry coverage includes nested constraints and padding, exact breakpoint boundaries, wide/narrow/wide restoration, min/max, 100/150/200% scale, wrapped text and retained virtualized scroll. Nested placement verifies that breakpoint reads use the window width rather than local bounds. A custom nonvirtualizing layout fixture proves the extension interface independently of the built-in algorithms. Measurement counts and reactive update counts provide bounded performance evidence; broad release certification is not required for this pre-release slice.

## References and alternatives

- [SwiftUI AnyLayout](https://developer.apple.com/documentation/swiftui/anylayout) and [LayoutValueKey](https://developer.apple.com/documentation/swiftui/layoutvaluekey): interchangeable arrangement preserving subview state, with typed child values.
- [WinUI attached layouts](https://learn.microsoft.com/en-us/windows/apps/design/layout/attached-layouts): separate container, strategy and per-container context; distinct virtualization contracts. Its generic LayoutPanel is documented as preview, so it is an architectural reference rather than a dependency choice.
- [Compose custom layouts](https://developer.android.com/develop/ui/compose/layouts/custom), [parent-data modifiers](https://developer.android.com/develop/ui/compose/modifiers#scope-safety-in-compose), and [parent constraints](https://developer.android.com/develop/ui/compose/layouts/basics#responsive-layouts): bounded measurement and useful child-property authoring constraints.
- [CSS container queries](https://drafts.csswg.org/css-conditional-5/#container-queries): research for the deferred container-query contract. This slice uses window breakpoints and retains typed style precedence.

Keeping a closed Grid/Flex enum forever would make custom high-level components reconstruct layout outside the framework. Exposing the full mutable element tree would compromise ownership and bounded projection. The chosen restricted strategy interface allows genuine variation while keeping those contracts inside Lucent.
