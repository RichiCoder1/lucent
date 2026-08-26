# Lucent Native milestones

Each milestone ends in a decision gate. Do not begin the next milestone merely because code exists; record the evidence and decision first.

## Milestone 1 — platform and rendering feasibility

### Work

1. Create the independent Native solution and package dependency proof.
2. Evaluate SDL3-CS against the official SDL C# bindings and record the binding choice.
3. Select and pin the Skia present path and shaping/font-fallback stack.
4. Open, resize, scale, and close an SDL3-CS Windows window.
5. Retrieve and safely subclass its HWND.
6. Present a Skia-rendered retained scene.
7. Expose a minimal UIA root through an AOT-compatible COM path.
8. Prove a minimal editable IME harness: composition start/update/commit/cancel, preedit versus committed text, caret-relative candidate positioning, focus loss, and one real non-Latin Windows IME.
9. Implement stable elements, deterministic dumps, and bounded row/column layout.
10. Render and measure shaped text, including ligatures, combining marks, bidi/RTL, fallback fonts, emoji, and DPI scaling.
11. Publish and launch the combined host + Skia + shaper + IME + UIA proof with NativeAOT and trimming enabled.

### Gate

- HWND/UIA integration is safe and observable.
- practical IME composition works without a delegated native control and passes the agreed composition matrix.
- resize, DPI, headless raster, native presentation, and retained-scene invalidation are deterministic.
- matching seeded headless/native scenes produce equivalent structural, layout, style, and semantic dumps.
- the complete selected native dependency/UIA graph publishes, launches, and executes under NativeAOT.

If SDL blocks UIA, IME, or NativeAOT, run only the separately tracked bounded direct-Win32 platform proof. "Redirect" means only that retry. Stop if it also fails; Milestone 2 may not begin on a generic redirect.

### Evidence

Record reproducible commands, source/dependency identities, machine-readable test results, headless/native parity artifacts, and the selected platform decision. Screenshots or manual statements may supplement but cannot replace executable evidence.

## Milestone 2 — interactive reactive application

### Work

1. Implement the UI-thread signal graph, batching, scopes, and cycle diagnostics.
2. Keep an explicit dependency-registration API that a future compiler could call; do not implement compiler or `.lui` integration.
3. Implement `Show`, keyed `For`, and async computed ownership.
4. Add hit testing, bubbling, pointer capture, focus scopes, and keyboard traversal.
5. Implement the typed style/token core, fluent utilities, state variants, light/dark switching, and bounded animation.
6. Add composable Button, TextField, selection/filter, ScrollViewport, and virtualized list behaviors.
7. Build the functional issue-browser gauntlet.
8. Before judging either side, freeze one common issue-browser feature walkthrough and rubric. Build a source-pinned Lucent-on-Avalonia baseline for that walkthrough and record the `.lui`/CSS versus C# authoring confound separately.

### Gate

- normal state, derived state, conditional regions, lists, and async flows remain obvious in straight C#;
- the application uses no imperative UI synchronization or general reconciliation;
- controls are keyboard operable and emit valid semantics;
- light/dark switching retains application state;
- the Lucent Native application is materially clearer than the Avalonia equivalent.

Stop if authoring is not materially simpler.

### Evidence

Record the shared walkthrough, source commits, build mode, exclusions, and rubric before comparison. Score state/derived state, structure, async flows, styling, accessibility, lifecycle, testing, and total integration separately. Store executable interaction results and the completed rubric.

## Milestone 3 — accessibility and viability proof

### Work

1. Complete UIA roles, names, values, actions, enabled state, selection, and focus.
2. Complete practical single-line editing and UIA Value behavior.
3. Exercise the issue browser with Narrator and keyboard only.
4. Validate a virtualized 10,000-row list with bounded realized nodes and memory.
5. Measure 60 Hz interaction, responsive resize, idle work, memory, and invalidation.
6. Complete headless snapshots, native smoke tests, and failure diagnostics.
7. Publish NativeAOT and repeat rendering, interaction, IME, and UIA smoke checks.
8. Verify final dependency licenses, attributions, notices, and native assets from the actual published output.
9. Produce the final adoption report and immediate macOS/Linux risk register.

### Gate

- Narrator and keyboard can complete the enumerated issue-browser walkthrough with zero emergency semantic suppressions in the final dump;
- Unicode/IME editing works in the custom field;
- interaction and resize meet a 16.7 ms frame budget on the recorded machine;
- idle schedules no frames;
- the 10,000-row workload realizes at most three times the visible row count;
- over 500 recorded interactions, p95 input-to-present is at most 16.7 ms and p99 is at most 33.3 ms;
- ten idle seconds schedule zero frames;
- after warm-up and 20 full-list scroll cycles, live managed memory growth is at most 16 MiB and returns within 10% plus 8 MiB of the post-warm baseline after collection;
- NativeAOT passes launch, interaction, rendering, and UIA smoke checks.

Stop on an accessibility wall or performance miss. Passing the gate permits an explicit decision to replace the unreleased Avalonia implementation; it does not perform that replacement automatically.

## Tracking

The live implementation slices and dependencies are maintained in the [Lucent Native GitHub Project](https://github.com/users/RichiCoder1/projects/4/views/1). Do not duplicate the issue-title catalog here.
