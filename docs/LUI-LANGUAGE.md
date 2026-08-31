# `.lui` language contract

Status: Accepted design for the first `0.2` implementation

## Boundary

`.lui` is a compile-time, opinionated authoring surface over ordinary Lucent C# APIs. It adds declarations, JSX-like element trees, typed style bodies, retained conditionals, and keyed iteration. It does not add a runtime parser, binding engine, template runtime, property lookup, component instance model, virtual DOM, or service container.

The first implementation converts the Issue Browser Filter Bar, then one virtualized Issue Row. Equivalent C# and `.lui` must produce identical behavior, ownership, diagnostics, and deterministic dumps before the application cuts over and superseded composition code is deleted.

## Documents and components

A document uses standard C# namespace and using syntax and may declare multiple explicit components. The initial convention is one public component per file; multiple declarations remain available for exceptional compositional families.

```lui
namespace Lucent.IssueBrowser;

using Lucent.Core;
using static Lucent.IssueBrowser.Theme;

public component FilterBar(Query Query, Action Clear) {
    <Row Style={Panel}>
        <TextField Value={Query.Text} OnChange={Query.SetText} />
        <Button OnInvoke={Clear}>Clear</Button>
    </Row>
}
```

A component lowers to a partial static C# recipe whose supported entry point is `Compose(CompositionContext, ...)`. Default accessibility is `internal`; `public` is explicit. An adjacent `FilterBar.lui.cs` may provide matching partial static helpers using normal project symbol semantics. The first release has no local state declaration, component instance, generic `.lui` declaration, method body, statement block, or embedded `code` block.

Handwritten and generated recipes mount through one public atomic C# operation shaped as:

```csharp
composition.Mount(parent, context => FilterBar.Compose(context, query, clear));
context.Mount(parent, childContent);
```

The exact names are sealed in #46, but the behavior is fixed: root and nested invocation share the same operation; each recipe/content call creates exactly one root; creation and commit are scope-owned; failures roll back all provisional structure and resources; disposing the mounted root retires the recipe scope. A content value is an in-process `Func<CompositionContext, Element>` capability, not a runtime template object or serializable value.

Ordinary parameters are construction-time values. Live inputs are explicit signal-bearing models or typed readers such as `Func<T>` consumed inside a binding or retained region. Generated code never turns an arbitrary value parameter into a live binding by rerunning a component.

C# component recipes opt in with `[LuiComponent]`. Content factories use explicit `[LuiContent]` metadata with an optional name and exactly one possible default. Generated `.lui` components expose equivalent metadata. There is no duck-typed recipe scan or string registry.

## Elements, parameters, and content

Tags resolve normal C# symbols. Parameter names and casing are exact C# names. Attribute order is irrelevant; duplicate, inaccessible, unknown, missing, and ambiguous parameters are errors. Normal Roslyn overload resolution applies to annotated C# components; `.lui` component declarations cannot overload initially.

Unwrapped children map only to the declared default content parameter. Named child blocks contextually resolve exact named content parameters rather than global component types:

```lui
<Card>
    <Header><Heading>Issue details</Heading></Header>
    <Content><IssueDetails Issue={Selected} /></Content>
</Card>
```

Simple body text is a trimmed string literal whose internal characters are preserved. Formatting-only whitespace around elements is ignored. Whitespace-sensitive or multiline content uses an explicit C# string expression. Mixed text does not implicitly stringify expressions initially:

```lui
<Text Content={$"{State.Count} issues"} />
```

## C# expressions and reactivity

Roslyn parses expression islands. The initial allowlist includes literals, member access, calls to resolved methods, simple operators, object/collection construction needed by target APIs, method groups, and short target-typed lambdas. Statements, declarations, awaiting bodies, local functions, arbitrary blocks, reflection evaluation, and runtime compilation are excluded. Complex logic moves to a named C# helper.

Expressions use ordinary C# conversions. Text body literals are the sole markup-specific primitive convenience. Future color/string or `bind:` sugar must lower at compile time to the same typed APIs and diagnostics; there is no implicit runtime string conversion.

Reactive expressions read existing `Signal`, `Derived`, `Effect`, and `AsyncValue` values under Lucent's runtime tracking. A component recipe establishes retained structure once. Live property expressions lower to the same public, scope-owned typed binding operation used by C#, shaped as `element.Bind(property, () => expression)`. The operation preserves author/variant precedence and provenance, commits on the UI thread, invalidates the affected layout/paint/semantic projection, and cannot commit after disposal. Composition exposes one portable coalescible invalidation notification; Windows turns it into an SDL wake/frame request so idle async completion is visible. There is no compiler-only setter, hidden dependency table, component rerender, or virtual-tree diff.

## Structural regions

Conditionals are C#-shaped compile-time constructs lowering to retained `When` regions:

```lui
if (State.Error is { } error) {
    <ErrorState Error={error} OnRetry={State.Retry} />
}
```

Dynamic collections initially require explicit identity and lower to `ForEach`:

```lui
foreach (var issue in State.Issues) keyed by issue.Id {
    <IssueRow Issue={issue} />
}
```

Keys obey the existing stable .NET equality/hash contract. Duplicate, null-invalid, unstable, or side-effecting key behavior fails through existing retained-region rules. Unkeyed dynamic iteration is deferred.

