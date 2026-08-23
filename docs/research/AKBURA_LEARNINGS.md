# What Lucent can learn from Akbura

Research snapshot: Akbura commit
[`61793db`](https://github.com/Asaicraft/Akbura/tree/61793dbbf12cbd563fc34d948ed008da23973bf3).
This note uses Akbura's published documentation and repository as primary
sources. Akbura is experimental, so these are design observations rather than
compatibility assumptions.

## Verdict

Akbura is useful evidence that Lucent's broad direction is sound: compile a
small declarative language to native Avalonia controls, keep C# expressions
typed, and make styles/tooling part of the compiler rather than runtime string
interpretation.

The strongest ideas to borrow are not XML syntax. They are Akbura's compact
single-component file, native template/markup-extension compatibility, and
cross-assembly semantic manifest. Its generated-control and ordered-hook models
solve different problems from Lucent's fragment and owner models and should not
replace them.

## Where the projects agree

| Area | Akbura | Lucent |
| --- | --- | --- |
| Platform | Native Avalonia controls and properties | Native Avalonia controls and properties |
| Compilation | Roslyn incremental generator over `.akbura`/`.akcss` additional files | Dedicated frontend generating C#, then Roslyn |
| Components | One component per file | One component per file |
| Expressions | Typed C# expressions in markup | Typed C# islands in declarative UI |
| Updates | Generated property/control updates | Generated fine-grained property and structural updates |
| Styling | Typed AKCSS compiled to Avalonia behavior | Typed CSS subset lowered to Avalonia styles |
| Status | Experimental | Experimental |

Sources: [Akbura overview](https://asaicraft.github.io/Akbura/),
[`AkburaCsGenerator.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.Furioso/AkburaCsGenerator.cs),
[`ComponentGenerator.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.Furioso/ComponentGenerator.cs),
and Lucent's [`ARCHITECTURE.md`](../ARCHITECTURE.md).

## 1. Prototype a focused one-root component form

Akbura's smallest stateful component is genuinely small:

```akbura
state int count = 0;

<Button Click={count++}>
    Count: {count}
</Button>
```

The filename supplies the component name, top-level declarations supply its
members, and the final element supplies its one root. This removes the repeated
`component`, state-wrapper, and `Fragment Render()` ceremony from the common
case. [Source](https://asaicraft.github.io/Akbura/#quick-start)

Lucent should not adopt XML to get that benefit. A bounded Lucent prototype
could combine two ideas already compatible with its model:

```csharp
component Counter()
{
    state int count = 0;

    Button {
        Content: $"Count: {count}";
        Click: () => count++;
    }
}
```

This would mean exactly the existing instance-owned `State<T>` and one-root
`Render()` contract. It must remain shorthand, not a second lifetime or update
model. Multiple roots, slots, structural regions, and unusual render logic can
keep explicit `Fragment Render()`.

**Recommendation:** prototype this only after the current compiler/tooling work
settles. Measure whether it materially improves Counter, Todo rows, and small
source-owned controls. Reject it if diagnostics or member/render disambiguation
become harder than the ceremony it removes.

This is the main authoring lesson from Akbura's focused format. Lucent's existing
language notes already treat `state T name = value` as a candidate, so the new
question is only whether a final root can be equally honest shorthand.

## 2. Deepen the native template seam

Akbura handles Avalonia properties marked with `TemplateContent` by generating
deferred content, and its item templates can infer an item type from
`ItemsSource`, expose a typed item variable, use compiled bindings, and capture
the item in event handlers.
[Sources: compatibility](https://asaicraft.github.io/Akbura/#compatibility),
[`ItemsControl` documentation](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/AkburaDocs/_pages/akbura/items-control.md),
and [`ComponentGenerator.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.Furioso/ComponentGenerator.cs).

Lucent currently has a deliberately narrow `ItemTemplate` form and rejects
events, components, slots, and structural regions inside it because Avalonia
owns realization and Lucent has no per-realization owner. That restriction is
correct today, but `ItemTemplate` should not become a pile of one-off lowering
rules.

**Recommendation:** when another template use case appears, deepen one internal
template module around Avalonia's native template metadata. It should own:

- template target discovery;
- typed data-context inference with an explicit fallback type;
- deferred control creation;
- generated binding/source rules;
- realization ownership and cleanup; and
- the exact feature subset allowed inside a realized template.

The interface should stay small: bind a template property and return typed
deferred native content plus diagnostics. Do not expose a generic template
framework until two native properties need it.

## 3. Add a narrow package semantic manifest

Akbura's build task embeds a versioned module manifest and project-relative
source resources. The manifest records component signatures, parameters,
injected services, commands, state, AKCSS modules, styles, utilities, generated
type names, and source spans. Tests load it from a compiled assembly and use it
as cross-assembly semantic input.
[Sources: `GenerateAkburaModuleManifest.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.Build.Tasks/GenerateAkburaModuleManifest.cs)
and [`AkburaBuildTargetsTests.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.UnitTests/AkburaBuildTargetsTests.cs).

This is a strong seam for Lucent packaging and tooling. A referenced assembly's
public Lucent shape should not require reparsing another project's workspace or
reverse-engineering generated C#.

**Recommendation:** define the smallest versioned manifest needed by a package
consumer:

- public component metadata name;
- parameter names, types, defaults, and source order;
- slot names and cardinality once those contracts stabilize;
- generated/native root facts needed for interop;
- public style/theme class metadata needed for completion; and
- source identity sufficient for navigation when source is available.

Do not copy Akbura's full manifest by default. Embedding complete application
source has package-size and source-disclosure costs. Public semantic metadata is
the useful module; embedded source should be an explicit packaging choice.

## 4. Make native compatibility a named, tested surface

Akbura explicitly documents compatibility for ordinary Avalonia controls,
bindings, markup extensions, and template content. `StaticResource`,
`DynamicResource`, and `Binding` use the same generated service-provider seam,
including target object/property and URI/type-resolution services. Custom markup
extensions are supported within documented limits.
[Sources: compatibility table](https://asaicraft.github.io/Akbura/#compatibility)
and [`markup-extensions.md`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/AkburaDocs/_pages/akcss/markup-extensions.md).

Lucent already has the right principle: resolve the real target property, keep
raw Avalonia/.NET available, and use explicit `binding(...)` only where Avalonia
should own observation. The missing lesson is product clarity. Native escape
paths should have a small compatibility matrix and executable examples rather
than being described only as "normal C# remains available."

**Recommendation:** document and test the exact native seams Lucent supports:

- direct properties and attached properties;
- routed and CLR events;
- compiled and raw bindings;
- static/dynamic resources;
- template content;
- custom/third-party controls; and
- host mounting and lifetime ownership.

This is more useful than chasing broad XAML compatibility.

## 5. Preserve Lucent's logical component model

Current Akbura generator tests assert that a generated component is a partial
class deriving from `AkburaControl`; its parameters are Avalonia properties and
the component can be placed directly in AXAML. `AkburaControl` owns one `Child`
and participates in Avalonia measure/arrange and the component tree.
[Sources: AXAML integration](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/AkburaDocs/_pages/akbura/axaml-integration.md),
[`AkburaControl.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura/AkburaControl.cs),
and [current generator tests](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.UnitTests/AkburaCsGeneratorTests.cs).

That gives Akbura excellent AXAML interoperability, but it couples every logical
component to a visual `Control`, one child, layout participation, and Avalonia
property-based parameters. Lucent's component can produce zero, one, or many
native roots without inserting a wrapper, while `MountRoot()` exposes an exact
single native root when the host needs one.

**Recommendation:** keep Lucent's current model. If AXAML embedding becomes a
real requirement, add an explicit generated adapter at that seam rather than
making every component a control. One hypothetical adapter is not enough reason
to change the core model.

## 6. Treat Akbura hooks as useful counter-evidence

Akbura supports `useEffect` with dependency arrays and tests hook count/order,
cleanup, cancellation, failed updates, nested registration, and update-loop
limits. The runtime fails when a render changes hook count or order.
[Sources: hooks overview](https://asaicraft.github.io/Akbura/#effects-and-hooks)
and [`UseHookRuntimeTests.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.UnitTests/UseHookRuntimeTests.cs).

Those tests are good engineering, but they also show the interface cost of an
ordered hook model. Callers must understand render-frame order, dependency-list
equality, cleanup timing, cancellation, and update stabilization.

**Recommendation:** do not copy hooks. Lucent's class-shaped instance and owned
state/computation model is the deeper module: lifecycle complexity stays behind
owner disposal rather than becoming an ordering rule every component author
must preserve. Revisit effects only with explicit ownership and compiler-derived
dependencies, as Lucent's current decisions already require.

## 7. Borrow AKCSS's precedence discipline, not its runtime

AKCSS separates ordinary Avalonia `Classes` from compiled AKCSS `class`, maps
normal and conditional declarations to Avalonia binding priorities, and resolves
utility conflicts per target property. Reactive style expressions subscribe to
Avalonia properties or `INotifyPropertyChanged` and reapply the cascade when a
dependency changes.
[Sources: AKCSS overview](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/AkburaDocs/_pages/akcss/index.md),
[inline styles](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/AkburaDocs/_pages/akcss/inline-akcss.md),
and [utility variants](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/AkburaDocs/_pages/akcss/utility-variants.md).

The useful lesson is to define style provenance and fallback precisely. As
Lucent adds application themes or global utility classes, it must decide how
component-scoped CSS, application Avalonia styles, local values, template
values, and pseudo-class styles compete. A disabled or inactive rule must reveal
the correct lower-priority value rather than overwrite it permanently.

**Recommendation:** continue generating ordinary Avalonia styles and use native
binding/style priority. Document one precedence model and test value restoration.
Do not add AKCSS-style reactive C# conditions, runtime cascade reevaluation,
interceptors, or variant conflict groups without a concrete application need.

## 8. Keep generated behavior inspectable

Akbura keeps generated-code assertions, source maps, parser/semantic benchmarks,
headless runtime tests, an AOT test application, diagnostics tooling, and a
feature gallery in the repository. The checked-in
`Counter.akbura.expected.cs` appears to represent an older generated base model,
so current generator assertions are the safer authority for present behavior.
[Source: repository tree](https://github.com/Asaicraft/Akbura/tree/61793dbbf12cbd563fc34d948ed008da23973bf3)
and [generator tests](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.UnitTests/AkburaCsGeneratorTests.cs).

Lucent already uses generated snapshots, headless tests, source mapping, and
tooling benchmarks. Akbura reinforces that these should remain first-class
package evidence rather than temporary compiler-development scaffolding.

## Priority

### Worth doing next

1. Design the narrow cross-assembly semantic manifest before package contracts
   harden.
2. Turn native Avalonia interop into a documented compatibility matrix with
   executable examples.

### Worth prototyping

3. Try the one-root/state shorthand against three small Lucent components.
4. Deepen template lowering only when a second native template property or
   richer item-template behavior is required.

### Keep as explicit non-directions

5. Do not adopt XML syntax merely for familiarity.
6. Do not make every Lucent component an Avalonia `Control`.
7. Do not adopt ordered hooks or AKCSS's reactive runtime cascade.
8. Do not switch compiler architecture solely because Akbura uses a Roslyn
   incremental generator; Lucent's shared compiler/language-server frontend has
   different tooling requirements.

## Sources consulted

- [Akbura published overview and compatibility table](https://asaicraft.github.io/Akbura/)
- [Akbura repository at `61793db`](https://github.com/Asaicraft/Akbura/tree/61793dbbf12cbd563fc34d948ed008da23973bf3)
- [`AkburaCsGenerator.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.Furioso/AkburaCsGenerator.cs)
- [`ComponentGenerator.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.Furioso/ComponentGenerator.cs)
- [`AkburaControl.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura/AkburaControl.cs)
- [`GenerateAkburaModuleManifest.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.Build.Tasks/GenerateAkburaModuleManifest.cs)
- [`AkburaBuildTargetsTests.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.UnitTests/AkburaBuildTargetsTests.cs)
- [`UseHookRuntimeTests.cs`](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/Akbura.UnitTests/UseHookRuntimeTests.cs)
- [Akbura AXAML integration](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/AkburaDocs/_pages/akbura/axaml-integration.md)
- [Akbura item templates](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/AkburaDocs/_pages/akbura/items-control.md)
- [AKCSS overview](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/AkburaDocs/_pages/akcss/index.md)
- [AKCSS utility variants](https://github.com/Asaicraft/Akbura/blob/61793dbbf12cbd563fc34d948ed008da23973bf3/AkburaDocs/_pages/akcss/utility-variants.md)
