# `.lui` language contract

Status: Current `0.2` authoring contract

## Boundary

`.lui` is a compile-time, opinionated authoring surface over ordinary Lucent C# APIs. It adds declarations, JSX-like element trees, typed style bodies, retained conditionals, and keyed iteration. It does not add a runtime parser, binding engine, template runtime, property lookup, component instance model, virtual DOM, or service container.

The Issue Browser uses `.lui` for its component tree, including its Filter Bar, virtualized Issue Rows, and retained details. Equivalent C# and `.lui` are checked for matching behavior, ownership, diagnostics, and deterministic evidence.

## C# recipe contract

`.lui` is the primary application authoring surface. Typed C# defines the underlying recipe contract and remains available for handwritten components; `.lui` has no privileged runtime operation. The C# vocabulary is:

- `ComponentRecipe`: a reusable in-process capability that creates exactly one retained root per mount;
- `ContentRecipe`: a capability that contributes zero or more retained entries below an existing root;
- `ComponentContent`: an immutable ordered collection of `ContentRecipe` values with C# collection-expression support.

A `ComponentRecipe` converts safely to one `ContentRecipe`. Retained `When` and `ForEach` helpers return `ContentRecipe`, so ordinary components and structural regions compose in one `ComponentContent`. These values are not component instances, virtual nodes, serializable templates, or rerender objects. Each mount owns independent retained structure and scope resources.

The framework allocates every recipe root. Advanced control authors use `ComponentRecipe.Create(kind, (context, root) => ...)`; they cannot omit the root, create two roots, attach to a foreign parent, or bypass rollback. Lucent's built-ins use the same public operation. Ordinary component authors compose existing recipes and do not handle `CompositionContext`, `Element`, or mounted state handles.

```csharp
using static Lucent.Core.Components;

public static partial class Components
{
    private static readonly Style FilterBarStyle = Style.Empty
        .Axis(LayoutAxis.Row)
        .Spacing(8)
        .Padding(Insets.Symmetric(12, 8));

    [LucentComponent]
    public static ComponentRecipe FilterBar(Query query, Action clear, Style? style = null) =>
        Row(
            [
                TextField(initialValue: query.Text, onChange: query.SetText),
                Button("Clear", onInvoke: clear)
            ],
            style: FilterBarStyle.With(style))
        .Named("filters");
}
```

`[LucentComponent]` marks an ordinary static method returning `ComponentRecipe`; `[DefaultContent]` marks its scalar or `ComponentContent` body parameter. The metadata is framework-wide rather than `.lui`-specific. Handwritten recipes may live in any static class; `Components` is the convention and SDKs or applications may opt into ordinary `global using static` imports. Generated `.lui` methods live in the declared namespace's partial static `Components` class.

`ComponentRecipe.Named` is the first universal composition trait. An explicit name is a stable local diagnostic and evidence label, not a DOM identifier, semantic name, element reference, or keyed-collection key. Unnamed recipes receive a deterministic kind-and-parent-local-ordinal name shared by C# and `.lui`; it may change when siblings are reordered. Similar universal accessibility or interaction traits require a proven cross-component contract before they are added.

`ComponentContent` accepts zero or more entries, validates and snapshots them when the owning recipe is created, and mounts them transactionally below that recipe's root. A component still creates exactly one stable root. Named slots reuse this content capability later, after a real compositional control proves their interface; the first implementation has default content only.

The initial author-facing built-ins are `Row`, `Column`, `Text`, `Button`, `TextField`, `TextArea`, `Selectable`, `ScrollViewport`, `VirtualizedList`, `Status`, and `Progress`. Redundant `Panel`, styled `Loading`, styled `Error`, mounted state handles, and duplicate public configurator overloads are not part of the ordinary recipe interface. The underlying element, presentation, behavior, and composition primitives remain the advanced custom-control seam.

Ordinary parameters are validated and captured when a recipe is created. They are construction-time values unless their declared type is explicitly live. Common static-or-live inputs may expose focused `T` and `Func<T>` overloads; inherently live inputs use `Func<T>` directly. There is no `ReactiveValue<T>` wrapper or combinatorial overload matrix. Each mounted reader uses the existing scope-owned reactive graph and is released on disposal. Programmatic controlled text synchronization remains deferred.

