# Lucent Native architecture

## Frame pipeline

```text
application state
    -> reactive graph
    -> stable element tree
       -> layout tree
       -> focus/input behaviors
       -> semantic accessibility tree
    -> retained scene
    -> renderer abstraction
    -> Skia
    -> SDL3/Windows presentation
```

Each arrow is an explicit boundary. Platform or renderer details must not appear in application elements, layout, reactivity, or semantics.

## Reactive graph

The scheduler follows the useful semantics of alien-signals rather than copying its optimized JavaScript representation:

- writes push pending/dirty state to subscribers;
- computed values pull lazily and memoize their value;
- changed branches replace their tracked dependency links;
- batches flush observable effects once;
- scopes own effects, structural regions, async cancellation, and cleanup;
- cycles fail with named dependency paths;
- the graph is confined to the UI thread.

Straight C# uses bounded read tracking inside explicit reactive callbacks. Future `.lui` output may register compiler-derived edges directly. Both paths use the same graph and scheduling rules.

Async computed values reuse behaviors validated in the current Lucent experiment: cancellation, stale-value retention, latest-generation wins, pending/error facets, and UI-scheduler commits. This is design evidence, not a compatibility promise.

The current bounded implementation caps one runtime callback at 64 distinct
reads and requires the host UI loop to call `Drain` for queued async commits.
`RegisterDependencies(target, sources)` is the only compiler-facing direct-edge
API. It deliberately has no `.lui` lowering, cross-thread graph access, or
general observer layer.

## Elements and structural regions

Elements are stable identities with optional facets for layout, paint, input, focus, and semantics. Reactive properties update those facets in place.

Structural changes are explicit:

- `Show` owns one conditional region;
- keyed `For` owns ordered retained child regions;
- scopes dispose effects, captures, semantics providers, and scene nodes together.

There is no general description reconciliation or virtual DOM.

## Controls and events

Controls compose elements and reusable behaviors. A button combines press, focus, keyboard activation, semantics, and themed style behavior rather than subclassing a base control.

Pointer events target the deepest hit element and bubble through ancestors until handled. Pointer capture and focus scopes are explicit. A general tunneling phase is out of scope.

Interactive behaviors must provide valid accessibility semantics. Debug and test builds fail when required role, name, value, or action contracts are absent. An emergency suppression requires a written reason and remains visible in semantic dumps.

## Layout

The spike owns a bounded flex system:

- row and column direction;
- grow and shrink;
- gap and padding;
- start, center, end, stretch, and space distribution;
- fixed, minimum, maximum, and available sizing;
- intrinsic text measurement;
- scroll viewport and virtualized list placement;
- device-scale rounding.

The finite algorithm clamps each fixed or intrinsic main-axis base to its
minimum/maximum, subtracts padding and fixed gaps, then distributes positive
free space by `grow` or negative free space by `shrink × clamped-base`.
`start`, `center`, `end`, `space-between`, and `space-around` consume remaining
positive space; cross-axis start/center/end/stretch is resolved before the final
away-from-zero device-scale rounding. Invalid available sizes, negative values,
and duplicate identities fail at this boundary. The implementation is capped at
32 children; it has no grid, wrapping, percentage/calc, absolute positioning,
baseline generalization, or CSS box model.

Grid, wrapping, and absolute-layout generalization are deferred.

## Styling and themes

The style core is typed and immutable. Styles compose in explicit order; the later value wins for the same property.

```csharp
Styles.Compose(
    Theme.Button,
    Theme.Destructive,
    Style.Px(4),
    Style.MinWidth(120));
```

Fluent utilities are authoring sugar over the same typed values. Theme tokens are typed semantic keys with layered scopes. Light/dark switching updates resolved token dependencies without rebuilding application state.

State variants cover hover, pressed, focus-visible, selected, disabled, and validation state. Basic animation is limited to opacity, color, transform, and focus/hover transitions. One scheduler-owned clock respects the Windows reduced-motion preference.

There is no selector matching, specificity, implicit inheritance beyond documented token/text properties, or runtime CSS parser.

## Retained scene and rendering

Element changes mark layout, paint, or semantics facets dirty. Layout changes propagate only as far as required by constraints. Paint changes rebuild affected scene subtrees. Idle performs no rendering work.

Skia is hidden behind one deliberately narrow renderer interface so native presentation and headless raster tests share the same scene contract. This is a sanctioned test seam, not a commitment to multiple production renderers. The spike does not own shaders, glyph atlases, or a custom GPU backend.

For the current Windows proof, that contract targets a CPU `SKCanvas`: a Skia bitmap is uploaded to an SDL `ABGR8888` streaming texture and `SDL_RenderPresent` presents it through the SDL-owned HWND. `SDL_RenderReadPixels` captures the composed SDL renderer framebuffer before present for the native raster comparison. Resize reads SDL's current logical window size and recreates that upload texture; DPI is recorded from `GetDpiForWindow` alongside SDL's display scale. This is the selected present path for the spike, not a second renderer or host.

## Platform boundary

SDL3-CS is the provisional host. The Windows adapter owns:

- window lifecycle and event translation;
- native HWND retrieval and safe message subclassing;
- IME/text composition and clipboard integration;
- DPI, cursor, reduced-motion, and timing services;
- UIA provider transport, including `WM_GETOBJECT`.

If SDL ownership prevents correct UIA or text integration, replace only this adapter with direct Win32.

Milestone 1 must exercise the selected SDL or Win32 host, Skia present path, text shaper, IME path, and UIA provider together from a NativeAOT-published build. A minimal unrelated AOT executable is not evidence. UIA COM interop must use an AOT-compatible generated COM or `ComWrappers` path; built-in runtime COM marshalling is not assumed.

## Text

The spike supports a practical single-line field:

- Unicode and IME composition;
- caret, selection, arrows, home/end, deletion, and clipboard;
- focus and visible focus state;
- UIA Value behavior.

Rich text, multiline layout, text ranges, and editor-grade undo are excluded.

`SkiaSharp.HarfBuzz.SKShaper` receives an explicit HarfBuzz buffer direction,
script, and language. Its exact glyph IDs, clusters, positions, and cached
`SKTextBlob` are used for intrinsic advance and the retained-scene Skia seam;
measurement does not call a separate text API. Mixed-script proof input is
explicit Latin/CJK/Arabic runs with covering faces. It proves this shaping seam,
not automatic font fallback, Unicode script segmentation, or a UBA implementation.

## Test architecture

The portable core must run without a window. Headless tests exercise signals, layout, hit testing, focus, semantics, virtualization, and Skia raster captures. Deterministic dumps include:

- element identity and hierarchy;
- computed bounds;
- resolved styles, composition order, and tokens;
- pseudo/focus state;
- accessibility semantics;
- reactive dependency edges.

A smaller Windows-native suite validates HWND lifecycle, DPI, IME, UIA/Narrator integration, and NativeAOT behavior.

The same seeded scenes must produce equivalent element, layout, style, and semantic dumps in headless and native paths. Raster comparisons use an explicitly recorded tolerance where exact pixel equality is inappropriate.

## NativeAOT

NativeAOT is a gate, not an aspiration. Avoid reflection-based control discovery, runtime code generation, and unbounded metadata scanning. Prefer explicit or generated registration tables.
