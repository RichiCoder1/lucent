# Layout and constrained paragraphs

Lucent will extend its managed layout engine behind an internal, Core-owned seam. The first production slice adds the Grid, Flex, constrained paragraph, viewport, and rounding behavior required by the links-and-notes shell. Taffy 0.14.0 is accepted as an architectural reference, not as a production runtime or build dependency.

## Decision drivers

The designed shell gives the decision concrete bounds. Its wide form has a 48-pixel capture row spanning `184 / 320 / minmax(482, 1fr)` columns at 1,060 logical pixels. Its medium form uses `64 / 300 / minmax(426, 1fr)` at 840 pixels. Compact mode has one 432-pixel content pane inside a 480-pixel window. The collection and editor are separate nested vertical viewports; a fixed-height 68-pixel list row is virtualized from the assigned collection cell. Text wraps within its final track. Layout thresholds remain logical at 100%, 150%, and 200% scale, and device rounding must preserve shared edges.

The current engine remains a useful retained projection for rows, columns, positive main-axis growth, clipping, scrolling, and cumulative edge rounding. It does not have Grid tracks or spans, Flex shrink or wrap, or constrained paragraph measurement. `TextMeasureRequest` contains typography and scale but no available width or height. `SceneLayout` shapes text before it solves a child's allocated size and asks for intrinsic child sizes with `float.MaxValue`. `VirtualizedRegion` realizes from an explicit viewport size or the outer window before its parent cell is laid out. Those are related ordering constraints, not independent features: track sizing needs intrinsic text contributions, final track width needs a constrained paragraph height, and nested virtualization needs the assigned cell before choosing rows.

## Bounded evaluation

The reproducible, test-owned probe sources under [`tests/Probes/Layout`](../../tests/Probes/Layout/) model only the representative shell mechanics; generated outputs stay under ignored `artifacts/layout-evaluation`. Its Rust `cdylib` uses Taffy 0.14.0. A .NET 10 NativeAOT executable calls a source-generated C ABI, creates and destroys an opaque native owner, and checks the native live-owner count returns to its baseline. The probe was built and run on Windows x64 on 2026-09-05.

| Scenario | Input | Observed result |
| --- | --- | --- |
| Wide Grid and span | 1,060 x 520; capture spans three columns; tracks `184, 320, 1fr` | body columns `184, 320, 556`; capture width 1,060 |
| Medium Grid | 840 x 520; tracks `64, 300, 1fr` | body columns `64, 300, 476` |
| Flex sizing | 432-wide toolbar; field basis 240 with grow/shrink; 80 button; 8 gap | `344, 80` |
| Flex wrapping | five 130 x 24 chips; width 300; gaps 8 x 6 | three lines |
| Synthetic wrapped paragraph callback | 113 UTF-16 units at 7-pixel synthetic advance; assigned width 210; 18-pixel line height | width 210, desired height 72, four callback invocations across intrinsic/final measurement |
| Nested scroll cell | 520-high Grid with 48 header and scrolling second track; content height 1,200 | assigned viewport 472, content/scroll extent 1,200 |
| Current managed row | frozen Release DLL observed during this concurrent batch; HEAD 8fea38a; dirty working-tree edits may be present; DLL hash is recorded by the probe; widths 184, 320, grow at 1,060 | 184, 320, 556 |
| Current managed constraint/text | 300-wide row with 240 + 80 children and 8 gap; same 113-unit text at widths 210 and 105 | children stay 240, 80 and overflow; both text layouts remain one 16-pixel line; captured requests are equal because width is absent |
| Fixed virtualization arithmetic | assigned height 472; scroll 128; row 64; overscan 2 | last exclusive row 12 |
| Cumulative device rounding | three equal tracks totaling 101 at scale 1.5 | `34, 33.333336, 34`; rounded total remains 101.333336 logical pixels (152 device pixels) |
| Native ownership | opaque owner created, evaluated, destroyed from NativeAOT host | load and disposal check passed; live-owner count returned to baseline |
| Diagnostic throughput | all scenarios plus one 1,001-node Taffy tree | 878 microseconds in one warm process sample |

The published host executable was 986,112 bytes and the Taffy probe DLL was 809,472 bytes. These sizes are evidence for this minimal probe, not an estimate of a packaged Lucent dependency. The single timing sample is a wiring and order-of-magnitude check, not a benchmark: it mixes tree creation, several layouts, a synthetic managed/native measurement callback, JSON formatting, and the 1,001-node layout. The callback uses fixed synthetic character metrics and is not evidence about real paragraph shaping.

The evaluation confirms that Taffy's Grid, Flex, measurement callback, dirty propagation, and rounding model can express the required mechanics. It does not establish production interop, real paragraph performance, packaging, accessibility, or virtualized re-entry behavior.

## Why the managed engine wins this slice

