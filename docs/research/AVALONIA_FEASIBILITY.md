# Avalonia feasibility review for Lucent

**Review date:** 2026-08-13  
**Scope:** Constraints in current Avalonia and .NET/Roslyn behavior that materially affect the Lucent proposal in `docs/ARCHITECTURE.md`, `docs/STYLING.md`, `docs/TOOLING.md`, `docs/LANGUAGE.md`, and `docs/ROADMAP.md`.

## Decision summary

Lucent’s proposed boundary is feasible: generate ordinary C# that creates and updates real Avalonia controls, while keeping Lucent component identity, dependency analysis, and lifecycle bookkeeping in Lucent. Avalonia already supports code-only UI, property observables, compiled bindings, styles, control themes, and standard .NET collection/property notification adapters.[9][14][15]

The proposal needs five firm constraints:

1. Treat Avalonia property priority as part of the generated-code contract. A direct `SetValue`/binding is normally a local value, which outranks style and pseudo-class setters; a Lucent reactive update can accidentally disable hover, pressed, focus, theme, or animation styling.[1]
2. Treat logical and visual trees as different projections. Resources, `DataContext`, and ordinary selectors use the logical tree; templates add visual-only parts, and template selectors are an explicit boundary.[2][4]
3. Make the runtime scheduler UI-thread aware. Control creation, property access, layout, rendering, input, and structural tree mutation must be marshalled to Avalonia’s UI dispatcher.[3]
4. Make generated metadata the default. Native AOT forbids runtime code generation and dynamic loading and depends on trim analysis; reflection-heavy discovery, string binding paths, and late component loading are incompatible or require explicit preservation.[9][24][25]
5. Keep hot reload and accessibility as explicit integration layers. Avalonia’s public hot-reload direction is not a foundation Lucent can assume, while custom controls and generated components need deliberate automation semantics.[20][21][22]

## Constraints and design consequences

### Property priorities and bindings

Avalonia resolves competing values using an ordered priority system. The documented order is animation, local value, style trigger, template, style, inherited, then unset/default.[1] A code assignment or a binding created directly on a control is generally a local value, so it wins over a class selector or pseudo-class trigger. Avalonia documents `SetCurrentValue` as the mechanism for changing the current effective value without creating a new local-value entry.[1]

This is the most consequential constraint for Lucent’s “direct reactive property update” claim. Generated code must distinguish at least:

- **instance values**: values intentionally owned by the Lucent node, which may be local values;
- **style-owned values**: values lowered into Avalonia styles/control themes so pseudo-classes and theme precedence remain effective;
- **template-owned values**: values applied inside a control template at template priority; and
- **binding/observable values**: values represented by an Avalonia binding or subscription with an explicitly selected priority.

For a reactive `Button.background` assignment, blindly calling `SetValue` on every invalidation will make `.button:pointerover` and `:pressed` styling unable to win. The compiler should therefore lower CSS declarations to style setters, and lower a Lucent property to a local value only when the language says that the component owns that value. Where runtime state needs to update a value without taking ownership from styles, the runtime should use `SetCurrentValue` or an equivalent supported binding strategy and test the resulting precedence.

Avalonia’s binding modes also matter to Lucent’s event/state model. `OneWay` propagates source-to-target, `TwoWay` synchronizes both directions, `OneTime` reads once, and the default is chosen by the target property; for example, `TextBlock.Text` is normally one-way while `TextBox.Text` is normally two-way.[10][11] This supports generated property-specific updates, but Lucent must not infer two-way behavior merely because a value is mutable in C#; it should use the target property’s declared default or an explicit language rule.

Avalonia provides compiled bindings that validate paths at build time and avoid reflection, while `ReflectionBinding` remains an escape hatch for dynamic paths.[9][12] In code-only UI, `CompiledBinding.Create` takes a typed expression and `GetObservable` exposes a property observable.[14] These APIs fit Lucent’s compiler-visible dependency analysis: prefer generated typed expressions or direct property subscriptions, and reserve reflection/string paths for explicitly dynamic interop.

### Logical tree, visual tree, styles, and selectors

Avalonia maintains two parallel trees. The logical tree contains declared controls and carries resource lookup and `DataContext` inheritance; the visual tree includes template parts and drives rendering, layout, hit testing, and routed events.[2] A control template’s contents are visual-tree elements but not logical-tree children.[2][19]

