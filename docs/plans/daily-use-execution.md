# Light Notes daily-use execution

Status: delivered and focused native closeout complete, September 6, 2026. Lucent b070cd4, packages 0.3.0-dev.21.1, and Light Notes application 6a44350. The next step is owner product/framework review.

## Outcomes and order

1. Reconcile #80 against the owner review and perform one coordinated published interaction/accessibility batch after the final app changes. Do not claim a manual walkthrough.
2. Complete #81: 750 ms debounced autosave (owner approved), explicit Save/Ctrl+S, flush before selection/close, uninterrupted editing during background writes, current-draft-aware feedback/retry, and HTTP/HTTPS Open link. Keep title/URL/body search and inbox/archive behavior coherent.
3. Complete #82 with evidence-led presentation, keyboard/minimum-size and error-state refinement. Deliver #85: validate a SQLite backup and restore it to an explicitly new workspace, preserving originals. No schema2 or import/merge feature is needed.
4. Deliver independent #84: architecture fail-fast, maintained extension tests, lock-aware CI cache and concise review evidence. Preserve exact restores and package publication only after required checks.
5. Complete #83's evidence selection and deliver #86's bounded explicit source-driven async resource path, reusing AsyncValue and synchronous UI-thread mounting. Prove it through .lui and NativeAOT. Do not move accepted writes into component cancellation.

## Ownership

Sol medium implements NoteWorkspace workflows and test seams. Luna max implements storage recovery and CI/review improvements, then the optional R3 integration. Sol medium also handles the focused presentation foundations. The coordinating agent implements the explicit async-resource convenience. The coordinating agent owns .lui UI, Program wiring, roadmap/tickets, integration and delivery. Shared builds run sequentially unless projects are demonstrably independent.

## Design constraints

Preserve the warm light palette, typography and existing responsive shell. Place Open link with its URL field; keep narrow layouts usable. Saving feedback must describe the current draft accurately and remain readable while typing. Do not reset editor sessions, caret or undo when a background write completes. Distinguish startup, save and link-open failures with useful recovery copy.

State stays where its lifetime belongs: collection viewport/presentation in the mounted component; drafts, accepted writes and cross-pane focus in the workspace. Restoration is an explicit maintenance command targeting a new directory. Application high-contrast/dark palette, icons, records, expression-bodied markup, registry/copy tools and speculative parallel mounting remain deferred unless a concrete finding changes their priority.

## Verification and review

Use focused race/failure/storage tests while implementing, then affected warning-clean suites. Publish and consume exact Lucent packages before updating Light Notes pins. Render representative wide/medium/compact and error states in one inspection batch, fix found defects together, and confirm once. The owner released the focus pause for the published FlaUI/Axe batch on September 6. That batch is complete. Coordinate any future focus-taking checks with the owner. Exercise backup/restore/export on temporary data using the published executable. Record automated, manually observed and remaining limits separately. Keep logs/captures under ignored artifacts and short results in tickets.

## Additional tickets