Styles are immutable recipe inputs. Standard properties have typed fluent methods over `Style.Set`/`Bind`; custom properties retain those universal methods. `Style.With(Style?)` composes left-to-right, treats null as no additional override, and lets an intentionally styleable component forward caller overrides. A component exposes `Style? style` only when its declared interface supports root restyling; the compiler adds no magical style parameter.

The SDK supplies an ordinary project-wide `Lucent.Core` namespace import. Built-in `Components` methods are implicit only as `.lui` element tags; `LayoutProperties`, `VisualProperties`, `TypographyProperties`, and `InputProperties` members are implicit only as style property names; and `VariantState` members are implicit only in `when` conditions. None enters component expressions, style value expressions, or project-wide static C# imports. Lowering resolves each name as its real Roslyn symbol and emits the fully qualified member. Custom components and properties continue to use ordinary C# imports; handwritten C# prefers component methods plus typed style fluency or explicitly qualified keys.

Handwritten and generated recipes mount through the same atomic operation:

```csharp
composition.Mount(parent, theme, FilterBar(query, clear));
```

Root mount receives `ThemeContext` explicitly. Creation, child content, retained regions, commit, rollback, and scope disposal form one owned transaction. Disposing the mounted root retires the complete recipe scope.

## Documents and components

A document uses standard C# namespace and using syntax and initially declares exactly one explicit component. Multiple declarations are reserved for a later real compositional-family use case.

```lui
namespace Lucent.IssueBrowser;
style FilterBarStyle {
    Axis: LayoutAxis.Row;
    Spacing: 8;
    Padding: Insets.Symmetric(12, 8);
}

public component FilterBar(Query query, Action clear, Style? style = null) {
    <Row name="filters" style={FilterBarStyle with style}>
        <TextField initialValue={query.Text} onChange={query.SetText} />
        <Button onInvoke={clear}>Clear</Button>
    </Row>
}
```

A component lowers to a `[LucentComponent]` method returning `ComponentRecipe` in the namespace's partial static `Components` class. Default accessibility is `internal`; `public` is explicit. An adjacent C# partial may provide normal helpers. The first release has no local state declaration, component instance, generic `.lui` declaration, method body, statement block, or embedded `code` block.

Component parameters use normal C# types, nullability, camel-case names, and constant default values. `ref`, `out`, `in`, `params`, generic declarations, parameter attributes, and destructuring are deferred. Ordinary parameters are construction-time values. Explicitly live component inputs use signal-bearing models or typed readers such as `Func<T>`; `.lui` supplies an actual target-typed lambda rather than silently wrapping an ordinary expression. Generated code never reruns a component to make values reactive.

Every component body has one component-element root for its mounted lifetime. A caller may choose between recipes from construction-time C# or place a component inside a retained `if`; reactively replacing the component document's own root is deferred. There is no duck-typed recipe scan or string registry.

## Elements, parameters, and content

Tags resolve normal C# symbols. Tag/component names are PascalCase; exact C# parameter and attribute names are camelCase. Attribute order is irrelevant; duplicate, inaccessible, unknown, missing, and ambiguous parameters are errors. Normal Roslyn overload resolution applies to annotated C# components; `.lui` component declarations cannot overload initially.

Unwrapped children map only to the declared `[DefaultContent]` scalar or `ComponentContent` parameter. `.lui` declarations opt in explicitly, just like C# components. Parameter spelling has no special meaning: an unannotated parameter named `content` does not accept an element body. Named child blocks remain reserved and rejected:

```lui
<Button name="save" onInvoke={save}>Save</Button>
<Text>{Label(state, issue)}</Text>
```

Quoted attributes are string literals; all other element expression islands use braces. Bare Boolean attributes, spread attributes, directive prefixes, and implicit string conversion are deferred. Simple body text is a trimmed string literal whose internal characters are preserved. A scalar-content body contains exactly one text or expression child, which lowers as the selected scalar `[DefaultContent]` argument with ordinary C# conversion and construction-time semantics. Formatting-only whitespace around component children is ignored. Whitespace-sensitive or multiline content uses an explicit C# string expression. Scalar bodies reject mixed text, multiple expressions, and structural siblings; combine scalar values in one C# expression:

```lui
<Text content={"  exact\ntext  "} />
```

Literal braces must be carried by an explicit string expression, since a brace at a body boundary starts an expression island.

A `.lui` component can declare one `[DefaultContent]` parameter. This is the only supported parameter annotation; general C# parameter attributes remain outside the grammar. For example, a scalar wrapper can use any parameter name:

```lui
public component Caption([DefaultContent] string label) {
    <Text>{label}</Text>
}
```

A `ComponentContent` body accepts elements, retained regions, and typed expression contributions in declaration order. A `ComponentRecipe` or `ContentRecipe` expression contributes one recipe. A `ComponentContent` expression splices its ordered recipes into that same body, without a wrapper element. Empty content contributes nothing. Strings, arbitrary enumerables, untyped null, and incompatible expression types are rejected instead of being converted into UI implicitly.

```lui
public component Framed([DefaultContent] ComponentContent children) {
    <Column>
        <Text>Header</Text>
        {children}
        <Text>Footer</Text>
    </Column>
}
```

A caller in another `.lui` document can nest wrappers and supply several children:

```lui
public component Example() {
    <Framed>
        <Framed>
            <Text>First</Text>
            <Text>Second</Text>
        </Framed>
        <Framed />
    </Framed>
}
```

`<Framed />` supplies an empty collection. `<Framed children={existing} />` forwards the explicitly supplied collection, and assigning both that attribute and an element body is an error (`LUI2008`). Omitted scalar content follows the declared parameter's ordinary required/default-value rules. A body on a component without default-content metadata is diagnosed with `LUI2011`; renaming an annotated parameter preserves implicit child syntax, while explicit attributes and forwarding expressions participate in ordinary parameter rename.

Forwarding evaluates recipe values when the containing recipe is constructed. Their existing live readers, retained regions, mount ordering, rollback, and disposal contracts remain intact. It does not make collection membership implicitly reactive. The initial shell needs these sibling contributions and fixed header/footer composition; named-slot tags, unnamed fragment tags, and multiple component or structural-branch roots remain deferred until a concrete control needs them.

## C# expressions and reactivity

Roslyn parses expression islands. The initial allowlist includes literals, member access, calls to resolved methods, simple operators, object/collection construction needed by target APIs, method groups, and short target-typed lambdas. Statements, declarations, awaiting bodies, local functions, arbitrary blocks, reflection evaluation, and runtime compilation are excluded. Complex logic moves to a named C# helper.

Expressions use ordinary C# conversions. Text body literals are the sole markup-specific primitive convenience. Future color/string, live-reader, spread, or directive sugar must lower at compile time to the same typed APIs and diagnostics; there is no implicit runtime string conversion.

Reactive expressions read existing `Signal`, `Derived`, `Effect`, and `AsyncValue` values under Lucent's runtime tracking. A component recipe establishes retained structure once. Inline style expressions lower through the same public typed `Style.Bind`/fluent-lambda operation used by C# and therefore track automatically. Ordinary component parameters remain construction-time unless their C# type is explicitly live. Each binding becomes an element-scope effect and retains that style candidate's component/author source, variant condition, and ordinal. It commits on the UI thread, participates in ordinary precedence/provenance, triggers current scene reprojection, and cannot commit after disposal. Live variants therefore need no control-channel override or compiler-only setter.

`ReactiveGraph.WorkAvailable` is edge-triggered when posted work changes from empty to nonempty, including worker-thread async completion. Windows translates that notification only into a registered SDL wake event; the UI thread drains, projects, and presents. Reset/recheck is lost-wake-safe, bursts coalesce, and no polling or worker-thread Core mutation is permitted. Zero queued work schedules no frames.

## Structural regions

Conditionals are C#-shaped compile-time constructs lowering to retained `Switch` regions:

```lui
if (state.Error is { } error) {
    <ErrorState error={() => error} onRetry={state.Retry} />
}
```

Dynamic collections initially require explicit identity and lower to `ForEach`:

```lui
foreach (var issue in state.Issues) keyed by issue.Id {
    <IssueRow issue={() => issue} />
}
```

These examples assume the receiving component declares a live reader parameter, such as `Func<Issue> issue`. A structural local still has the authored record's type: `issue.Title` is ordinary member access. Body reads lower through a scope-owned current-item reader, so `() => issue.Title` reads the latest same-key record without remounting the row. An ordinary attribute such as `issue={issue}` is still evaluated once when that row's recipe is constructed; it does not become live automatically. Styles and explicit live component inputs retain their existing dependency-tracking rules.