Lucent’s component tree must therefore remain its own logical identity model while projecting controls into Avalonia’s logical tree. A component that emits several controls should not become an Avalonia control solely to obtain identity. Conversely, a projected control must be attached/detached using Avalonia’s tree/lifecycle rules, and structural changes must not assume that the visual tree is the same shape as the source component tree.

Avalonia style selection walks the logical tree upwards, and selectors support type, class, pseudo-class, descendant, child, name, negation, and template traversal forms.[4][5] The `/template/` selector is a deliberate escape into a control’s visual/template subtree; ordinary selectors do not cross that boundary.[4] This means Lucent’s initial CSS subset can lower cleanly to logical-tree selectors, but CSS scoping and descendant semantics must specify whether they target Lucent component boundaries, Avalonia logical parents, or template parts. “CSS class on a component” cannot be assumed to style arbitrary descendants unless Lucent deliberately projects that class to the relevant Avalonia controls.

Classes and pseudo-classes are not interchangeable. User style classes are assigned to a control’s `Classes` collection, while pseudo-classes are control-owned state labels such as `:pointerover`, `:pressed`, `:focus`, and `:checked`.[6] A custom control can expose a pseudo-class from control logic, but a plain Lucent component cannot safely invent a pseudo-class on a child without deciding which projected control owns the state. Lucent should initially map author classes to `Classes`, map supported Avalonia pseudo-classes through selectors, and model component state with generated classes or explicit style values unless it generates a real custom control.

Style order and control themes add another constraint. Avalonia evaluates matching styles in declaration order, with later declarations winning for equal specificity; control themes can override ordinary styles and include pseudo-class styles.[8] Control themes are the default visual contract for templated controls, while application styles are for broader application/component styling.[5] Lucent’s CSS compiler should emit deterministic style order and preserve the distinction between ordinary styles and control themes rather than treating all declarations as one flat cascade.

### Templates and control projection

Templated controls separate behavior from appearance and require a default `ControlTheme` containing a `ControlTemplate`.[7] Consumers may replace the template, so template parts are optional and custom-control code must null-check `PART_` lookups after `OnApplyTemplate`.[7] `TemplateBinding` is limited to one property and one-way mode; property paths or two-way behavior require a full relative-source binding.[7]

This limits a “universal Lucent component” strategy. Source-owned Lucent components can remain lightweight logical instances whose `Render()` methods compose controls, but reusable controls with replaceable themes need an Avalonia control surface: registered `StyledProperty` fields, a control theme/template, pseudo-class ownership, and lifecycle-safe part access. Lucent should expose an escape hatch to raw/custom/third-party controls exactly as the architecture proposes, and should not hide template replacement behind a generic component abstraction.

The property kind is also semantically important. Avalonia styled properties participate in styling, animation, and value precedence; direct properties are lighter but do not provide the full styled-property behavior.[18] A projection generator should classify each target property and reject or diagnose a Lucent feature that requires styling, inheritance, coercion, or animation when the target is only a direct property.

Structural updates have lifecycle restrictions. Avalonia documents constructors, `OnApplyTemplate`, routed handlers, and `Loaded` as safe places to add/remove logical children, and warns against doing so from `OnPropertyChanged` or `DataContextChanged` callbacks while the framework may be walking the tree.[2] Lucent’s reactive invalidation must therefore separate “mark dirty” from “mutate structure”: property invalidation can update a property directly, but conditional/keyed region reconciliation must enqueue a dispatcher-phase structural operation.

### Thread affinity and notification interop

Avalonia uses a single-threaded UI model. Control creation, layout, rendering, input, and control property access must occur on the UI thread; cross-thread access can throw `InvalidOperationException`, and `Dispatcher.UIThread` is the supported marshal point.[3] Lucent’s effect scheduler and generated event handlers need an explicit UI-dispatch boundary, especially when effects await background work or consume external observables.

Avalonia’s code-level property observables emit the current value upon subscription and subsequent changes, and the returned subscription is disposable.[17] This maps well to Lucent-owned subscriptions and cleanup, but the runtime should retain and dispose each subscription with the logical component instance rather than treating an observable as an unbounded global stream.

