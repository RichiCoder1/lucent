# Lucent Native milestones

Each milestone ends in a decision gate. Do not begin the next milestone merely because code exists; record the evidence and decision first.

## Milestone 1 — platform and rendering feasibility

### Work

1. Create the independent Native solution and package dependency proof.
2. Open, resize, scale, and close an SDL3-CS Windows window.
3. Retrieve and safely subclass its HWND.
4. Present a Skia-rendered retained scene.
5. Implement stable elements, deterministic dumps, and bounded row/column layout.
6. Render and measure shaped text.
7. Expose a minimal UIA root to Accessibility Insights.
8. receive and display SDL Unicode/IME composition events.
9. Publish and launch a NativeAOT build.

### Gate

- HWND/UIA integration is safe and observable.
- practical IME composition works without a delegated native control.
- resize, DPI, headless raster, and retained-scene invalidation are deterministic.
- NativeAOT publishes and launches.

If SDL blocks UIA, IME, or NativeAOT, repeat only the platform proof with direct Win32. Stop if that also fails.

## Milestone 2 — interactive reactive application

### Work

1. Implement the UI-thread signal graph, batching, scopes, and cycle diagnostics.
2. Add compiler-declared dependency registration alongside runtime tracking.
3. Implement `Show`, keyed `For`, and async computed ownership.
4. Add hit testing, bubbling, pointer capture, focus scopes, and keyboard traversal.
5. Implement the typed style/token core, fluent utilities, state variants, light/dark switching, and bounded animation.
6. Add composable Button, TextField, selection/filter, ScrollViewport, and virtualized list behaviors.
7. Build the functional issue-browser gauntlet.
8. Build the equivalent bounded Avalonia screen for authoring comparison, unless an existing example provides a fair comparison.

### Gate

- normal state, derived state, conditional regions, lists, and async flows remain obvious in straight C#;
- the application uses no imperative UI synchronization or general reconciliation;
- controls are keyboard operable and emit valid semantics;
- light/dark switching retains application state;
- the Lucent Native application is materially clearer than the Avalonia equivalent.

Stop if authoring is not materially simpler.

## Milestone 3 — accessibility and viability proof

### Work

1. Complete UIA roles, names, values, actions, enabled state, selection, and focus.
2. Complete practical single-line editing and UIA Value behavior.
3. Exercise the issue browser with Narrator and keyboard only.
4. Validate a virtualized 10,000-row list with bounded realized nodes and memory.
5. Measure 60 Hz interaction, responsive resize, idle work, memory, and invalidation.
6. Complete headless snapshots, native smoke tests, and failure diagnostics.
7. Publish NativeAOT and repeat rendering, interaction, IME, and UIA smoke checks.
8. Produce the final adoption report and immediate macOS/Linux risk register.

### Gate

- Narrator and keyboard can complete the agreed issue-browser walkthrough;
- Unicode/IME editing works in the custom field;
- interaction and resize meet a 16.7 ms frame budget on the recorded machine;
- idle schedules no frames;
- the 10,000-row workload remains virtualized and bounded;
- NativeAOT passes launch, interaction, rendering, and UIA smoke checks.

Stop on an accessibility wall or performance miss. Passing the gate permits an explicit decision to replace the unreleased Avalonia implementation; it does not perform that replacement automatically.

## Tracking ticket titles

The GitHub Project should use these implementation slices:

1. `[Native M1] Scaffold isolated solution and dependency/AOT proof`
2. `[Native M1] Prove SDL3 Windows host, HWND subclassing, UIA root, and IME`
3. `[Native M1] Build stable elements, retained scene, Skia renderer, and headless dumps`
4. `[Native M1] Implement bounded flex layout and text measurement`
5. `[Native M1] Record feasibility gate and SDL-versus-Win32 decision`
6. `[Native M2] Implement signal scheduler, scopes, batching, and async computed`
7. `[Native M2] Implement structural regions, input routing, focus, and semantics validation`
8. `[Native M2] Implement typed styles, tokens, fluent utilities, and basic animation`
9. `[Native M2] Implement composable controls and practical single-line text field`
10. `[Native M2] Implement scroll viewport and 10,000-row keyed virtualization`
11. `[Native M2] Build issue-browser gauntlet and Avalonia authoring comparison`
12. `[Native M2] Record interactive authoring gate`
13. `[Native M3] Complete UIA providers and Narrator/keyboard walkthrough`
14. `[Native M3] Add performance, idle, memory, and invalidation benchmarks`
15. `[Native M3] Pass NativeAOT end-to-end smoke`
16. `[Native M3] Publish final adoption and macOS/Linux follow-up report`
