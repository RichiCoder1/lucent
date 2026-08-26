# Lucent Native validation spike

## Status

**Planning.** This folder is intentionally independent of the existing Avalonia runtime and main solution.

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

Framework internals may be substantial if they remain coherent and testable. Raw line count is not itself a failure.

## Documentation

- [Architecture](ARCHITECTURE.md)
- [Milestones and gates](MILESTONES.md)
- [References and credits](REFERENCES.md)

## Explicit exclusions

- `.lui` syntax and compiler lowering;
- runtime CSS, selectors, specificity, or cascade;
- rich or multiline text editing;
- grid, flex wrapping, and layout animation;
- multiple windows, dialogs, and drag/drop;
- an interactive developer-tools inspector;
- macOS and Linux adapters before the Windows decision.

Headless tree, layout, style, semantic, and reactive dumps are included because they enable deterministic tests. They are not an interactive inspector.