Pattern locals carried into a retained conditional body use the same current-item rule. A compatible same-branch update replaces the matched payload while preserving the mounted root and local state. Branch changes mount a fresh root. The compiler must reject unsupported pattern-local capture shapes instead of silently retaining a stale value.

In handwritten C#, retained collection factories receive `CurrentItem<T>` and read `.Value`; source enumeration and key selection still receive `T`. `VirtualizedList` row factories use this same reader. The capability is read-only, bound to the mounted entry's scope, and throws after disposal. A retained update installs the exact replacement record even if value equality considers it equal to the prior record. Virtualization eviction ends the mounted entry; later re-entry creates a new scope from the latest accepted source.

Keyed updates validate keys and provisional factories before changing retained payloads. Pre-commit failure preserves the prior tree and payloads; notification and cleanup failures after commit are reported after the sibling updates are attempted. Virtualized source updates and viewport realization are separate transactions: a later row-factory failure does not undo an already accepted source/payload update. Applications should handle that failure and retry realization against the current source.

Keys obey the existing stable .NET equality/hash contract. Duplicate, null-invalid, unstable, or side-effecting key behavior fails through existing retained-region rules. Unkeyed dynamic iteration is deferred.

## Styles

Named and inline styles use one typed body grammar. Style property names resolve real C# `Property<T>` symbols through standard namespace/using/global-using semantics. The compiler owns no alias table, selector engine, cascade, specificity, utility-class parser, or runtime stylesheet.

```lui
style PrimaryButton {
    Background: Colors.Primary;
    TextColor: Colors.OnPrimary;

    when Hover {
        Background: Colors.PrimaryHover;
    }
}

<Button name="save" style={PrimaryButton with {
    Padding: Insets.Symmetric(horizontal: 12, vertical: 8);
}} onInvoke={save}>Save</Button>
```

### Authoring conventions

- End every named, inline, and variant style assignment with `;`. A missing terminator is a recoverable parse error so later assignments remain available to diagnostics and editor features.
- Prefer bare numeric literals when ordinary C# conversion is unambiguous. Use a suffix only when it is needed to select a type, overload, or arithmetic behavior.
- Put literal text and component/default content between tags. A single dynamic scalar expression may appear between tags. Body and named-attribute expressions keep ordinary target-typed semantics: plain scalars are construction-time values, while a lambda is live only when the C# parameter type defines a live reader.
- Application theme keys conventionally live in an accessible top-level static `<RootNamespace>.Tokens` class. Its `Token<T>` fields and properties are implicitly available only inside named, inline, and variant style-value expressions. The compiler resolves them through ordinary C# rules and lowers fully qualified symbols; component parameters and structural expressions receive no implicit token scope. Runtime `ThemeContext` state remains composition-owned; token declarations are not mutable global theme state.

`with` is the sole initial style composition syntax. It accepts named style values, nullable style parameters, and inline bodies; evaluation is left to right and the rightmost assignment wins. It lowers to ordered `Style.With` and `Style.When` calls. Compound variants use the real finite flags expression, for example `when Selected | FocusVisible`. A bound candidate's expression is evaluated only while its variant condition is satisfied; inactive variants retain no live expression dependency. Named styles are internal to the document initially. `public style` is reserved as the fast-follow export syntax; shared styles remain ordinary public C# symbols until cross-document component binding/maps prove that feature. Declarative transitions and keyframes are excluded until Core owns automatic style-winner sampling, interpolation, clock/frame wake, interruption, and reduced-motion behavior; manual transition samples are not sufficient.

The Lucent SDK supplies an opt-out ordinary `Lucent.Core` namespace using only. Built-in `Components` are tag-only, framework properties are style-left-hand-side-only, and `VariantState` is `when`-only; application and third-party modules may publish ordinary static imports. All names remain real C# symbols and participate in completion, rename, references, and diagnostics. A unique built-in style name is resolved only in the property position on the left of `:`. This lets `GridPlacement: new GridPlacement(...)` and `TextWrap: TextWrap.WordWithGraphemeFallback` use the normal C# type names on the value side; ordinary C# member access keeps its usual binding rules.

Source-copied components become application-owned and therefore bind unqualified style tokens against the consuming project's `<RootNamespace>.Tokens`. Missing tokens are ordinary compilation errors. Components requiring a fixed token contract qualify their own token class explicitly.

