# Lucent Native validation spike

## Status

**Milestone 1 SDL feasibility gate is reproducible.** This folder is intentionally independent of the existing Avalonia runtime and main solution.

Lucent Native asks whether Lucent should own the UI semantic stack while borrowing only platform integration, text shaping, and rendering. It is a falsifiable experiment, not a second supported backend.

If the spike passes, Lucent will replace the unreleased Avalonia implementation rather than preserve backend or source compatibility. If it fails, this folder should remain a documented result or be deleted.

## Product hypothesis

A .NET UI framework can provide a materially simpler application-authoring model than the current Avalonia-backed Lucent implementation by combining:

- Solid-style fine-grained reactivity and explicit structural regions;
- a typed immutable style core with Shadcn-inspired semantic tokens;
- Tailwind/GPUI-style fluent utilities over that typed core;
- composable interaction and accessibility behaviors instead of control inheritance;
- a retained element and render scene with deterministic invalidation;
- compiler-declared dependencies for future `.lui` code and bounded runtime tracking for straight C#.

The spike is Windows-first. macOS and Linux/Wayland are immediate wants only after the Windows evidence passes.

## Ownership boundary

Lucent owns:

- signals, computed values, effects, batching, ownership, and async generations;
- stable elements, keyed regions, layout, virtualization, and invalidation;
- typed styles, tokens, state variants, focus, input routing, and controls;
- the semantic accessibility tree and its validation rules;
- the retained scene projected to the renderer.

Lucent borrows:

- SDL3 platform windows and events, provisionally through SDL3-CS;
- Win32 services required for UI Automation, IME, clipboard, cursors, and DPI;
- Skia rendering behind a small renderer boundary;
- established font shaping and fallback machinery.

SDL3-CS is provisional. Milestone 1 must prove safe HWND/UIA integration, practical IME, and NativeAOT. The Windows adapter falls back to direct Win32 if any of those fail; the portable core does not change.

## Authoring model under test

The runtime-first spike uses straight C#. `.lui` lowering is deliberately deferred until the runtime model passes.

```csharp
var count = Signal(0);

Column(
    Text(() => $"Count: {count.Value}"),
    Button("Increment", () => count.Value++)
        .Style(Styles.Compose(
            Theme.Button,
            Theme.Primary,
            Style.Px(4),
            Style.Hover(x => x.Bg(Tokens.AccentHover)))));
```

Reactive reads are tracked only inside explicit reactive callbacks. A later compiler may provide static dependency tables to the same scheduler. Stable nodes update directly; `Show` and keyed `For` own structural regions. There is no general virtual DOM or runtime selector engine.

## Validation application

The gauntlet is a coherent Shadcn/Linear-inspired issue browser, not a control gallery. It must exercise:

- search, filters, selection, details, and editable title;
- a virtualized 10,000-row issue list;
- keyboard, pointer, focus, clipboard, Unicode, and IME interaction;
- asynchronous refresh, stale results, cancellation, failure, and retry;
- live light/dark themes and reduced-motion-aware transitions;
- UI Automation roles, names, values, actions, and focus.

## Decision rules

Stop the experiment if:

1. practical IME or UIA/Narrator support requires disproportionate machinery or delegating controls to another UI framework;
2. issue-browser application code is not materially clearer than an equivalent Avalonia implementation;
3. it misses the agreed interaction, idle, resize, virtualization, or memory evidence.

Framework internals may be substantial if they remain coherent and testable. Raw line count or elapsed time is not itself a failure. For this spike, "disproportionate machinery" means the bounded platform proof cannot satisfy UIA, real IME, or NativeAOT without delegating controls to another UI framework or introducing a second general UI stack.

## Documentation

- [Architecture](ARCHITECTURE.md)
- [Milestones and gates](MILESTONES.md)
- [References and credits](REFERENCES.md)
- [Adversarial plan review](REVIEW.md)

## Issue #2 acceptance evidence

