# `.lui` language contract

Status: Accepted design for the first `0.2` implementation

## Boundary

`.lui` is a compile-time, opinionated authoring surface over ordinary Lucent C# APIs. It adds declarations, JSX-like element trees, typed style bodies, retained conditionals, and keyed iteration. It does not add a runtime parser, binding engine, template runtime, property lookup, component instance model, virtual DOM, or service container.

The first implementation converts the Issue Browser Filter Bar, then one virtualized Issue Row. Equivalent C# and `.lui` must produce identical behavior, ownership, diagnostics, and deterministic dumps before the application cuts over and superseded composition code is deleted.

## C# recipe contract

Typed C# is the canonical, pleasant authoring surface. `.lui` removes ceremony but has no privileged runtime operation. The ordinary C# vocabulary is:

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

The initial author-facing built-ins are `Row`, `Column`, `Text`, `Button`, `TextField`, `Selectable`, `ScrollViewport`, `VirtualizedList`, `Status`, and `Progress`. Redundant `Panel`, styled `Loading`, styled `Error`, mounted state handles, and duplicate public configurator overloads are not part of the ordinary recipe interface. The underlying element, presentation, behavior, and composition primitives remain the advanced custom-control seam.

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

Unwrapped children map only to the declared `[DefaultContent]` scalar or `ComponentContent` parameter. Named child blocks are reserved and rejected initially:

```lui
<Button name="save" onInvoke={save}>Save</Button>
```

Quoted attributes are string literals; all other element expression islands use braces. Bare Boolean attributes, spread attributes, directive prefixes, and implicit string conversion are deferred. Simple body text is a trimmed string literal whose internal characters are preserved. Formatting-only whitespace around component children is ignored. Whitespace-sensitive or multiline content uses an explicit C# string expression. Mixed text does not implicitly stringify expressions initially:

```lui
<Text content={"  exact\ntext  "} />
```

## C# expressions and reactivity

Roslyn parses expression islands. The initial allowlist includes literals, member access, calls to resolved methods, simple operators, object/collection construction needed by target APIs, method groups, and short target-typed lambdas. Statements, declarations, awaiting bodies, local functions, arbitrary blocks, reflection evaluation, and runtime compilation are excluded. Complex logic moves to a named C# helper.

Expressions use ordinary C# conversions. Text body literals are the sole markup-specific primitive convenience. Future color/string, live-reader, spread, or directive sugar must lower at compile time to the same typed APIs and diagnostics; there is no implicit runtime string conversion.

Reactive expressions read existing `Signal`, `Derived`, `Effect`, and `AsyncValue` values under Lucent's runtime tracking. A component recipe establishes retained structure once. Inline style expressions lower through the same public typed `Style.Bind`/fluent-lambda operation used by C# and therefore track automatically. Ordinary component parameters remain construction-time unless their C# type is explicitly live. Each binding becomes an element-scope effect and retains that style candidate's component/author source, variant condition, and ordinal. It commits on the UI thread, participates in ordinary precedence/provenance, triggers current scene reprojection, and cannot commit after disposal. Live variants therefore need no control-channel override or compiler-only setter.

`ReactiveGraph.WorkAvailable` is edge-triggered when posted work changes from empty to nonempty, including worker-thread async completion. Windows translates that notification only into a registered SDL wake event; the UI thread drains, projects, and presents. Reset/recheck is lost-wake-safe, bursts coalesce, and no polling or worker-thread Core mutation is permitted. Zero queued work schedules no frames.

## Structural regions

Conditionals are C#-shaped compile-time constructs lowering to retained `When` regions:

```lui
if (state.Error is { } error) {
    <ErrorState error={error} onRetry={state.Retry} />
}
```

Dynamic collections initially require explicit identity and lower to `ForEach`:

```lui
foreach (var issue in state.Issues) keyed by issue.Id {
    <IssueRow issue={issue} />
}
```

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
- Put literal text and component/default content between tags. Keep dynamic scalar values as named attributes until expression children are added.
- Application theme keys conventionally live in an accessible top-level static `<RootNamespace>.Tokens` class. Its `Token<T>` fields and properties are implicitly available only inside named, inline, and variant style-value expressions. The compiler resolves them through ordinary C# rules and lowers fully qualified symbols; component parameters and structural expressions receive no implicit token scope. Runtime `ThemeContext` state remains composition-owned; token declarations are not mutable global theme state.