An explicit `Style? style` component parameter is the initial styleability capability: it declares that callers may override the component root. Lucent does not define layout/text/input traits or concrete-control style targets. If real invalid property/component combinations later justify applicability checks, property metadata may declare inferred requirements; no syntax or public trait contract is reserved yet.

## Color, brush, padding, and opacity

The compiler targets the framework contracts rather than defining them:

- immutable sRGB RGBA `Color`, with explicit parse/try-parse APIs;
- immutable closed `Brush` with solid and bounded linear-gradient variants;
- safe implicit `Color -> Brush` and `LinearGradient -> Brush` conversions;
- two to sixteen finite ordered opaque gradient stops and native sRGB interpolation;
- physical logical-pixel four-edge `Insets` (`Left`, `Top`, `Right`, `Bottom`), finite and nonnegative, and `Padding` that constrains child content while background covers the arranged box;
- composited subtree `Opacity`; opacity zero does not change layout, hit testing, focus, or semantics;
- separate `Clip`; no image, background layers, border, radius, blend mode, repeating/radial/conic gradient, or runtime textual color parser in `.lui`.

Future `.lui` color literals parse at compile time. A shared golden corpus keeps compile-time conversion identical to public `Color.Parse`/`TryParse` behavior.

The exact initial author-facing property surface is:

| Group/member | Type | Default | Inherits |
| --- | --- | --- | --- |
| `LayoutProperties.Mode` | `LayoutMode` | `Flex` | no |
| `Axis` | `LayoutAxis` | `Column` | no |
| `Columns` / `Rows` | `GridTracks` | empty | no |
| `ColumnGap` / `RowGap` | `float` | `0` | no |
| `GridPlacement` | `GridPlacement?` | `null` | no |
| `Width` / `Height` | `float?` | `null` | no |
| `MinWidth` / `MinHeight` | `float` | `0` | no |
| `MaxWidth` / `MaxHeight` | `float` | positive infinity | no |
| `Spacing` | `float` | `0` | no |
| `MainBasis` | `float?` | `null` | no |
| `MainGrow` / `MainShrink` | `float` | `0` | no |
| `Wrap` | `bool` | `false` | no |
| `MainAlignment` | `LayoutAlignment` | `Start` | no |
| `CrossAlignment` | `LayoutAlignment` | `Stretch` | no |
| `Padding` | `Insets` | zero | no |
| `Clip` | `bool` | `false` | no |
| `Scroll` | `ScrollOffset` | zero | no |
| `VisualProperties.Background` | `Brush` | transparent solid | no |
| `Opacity` | `float` | `1` | no |
| `TypographyProperties.TextColor` | `Color` | opaque black | yes |
| `FontFamily` | `string` | `Segoe UI` | yes |
| `FontSize` | `float` | `14` | yes |
| `Language` | `string` | `en` | yes |
| `Direction` | `TextDirection` | `LeftToRight` | yes |
| `TypographyProperties.TextWrap` | `TextWrap` | `NoWrap` | yes |
| `MaxLines` | `int?` | `null` | yes |
| `Overflow` | `TextOverflow` | `Clip` | yes |
| `InputProperties.Enabled` / `Visible` | `bool` | `true` | no |

`Arrangement`, public `SceneProperties`, `Fill`, and `Foreground` are removed during the unreleased API change. The typography inheritance table is an intentional behavior change from the current surface and receives resolution/dump/row-scale cost evidence. `TextColor` remains eligible for the existing manual `TransitionKind.Color` channel after that channel is retyped to `Color`; `Background : Brush` is transition-ineligible initially. Raw text, selection, caret, virtual-row metadata, and projection bookkeeping are internal/compiler-excluded. Portable retained-scene DTOs remain the explicit Core-to-renderer seam.

`Color` stores canonical 8-bit sRGB RGBA channels and equality/hash follows those channels. `Parse`/`TryParse` initially accept invariant `#RRGGBB` and `#RRGGBBAA` only. `LinearGradient` uses normalized box-relative start/end points, two to sixteen opaque stops with finite nondecreasing positions in `[0,1]`, permits equal-position hard stops, and rejects a degenerate vector. Invalid constructors throw argument exceptions; try-parse returns false. The first renderer uses SkiaSharp's native sRGB interpolation. Transparent and selectable linear-light gradients are deferred until the renderer binding can prove their interpolation contract; solid brushes continue to support alpha.