- [#84 CI fail-fast and review evidence](https://github.com/RichiCoder1/lucent/issues/84)
- [#85 Restore a validated backup into a new workspace](https://github.com/RichiCoder1/lucent/issues/85)
- [#86 Explicit source-driven async resource authoring](https://github.com/RichiCoder1/lucent/issues/86)

## Owner refinements

The owner asked debounce to exercise reusable ecosystem reactive primitives. #87 adds an optional R3 1.3.1 adapter with explicit TimeProvider and scope/UI dispatch; Core stays dependency-free and persistence stays workspace-owned. R3 Debounce is the quiet-period operation, distinct from ThrottleLast sampling. Selected behavior must pass NativeAOT before adoption.

The owner also requested a closer match to the mockup's alignment, pixelation/rendering and rounding. #88 therefore includes bounded corner radius, font-weight hierarchy, and renderer glyph/edge rasterization as needed by the inspected reference. Preserve the incumbent palette and overall layout; inspect against the responsive board and real output. A composed-content Selectable overload separates its live accessible label from independently styled row title and metadata, addressing the remaining row hierarchy gap. This explicitly brings rounded presentation foundations into this chunk.

## Authoring selection from observed friction (#83)

| Priority | Evidence | Selected work | Semantic and maintenance cost | Tooling cost |
| --- | --- | --- | --- | --- |
| 1 | Component-local state review called for explicit overlapping loads, failure and retry without asynchronous mounting. | #86 source/fetcher AsyncValue overloads and Refresh. | Low: reuse the existing lazy generation, cancellation and stale-value model. | Low: ordinary typed calls in readonly declarations; packed .lui proof, no grammar. |
| 2 | Autosave needs quiet-period scheduling, deterministic timing tests and owner-thread delivery; a hand-written timer would duplicate ecosystem behavior. | #87 optional R3 owned debounce and Core callback dispatch. | Medium: dependency and callback lifetime boundary, contained by generation/disposal tests and NativeAOT. | Low: ordinary C# integration; no ambient subscription interception or compiler magic. |
| 3 | NoteRow's single combined text label cannot give titles and metadata the mockup's distinct hierarchy. | #88 composed Selectable content with a separate live accessible label. | Low: reuse content recipes and the existing selectable behavior; preserve one selectable root. | Low: one overload and existing content lowering, catalog and authored-consumer checks. |
| 4 | The actual app has square controls and thin text where the mockup has rounded surfaces and stronger headings. | #88 typed radius/weight and coherent renderer propagation. | Medium: shaping/cache and edge/clip correctness across DPI. | Low: typed style properties through the existing catalog. |

The earlier stateful trial already addressed the repeated NoteWorkspace/presentation-colocation concern: declaration inference, readonly/[Once], ordinary component methods and synchronous Setup are available. Do not add another state grammar while these contracts are still being exercised.

Defer record sugar, expression-bodied/root-switching markup, additional style-sharing syntax and generic async boundaries until repeated authored examples identify a specific benefit. Their compiler, diagnostics, source-map and editor maintenance cost exceeds the demonstrated benefit in this slice. Keep command/focus extraction separate from replaceable component state because draft flush and cross-pane routing share application ownership. Hot reload, a designer, general templates and a component registry are outside this selection.

## Adoption finding

[#89](https://github.com/RichiCoder1/lucent/issues/89) fixes a compiler defect exposed by the #86 packed consumer: two stateful authored files in one namespace generated the same private helper type. The fix must use deterministic document/component identity and retain a two-file runtime regression. This is required for ordinary application growth, not an additional language feature.

## Delivered result and verification

The selected compiler/runtime, R3, restore, autosave and presentation work is implemented. Final review aligned rounded ancestor clips with pointer/point lookup and fixed the wrapped flex paragraph measurement exposed by the minimum-size startup error view (#90). Independent review found and corrected both in-flight-save/undo status handling and dependency tracking after undo, as well as a debounce completion-flush mistake during development. Regression tests retain those cases.

Background evidence: 195 Core/compiler/generator/language-server/reference-app tests, 15 renderer tests, 7 R3 tests under both managed execution and NativeAOT, 11 extension tests, and the full packed SDK matrix including NativeAOT passed. An isolated Light Notes consumer of the six local development packages passed 17 storage and 16 application/offscreen tests. Wide/medium/compact and 150-percent minimum-size images were inspected; confirmation fixes the URL row and disabled control backgrounds. Three rounded-input regressions and the nested wrapped-paragraph regression pass; the final offscreen minimum-size error capture shows wrapped recovery guidance and visible Retry. Local package proof is development evidence; official package and application source identities belong in the delivery comments on the linked issues.

The owner subsequently authorized the native batch: both maintained desktop tests passed against the published 6a44350 application. Physical capture, SQLite-confirmed autosave before close, reopen, responsive navigation/draft retention, minimum size and Ctrl+N focus passed. Three Axe.Windows scans reported zero errors. Settled desktop captures were inspected at wide, medium, compact and minimum widths. The test now expects Save now to be disabled after autosave and allows presentation to settle before screenshots. The published console-only maintenance check passed backup/export/restore with 23 equal records, corrupt/existing-destination rejection, default-workspace isolation and source preservation. The later screenshot-confirmation run passed the responsive test; its capture process exited during keyboard input, so the earlier successful unchanged persistence test remains the evidence. Native wheel interaction, a broad DPI/theme pass, screen-reader and manual Accessibility Insights walkthroughs were not repeated. Owner feedback remains the next step, not a release gate.

[#90](https://github.com/RichiCoder1/lucent/issues/90) records the auto-height flex paragraph correction. Error-state inspection showed that finite-width wrapping could retain an earlier one-line intrinsic height and hide recovery guidance. Keep the correction in shared measurement, with explicit height/block limits preserved, rather than requiring application-specific paragraph heights.
