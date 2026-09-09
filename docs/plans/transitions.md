# Production transitions and animation

Status: design accepted September 8 and implementation authorized September 9, 2026, after the review follow-ups and Light Notes focus continuity. Tracked by [#142](https://github.com/RichiCoder1/lucent/issues/142), following [#119](https://github.com/RichiCoder1/lucent/issues/119) and coordinated with [#140](https://github.com/RichiCoder1/lucent/issues/140). The implementation breakdown records delivery progress; declarations below remain planned until their execution checks pass.

Deliver implicit, finite transitions of explicitly eligible visual properties when their resolved targets change. One composition-owned motion module handles sampling, interruption and lifetime; native and headless hosts supply frame opportunities through the same seam. Applications declare intent in styles rather than installing effects, timers or element references.

## Current evidence and constraints

Inspected Lucent `2bd1c7f`, including the delivered nested-style and constrained-layout corrections, not the older review snapshot. Relevant implementation:

| Evidence | Current behavior and design consequence |
| --- | --- |
| [ElementPresentation.cs](../../src/Lucent.Core/ElementPresentation.cs), `ResolveLocal`, `TransitionController` | Resolves default/inherited/component/author/variant/control candidates. Manual samples are appended above controls. `Start` stores one value until expiry; there is no interpolation or automatic target-change observation. Replace that experimental model rather than extrapolating a production guarantee from its names. |
| [PropertyTheme.cs](../../src/Lucent.Core/PropertyTheme.cs), [Style.cs](../../src/Lucent.Core/Style.cs) | Real `Property<T>` identities, immutable assignments, token resolution, reactive conditions and finite transition-kind validation already exist. Extend the authoritative property descriptor; do not create a name-based animation registry. |
| [ColorBrush.cs](../../src/Lucent.Core/ColorBrush.cs), [LayoutScene.cs](../../src/Lucent.Core/LayoutScene.cs) | `Color` is immutable 8-bit sRGB RGBA. Background is `Brush`; text uses `Color`. Opacity has a transition kind; Brush does not. Merely permitting Color cannot animate stock backgrounds. |
| [Composition.cs](../../src/Lucent.Core/Composition.cs), [Element.cs](../../src/Lucent.Core/Element.cs) | Manual `AdvanceTransitions(int)` and element removal exist. Reuse composition/element ownership, but remove manual clock responsibility from application authors. |
| [WindowsBootstrap.cs](../../src/Lucent.Platform.Windows/WindowsBootstrap.cs), [WindowsFrameContract.cs](../../src/Lucent.Platform.Windows/WindowsFrameContract.cs) | Event-driven frame requests, timed caret/popup waits and vsync presentation exist. No call advances transitions. Add motion deadlines to this scheduler; do not add a permanent timer. |
| [HeadlessApplication.cs](../../src/Lucent.Testing/HeadlessApplication.cs) | `AdvanceAsync` advances fake time and separately truncates/clamps elapsed milliseconds for manual samples. Replace this dual accounting with one monotonic time source. |
| [SceneLayout.cs](../../src/Lucent.Core/SceneLayout.cs) | Projection includes bounded responsive discovery, realization, layout, scene and input installation. Paint reads are already distinguished from input tracking, but this does not establish a general layout-free animation frame path. That path needs explicit implementation and measurement. |
| [IssueRow.lui](../../apps/Lucent.IssueBrowser/IssueRow.lui), [Header.lui](../../apps/Lucent.IssueBrowser/Header.lui) | Stock Selectable/Button plus application layout styles are the proving surface. Keep stock tokens; no application palette fork. |

Read ADRs [0001](../adr/0001-native-first-runtime.md), [0003](../adr/0003-application-services-and-shutdown.md), [0005](../adr/0005-component-local-state.md), and [0006](../adr/0006-style-driven-layout.md). Light Notes was separately inspected read-only at `D:/src/richicoder1/light-notes`, HEAD `7e523b5`: `NoteRow.lui` has combined hover/pressed/selected/focus styles; `EditorPane.lui` retains responsive panes and editor content through Participation. These are source observations, not executed app validation.

The typed-style owner confirmed on September 8 that `2bd1c7f` is authoritative: the Arrangement/public SceneProperties/Fill/Foreground migration has already landed, despite historical future-tense wording in the language guide. There is no pending replacement registry. `ControlThemes.Foreground` is an internal Color token, not the removed author property. Reserved Transform/FocusRing transition kinds do not establish working interpolation; the actual FocusRing value is a struct whereas the legacy reserved matcher expects float. The recommended descriptor extension below is new design, not an already accepted interface.

Accepted owner constraints remain stronger than motion:

- Hover/focus/pressed state, disabled activation blocking and applied selection change immediately. Requested selection is not applied selection and must not start a selected-state transition until accepted by the owner.
- Unexpected escaping callbacks/effects/layout failures terminate the application session with orderly cleanup and preserved reporting. Fatal shutdown cancels in-flight close preparation and proceeds to terminal cleanup; late completion cannot mutate terminal state. Accepted writes remain application-service owned and must finish or be reported, without a silent-discard framework timeout. Reporting is structured, with a minimal fallback diagnostic and optional logging integration; application presentation is optional, with no mandatory dialog. Reporting cannot resume execution, gate cleanup indefinitely or replace the original error. These owner decisions were relayed by Code Review and recorded in #124/#119 after this checkout; motion must follow their delivered implementation. Expected application errors remain state.
- Derived equality suppresses downstream execution with default .NET equality. An equal target does not restart a transition. This checkout's dirty propagation is not evidence that the pending equality contract is delivered.
- Preserve retained ownership, NativeAOT, portable Core and `.lui` source maps. No container queries, control rewrite, general error boundaries or parallel mounting.

## Model and precedence

The **target value** is the winning typed value after all ordinary resolution, with transitions excluded. The **presented value** is the value used to draw at a particular frame time. A **transition** temporarily moves the presented value toward a changed target. A **motion track** is one element/property's owned transition state. **Frame demand** means future sampling is needed; it does not imply layout is dirty or that a frame must be produced while hidden.

Keep `Element.Resolve(property)` authoritative: its value and winner describe the target. Add a separate typed presentation read for scene construction and inspection, provisionally `ReadPresentation<T>(Property<T>)`. Runtime internals may keep this seam internal initially; headless snapshots expose both values. Application bindings and custom layout reads use targets. Sampling never writes a style candidate, control value, theme token, Signal representing application intent, or another transition's target.

Resolve values using today's variant ordering, component/author precedence and source ordinal. Conditions filter assignments, not specificity. Track policy declarations separately from value declarations, using the same ordering rules; policy is keyed by the actual property identity. The after-change winning policy governs a new transition. An author `transition Background: Motion.None;` overrides the corresponding component policy at the same variant tier. As with value assignments, a more specific active variant can still win; dumps must make that visible. No implicit `transition: all` and no style-wide duration list with positional property matching.

Recommended authority rule: a control-owned winning slot cancels motion on that property and presents the control value immediately, even when an author requested a transition. This closes the experimental sample-over-control hole. Do not make all properties of a control nonanimated: its style-owned Background can animate while its control-owned text/scroll/selection remains authoritative. A later control may explicitly own a decorative animated value through a separately designed capability; that is not an exception implementers may infer now.

Inherited properties resolve their target through ancestor targets, never ancestor samples. For presentation, an inheriting child with no local value and no local transition policy uses its ancestor's presented inherited value without creating a second track. This also applies to a newly mounted child while its ancestor is moving: it joins that existing presentation, not a new entry animation. First-presentation snapping applies to locally owned presentation, not this delegation. A child with a local policy owns its own interpolation from its previously presented value toward the inherited target; explicit None presents the target immediately and blocks ancestor motion. Policies themselves do not inherit. This avoids cascading delays, and requires invalidating affected descendant paint without recomputing text geometry.

Removing the child's local policy cancels its track and switches directly to delegated ancestor presentation when no local value/control slot exists; otherwise it snaps to its own target. Adding a local policy alone captures no new transition and continues delegation until a target changes; explicit None instead snaps immediately. The next inherited target change starts from the then-presented inherited sample. If inheritance source changes, resolve it in the same target commit: a same-valued source change updates provenance, but any change of authority or presentation delegation still applies immediately and can repaint. A switch to a different ancestor sample may be discontinuous; do not fabricate an extra transition from policy/source changes alone.

## Authoring surface

Add a small statement to the existing style grammar rather than a second markup subsystem:

```lui
// Proposed new statement; ordinary property values and variants are unchanged.
style GentleFeedback {
    transition Background: Motion.Quick;
    transition Opacity: Motion.Duration(120, Easing.EaseOut);
    when Pressed {
        transition Background: Motion.None;
    }
}
```

`Motion.Duration` takes nonnegative integer milliseconds, with a documented upper bound of 60,000 for these finite UI transitions; zero equals None. First delivery has Linear and EaseOut (the exact EaseOut curve is `1 - (1 - p)^3`). No delay, negative duration, repetition or callback easing. Proposed stock Quick is 120 ms EaseOut; these are motion defaults, not palette values. Policy expressions are construction-time immutable values initially, including parameters to named style factories. Dynamic choices belong in existing `when` groups. Diagnose reactive policy expressions that would otherwise silently snapshot. A later duration-token design must preserve one property/metadata system.

Lower the statement to the same typed C# operation:

```csharp
// Proposed interface; transition declarations are nodes in immutable Style.
Style.Empty.Transition(VisualProperties.Background, Motion.Quick)
    .When(VariantState.Pressed,
        Style.Empty.Transition(VisualProperties.Background, Motion.None));
```

Compiler, formatter, generator and editor must share real property-symbol discovery with ordinary assignments. Reject unsupported property declarations at their source span when statically known. Preserve source maps for property, policy expression and nested condition. Completion offers eligible properties, typed policy constructors and clear None behavior; hover explains runtime pair restrictions. Custom properties still bind by real symbols, with runtime validation before mount publication where compile-time eligibility is unavailable. Duplicate unconditional policy declarations in one style body are diagnostics; composition/variant overrides remain legal and inspectable.

Stock controls own an optional shared background policy, delivered only after runtime/host contracts pass. Use immediate Pressed, Disabled, Invalid and FocusVisible feedback; normal hover entry/exit may transition. FocusRing and selection outline are immediate in all cases. First delivery leaves TextColor immediate in stock controls to avoid transient contrast failures, although authors may explicitly animate eligible text color. Stock selection feedback, including its background, remains immediate in first delivery.

Issue Browser needs no per-row timer or palette:

```lui
// Proposed addition to the existing layout-only IssueRowLayoutStyle.
style IssueRowMotion {
    transition Background: Motion.Quick;
    when Selected { transition Background: Motion.None; }
    when Pressed { transition Background: Motion.None; }
    when FocusVisible { transition Background: Motion.None; }
    when Disabled { transition Background: Motion.None; }
}
// Inside the existing IssueRow component; existing content remains its body.
<Selectable label={() => Label(issue())}
    selected={() => browser.IsSelected(issue().Number)}
    onSelect={() => OpenIssue(browser, issue().Number, view)}
    style={IssueRowLayoutStyle(view).With(IssueRowMotion)}>
    <Text style={PresentationStyles.Body}>{issue().Title}</Text>
</Selectable>
```

Once stock Selectable provides this exact policy, remove the redundant consumer declaration; the reference test should prove stock defaults with its existing title/caption content. Density, breakpoints, splitter extent and row height continue to change immediately. Only realized rows can own tracks; 10,000 source issues must not create 10,000 animation registrations.

Light Notes can add the same declarations directly to its existing `NoteRowStyle`, keeping `LightNotesTheme` and all combined-state values. Its `when Selected`, `when Pressed`, `when FocusVisible` and more specific combined groups explicitly use None where required; its selection Border and menu FocusRing remain immediate. `BackActionStyle` can soften Background hover while preserving immediate press/focus. Do not animate `EditorPaneStyle.Participation`, retained editor size, saved text, title/body drafts or autosave indicators through delayed semantic changes. A decorative saved-status opacity transition is possible only on a stable mounted visual whose readable status updates immediately. No entry/exit animation is implied when a conditional mounts that status element.

These excerpts are design fixtures to become compiled tests. They do not establish that today's SDK can parse them or that Light Notes has consumed a framework change.

## Typed eligibility and interpolation

Extend the canonical `Property<T>` descriptor with a typed interpolation capability and invalidation impact, using the metadata location agreed with ongoing style work. Names here describe responsibilities, not a second public registry. A capability validates a pair, prepares typed endpoints, compares values and samples a finite normalized progress. Cache preparation once per start/retarget. Keep generic dispatch statically reachable; avoid reflection, dynamic code, string property lookup, arbitrary object lerp or runtime type discovery.

| Property/value | First delivery | Rule |
| --- | --- | --- |
| Opacity / float | Eligible, paint impact | Finite endpoints in [0,1]; scalar interpolation; exact final target. Opacity does not make an element unavailable or pointer-transparent. |
| TextColor / Color | Eligible, paint impact | Linear-light premultiplied sRGB interpolation; do not reshape text. Stock policy stays immediate. |
| Background / Brush | Eligible only for solid-to-solid pairs, paint impact | Apply the same color interpolation; brush presence and box geometry stay unchanged. |
| Gradient/solid or gradient/gradient | Valid style, pair unsupported | Snap to target, cancel old track, record pair-ineligible. Do not synthesize layers or silently choose gradient topology. |
| Border / FocusRing | Ineligible initially | Composite widths/brushes and focus visibility need their own contract; immediate existing behavior. |
| Matrix transform | Deferred | Do not interpolate matrix coefficients by default; decomposition, transform origin, clipping, hit geometry and UIA must be designed together. |
| Layout, participation, scroll, text/content, enabled, selection | Ineligible | No tween of discrete state, algorithms, auto lengths, constraints or control authority. |

Color math: decode sRGB channels to linear-light, multiply by alpha, interpolate channels and alpha, unpremultiply if alpha > 0, encode to sRGB and quantize once for the published Color. At zero alpha use zero RGB; at progress 1 publish the exact target bytes including its transparent RGB. Retarget from an unquantized internal sample to avoid repeated rounding drift. Check a black/white midpoint and transparent-red/opaque-blue cases; screenshots alone are insufficient color evidence. This color choice is accepted as the design contract and still requires Skia visual verification during implementation.

Endpoint validity errors are errors in the authored value and must not be converted to a silent snap. Valid but unsupported pairs use the explicit discrete fallback above. A registered custom interpolator throwing, mutating state or returning an invalid sample is a fatal unexpected callback failure, not a skipped frame. Defer public custom interpolator registration in first delivery; implement typed built-ins behind the future-compatible descriptor seam.

## Target commits and timeline

Sampling must not start inside a property getter or while traversing layout. Introduce a bounded target-commit phase shared by native/headless presentation:

1. Drain accepted owner work and reactive changes under the existing work budget; install current appearance, participation and window breakpoint inputs. Run current bounded responsive/realization discovery, including newly mounted nodes.
2. Resolve dirty eligible target/policy slots with samples excluded. Coalesce all updates in this transaction: A→B→C before presentation commits starts A→C. Apply authority, suppression, policy-removal and delegation changes before the equal-target fast path. Update provenance even when values are equal. Equality suppresses downstream value work, not required authority/presentation changes or diagnostic metadata updates.
3. Use one monotonic timestamp for the transaction. Sample existing active tracks at that time before applying retargets. Prepare every new entry before publishing a consistent batch; failed preparation does not publish half a new presentation.
4. Commit all target changes, policy decisions and samples. Build a scene from the committed presentation snapshot. No user callback during interpolation publication, and no track mutation from scene getters. Layout remains based on targets.
5. Acknowledge the presented scene generation. Host time and presentation acknowledgement are distinct: elapsed time can advance without a successful native present.

Do not add a second unbounded settle loop around `SceneLayout.Project`. Split only the phases necessary to insert the target commit after current responsive settling and before final drawing. New input/work during projection queues the next transaction; existing mutation/freshness guards still apply. Manual offscreen projection must establish a consistent first snapshot through the same path.

Per track store element epoch/id, property identity, resolved target version, start sample, target, start timestamp, duration, easing, policy source and presentation generation. Element identity must not be a reusable row index. No state is stored on a reusable Style or component recipe.

| Event | Deterministic behavior |
| --- | --- |
| First mount / no acknowledged prior presentation | Snap locally owned presentation to resolved target; record first-presentation. Delegated inherited presentation joins its ancestor's current sample without a track. No default-to-target flash or implicit entry animation. Headless scene installation acknowledges its first presentation. |
| Target changes while idle | Start from previous presented value if eligible and policy permits. First frame at start time has progress zero; semantic/input state is already current. |
| Target changes while active | Sample old trajectory at current time, then start toward new target for the new policy's full duration. Position/color continuity, no promised velocity continuity. Reversing uses this same rule, not CSS reversal shortening. |
| Target unchanged, even if winner provenance changes | With unchanged authority, eligibility, suppression and delegation, do not restart or wake. If current target equals current sample, finish immediately. A same-valued control takeover still cancels/snap-repaints an active track. |
| Policy changes alone | None or no winning policy cancels and snaps; inherited delegation resumes where applicable as specified above. Otherwise keep the running track's captured duration/easing. The next target change uses the new policy. |
| Deadline reached / long frame gap | Publish exact target and remove track. No catch-up frame loop and no spring integration steps. |
| Valid but incompatible pair / control becomes authoritative | Cancel and snap immediately with a reason. |
| Collapse, hidden participation, removal, mount rollback | Cancel tracks for the affected subtree. Retained hidden state survives but samples do not. Reappearance snaps to current target. |
| Same-key payload update | Preserve mount; retarget only changed values. Removal and later reuse of that key creates a fresh first presentation. |
| Reduced motion enabled / contrast mode changed | Cancel and snap before the next scene; remove frame demand. Re-enabling does not replay old changes. |
| Terminal shutdown / fatal failure | Clear registrations and reject further sampling, then follow session cleanup/reporting contract. No animation completion callback or await can hold shutdown open. |

First delivery has no user completion events or `Task` per implicit transition. Such callbacks are easy to mistake for durable completion of user intent after retarget/disposal. Diagnostics report finished/canceled state. Future explicit animation handles need typed Completed/Canceled/Disposed outcomes and cannot extend element lifetime implicitly.

## Clocks, wakes and invalidation

Core owns interpolation and track lifetime. A portable composition frame-driver seam takes an absolute monotonic timestamp and returns whether pixels changed plus next frame demand. Time never comes from wall-clock calendar time. A native adapter uses a monotonic timestamp source; a deterministic adapter derives the same units from headless fake time. Reject backwards test timestamps, use checked/bounded conversion, and preserve sub-millisecond advances and long gaps. Do not separately add elapsed milliseconds to a second motion clock.

The host provides frame opportunities, not one timer per track. On zero-to-nonzero demand request one owner wake. After an active frame, request the next opportunity at the host cadence. Existing vsync is useful pacing, but a presenter that returns immediately must still have a future deadline (fallback nominal 60 Hz); otherwise continuous request=true creates a busy loop. Slow frames jump directly to elapsed progress. This is an initial policy, not a claim of universal refresh-rate synchronization.

Windows takes the minimum of motion, active caret and menu-intent deadlines when computing its existing event wait. No motion demand means no animation timeout and no animation-caused frame. Coalesce input/appearance/resize/animation requests; do not downgrade a geometry/input frame into paint-only work. Install the wake transport before mounts and recheck pending work/demand after subscribing and before entering a wait to avoid a lost wake. Timer callbacks, if required by a platform adapter, post owner work only; disposal uses a generation token so a late callback is harmless.

Minimized or nonrenderable windows disarm motion frame demand and snap finite tracks to their targets. Restore presents current state once. Unknown occlusion is not an invitation to poll windows for visibility; treat renderable hosts normally until an explicit visibility signal exists. Each independently owned popup composition uses the same clock seam and explicit local visibility/lifetime, while the Windows owner loop coalesces deadlines across surfaces. Native OS menus remain outside Lucent animation. Live-resize callbacks must share the last committed timestamp/generation guard, so reentrancy cannot advance a track twice or run composition mutation in a native callback.

First delivery must distinguish:

- **Target/structure/layout/input dirty:** use the current full projection and its freshness guards.
- **Only presentation samples dirty:** rebuild paint nodes from an owned valid layout snapshot; reuse geometry, paragraph shaping results, input and semantic snapshot. Update background/text paint and opacity groups without remeasurement or semantic refresh per frame.
- **Expose/caret only:** retain their existing supported reuse path, unless motion/target demand makes that scene stale.

This requires a bounded retained paint projection plan or equivalent internal cache, not merely relabeling a full `SceneLayout.Project` as a frame-only operation. It must invalidate on geometry/viewport/DPI, text shaping inputs, participation, structure, renderer resource reset and ownership changes. Keep immutable frame resource leases alive until the renderer finishes. Coordinate #114/#136 on projection/cache ownership and #141 on semantic refresh; do not build a general scene-graph replacement. If correct layout reuse cannot be contained, stop and report the measured cost/design conflict before enabling stock motion. Paint-only does not mean zero raster/upload cost on the current CPU presenter.

## Appearance, accessibility and lifecycle

Recommended first policy: theme palette, color scheme, contrast and presentation-mode changes are atomic snaps, even if ordinary style transitions are configured. Appearance changes can otherwise leave text and background at incompatible intermediate contrast. Record an appearance generation so token-driven changes are classified correctly; do not guess from value types or token names. Custom application tokens changed outside an appearance transaction are ordinary target changes; authors can use a future explicit appearance transaction only once that seam is agreed. No public ambient 'suppress all changes' global is needed for first delivery.

Reduced motion is a runtime theme/host preference checked at target commit and frame sample. Effective suppression is the logical OR of platform preference and app/theme suppression; local policy cannot override platform reduction. Keep existing platform capability diagnostics if preference is unknown, with unknown treated as no platform suppression and an application opt-out available. High contrast always snaps. Cosmetic stock transitions have no essential-motion exemption. Windows settings updates must reach every live composition including popups; #129 owns the underlying appearance propagation correction.

Track cleanup is synchronous on the owner thread, element-scoped and transactional with mount rollback. Dispose clears subscriptions, prepared endpoints and frame registrations without delivering callbacks into disposed nodes. A composition cannot hold a disposed element through the scheduler. Close preparation does not automatically cancel ordinary visual work while the app remains interactive; when session state becomes terminal, stop animation before disposing composition. Fatal failure cancels in-flight preparation and proceeds to cleanup under #124; a late prepare result, queued frame or reporting continuation cannot revive the session. Do not use the preparation token to cancel an accepted service-owned write.

## Diagnostics and performance evidence

Extend deterministic dumps with target value/source, presented value, policy/source, captured duration/easing, logical start/elapsed, progress, track generation and suppression/cancellation reason. Sort by element identity and property identity; no wall-clock timestamps, addresses or incidental enumeration order. Scene dumps remain snapshots, not the source of transition state. Version/document any dump or public-resolution change with #140.

Keep bounded aggregate counters for active tracks, starts, retargets, equality skips, cancellations by reason, sampled tracks, animation wake requests, presented animation frames, full layout passes and paint-only passes. No unbounded event history or per-frame string formatting. Detailed recording is opt-in. Deterministic headless snapshots expose active-count and next demand so tests can assert idle behavior without sleeping.

Performance hypotheses, not measured claims: scheduling/sample work should scale with active realized tracks; dirty-target resolution should scale with affected subscriptions rather than all source items; an unchanged composition has zero animation work after initial setup. Prepared endpoints and active-entry storage avoid per-frame delegates/tasks and repeated reflection. Immutable scene/brush allocation may remain; measure it separately instead of claiming zero allocation.

Measure 1, 100 and 1,000 simultaneously changing visible visual properties; one hovered row in Issue Browser's 10,000-item data source; Light Notes row interactions and editor typing during background motion. Report p50/p95 frame phases, allocations, active entries, layout/shaping counts, input latency, raster/upload time and idle wake counts against a no-motion run on the same artifact. Establish numeric regression budgets from the actual baseline before enabling stock defaults; do not infer end-user latency from a synthetic lerp benchmark.

## Bounded delivery and later animation

First delivery is production implicit visual transitions: opacity, text color, solid backgrounds; Linear/EaseOut; style declarations and None; automatic winner changes; deterministic retargeting; ownership; native/headless frame parity; reduced motion/appearance snaps; frame-only projection; diagnostics and AOT proof. It is not 'complete animation support' in marketing or roadmap language.

Follow-ups, separately designed and approved: border brush interpolation with fixed geometry; compatible gradient pairs; decomposed transforms with matching hit/semantic geometry; explicit animation handles; springs with velocity continuity; keyframes/timelines/delays/repeats; enter/exit presence ownership; coordinated layout transitions/shared elements; essential-motion classification; motion theme tokens and appearance crossfades. Explicit and implicit motion should eventually arbitrate through the same per-property owner, not competing overrides. Exit animation cannot retain removed application subscriptions or accepted writes by accident. Do not add placeholder public APIs promising these semantics in the first release.

The [implementation breakdown](transition-implementation.md) defines sequencing and evidence. The [framework research](transition-framework-research.md) compares alternatives with primary citations. The [accepted ADR](../adr/0007-target-and-presentation-transitions.md) records the main tradeoff; its design decisions were accepted on September 8, 2026.

## Accepted owner decisions

The owner accepted all seven recommendations on September 8, 2026. Earlier proposal language records the design rationale, not outstanding approval. Exact interface spelling and prerelease version are implementation planning details. Runtime execution requires separate authorization.

| Decision | Accepted recommendation and consequence |
| --- | --- |
| Authority of control values | Cancel/snap on control-owned winning slots. Avoid animation hiding authoritative text, scroll or state. |
| Bounded production scope | Approve the first-delivery list above. Defer layout/transforms/keyframes rather than promising them through the manual API. |
| Retarget feel | Full-duration retarget from current sample; no velocity guarantee. Predictable and small implementation, but repeated reversals can feel slower than a spring or CSS shortened reversal. |
| Stock opt-in | Enable only short hover background transitions after representative performance and contrast evidence; press/focus/disabled/selection feedback remain immediate. Start with Quick = 120 ms EaseOut. |
| Color and unsupported pairs | Linear-light premultiplied sRGB; valid incompatible brush pairs snap with a diagnostic. Affects visual midpoint expectations and must be documented. |
| Appearance and hidden state | Snap theme/contrast/presentation-mode changes; cancel on hidden/minimized and snap on restore. Avoid background work and inaccessible transient palettes. |
| Experimental migration | Remove/obsolete held-sample APIs visibly in a prerelease change; do not silently reinterpret callers' temporary overrides as target transitions. Coordinate exact warning/removal version with #140. |
| Metadata interface spelling | Extend the existing Property<T> identity with typed interpolation and invalidation metadata; settle exact public/internal shape during T1. The typed-style migration is already delivered. #124's accepted failure contract is an implementation prerequisite, not an open owner choice. |

These choices settle the design decision list. The owner designated CSS and [Motion](https://motion.dev/) as comparative references for further refinement. Compare semantics, authoring ergonomics and measured cost before adopting refinements; neither a new dependency nor expansion of first delivery is implied.