`Opacity` must be finite in `[0,1]`; invalid assignments fail before scene publication. One retained opacity group wraps background, text, and descendants; nested values multiply. With `Clip=false`, group bounds include visible descendant overflow rather than implicitly clipping to the element box. With clipping enabled, clipping bounds the group consistently. Renderer tiling/culling is allowed only when pixels remain equivalent and allocations stay within the issue #45 evidence bounds.

Padding participates in Lucent's own bounded algorithm: intrinsic outer size includes the insets; explicit/min/max constraints apply to the outer box; child layout uses an inner box clamped to zero when insets exceed available space; text/caret/selection origins and row/column alignment use that same inner box. A scroll viewport clips scrolled children to its inner content box; leading and trailing padding participate in scroll extent so content may rest at padded ends. Fixed virtual row height is the row's total outer extent; realization uses the viewport's inner height. Shared outer/inner edges round independently at each declared scale without cumulative drift.

### Grid, Flex, paragraphs, and responsive constraints

`LayoutMode.Grid` uses only explicitly declared tracks. `GridTrack.Fixed`, `Content`, `Fraction`, and `MinMax` cover the first framework slice. Every direct child requires a zero-based `GridPlacement`; spans are positive and contiguous. A placement outside the declared rows or columns fails projection. No implicit tracks, named lines, auto-placement, dense packing, or general CSS Grid compatibility are claimed.

The default `LayoutMode.Flex` preserves Row/Column behavior. `MainBasis`, `MainGrow`, and opt-in `MainShrink` determine main-axis sizing without crossing min/max constraints. `Wrap` forms additional lines within the assigned main size; `Spacing` is the item gap and `RowGap` is the line gap. Overflow remains visible unless `Clip` is set.

Wrapped text uses the final assigned inline width. `TextWrap.WordWithGraphemeFallback` keeps grapheme clusters intact when one word exceeds the line, while `ExplicitBreaks` honors authored breaks without automatic wrapping. `MaxLines`, the assigned block constraint, and `TextOverflow` produce one immutable paragraph result shared by measurement, paint, hit testing, caret placement, selection, and semantic geometry.

A `ResponsiveConstraints` value is hoisted with application state and passed to `ResponsiveContainer`. Its `Current.Width` and `Current.Height` are logical content-box units. The container publishes one assigned value before the final projection pass. Retained branches bind `VisualProperties.Participation` to that value so hidden or collapsed arrangements preserve their owned sessions. A branch that changes the responsive container's own assigned constraints after the bounded correction pass fails with a feedback diagnostic. Keep the responsive containers themselves mounted: adding or removing one during that pass is rejected. Virtualized viewports inside their branches may mount or resize; a post-branch assignment pass supplies the actual cell bounds before rows are realized.

~~~csharp
using var constraints = new ResponsiveConstraints(applicationScope);

var shell = Components.ResponsiveContainer(
    [
        Components.Column(content, style: Style.Empty
            .Mode(LayoutMode.Grid)
            .Columns(GridTracks.Create(
                GridTrack.Fixed(184),
                GridTrack.Fixed(320),
                GridTrack.MinMax(482, GridTrack.Fraction())))
            .Rows(GridTracks.Create(GridTrack.Fixed(48), GridTrack.Fraction()))
            .ColumnGap(8))
    ],
    constraints);
~~~

Brush equality/hash/dumps are canonical. Gradient stops are finite, ordered, and box-relative; nested opacity multiplies and one group covers background, text, and descendants, including visible overflow when clipping is disabled. Brush alpha affects only that paint. Dumps contain no renderer object/cache identity.

### Application commands and wheel scrolling

Application models own `ApplicationCommand` instances in a `ReactiveScope`. `TryExecute()` accepts at most one execution while enabled; `IsEnabled`, `IsBusy`, and `Error` are reactive reads. Construction and observation do not start work. Owner disposal cancels pending work, and asynchronous completion returns through the graph's work queue. Supply an enabled predicate for application availability and expose failures as a useful retry state.

A `CommandBindings` table pairs commands with exact `KeyChord.Ctrl(Key.N)`, `KeyChord.Ctrl(Key.F)`, or `KeyChord.Ctrl(Key.S)` gestures; explicit Meta gestures are also available. Duplicate gestures in one table are rejected. The nearest focused-ancestor `CommandScope` consumes its matching chord even while its command is disabled or busy, and ignores key repeats. Normal text entry, AltGr, editing commands, and IME keep their existing routes.