Taffy is a credible engine. Its official repository implements CSS Block, Flexbox, and Grid; its 0.14.0 release was published on 2026-08-24; the project has active releases and downstream use; and it is MIT licensed. `TaffyTree` offers custom leaf measurement, dirty propagation, and optional rounding. These are useful references for Lucent's internal algorithms.

The adoption cost is concentrated exactly at Lucent's ownership boundary. The official repository still describes C bindings as work in progress. A production integration would therefore require a Lucent-owned Rust wrapper, native ABI and error contract, target-specific build and package assets, opaque-handle lifetime tests, and managed/native measurement callbacks. `TaffyTree` is neither `Send` nor `Sync`, so it would remain tied to Lucent's layout owner. Lucent would also maintain a second node/style tree and identity map alongside the retained composition. Taffy deliberately does not perform text layout, so the constrained paragraph seam and its invalidation rules remain Lucent work in either design.

That cost is not justified for the bounded subset. Keeping the seam internal makes the decision reversible if future layout requirements exceed the managed implementation. No Taffy handle, Rust type, package, or CSS-shaped abstraction enters portable Core, the public control API, or `.lui`.

Primary evidence: [Taffy repository and supported algorithms](https://github.com/DioxusLabs/taffy), [Taffy 0.14.0 release](https://github.com/DioxusLabs/taffy/releases/tag/v0.14.0), [MIT license](https://github.com/DioxusLabs/taffy/blob/main/LICENSE), [`TaffyTree` API and ownership traits](https://docs.rs/taffy/0.14.0/taffy/tree/struct.TaffyTree.html), [custom measurement](https://docs.rs/taffy/0.14.0/taffy/tree/struct.TaffyTree.html#method.compute_layout_with_measure), [rounding behavior](https://docs.rs/taffy/0.14.0/taffy/compute/trait.RoundTree.html), and the [open C-binding work](https://github.com/DioxusLabs/taffy/issues/399).

## Internal layout seam

Core owns immutable engine inputs and results. The exact names may change during implementation, but the boundary has these responsibilities:

- A layout-tree snapshot contains stable Lucent element identity, parent/child order, computed Grid/Flex style, intrinsic contribution hints, and paragraph leaf identity. It contains no renderer object or platform handle.
- `LayoutConstraint` represents a finite nonnegative value or an explicit unbounded value. Infinity and `float.MaxValue` are not sentinel values. Public invalid numeric inputs continue to fail closed.
- An internal layout engine accepts a root constraint, device scale, and paragraph-measure callback, and returns Core-owned boxes, overflow extents, assigned viewport rectangles, and paragraph results.
- Cache keys use stable element identity plus content, computed typography, writing direction, wrap policy, constraints, and scale generations. Dirty propagation stops when an ancestor's intrinsic and final geometry are unchanged.
- The retained composition remains the owner. Engine nodes, cached paragraphs, and native resources if an engine is reconsidered later are released with the composition on its owner.

The first Grid subset is explicit tracks (`fixed`, `auto`, `fr`, and `minmax`), row/column gaps, explicit placement, and contiguous spans. The first Flex subset is row/column direction, basis, grow, shrink, gaps, alignment, and wrapping. Unsupported combinations fail during authored-style validation rather than silently approximating CSS. Responsive choice reads the shell's assigned content box once per projection; a child contribution cannot select its own breakpoint.

## Constrained paragraph contract

A paragraph request contains the immutable text snapshot or stable text-version identity; resolved font family, size, weight, style, language, and base direction; finite or unbounded inline and block constraints; wrap policy; optional maximum line count and overflow policy; and device scale. The initial wrap policy is no-wrap, word wrap with grapheme fallback, or explicit line breaks. Both dimensions are explicit even when one is unbounded, so a vertical scroller can request a finite inline width and unbounded block extent without losing its viewport constraint.

A paragraph result is immutable and reusable for layout, paint, input, and accessibility. It contains:

- desired inline and block size, overflow/truncation flags, and the constraints and content/style generations that produced it;
- lines with a UTF-16 logical text range, top, baseline, ascent, descent, leading, advance, trailing-whitespace advance, and hard-break status;
- directional/font runs with UTF-16 logical ranges, glyph identifiers, glyph positions/advances, font identity, and run metrics;
- grapheme-safe caret stops that map logical UTF-16 boundaries and affinity to line/x geometry;
- hit-test results expressed as logical position plus upstream/downstream affinity, selection rectangles derived from logical ranges, and caret geometry derived from the same stops; and
- an immutable renderer payload that can be painted repeatedly without rebuilding a text blob on every frame.

UTF-16 ranges match the editor and Windows contracts, but all editing and geometry operations normalize to valid grapheme boundaries. Logical ranges remain authoritative across directional visual runs. The existing bounded bidirectional behavior remains the first implementation boundary; the contract does not claim full Unicode paragraph bidi before it is implemented and tested.

## Measurement, scrolling, and virtualization order

Layout has a bounded feedback loop:

1. Collect fixed sizes and cached min-content/max-content paragraph contributions needed by Grid/Flex.
2. Resolve tracks or flex lines from the parent's assigned constraint.
3. Measure each paragraph at its resolved inline width and block constraint.
4. Use the returned desired block size for auto tracks and cross sizes, then perform at most one correction pass when those results change an ancestor's auto size.
5. Assign responsive branches, then perform one post-branch assignment pass so nested viewport rectangles reflect the selected Grid/Flex arrangement. Realize fixed-height virtualized rows from those assigned rectangles, flush their retained effects, and perform one bounded final publication pass. Missing or subsequently changed viewport geometry fails projection.

A repeated state or a second unresolved correction fails with a diagnostic in debug/test builds; the engine must not iterate until convergence. Breakpoint selection occurs outside child measurement, preventing a paragraph from changing the constraint that selected its responsive branch.

The editor viewport supplies a finite inline width and an unbounded block constraint to its paragraph. The paragraph returns a finite desired block extent; the viewport clamps the assigned box and derives scroll extent from the difference. The list viewport uses its actual Grid cell after fixed headers and tracks are resolved. Fixed-height virtualization stays fixed-height: a two-line title must fit or clip inside the declared row, and content cannot silently change the row-height/index mapping. Wheel bubbling and scroll ownership remain input behavior layered over these Core viewport results.

Device rounding happens once in Core after logical layout. Rounding cumulative absolute edges at the actual device scale preserves shared boundaries and derives each width from adjacent rounded edges. The implementation must not combine Taffy's scale-independent integer rounding with Lucent's device-scale rounding.

## Implementation budgets

The production target is the designed shell with about 200 mounted elements, a 10,000-item source with only the viewport plus two overscan rows realized, and an active editor document of 20,000 UTF-16 code units. These are acceptance workloads, not claims about the current implementation.

- Ordinary layout and virtualization perform no more than two layout passes and one realization/flush pass. A responsive transition that can change a virtualized viewport performs at most three layout passes: initial constraint assignment, post-branch viewport assignment, and final publication, with one realization/flush between the last two. Unchanged paragraphs are cache hits within each projection pass.
- After a single edit, shaping and line breaking are limited to the changed paragraph and affected following lines; caret moves and selection-only changes do not reshape text.
- Paragraph hit testing and caret lookup are logarithmic in line count plus logarithmic or bounded lookup within one line. They do not scan the full document or recreate grapheme maps per call.
- Painting reuses shaped paragraph output and allocates no managed object or native text blob per unchanged run per frame.
- Skia's final positioned glyph edge is the canonical run width. In the 20,000-unit repeated-glyph characterization, naively accumulating single-precision HarfBuzz advances drifted 6.4 logical pixels from that edge; the renderer instead derives each retained advance from consecutive positioned origins and verifies the advance sum and final edge against one width.
- A committed edit performs at most one full-document string snapshot. Internal edit operations must not copy the full 20,000-unit document once per grapheme.
- Each renderer bounds shaped paragraph data and native text blobs independently by estimated retained byte cost: 16 MiB for shaped data plus 16 MiB for blobs (32 MiB combined), each with least-recently-used eviction. Shape estimates reserve the lazy grapheme/caret index footprint; eviction releases associated renderer-owned blobs on the renderer owner.
- On the 200-element/20,000-unit reference scene, warm layout plus paragraph work targets a p95 below 4 ms during resize and below 8 ms for an initial full paragraph on the minimum supported Windows machine. These targets must be calibrated on that machine before enforcement; until then they remain provisional and are not verified guarantees.

Focused tests cover the three shell widths, Grid span and `minmax`, Flex grow/shrink/wrap, word/grapheme wrapping, explicit breaks, constrained-height overflow, nested independent viewports, fixed-row realization after assigned cell layout, 100/150/200% shared-edge rounding, retained invalidation, and disposal. Text fixtures include combining sequences, surrogate pairs, mixed supported-direction runs, selection across lines, trailing whitespace, empty paragraphs, and 20,000-unit edits. A NativeAOT smoke remains required only if a native engine is reconsidered; the current probe demonstrates feasibility, not production adoption.

## Consequences and reconsideration

Issue #77 should implement the managed internal tree, the bounded Grid/Flex subsets, paragraph feedback, assigned-cell viewport realization, cumulative device rounding, invalidation diagnostics, and the reference workloads above. It should not expose a general CSS engine or ship Taffy.

Issue #79 should make the owned multiline editor consume paragraph logical geometry for wrapping, vertical navigation with a preserved desired x, caret, selection, hit testing, scrolling, IME positioning, and accessibility ranges. Caret or selection changes must reuse paragraph output; long-document tests enforce the copy and shaping budgets.

Reconsider an external engine when production requirements demand substantially broader CSS Grid/Flex conformance, when maintaining Lucent's algorithms exceeds the adapter cost, or when Taffy offers a stable supported C ABI and distributable Windows assets. Any reconsideration repeats NativeAOT load/unload, callback re-entry, owner-thread, package-size, license, and failure-atomic disposal tests against the then-current release.