`NativeStack.sln` contains `NativeStackProbe`, a `net9.0-windows` NativeAOT executable, and the separate non-AOT `UiaExternalHelper`. `run-probe.ps1` publishes `win-x64` and runs the published probe. A successful JSON record proves a hidden SDL window and nonzero HWND, generated `GetDpiForWindow`, a Skia raster/PNG, HarfBuzz shaping of `office` and Arabic, an available fallback font, and loaded SDL/Skia/HarfBuzz native modules. It exits nonzero if any probe fails.

This is dependency and NativeAOT evidence for issue #2 only. It does **not** pass the Milestone 1 UIA, IME, presentation, retained-scene, resize, DPI, or parity gates.

## Issue #3 slice A host proof

The default `run-probe.ps1` behavior remains the issue #2 dependency probe. The same published executable has explicit bounded host modes:

```powershell
# visible create/show/resize/pump/subclass/remove/destroy proof; exits after about a second
./run-probe.ps1 --automated

# visible IME target; type with a real Windows IME, then close the window
./run-probe.ps1 --manual .\ime-events.jsonl
```

Automated JSON exits nonzero unless stable/nonzero HWND and DPI, positive current scale, exact `SDL_SyncWindow` resize, shown/resized events, forced focus-loss (`SDL_HideWindow`), a real `WM_CLOSE`-driven SDL close-request event, duplicate `SetWindowSubclass` installation, exactly one callback for one `SendMessage`, removal with zero post-removal callbacks, and destruction all pass. It reports but does not require a display-scale-changed event because the host cannot force a monitor scale transition. It uses SDL's event pump only.

Manual mode sets `SDL_IME_IMPLEMENTED_UI=composition` before SDL initialization and calls `SDL_StartTextInput`, `SDL_SetTextInputArea`, and `SDL_StopTextInput`. Keep the native OS candidate UI enabled, select Japanese **あ** mode, compose, commit, cancel with an empty preedit, switch focus away/back, then close. The owned surface shows muted instructions, committed text, accent underlined preedit, and a moving caret; it does not render candidates. The requested JSONL path is AutoFlush-written for `editing`, `input`, focus, and one close record with committed and preedit text separated. Completed transcripts and visual review are recorded under [`evidence/`](evidence/README.md).

## Issue #3 slice B UIA proof

```powershell
./run-uia-proof.ps1

# lifecycle-only hand-written COM diagnostic; phase may be wrappers, create,
# qi, options, disconnect, or release
./run-probe.ps1 --uia-ccw-self-check release
```

For parent Accessibility Insights review, start the visible manual host, attach to the HWND recorded in `uia-ready.json`, then close its window (or create the close-signal file). It has the same Simple-only provider and lifecycle checks as the automated host; it adds no UIA tree or patterns.

```powershell
./NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe --uia-manual .\uia-ready.json .\uia-close.signal
```

The script performs a locked restore, warning-free isolated build, NativeAOT publish, then starts the published SDL host and a separate non-AOT UIAutomation client process. It fails closed unless the external Name/AutomationId assertion and provider property-call count pass. The isolated published hand-written CCW lifecycle check reaches wrapper construction, CCW creation, Simple QI, a native-vtable `ProviderOptions` call, disconnect, and release. The real external proof passes with exact Name/AutomationId across repeated and worker-thread reads, genuine `WM_GETOBJECT` delivery, property calls, Simple interface creation, successful disconnect, subclass removal, provider release, and HWND destruction.

The provider is Simple-only: server-side, immutable Name/AutomationId/ControlType snapshot, null pattern providers, and a native host provider. A dedicated hand-written `ComWrappers` vtable exposes exactly the SDK IUnknown and four `IRawElementProviderSimple` HRESULT slots. Its raw `VARIANT` writes BSTR/I4 values directly. COM callbacks do not call SDL or Skia.

Issue #19’s direct-Win32 discriminator is conditional and superseded by the passing SDL UIA proof. Do not begin Win32 IME or adapter work.

## Issue #4 retained-scene proof

```powershell
./run-scene-proof.ps1
```