~~~lui
public component CaptureActions(CommandBindings bindings, ApplicationCommand capture) {
    <CommandScope bindings={bindings}>
        <Column>
            <Button onInvoke={() => capture.TryExecute()} style={Style.Empty.Enabled(() => capture.IsEnabled)}>Capture</Button>
            <Text content={() => capture.IsBusy ? "Working" : capture.Error == null ? "Ready" : "Retry"} />
        </Column>
    </CommandScope>
}
~~~

Wheel input needs no application event handler. The Windows adapter preserves fractional deltas, maps a wheel unit to 40 logical pixels, and honors reversed-device direction. Core hit-tests at the pointer position, scrolls the nearest viewport, clamps each axis, and passes unconsumed deltas to scrollable ancestors. Trackpad hardware gestures beyond SDL wheel events are not a separate gesture API.

## Diagnostics, recovery, and formatting

Stable diagnostic categories begin immediately: parse (`LUI1xxx`), symbol/type (`LUI2xxx`), lowering/lifetime (`LUI3xxx`), and source-map/build (`LUI4xxx`). Invalid, duplicate, ambiguous, stale, or unsupported input fails the current build; stale generated UI is never reused.

The parser recovers at component, element, attribute, style, and structural-region boundaries. Missing tokens remain explicit syntax nodes so one error does not suppress diagnostics or completion for later independent constructs. An invalid component does not emit.

One deterministic formatter owns document and range formatting plus CLI/check surfaces. Initially it formats `.lui` structure while preserving C# expression-island token text verbatim, comments, line endings under the selected formatter policy, and runtime-significant text. The build never rewrites source automatically. Initial lints are objective only: unstable/missing keys, duplicate or impossible content, unused private styles, and unsupported constructs.

## Generated identity and source maps

Component identity is resolved namespace plus declared name. Stable generated hint names add a project-relative path hash only for collision resistance; absolute paths, declaration order, and syntax offsets never define identity.

Generated sources live under Roslyn/`obj`, are inspectable on demand, and are not checked in. Only the declared `[LucentComponent]` method on the namespace's partial static `Components` class is a callable contract. Helpers and maps are generated implementation details hidden from completion where practical.

Enhanced `#line` directives map compiler/debugger diagnostics and C# expression spans. A compact deterministic compiler-owned map covers every syntax and generated construct bidirectionally for completion, hover, diagnostics, rename/references, formatting, semantic navigation, and generated-code navigation. There is no runtime mapping service.

## Version and deferred surface

`<LucentLuiLangVersion>` defaults to `preview` from the installed SDK. Unknown/newer versions fail clearly. Numeric versions begin only when Lucent intentionally retains an older syntax contract.

Deferred work includes local state sugar, broader C# islands, relaxed live-reader sugar, two-way binding shorthand, textual color sugar, `public style`, named slots, generic declarations, general element references, reactive component-root switching, implicit/unkeyed dynamic loops, spread/directive syntax, service injection, hot reload, visual designer, shared-component copy tooling, token declarations/import, keyframes, and rich paint/layout primitives beyond the accepted contracts.

Control-owned values outrank every component/author style candidate, including a binding. This preserves existing control-state authority for text, scroll, selection, and similar properties; dumps retain the overridden binding candidate and provenance.

Source-copied components are ordinary project `.lui`/C# inputs with normal namespaces, metadata, maps, formatting, review, and ownership. Updates are manual file changes initially; no registry, installer, updater, manifest, or compatibility layer exists.

## Application-owned control state

Pass a model-owned `FocusTarget` to `TextField` or `TextArea` with `focusTarget={model.CaptureFocus}`. The model can request keyboard focus, optionally selecting all text, without traversing the mounted tree. A pending request waits for an eligible installed target; it does not make a collapsed pane visible. Route/participation changes remain application state.

`VirtualizedList` accepts `viewport={model.ListViewport}` when the application needs scroll state to survive removal and later remounting. Editor controls continue to consume owned `EditorSession` values through `session`.

Style property keys use the actual property names: for example `Overflow: TextOverflow.Ellipsis;`. A type can share the name of a statically imported property; use qualified value constructors such as `Lucent.Core.Border.Hairline(...)` or typed theme tokens. Prefer `BaseStyle with { Enabled: model.CanEdit; }` for declarative live style overrides.