`with` is the sole initial style composition syntax. It accepts named style values, nullable style parameters, and inline bodies; evaluation is left to right and the rightmost assignment wins. It lowers to ordered `Style.With` and `Style.When` calls. Compound variants use the real finite flags expression, for example `when Selected | FocusVisible`. A bound candidate's expression is evaluated only while its variant condition is satisfied; inactive variants retain no live expression dependency. Named styles are internal to the document initially. `public style` is reserved as the fast-follow export syntax; shared styles remain ordinary public C# symbols until cross-document component binding/maps prove that feature. Declarative transitions and keyframes are excluded until Core owns automatic style-winner sampling, interpolation, clock/frame wake, interruption, and reduced-motion behavior; manual transition samples are not sufficient.

The Lucent SDK supplies an opt-out ordinary `Lucent.Core` namespace using only. Built-in `Components` are tag-only, framework properties are style-left-hand-side-only, and `VariantState` is `when`-only; application and third-party modules may publish ordinary static imports. All names remain real C# symbols and participate in completion, rename, references, and diagnostics.

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
| `LayoutProperties.Axis` | `LayoutAxis` | `Column` | no |
| `Width` / `Height` | `float?` | `null` | no |
| `MinWidth` / `MinHeight` | `float` | `0` | no |
| `MaxWidth` / `MaxHeight` | `float` | positive infinity | no |
| `Spacing` | `float` | `0` | no |
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
| `InputProperties.Enabled` / `Visible` | `bool` | `true` | no |

`Arrangement`, public `SceneProperties`, `Fill`, and `Foreground` are removed during the unreleased API change. The typography inheritance table is an intentional behavior change from the current surface and receives resolution/dump/row-scale cost evidence. `TextColor` remains eligible for the existing manual `TransitionKind.Color` channel after that channel is retyped to `Color`; `Background : Brush` is transition-ineligible initially. Raw text, selection, caret, virtual-row metadata, and projection bookkeeping are internal/compiler-excluded. Portable retained-scene DTOs remain the explicit Core-to-renderer seam.

`Color` stores canonical 8-bit sRGB RGBA channels and equality/hash follows those channels. `Parse`/`TryParse` initially accept invariant `#RRGGBB` and `#RRGGBBAA` only. `LinearGradient` uses normalized box-relative start/end points, two to sixteen opaque stops with finite nondecreasing positions in `[0,1]`, permits equal-position hard stops, and rejects a degenerate vector. Invalid constructors throw argument exceptions; try-parse returns false. The first renderer uses SkiaSharp's native sRGB interpolation. Transparent and selectable linear-light gradients are deferred until the renderer binding can prove their interpolation contract; solid brushes continue to support alpha.

`Opacity` must be finite in `[0,1]`; invalid assignments fail before scene publication. One retained opacity group wraps background, text, and descendants; nested values multiply. With `Clip=false`, group bounds include visible descendant overflow rather than implicitly clipping to the element box. With clipping enabled, clipping bounds the group consistently. Renderer tiling/culling is allowed only when pixels remain equivalent and allocations stay within the issue #45 evidence bounds.

Padding participates in Lucent's own bounded algorithm: intrinsic outer size includes the insets; explicit/min/max constraints apply to the outer box; child layout uses an inner box clamped to zero when insets exceed available space; text/caret/selection origins and row/column alignment use that same inner box. A scroll viewport clips scrolled children to its inner content box; leading and trailing padding participate in scroll extent so content may rest at padded ends. Fixed virtual row height is the row's total outer extent; realization uses the viewport's inner height. Shared outer/inner edges round independently at each declared scale without cumulative drift.

Brush equality/hash/dumps are canonical. Gradient stops are finite, ordered, and box-relative; nested opacity multiplies and one group covers background, text, and descendants, including visible overflow when clipping is disabled. Brush alpha affects only that paint. Dumps contain no renderer object/cache identity.

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
