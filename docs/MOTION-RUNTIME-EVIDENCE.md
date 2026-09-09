# Motion runtime evidence

Verified September 9, 2026 for the first production implicit-transition slice in issue #198.

## Implemented contract

- `Style.Transition` accepts only the canonical `Background`, `Opacity`, and `TextColor` property identities. Opacity requires finite endpoints in `[0,1]`; background animates only solid-to-solid brush pairs; incompatible gradients snap with `pair-ineligible` diagnostics.
- `Motion.Duration` accepts integer durations from 0 through 60,000 milliseconds. Zero equals `Motion.None`; `Motion.Quick` is 120 milliseconds with cubic ease-out. Sampling uses absolute nonnegative `TimeSpan` values, preserves fractional time, and rejects backwards timestamps.
- Targets remain authoritative through `Element.Resolve`. Paint reads use the separate committed presentation snapshot. Sampling is imperative and writes no application `Signal`.
- Control authority, explicit `None`, reduced motion, high contrast, appearance changes, hidden ancestors, unavailable presentation surfaces, removal, disposal, and fatal frame-demand observer failures cancel and snap active tracks.
- Effective reduced motion is application suppression OR platform suppression. A false or unknown platform value does not overwrite an application choice.
- A transition starts only from an acknowledged presentation of that exact visible element. Stale or mutated scene acknowledgements are rejected. Inherited children delegate to an ancestor sample until a local policy observes a later target change; explicit `None` blocks delegation, and removing `None` resumes it without fabricating motion.
- Active-track sampling and owner removal are bounded by active tracks and the three eligible property keys. Aggregate diagnostics retain counters only; `PresentationDump` is an opt-in deterministic current-state view and the existing value-free structural dump remains unchanged.

## Focused verification

The affected Core project built with zero warnings and zero errors. Thirty focused contracts passed across motion, inherited motion, retained paint projection, paint sampling, stock control policies, and image lease lifetime. These cover target coalescing and equality, current-sample retargeting, captured policy duration, linear-light premultiplied color math, unsupported brush pairs, transactional validation, sample-time suppression, acknowledgement, hidden/disposed ownership, paint-only input reuse, and stock actionable-state snapping.

The full Core suite passes 349/349. Integration reproduced a theme-writer feedback loop in `CaptureContinuityAcrossReprojection`: new theme setter equality guards had read their old signals reactively. Those guards now read without tracking. `ThemeMutationContracts` verifies that writing Theme, Appearance and PresentationMode does not subscribe the writing effect to their previous values, while explicitly read application state remains reactive. The existing bounded drain limit is unchanged.

The renderer characterization passes with 1, 100 and 1,000 active tracks, and Issue Browser's real virtualized hover/selection contract passes. A fresh NativeAOT Windows TestHost passes all three physical motion checks: autonomous intermediate pixels and active close, minimize/restore snapping, and application reduced-motion suppression. The [public guide](TRANSITIONS.md) records frame measurements. Independent Light Notes package integration is recorded in its repository and execution issue #202.