## Styles

Named and inline styles use one typed body grammar. Style property names resolve real C# `Property<T>` symbols through standard namespace/using/global-using semantics. The compiler owns no alias table, selector engine, cascade, specificity, utility-class parser, or runtime stylesheet.

```lui
style PrimaryButton {
    Background: Colors.Primary
    Color: Colors.OnPrimary

    when Hover {
        Background: Colors.PrimaryHover
    }

}

<Button Style={PrimaryButton with {
    Padding: Insets.Symmetric(horizontal: 12, vertical: 8)
}} OnInvoke={Save}>Save</Button>
```

`with` is the sole initial style composition syntax. Evaluation is left to right and the rightmost assignment wins. It lowers to existing ordered `Style.Compose` and `Style.When` APIs. Named styles are file/component-private initially; shared styles are ordinary public C# symbols. Declarative transitions and keyframes are excluded until Core owns automatic style-winner sampling, interpolation, clock/frame wake, interruption, and reduced-motion behavior; manual transition samples are not sufficient.

The Lucent SDK may supply opt-out ordinary global/static C# usings for author-facing property groups so style names work without repetitive imports. Those remain real symbols and participate in completion, rename, references, and diagnostics.

## Color, brush, padding, and opacity

The compiler targets the framework contracts rather than defining them:

- immutable sRGB RGBA `Color`, with explicit parse/try-parse APIs;
- immutable closed `Brush` with solid and bounded linear-gradient variants;
- safe implicit `Color -> Brush` and `LinearGradient -> Brush` conversions;
- two to sixteen finite ordered gradient stops and premultiplied linear-sRGB interpolation;
- physical logical-pixel four-edge `Insets` (`Left`, `Top`, `Right`, `Bottom`), finite and nonnegative, and `Padding` that constrains child content while background covers the arranged box;
- composited subtree `Opacity`; opacity zero does not change layout, hit testing, focus, or semantics;
- separate `Clip`; no image, background layers, border, radius, blend mode, repeating/radial/conic gradient, or runtime textual color parser in `.lui`.

Future `.lui` color literals parse at compile time. A shared golden corpus keeps compile-time conversion identical to public `Color.Parse`/`TryParse` behavior.

Padding participates in Lucent's own bounded algorithm: intrinsic outer size includes the insets; explicit/min/max constraints apply to the outer box; child layout uses an inner box clamped to zero when insets exceed available space; text/caret/selection origins and row/column alignment use that same inner box; scrolling and clipping use explicit tested inner/outer extents; fixed virtual row height is the row's total outer extent; realization uses the viewport's inner height. Shared outer/inner edges round independently at each declared scale without cumulative drift.

Brush equality/hash/dumps are canonical. Gradient stops are finite, ordered, and box-relative; nested opacity multiplies and one group covers background, text, and descendants, including visible overflow when clipping is disabled. Brush alpha affects only that paint. Dumps contain no renderer object/cache identity.

## Diagnostics, recovery, and formatting

Stable diagnostic categories begin immediately: parse (`LUI1xxx`), symbol/type (`LUI2xxx`), lowering/lifetime (`LUI3xxx`), and source-map/build (`LUI4xxx`). Invalid, duplicate, ambiguous, stale, or unsupported input fails the current build; stale generated UI is never reused.

The parser recovers at component, element, attribute, style, and structural-region boundaries. Missing tokens remain explicit syntax nodes so one error does not suppress diagnostics or completion for the rest of the file. Invalid components do not emit; valid sibling components may emit deterministically.

One deterministic formatter owns document and range formatting plus CLI/check surfaces. Initially it formats `.lui` structure while preserving C# expression-island token text verbatim, comments, line endings under the selected formatter policy, and runtime-significant text. The build never rewrites source automatically. Initial lints are objective only: unstable/missing keys, duplicate or impossible content, unused private styles, and unsupported constructs.

## Generated identity and source maps

Component identity is resolved namespace plus declared name. Stable generated hint names add a project-relative path hash only for collision resistance; absolute paths, declaration order, and syntax offsets never define identity.

Generated sources live under Roslyn/`obj`, are inspectable on demand, and are not checked in. Only the declared component symbol and supported `Compose` entry point are callable contracts. Helpers and maps are generated implementation details hidden from completion where practical.

Enhanced `#line` directives map compiler/debugger diagnostics and C# expression spans. A compact deterministic compiler-owned map covers every syntax and generated construct bidirectionally for completion, hover, diagnostics, rename/references, formatting, semantic navigation, and generated-code navigation. There is no runtime mapping service.

## Version and deferred surface

`<LucentLuiLangVersion>` defaults to `preview` from the installed SDK. Unknown/newer versions fail clearly. Numeric versions begin only when Lucent intentionally retains an older syntax contract.

Deferred work includes local state sugar, broader C# islands, two-way binding shorthand, textual color sugar, exported `.lui` styles, generic declarations, general element references, implicit/unkeyed dynamic loops, service injection, hot reload, visual designer, shared-component copy tooling, token declarations/import, keyframes, and rich paint/layout primitives beyond the accepted contracts.

Source-copied components are ordinary project `.lui`/C# inputs with normal namespaces, metadata, maps, formatting, review, and ownership. Updates are manual file changes initially; no registry, installer, updater, manifest, or compatibility layer exists.
