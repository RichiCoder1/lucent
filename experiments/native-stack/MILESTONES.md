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

### Recorded SDL decision

`run-milestone-1-gate.ps1` is the combined gate. A passing
`evidence/milestone-1/proof.json` records **SDL3-CS / proceed** and validates
the issue #3 IME evidence plus the issue #4/#5 parity artifacts. Direct Win32
is not run when that SDL gate passes.

## Milestone 2 — interactive reactive engine

### Work

1. Implement the UI-thread signal graph, batching, scopes, and cycle diagnostics.
2. Keep an explicit dependency-registration API that a future compiler could call; do not implement compiler or `.lui` integration.
3. Implement `Show`, keyed `For`, and async computed ownership.
4. Add hit testing, bubbling, pointer capture, focus scopes, and keyboard traversal.
5. Implement the typed style/token core, fluent utilities, state variants, light/dark switching, and bounded animation.
6. Add composable Button, TextField, selection/filter, ScrollViewport, and virtualized list behaviors.
7. Build the functional issue-browser gauntlet as an integration probe.

### Recorded engine result

Issues #7–#11 proved the reactive graph, structural ownership, input/focus,
portable semantics, typed style core, controls, and bounded virtualization under
NativeAOT. The issue browser exposed that these engine modules did not yet form
an application-facing authoring interface: application code still rendered
directly, synchronized virtualization manually, and duplicated behavior and
semantic wiring.

That is a missing product layer, not evidence against the engine hypothesis.
Milestone 2A is the approved bounded attempt to add it.

### Recorded first authoring attempt

The frozen issue-browser rubric in `evidence/issue-13/` records **STOP**:
Native won 3/8 categories and trailed by two points in styling, missing both
predeclared scoring conditions. That result remains valid for commit `3d0da02`;
it is not rescored or weakened. It compared a mature Avalonia authoring surface
to a Native integration probe that bypassed its own style and retained-element
modules. The approved reframe registers a new attempt with prerequisites that
the first comparison did not require.

### Evidence

The original evidence remains under `evidence/issue-13/` and the frozen contract
remains in `GAUNTLET.md`.

## Milestone 2A — application authoring interface

The detailed contract and exclusions are in [AUTHORING-REFRAME.md](AUTHORING-REFRAME.md).

### Work

1. Put a small typed C# composition interface over stable elements: layout,
   text, controls, `Show`, keyed `For`, and virtualized lists.
2. Make the generic retained projection and renderer consume that element tree;
   application code must not draw on Skia or assign pixel geometry.
3. Bind reactive values and collections directly to element properties and
   virtualization. Remove application-level `Sync`/`SetItems` reconciliation.
4. Compose input, focus, semantics, and disposal as reusable behaviors owned by
   elements rather than by the application.
5. Complete the finite typed style interface needed by the issue browser:
   semantic tokens, layout, typography, background, border, radius, shadow,
   opacity, transform, state variants, theme switching, and bounded transition
   chaining. Do not add CSS or a selector engine.
6. Expose deterministic element, layout, resolved-style, reactive, and semantic
   dumps through the same interface used by tests.
7. Rewrite the Native issue browser using only this authoring interface.

### Gate

- the issue browser contains no canvas calls, renderer, direct bounds, manual
  semantic mirror, or application-level synchronization method;
- state, derived state, structure, async ownership, and virtualized collections
  are declared once and update through the reactive graph;
- normal styling is concise typed assignment/chaining, while state variants,
  themes, and reduced motion do not require application event handlers;
- reusable framework modules, not issue-browser helpers, own rendering, input,
  focus, semantics, and disposal;
- the frozen W1–W12 walkthrough still passes under NativeAOT and deterministic
  dumps identify the resolved tree and style state.

Stop if satisfying this gate requires a virtual DOM, general reconciliation,
runtime selectors, or application-specific framework hooks.

### Recorded decision

**PROCEED.** Issue #23 passes the absolute Milestone 2A gate. The Native issue
browser now uses the bounded typed composition interface, stale-retaining async
derived data, composition-owned keyed virtualization, mounted behaviors,
inherited typed themes, reduced-motion-aware transitions, complete projected
semantics, and generic SDL input translation. It contains no application
renderer, direct bounds/scene commands, `Sync`, or `SetItems`. W1–W12 and the
issues #20–#22 regressions pass from the recorded NativeAOT executable. Sol
xhigh's final adversarial review found no blocker or major finding.

## Milestone 2B — registered authoring comparison

### Work

1. Freeze a new comparison contract before scoring. Keep the existing Avalonia
   baseline, seed, walkthrough, and first-attempt evidence source-pinned.
2. Score the rewritten Native application against both Avalonia `.lui`/CSS and
   the bounded straight-C# Avalonia comparison where syntax is the confound.
3. Run a second small feature change selected before measurement and record
   touched sites, authored lines, imperative synchronization, and diagnostics.

### Gate

Native must:

- score at least 2 in every category;
- score 3 in reactive state/derived state, async/lifecycle ownership, and typed
  styling/state variants;
- beat Avalonia in at least three of the five hypothesis categories: reactive
  state, structural composition, async/lifecycle, styling/state variants, and
  change locality;
- have no two-point deficit in any category; and
- pass the change task without more touched authoring sites than Avalonia.

The rubric must reward concise composition, assignment, and chaining—not merely
the existence of a declarative file format. Visual polish and control breadth
are parity checks, not substitutes for authoring clarity.

Stop if the completed authoring interface still does not meet this gate.
Milestone 3 remains unauthorized until Milestone 2B records **PROCEED**.

### Recorded decision

**PROCEED.** The frozen Closed-filter task passed at exactly 667 results on both
implementations. Native scored 20 versus Avalonia's 14, met every minimum and
required category score, won three of five hypothesis categories, had no
two-point deficit, and touched one authored file/eight hunks versus Avalonia's
two authored files/eleven hunks. Sol xhigh independently verified the scores
and gate arithmetic after correcting the hunk counts. Milestone 3 is authorized.

### Evidence

Record the new contract hash, source commits, exact authored-file boundaries,
walkthrough artifacts, dumps, category rationales, and change-task diff. Keep
the first STOP result intact beside the new result.

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
