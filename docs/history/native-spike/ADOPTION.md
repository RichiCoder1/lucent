# Lucent Native adoption decision

> Historical and superseded for execution. This file records the validation spike's decision and evidence; active production work follows [`../../ARCHITECTURE.md`](../../ARCHITECTURE.md) and [`../../ROADMAP.md`](../../ROADMAP.md).

## Decision

**Replace the unreleased Avalonia runtime with the Lucent Native architecture.**

This is a direction, not a claim that the experimental tree is production-ready or should be copied into `src/` unchanged. The spike proved the risky parts of owning the stack on Windows: native hosting, rendering and shaping, retained composition, reactivity, typed styling, controls, virtualization, practical IME, retained-tree UI Automation, performance, and NativeAOT packaging. It also showed that application code can be materially clearer once those pieces are exposed through one composition interface.

Do not build a second backend, preserve the Avalonia runtime as a compatibility path, or bridge the old manifest and styling behavior. Lucent is unreleased and has one user. Carrying both designs would keep the complexity we ran the spike to remove.

## Gate results

| Gate | Result | Evidence |
| --- | --- | --- |
| Milestone 1, platform feasibility | Pass | SDL HWND ownership, Skia presentation, HarfBuzz shaping and fallback, Japanese IME evidence, retained UIA transport, deterministic scene parity, and the [combined NativeAOT gate](https://github.com/RichiCoder1/lucent/blob/archive/avalonia-final/experiments/native-stack/evidence/milestone-1/proof.json). |
| Initial Milestone 2 authoring comparison | Stop, correctly | The [first comparison](https://github.com/RichiCoder1/lucent/blob/archive/avalonia-final/experiments/native-stack/evidence/issue-13/gate.json) exposed an issue browser that bypassed the framework surface and left rendering, geometry, synchronization, behavior, and semantics in application code. |
| Milestone 2A, application-facing composition | Pass | `NativeUi` owns retained mounting, reactive binding, keyed virtualization, input, focus, semantics, typed styles, transitions, projection, and disposal. The [rewritten issue browser](https://github.com/RichiCoder1/lucent/blob/archive/avalonia-final/experiments/native-stack/evidence/issue-23/proof.json) contains none of the forbidden application seams. |
| Milestone 2B, registered comparison | Pass | The [frozen comparison](https://github.com/RichiCoder1/lucent/blob/archive/avalonia-final/experiments/native-stack/evidence/issue-24/proof.json) scored Native 20 and Avalonia 14. The useful difference came from reactive state, structural composition, typed style chaining, async ownership, and change locality, not from comparing a half-built renderer with a polished control catalog. |
| Milestone 3, accessibility | Pass | The [accessibility proof](https://github.com/RichiCoder1/lucent/blob/archive/avalonia-final/experiments/native-stack/evidence/issue-14/proof.json) covers the keyboard/Narrator walkthrough, retained child providers and patterns, external UIA automation, zero emergency semantic suppressions, and manual Accessibility Insights review. |
| Milestone 3, viability | Pass | The [performance proof](https://github.com/RichiCoder1/lucent/blob/archive/avalonia-final/experiments/native-stack/evidence/issue-15/proof.json) records 500 semantic-input-to-present samples at p95 8.80 ms and p99 9.86 ms, plus 500 resize samples at p95 9.49 ms. Ten idle seconds scheduled and presented zero frames. The 10,000-row list realized 21 rows against a limit of 51, and 20 full-list cycles returned to the managed-memory budget. |
| Milestone 3, distribution | Pass | One [clean `win-x64` NativeAOT/trimming publish](https://github.com/RichiCoder1/lucent/blob/archive/avalonia-final/experiments/native-stack/evidence/issue-16/proof.json) passed launch, rendering, interaction, IME-state, UIA, accessibility, virtualization, parity, and performance checks. Its exact native assets and all locked dependency obligations are recorded with shipped notices. |

None of the kill criteria fired:

- IME and UIA worked without delegated controls or a second UI framework.
- The final application-facing API was materially clearer than the Avalonia baseline on the frozen task.
- Interaction, resize, idle, virtualization, memory, accessibility, and NativeAOT stayed inside the predeclared gates.

## What actually differentiated the stack

The renderer was not the main win. Skia and SDL are replaceable adapter details. The useful result is the model above them:

- runtime dependency tracking with lazy memoized derived state, batching, latest-generation async results, named cycles, and scope-owned cleanup;
- stable retained elements and explicit structural regions without a virtual DOM or general reconciliation pass;
- typed composition where normal styling is concise assignment and chaining, while state variants, themes, transitions, and reduced motion remain framework-owned;
- controls built from behavior, semantics, focus, and composition instead of a parallel inheritance hierarchy;
- deterministic tree, layout, style, semantic, and raster evidence that makes the stack testable without scraping a live window;
- NativeAOT as a standing constraint rather than packaging work deferred until release.

The cost is also clear. Lucent now owns platform accessibility, IME, window/input integration, control behavior, and debugging tools. The Windows UIA work was difficult and ABI-sensitive. That is acceptable because it stayed behind a bounded adapter and passed real external clients, but the same risk must be disproved independently on macOS and Wayland.

## Migration and deletion

Migrate forward. Do not keep Avalonia and Native implementations alive in parallel.

1. Promote the portable reactive, composition, style, layout, scene, control, and diagnostic modules from the experiment into normal `src/` projects. Keep Windows hosting/UIA/IME in a separate adapter project.
2. Make the C# `NativeUi` surface the first supported authoring path and move one representative sample plus the issue-browser proof with it. Preserve the executable dumps and gates while removing probe-only switches and fixtures from runtime code.
3. Rework `.lui` lowering against the stabilized Native composition API. Generate explicit registrations for NativeAOT; do not add runtime discovery or an Avalonia/Native target switch.
4. Migrate the useful examples and Workbench. Replace the existing theme packages with typed Native theme/style modules rather than translating Avalonia selectors and setters.
5. Delete the Avalonia runtime, emitters, styling integration, package references, and Avalonia-only tests once their replacement checks pass. Regenerate local artifacts instead of adding fallback readers or compatibility shims.
6. Keep the experiment evidence as the decision record. Delete duplicated probe implementation after the promoted modules own equivalent tests.

The migration should pause if promotion reveals that the clean composition API depended on issue-browser-specific hooks. It should not pause merely because the Native control catalog is smaller; controls are follow-on product work, while the spike was testing whether the underlying ownership model holds.

## macOS adapter risk register

| Risk | Why it matters | Smallest validation |
| --- | --- | --- |
| AppKit main-thread and SDL window ownership | Window lifecycle and event integration must not split ownership. | NativeAOT `osx-arm64` host creates, shows, resizes, presents, closes, and reports scale from one adapter. Add `osx-x64` only if distribution still needs Intel. |
| `NSTextInputClient` composition | SDL text events alone may not prove candidate placement, selection ranges, and focus transitions. | One custom single-line field with Japanese composition start/update/commit/cancel, candidate positioning, focus loss, and scalar-safe caret movement. |
| `NSAccessibility` provider bridge | The Windows COM implementation cannot be reused, and Objective-C interop under NativeAOT is the highest platform risk. | Expose the issue-browser root, edit field, button, list, selected row, Invoke/Value/Selection behavior, focus, and stale-node rejection to VoiceOver and Accessibility Inspector. |
| CoreText/font fallback parity | Font choice and metrics can change layout and deterministic dumps. | Pin requested/resolved fonts and run the existing ligature, combining, bidi, fallback, emoji, culture, and scale corpus through headless and native paths. |
| Scale and color behavior | Retina backing scale and color-space differences can break snapping or raster comparisons. | Run 1×/2× resize and monitor-transition checks with structural/layout parity and an explicit raster tolerance. |
| NativeAOT app distribution | Universal binaries, signing, hardened runtime, and notarization affect the real package. | Publish the selected RIDs, inventory assets/notices, sign, launch the packaged `.app`, and repeat the smoke from the packaged output. |

Validation order: host/present and NativeAOT, text shaping and scale, real IME, accessibility, issue-browser walkthrough/parity, performance, then signed packaging. Stop before porting controls if practical IME or VoiceOver requires delegated controls, a second UI framework, runtime code generation, or an unbounded Objective-C bridge.

## Linux/Wayland adapter risk register

| Risk | Why it matters | Smallest validation |
| --- | --- | --- |
| Wayland SDL lifecycle | Wayland has stricter ownership and positioning rules than Win32/X11. | On the target Ubuntu release, force SDL's Wayland backend and prove create/show/resize/present/close, fractional scale reporting, and clean teardown under NativeAOT. Do not count an X11 fallback as a pass. |
| IBus/fcitx composition | Input-method behavior varies by compositor and desktop, especially candidate rectangles and focus. | Exercise the custom field with the default Ubuntu IME plus one fcitx configuration: preedit, commit, cancel, candidate position, focus loss, and Unicode caret/selection. |
| AT-SPI2 over D-Bus | UIA concepts map imperfectly, and a custom provider may become another large transport layer. | Expose the same root/edit/button/list/selection/value/focus contract to Orca and Accerciser, including stale-node rejection and keyboard-only W1-W12. |
| Fontconfig/Freetype fallback | Installed fonts and fallback order vary across distributions. | Use a pinned CI font set for deterministic proof, record the system-resolved set for manual smoke, and rerun the shaping/parity corpus. |
| Fractional scaling and compositor variance | 125%/150% output and damage behavior can affect layout, raster, and idle work. | Test 1×, 1.25×, 1.5×, and 2× with resize, clipping, native/headless dumps, idle frames, and explicit raster tolerance. |
| Clipboard, cursors, and portals | Sandboxed distributions can route basic desktop services through portals. | Validate clipboard, cursor, URI/file requests, and focus behavior in a normal package first; add Flatpak only after the unsandboxed adapter passes. |
| Native dependency packaging | glibc baseline and transitive `.so` discovery can make a local publish nonportable. | Run the exact asset/license audit on a clean Ubuntu image, then launch the packaged output on the oldest supported image without a development SDK installed. |

Validation order: Wayland host/present and NativeAOT, shaping/fractional scale, real IME, AT-SPI/Orca, issue-browser walkthrough/parity, performance, then packaging. Stop if Wayland requires X11 for correctness, AT-SPI needs delegated GTK controls, or the agreed NativeAOT output cannot run on the chosen Ubuntu baseline.

## Immediate scope

Windows remains the implementation target. macOS and Wayland work should begin as short adapter falsification spikes in the order above, not as simultaneous ports of the control catalog or `.lui` compiler. Passing either adapter spike adds that platform; failure does not reopen the Avalonia compatibility path automatically. It triggers an explicit product decision about the platform or the Native direction.
