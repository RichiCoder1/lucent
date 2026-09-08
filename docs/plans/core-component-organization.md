# Component organization and framework-authored `.lui`

Status: approved direction, implementation pending in [#143](https://github.com/RichiCoder1/lucent/issues/143). Part of the correctness and quality work in [#119](https://github.com/RichiCoder1/lucent/issues/119). The implementation session owns delivery and coordination with active control/compiler changes.

## Outcome

Make a stock component's recipe, presentation and component-specific state easy to find together, then ship one useful stock composite authored in `.lui`. Keep the existing package and public `Lucent.Core.Components` type. A folder move is not a namespace or assembly migration.

The current `Components.cs` aggregates public recipes; `Controls.cs` combines themes, styles, state and internal configuration. Other controls already have dedicated files. The compiler references Roslyn and the generator references the compiler; neither project references Core. This makes same-assembly `.lui` generation plausible, but it still requires a clean-build proof against Core's own source symbols and the generated `Components` partial class.

## 1. Organize without changing behavior

Coordinate with the active owners of #123, #129 and #130 before moving their control/input files. Integrate their current changes or wait for a clean handoff; do not mechanically move files while another worker is editing them. Compiler fixes #126/#127 must be integrated before the generation proof. This work does not block those correctness fixes.

Inventory each public recipe, its internal configuration, state and tests. Split the aggregates into component-family folders. Suggested structure:

```text
src/Lucent.Core/
  Components/
    Button/
      Button.cs
      ButtonPresentation.cs
    TextField/
      TextField.cs
      TextFieldState.cs
    TextArea/
      TextArea.cs
    Layout/
      Layout.cs
      Row.cs
      Column.cs
      ResponsiveContainer.cs
    Scrolling/
      ScrollViewport.cs
      VirtualizedList.cs
    Status/
      Loading.cs
      Progress.cs
  Presentation/
    ControlThemes.cs
```

Adapt filenames to avoid confusing recipe and behavior classes. Keep all overloads for one recipe together. Public recipe methods remain members of `public static partial class Components` in namespace `Lucent.Core`; internal configuration may use a partial internal helper where that avoids an unrelated refactor. Keep only genuinely shared helpers shared, with a clear owner rather than a new catch-all utility file. Shared editor sessions, reactive runtime, input routing, layout engine and composition remain independent modules; do not move them into one component merely because it uses them.

Preserve signatures, parameter/default-content metadata, recipe/root identity, visibility, defaults, semantics, style precedence and behavior. Do not use this step to rename APIs, alter the control model or fix unrelated bugs. Update path-dependent verification scripts/docs and examples. Separate this change from generation and component behavior changes in reviewable commits.

## 2. Prove `.lui` generation in Core

Use the in-repository compiler/generator as build-time tooling. Prove the build graph works from a clean checkout without a previously built Core DLL, downloaded older Lucent package or checked-in generated source. Preserve source/metadata binding parity when `.lui` and C# primitives share the `Lucent.Core.Components` type. Prevent ambiguous primitive/composite resolution or accidental recursive calls.

Retain the ordinary application authoring rules: generated code must use the same supported recipe, style, ownership and semantic capabilities exposed to consumers. No framework-only compiler path, privileged template escape hatch, or runtime compiler dependency. Test build ordering, incremental rebuild after editing a `.lui` file and a C# primitive, design-time/editor resolution, and package consumption. The renderer and platform must not acquire compiler/Roslyn runtime dependencies. Preserve NativeAOT and the architecture checks.

If same-assembly generation exposes a blocker, reduce it to a focused compiler/build regression. Do not silently publish a new package to bypass it. A separate assembly is a possible later architectural decision, not a prerequisite or authorized outcome of this issue.

Public component XML documentation authored in `.lui` is tracked separately in [#152](https://github.com/RichiCoder1/lucent/issues/152). Until that lowering gap is closed, a thin documented public C# adapter may expose an internal generated composition without changing its ordinary runtime or package boundary.

## 3. Ship one substantive `.lui` stock composite

Default proving component: a small error notice with a message and an optional retry action, composed from stock layout, text and button primitives. Choose the final public name after checking existing names; the intended behavior is a presentation of recoverable errors, not an exception boundary. Prefer replacing a real repeated Issue Browser error/retry composition over adding an unused demonstration.

The component must exercise live message updates, conditional action content, ordinary typed callbacks and theme-backed styling in `.lui`. Accept application-owned error/retry state; do not add a persistence or async coordinator to the control. Preserve message accessibility, keyboard reachability, exactly-once invocation and normal disabled/busy command behavior where the consumer uses commands. Do not claim status/live-region semantics that the primitives do not provide. If a small reusable public primitive is necessary for honest semantics or root styling, specify and test it as part of the implementation; do not reach into internal projection or input state from markup.

Keep mounting, text shaping/editing, IME, low-level input, layout/virtualization and platform adapters in C#. `.lui` owns useful composition and presentation above those mechanisms. Thin public C# overload adapters are acceptable where they preserve an existing API, provided the component's substantive composition is authored in `.lui` and also exercised through `.lui` by its consumer.

Replace one actual Issue Browser error/retry view, retaining its stock theme and layout. Check applicability to Light Notes and use it there only if it fits the existing error presentation without changing app identity or adding application-specific knobs. If unsuitable, record the reason; do not manufacture a second migration. Package-only consumption must prove that consumers need neither Core's `.lui` source nor its build tools at runtime.

## Verification and completion

- File organization preserves public signatures and metadata, with affected existing Core and consumer contracts passing. Do not duplicate every test merely for moved files.
- A clean in-repository build generates the Core `.lui` component against current primitives; edits rebuild correctly without stale generated code or self-package dependency.
- A compiled `.lui` consumer exercises message changes, retry appearance/removal, invocation, focus, disabled behavior, root/semantic shape and disposal. Check that presentation wrappers do not break spacing or accessibility.
- One real-Skia render/geometry test covers the integrated stock presentation. Keep Windows accessibility/focus smoke targeted to any behavior the change actually affects.
- Existing applicable architecture, package-only and NativeAOT checks pass. Record toolchain/configuration and exact evidence without adding a release gate.
- Document how contributors find a component and choose C# primitives versus `.lui` composition. Record specific authoring/build gaps as follow-up issues with repros rather than broad speculative redesigns.
- Update the issue, roadmap and proving-consumer package pin after publication. Claim completion only when both the organization and the substantive `.lui` component are delivered.

## Deferred decisions

No new assembly, package or `Lucent.Core.Components` namespace in this slice: that name already identifies a public type, and partial types cannot span assemblies. Evaluate a later `Lucent.Controls` assembly only if this trial demonstrates a useful independently consumable layer with a clear dependency graph. Broad stock-control conversion, new template systems, shadcn-style distribution, full animation and general error boundaries remain separate work.