For .NET interop, `ObservableCollection<T>` emits `INotifyCollectionChanged` events that Avalonia list controls consume for add/remove/reorder; item property changes still require `INotifyPropertyChanged`, and replacing the collection property requires property notification.[16] Lucent’s proposed adapters are therefore viable, but keyed regions must define how `NotifyCollectionChangedAction.Move`, `Replace`, and `Reset` map to identity. A collection event is not enough to infer stable logical identity without the Lucent key expression.

### Build integration, source generation, and AOT

Avalonia compiles AXAML at build time into IL that constructs the visual tree, and compiled bindings provide build-time path validation and avoid runtime reflection.[13] Its MSBuild targets expose Avalonia XAML as Roslyn `AdditionalFiles`, specifically so source generators and other Roslyn extensions can see the files alongside the compilation.[23] Roslyn’s generator context exposes both `Compilation` and `AdditionalFiles`, and generators can add source and diagnostics.[27]

Lucent can therefore integrate as a normal MSBuild/Roslyn build participant: include `.lui` and `.css` as additional inputs, run the Lucent frontend, emit deterministic generated C# via `AddSource`, and report diagnostics with source spans. The Lucent compiler need not fork Roslyn, but it must own parsing and semantic analysis for `.lui`; Roslyn remains the host for generated C# compilation and ordinary .NET diagnostics. Build ordering, incremental inputs, generated-file naming, and source mapping are not optional polish because they determine whether IDE and command-line builds see the same symbols and diagnostics.

Native AOT imposes the hard runtime ceiling. Native AOT does not support dynamic loading or runtime code generation, requires trimming, and has known limitations around reflection and interpreted expression trees.[28] The trimmer cannot reliably preserve members reached through runtime-determined types, member names, or assemblies; Microsoft recommends eliminating reflection, annotating statically known patterns, or using source generation, and says trim warnings should be treated as potential behavior changes or crashes.[25][26]

Consequences for Lucent are favorable if enforced early: generated constructors, property accessors, event hookups, style registrations, component tables, and route/slot metadata should be statically emitted. Runtime discovery by component name, reflection-based property bags, dynamic assembly loading, and reflection-heavy hot paths should be explicit opt-in features with documented non-AOT status. AOT should be a publish-time validation lane for the dogfood app, not a promise inferred from a successful JIT build. Avalonia’s compiled-binding path and Lucent’s own generated dependency graph should be the default; any reflection interop must carry trim/AOT annotations or produce a build diagnostic.

### Hot reload

Avalonia’s own public roadmap discussion describes Hot Reload as a later-phase feature and calls it a significant engineering challenge.[21] The long-standing open XAML hot-reload issue also shows that built-in reload is not a stable capability to assume in the core framework.[22] This does not block Lucent’s roadmap item, but it changes its dependency: Lucent must build hot reload around its own compiler artifacts, generated implementation boundaries, and state identity, then patch the existing Avalonia projection. It should not depend on Avalonia replacing templates or rebuilding the entire visual tree while preserving Lucent state.

The architecture’s proposed “compile affected region → patch implementation → retain compatible state” is consequently the right seam, but it needs a stable generated component-instance contract and a development-only protocol for disposal/reconciliation. Initial releases should label hot reload experimental and continue to support restart/rebuild.

### Accessibility

Avalonia’s accessibility model is based on automation peers; built-in controls provide peers for platform APIs, while a custom control may need to override `OnCreateAutomationPeer` and provide a custom peer.[20] Attached `AutomationProperties` supply names, IDs, labels, live settings, and other metadata.[20]

Lucent components should not attempt to replace Avalonia’s accessibility tree. The projection layer should forward accessibility properties to the concrete control and provide authoring support for `AutomationProperties.Name`, labels, IDs, and live announcements. If Lucent generates a custom Avalonia control, its generated skeleton must include an automation-peer extension point and tests for the relevant platform adapters. When a lightweight component instance composes standard controls, accessibility comes from those controls, but generated wrappers must not accidentally hide or duplicate them in the logical/automation tree.

## Recommended changes to the proposal

The current Lucent direction should proceed with these explicit contracts:

- Add a **value-ownership/lowering table** to the IR: local value, binding, style setter, style trigger, template setter, inherited value, or `SetCurrentValue` update.
- Define **two identities** in the runtime: Lucent logical instance identity and Avalonia control identity. Structural reconciliation operates on the former; tree attachment and template lifecycle operate on the latter.
- Make every structural effect **dispatcher-enqueued and lifecycle-aware**; never mutate Avalonia logical children from a property notification callback.
- Make generated code and metadata the **AOT-safe default**. Add publish tests with trimming/AOT warnings treated as failures for the dogfood app.
- Treat CSS as a typed compiler frontend targeting Avalonia’s selector/style model, not as browser CSS. Document the logical-tree boundary and reserve `/template/`/control-theme generation for explicit control authoring.
- Keep raw control projection first-class, including styled/direct property classification, template/theme inclusion, events, `AutomationProperties`, and nullable template-part handling.
- Defer hot reload until generated identity, disposal, source maps, and structural patching have executable tests; do not count on Avalonia’s built-in hot reload.

## Bottom line

Avalonia is a strong renderer/runtime target for Lucent’s proof of concept, particularly because code-only UI, typed compiled bindings, direct property observables, styles, and control themes already expose the integration seams Lucent needs.[9][14][15] The risky work is not creating a window; it is preserving Avalonia’s property precedence, logical/visual/template boundaries, dispatcher rules, notification semantics, accessibility contracts, and AOT-safe build shape while adding Lucent’s finer-grained identity and reactivity. If those are encoded in the IR and projection layer, the roadmap’s compiler-first architecture remains credible. If they are hidden behind generic rerendering or local-value assignments, styling, templates, AOT publishing, and accessibility will fail in ways that are difficult to diagnose.

## Sources

[1] https://docs.avaloniaui.net/docs/properties/value-precedence — Avalonia property value precedence
[2] https://docs.avaloniaui.net/docs/fundamentals/visual-and-logical-trees — Avalonia visual and logical trees
[3] https://docs.avaloniaui.net/docs/app-development/threading — Avalonia threading model
[4] https://docs.avaloniaui.net/docs/styling/style-selectors — Avalonia style selectors
[5] https://docs.avaloniaui.net/docs/styling/styles — Avalonia styles
[6] https://docs.avaloniaui.net/docs/styling/style-classes — Avalonia style classes
[7] https://docs.avaloniaui.net/docs/custom-controls/templated-controls — Avalonia templated controls
[8] https://docs.avaloniaui.net/docs/styling/style-best-practices — Avalonia styling best practices
[9] https://docs.avaloniaui.net/docs/data-binding/compiled-bindings — Avalonia compiled bindings
[10] https://docs.avaloniaui.net/docs/data-binding/data-binding-syntax — Avalonia data binding syntax
[11] https://docs.avaloniaui.net/docs/data-binding/binding-classes — Avalonia introduction to data binding
[12] https://docs.avaloniaui.net/docs/data-binding/binding-debugging — Avalonia binding debugging
[13] https://docs.avaloniaui.net/docs/xaml/compilation — Avalonia XAML compilation
[14] https://docs.avaloniaui.net/docs/fundamentals/coded-ui — Avalonia code-only UI
[15] https://docs.avaloniaui.net/docs/app-development/performance — Avalonia performance optimization
[16] https://docs.avaloniaui.net/docs/how-to-bind-to-a-collection — Avalonia collection binding
[17] https://v11.docs.avaloniaui.net/docs/guides/data-binding/binding-from-code — Avalonia binding from code
[18] https://docs.avaloniaui.net/docs/custom-controls/defining-properties — Avalonia defining properties
[19] https://docs.avaloniaui.net/docs/custom-controls/control-trees — Avalonia control trees
[20] https://docs.avaloniaui.net/docs/app-development/accessibility — Avalonia accessibility
[21] https://github.com/AvaloniaUI/Avalonia/discussions/16997 — Avalonia Accelerate roadmap discussion: hot reload
[22] https://github.com/AvaloniaUI/Avalonia/issues/3266 — Avalonia XAML hot reload issue
[23] https://github.com/AvaloniaUI/Avalonia/blob/main/packages/Avalonia/AvaloniaBuildTasks.targets — Avalonia build tasks targets
[24] https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot — .NET Native AOT deployment overview
[25] https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-concepts — .NET trim analysis concepts
[26] https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/fixing-warnings — .NET fixing trim warnings
[27] https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.sourcegeneratorcontext — Roslyn SourceGeneratorContext
[28] https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings — .NET Native AOT warnings
