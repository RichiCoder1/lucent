# Presentation transitions

Lucent styles can interpolate a small set of visual properties when their resolved targets change. Layout, input, accessibility and application state continue to use the target immediately. The composition owns the finite animation; components do not need timers or frame callbacks.

## Authoring

```lui
style CardStyle {
    Background: Color.Parse("#FFFFFF");
    transition Background: Motion.Quick;
    when Hover {
        Background: Color.Parse("#E2E8F0");
    }
    when Pressed {
        transition Background: Motion.None;
        Background: Color.Parse("#CBD5E1");
    }
}
```

The same statement works in parameterized and inline styles. Policy expressions are evaluated during style construction. Use variant or reactive `when` blocks to select an already constructed policy; the compiler diagnoses a policy expression that would silently snapshot reactive component state. Policy and value declarations follow the same variant specificity and author/component precedence rules.

The C# equivalent uses the typed property directly:

```csharp
Style.Empty
    .Transition(VisualProperties.Background, Motion.Quick)
    .Transition(VisualProperties.Opacity, Motion.Duration(180, Easing.EaseOut));
```

| Property | Supported interpolation |
| --- | --- |
| `Background` / `VisualProperties.Background` | Solid brush to solid brush |
| `TextColor` / `TypographyProperties.TextColor` | Color |
| `Opacity` / `VisualProperties.Opacity` | Finite float from zero through one |

`Motion.None` is immediate. `Motion.Quick` is 120 milliseconds with cubic ease-out. `Motion.Duration(milliseconds, easing)` accepts an integer from zero through 60,000; zero is immediate. The easing options are `Linear` and `EaseOut`, where ease-out is `1 - (1 - progress)^3`. Elapsed frame time preserves fractional milliseconds.

Valid incompatible brush pairs, including gradients, snap to the target. Invalid values fail before publication. Other properties and custom property identities cannot opt into this first slice. Layout, transforms, entry/exit animation, springs and keyframes are outside this contract.

Color channels interpolate in linear-light, premultiplied sRGB and publish exact endpoints. A halfway black-to-white transition therefore produces approximately sRGB 188, rather than 128. Transparent color channels do not bleed into an opaque endpoint.

## Targets, interruption and lifetime

`Element.Resolve(property)` returns the authoritative target and its provenance. Painting reads a separate committed presentation sample. Sampling does not write signals, style values, control state or theme tokens.

A newly mounted element snaps until a frame containing it has actually been presented. Repeated target changes before a projection coalesce into the final target. Equal targets do not restart a track. A changed target during motion starts a new full-duration transition from the current sample, including when reversing direction. Changing duration or easing alone does not rewrite an active track's captured policy; it applies to the next target change. Removing the policy or selecting `Motion.None` cancels and snaps.

Control-owned values remain authoritative. A control taking ownership cancels motion even when its target is numerically equal to the previous target.

An inherited `TextColor` with no local value or policy follows its ancestor's presented color without allocating a second active track. A child mounted midway through that transition joins the current sample. A local policy can animate the inherited target independently; explicit `Motion.None` uses the inherited target immediately. Removing the local policy resumes delegation.

Hidden, collapsed, removed and disposed elements release their motion. Reappearing or remounted content starts from its current target. Minimized or nonrenderable host surfaces snap and disarm demand; restoring a window does not replay the hidden interval.

Theme, appearance, contrast and presentation-mode changes snap atomically. High contrast always suppresses cosmetic motion. Effective reduced motion is the logical OR of `ThemeContext.ReducedMotion` (the application choice) and the platform preference. `SetPlatformReducedMotion(bool?)` updates the latter without overwriting the application choice; unknown platform preference adds no suppression.

## Stock presentation

Stock buttons and selectable rows use `Motion.Quick` for hover entry. Hover exit and pressed, focus-visible, selected, disabled and invalid presentation are immediate. Text color and focus rings remain immediate. Issue Browser consumes those defaults without application policy or palette overrides. Light Notes declares its intended row and back-action hover policy in `.lui`.

Authors can choose a base `Motion.Quick` policy, as in the example above, to interpolate both entering and leaving a hover value. The stock policy deliberately keeps its narrower behavior.

## Hosting and diagnostics

The Windows and headless hosts drive motion automatically. Custom portable hosts use this order:

1. Call `SamplePresentation` with an absolute monotonic `TimeSpan` at a frame opportunity.
2. Use `SceneLayout.ProjectFrame` with the preceding retained scene. It commits targets after bounded responsive/virtualized discovery, or reuses geometry and shaped text when only paint needs updating.
3. Install the accepted scene in the input router, present it, then acknowledge its exact generation with `TryAcknowledgePresentation`.
4. Observe `PresentationDemandAvailable` and `PresentationDemand` to schedule the next opportunity. Stop scheduling when demand becomes inactive. Use `SetPresentationAvailable(false)` when the surface cannot render.

Subscribe before mounting and recheck demand before waiting. Keep time advancement separate from acknowledgement: failed presentation must not establish an entry baseline. Pass no preceding scene after replacing renderer resources. Retained scenes independently own their image leases and must be disposed by their holders.

`RetainedScene.IsPaintOnly` identifies frames that reuse layout, shaped text and input data. Ordinary reactive changes, structural/input changes, viewport changes and a different text shaper force full projection. The Windows accessibility adapter also retains its semantic snapshot when its semantic revision and input geometry remain unchanged.

`PresentationDiagnostics` provides bounded counters for active tracks, starts, retargets, sampled tracks, cancellations and wake requests. `PresentationDump()` is an opt-in view of current paint targets, samples and motion state. The existing value-free `Composition.Dump()` keeps its original contract.

For the bounded performance characterization, set `LUCENT_MOTION_CHARACTERIZATION` to an artifact directory and run the `MotionPerformanceTests` and `StockRowHoverUsesRetainedPaintAndKeepsSelectionImmediate` tests. The tests report real frame phases and allocations; portable timing assertions use fake time rather than sleeps. See [TESTING.md](TESTING.md) for native scheduling and lifecycle checks.

### Local frame characterization

The September 9, 2026 Release run compared active linear background transitions with `Motion.None` on the same build. Each case used 160-pixel-wide, 18-pixel-high text rows, a bitmap tall enough for every row, and 30 frames at 20 ms intervals; the first five frames were discarded. Sampling, retained projection and input installation form the projection phase; the raster phase includes real Skia drawing and acknowledgement. Allocation values cover both phases on the current thread.

| Rows / tracks | None projection p50 / p95 | Active projection p50 / p95 | Active raster p50 / p95 | None / active allocation p50 |
| --- | --- | --- | --- | --- |
| 1 | 0.003 / 0.005 ms | 0.003 / 0.005 ms | 0.002 / 0.005 ms | 1,960 / 2,080 B |
| 100 | 0.155 / 0.180 ms | 0.154 / 0.220 ms | 0.207 / 0.303 ms | 79,056 / 88,680 B |
| 1,000 | 1.232 / 4.692 ms | 1.820 / 2.510 ms | 2.219 / 3.118 ms | 771,176 / 867,200 B |

All six cases performed zero additional text shaping. Each active case issued one coalesced start wake; `None` issued zero, and completing the finite tracks cleared demand. The no-motion control also replays paint, so this comparison measures interpolation overhead rather than savings against full layout. Timing variance, including the higher no-motion p95 at 1,000 rows, is not evidence that animation makes projection faster. These local observations are not universal latency guarantees.

Issue Browser's 10,000-item source realized 94 layout boxes in the tested viewport. Six hover sample frames reused geometry/input and added zero semantic revisions; projection measured 1.240 ms median and 3.153 ms maximum. Selection canceled cosmetic hover on its first committed frame. The maintained tests generate the detailed reports under the selected artifact directory.

The final managed-suite repeat measured 1.834 ms median / 4.086 ms p95 active projection and 2.159 ms median / 3.187 ms p95 raster at 1,000 tracks, with the same 867,200-byte allocation median. Issue Browser repeated at 0.940 ms median / 2.511 ms maximum. The variation reinforces keeping these as characterization rather than a wall-clock CI gate.

## Prerelease migration

The experimental held-sample APIs `Transition.For`, `Element.StartTransition`, `Composition.AdvanceTransitions` and the `Present(... transitions)` argument have been removed. Replace them with a typed style policy and an ordinary target change. Applications using the supported Windows or headless host do not need to call the frame APIs themselves.

The accepted architectural record is [ADR 0007](adr/0007-target-and-presentation-transitions.md). The [implementation plan](plans/transition-implementation.md) records the bounded delivery scope and evidence.