The seeded fixed-bounds scene has explicit value IDs (`app.root`, `app.panel`, and `app.badge`), not traversal IDs. Layout, style, and semantic snapshots are keyed by those IDs and update only from their respective dirty projection paths; canonical dumps read snapshots rather than mutable elements. Separate seeded element/scene instances run in headless and native paths before their independently projected hierarchy, layout, style, and semantic dumps are compared.

The selected present path is CPU Skia raster (`SKBitmap`/`SKCanvas`) uploaded as an `ABGR8888` SDL streaming texture and presented with `SDL_RenderPresent` on the existing SDL HWND window. `SDL_RenderReadPixels` captures the SDL renderer framebuffer after `SDL_RenderTexture` and before present; it is converted to RGBA for comparison with the headless input raster. Each native present reads the current SDL logical size; the proof resizes from 128×96 to 256×192 through `SDL_SetWindowSize` + `SDL_SyncWindow`, recreates the upload texture, and requires positive display scale and nonzero `GetDpiForWindow` DPI.

`run-scene-proof.ps1` publishes the locked NativeAOT executable, requires exactly six capture artifacts in each run before comparing names and SHA-256 values, and fails on identity/facet/dump/raster/frame/present/resize failure. Idle and semantic-only writes cause zero scheduled native presents; the changed panel bounds cause one layout-driven present and the changed badge style causes one further paint-driven present. Its JSON records a zero-pixel tolerance.

## Issue #5 bounded layout and text proof

```powershell
./run-layout-proof.ps1
```

The dedicated NativeAOT self-check has a fixed 32-child row/column flex ceiling.
It checks grow/shrink, min/max clamping, padding/gap, start/center/end/stretch,
space-between/around, and 1.25×/2× rounding; it fails invalid constraints and
duplicate IDs. Its retained shaped runs use the pinned `SkiaSharp.HarfBuzz`
result for both intrinsic width and scene-seam glyph rasterization. Two runs
must yield exactly `layout-text.json` and `layout-text.png` with matching SHA-256
hashes. The dump records package versions, requested/resolved typefaces, glyph
counts, advances/bounds, direction, cultures, scales, and shared shape IDs.
Globalization is enabled so the NativeAOT proof can execute en-US/tr-TR; the
recorded published `win-x64` directory is 156,895,896 bytes (the prior
invariant-globalization output is not a comparable retained artifact).

## Issue #7 reactive graph proof

```powershell
./run-reactive-proof.ps1
```

The UI-thread-only graph has named signals, lazy memoized computed values,
batched effects, ownership disposal, and a 64-read cap for runtime dependency
tracking. `RegisterDependencies(target, sources)` is the single direct edge
registration seam for future compiler output; no compiler or `.lui` integration
is included. Async computed values retain their last result while pending,
cancel superseded owned work, and only commit the current generation through
the UI-thread `Drain` queue. The proof records zero frames for unrelated writes
and rejects stale/disposed completions. It does not provide cross-thread graph
access, automatic UI-loop pumping, or a general observer API.

## Milestone 1 combined gate

```powershell
./run-milestone-1-gate.ps1
```

The gate locked-restores, warning-free builds, and NativeAOT-publishes once,
then runs the selected SDL host lifecycle, text-state, UIA, retained-scene, and
layout/shaper checks from that published graph. It writes
`evidence/milestone-1/proof.json` with source, lock-file, machine, artifact,
and decision evidence. It validates the existing issue #3 real Japanese IME
transcripts and issue #4/#5 artifact sets and hashes; those transcripts remain
supplemental manual SDL/Windows evidence, not an automated OS IME claim.

## Explicit exclusions

- `.lui` syntax and compiler lowering;
- runtime CSS, selectors, specificity, or cascade;
- rich or multiline text editing;
- grid, flex wrapping, and layout animation;
- multiple windows, dialogs, and drag/drop;
- an interactive developer-tools inspector;
- macOS and Linux adapters before the Windows decision.

Issue #3's real-IME and Accessibility Insights evidence is recorded under [`evidence/`](evidence/README.md). The broader Narrator walkthrough remains a later Milestone 3 gate.

Headless tree, layout, style, semantic, and reactive dumps are included because they enable deterministic tests. They are not an interactive inspector.
