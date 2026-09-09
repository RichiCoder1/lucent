# Transition mechanisms: primary-source comparison

Research for the transition design following #119, coordinated with #140. Consulted September 8, 2026. This is a proposed conceptual influence ledger, not implementation approval or a claim of framework compatibility. No upstream source is copied or translated.

## Lucent baseline

The inspected checkout already has typed property channels, source and variant provenance, control-owned values, a reduced-motion theme flag, and scope cleanup. However, `TransitionController.Start` stores one supplied value until expiration; `Advance` expires entries. It does not interpolate endpoints or observe winner changes. `ResolveLocal` adds that sample above the control value and suppresses its presentation under reduced motion. Those facilities remain experimental. See [ElementPresentation.cs](../../src/Lucent.Core/ElementPresentation.cs), [PropertyTheme.cs](../../src/Lucent.Core/PropertyTheme.cs), and [ADR 0002](../adr/0002-lui-authoring-surface.md).

The design must attach to the current resolved property model rather than the archived Avalonia implementation. Final property metadata and invalidation policy must be reconciled with #140 before runtime implementation. No framework mechanism below justifies changing disabled activation, requested-versus-applied selection, equality suppression, or the application-session failure contract.

## What the primary sources establish

### CSS Transitions: a precise target-change boundary

CSS Transitions Level 1 defines a style-change event with before/after values. The after-change calculation excludes transition samples, while the before-change side samples existing animation at the current time. Declarations from the new style govern newly started transitions. Initial insertion does not start a normal transition. A changed endpoint can restart from the current sample; reversal has a separate shortening rule. Removing the matching transition declaration cancels the transition. Eligibility depends on both the property's animation type and the particular value pair. This is a Working Draft, not a Lucent conformance requirement. [CSS Transitions Level 1, January 8, 2026, sections 3–4](https://www.w3.org/TR/2026/WD-css-transitions-1-20260108/#starting).

Level 2 supplies `@starting-style` for cases without a preceding rendered style. That is useful evidence that entry animation needs an explicit origin policy; it is not a reason to infer an entry effect for every newly mounted Lucent element. [CSS Transitions Level 2, starting styles](https://www.w3.org/TR/css-transitions-2/#defining-before-change-style).

The reduced-motion media feature conveys a user preference to remove or replace troublesome motion; it is a preference input rather than a universal automatic transition-cancellation algorithm. [Media Queries Level 5, prefers-reduced-motion](https://www.w3.org/TR/mediaqueries-5/#prefers-reduced-motion).

### Avalonia: automatic declarations with type-specific interpolation

Avalonia attaches transitions to controls or styles. Each declaration names a property, duration, delay, and optional easing; the transition class must match the property's type. The documented transform path uses transform operations, and the documentation explicitly excludes WPF-style transform objects from that transition path. This supports an ergonomic property declaration backed by explicit interpolation eligibility rather than assuming every structurally numeric object is interpolable. [Avalonia control transitions](https://docs.avaloniaui.net/docs/graphics-animation/control-transitions).

The consulted public page does not establish all interruption, idle-clock, property-priority, or detach semantics. Attempts to fetch `Animatable.cs` through the web source reader failed; this note deliberately makes no claims about those internals. Do not infer that Lucent needs Avalonia's observable/property machinery. Context7 resolved `/avaloniaui/avalonia-docs` and returned the official type and declaration guidance before direct-source inspection.

### Flutter: target retargeting and an owned frame participant

Flutter's implicit widget update implementation evaluates the existing tween at the current animation value, installs that result as the new beginning, assigns the new endpoint, and restarts its controller. That gives position continuity under retargeting, not a promise of velocity continuity. [ImplicitlyAnimatedWidgetState.didUpdateWidget implementation](https://api.flutter.dev/flutter/widgets/ImplicitlyAnimatedWidgetState/didUpdateWidget.html).

Its `Ticker` begins disabled, invokes callbacks once per animation frame when running, supports cancellation of the next scheduled callback, and releases resources on disposal. Muting suppresses callbacks while time continues to elapse. These are useful separate concepts: an animation can have elapsed time without deserving a frame callback. [Ticker API](https://api.flutter.dev/flutter/scheduler/Ticker-class.html).

`AnimationController` obtains frame timing through a ticker provider, is normally created during owner initialization, and must be disposed with that owner. Controller futures distinguish cancellation through `orCancel`; callers must understand that an ordinary canceled completion future need not finish. [AnimationController API](https://api.flutter.dev/flutter/animation/AnimationController-class.html).

`AnimationBehavior.normal` reduces duration when the accessibility flag disables animations. `preserve` retains behavior for cases such as scrolling; the documentation also calls out repeating-animation flashing hazards. This is evidence against blindly applying duration scaling to every temporal behavior. [AnimationBehavior API](https://api.flutter.dev/flutter/animation/AnimationBehavior.html).

Context7 resolved the Flutter API and official website collections; the first API search returned unrelated embedder results, so the claims above use the directly inspected first-party API pages. The official website query additionally confirmed `AnimatedContainer`'s target-driven authoring, but Lucent does not need an animated wrapper for each primitive.

## Evaluation and recommended boundaries

The following are Lucent design judgments, not claims about equivalent behavior or measured performance in the compared frameworks.

| Concern | Recommendation for Lucent | Developer and performance consequence |
| --- | --- | --- |
| Change sampling | Resolve base winners for a committed update, excluding animation samples; compare typed values, then sample an interrupted track at the same timestamp. | One meaningful transition per committed target change; no animation feedback into its own endpoint. |
| Equal values | Update provenance without restarting when the winning typed value is equal. | A source change can remain diagnostically visible while equal `Derived` values suppress downstream work. |
| Precedence | Animate the already selected eligible presentation target. Preserve a separate target/provenance read and sampled presentation read. | Animation does not become an alternate way to write a control-owned logical value or change selection ownership. |
| Authoring | Put a finite property-to-spec mapping in ordinary `.lui` styles; infer the sampler from typed property metadata. | One concise declaration works with hover, pressed, token changes, and reactive values. No application timer, wrapper component, or string property lookup. |
| Property eligibility | Start with bounded paint properties; require a supported value pair as well as a supported property. | Prevent accidental layout work, unsupported brush morphing, or matrix interpolation merely because fields are numbers. |
| Retargeting | Start from the sampled visible value with a fresh finite duration. Document that velocity can change. | Simple deterministic behavior; springs and CSS-style reversal shortening can be evaluated later using concrete UX cases. |
| Entry | Seed first presentation directly to the final target. | No invisible first frame, implicit fade from defaults, or mount replay under list virtualization. Explicit entry/exit ownership is separate work. |
| Frame ownership | One composition-owned scheduler, mount-owned tracks, a monotonic host clock, and a manually advanced headless clock. Only request another frame while live visible tracks need it. | Active-track work is bounded by animated mounted properties; no per-element timers or animation-induced idle polling. Hidden-window policy must explicitly decide how elapsed time resumes. |
| Reduced motion | Settle decorative transitions immediately on enable and remove their frame demand. Do not replay them when disabled again. | Strong, testable accessibility behavior; future scrolling/physics policies remain separately specified. |
| Appearance | Settle on high-contrast changes. Prefer immediate ordinary theme changes for the first delivery unless the owner explicitly chooses a coordinated theme-transition policy. | Avoid temporarily unreadable mixed palettes and large scene-wide workloads. Stock semantic tokens remain authoritative. |
| Invalidation | Use property metadata to choose frame or layout work; the first supported set must be paint-only end to end. | A paint property that still triggers full paragraph/layout projection does not meet the performance acceptance criterion. |
| Failure | Use existing session termination, cleanup, and reporting for escaping callbacks; prefer no user callbacks in initial transition declarations. | No animation-only error boundary or silently swallowed exception. Cancellation diagnostics can remain data. |
| NativeAOT | Statically reachable typed samplers and compiler-lowered declarations, with no reflection discovery or runtime expression compilation. | Keep the closed initial surface inspectable and trimming-safe. |

No comparison here proves a performance win. Validate zero animation wakes when settled, equal-target suppression, bounded active-track counts, paint-only invalidation, disposal during a queued frame, and deterministic large clock jumps. Repeated reversal fixtures should show actual intermediate values so a later shortened-reversal or spring proposal can demonstrate a developer-visible improvement.

## Owner-designated refinement references

On September 8, 2026 the owner accepted the seven Lucent design recommendations and designated CSS and [Motion](https://motion.dev/) as comparative references for further refinement. Keep CSS's target-change and interruption rules as a baseline comparison. Use Motion to investigate richer authoring and motion behavior when a concrete follow-up warrants it; this instruction does not adopt Motion or reopen first-delivery scope.

Motion's [easing documentation](https://motion.dev/docs/easing-functions) distinguishes tween duration from the curve distributing movement over that duration. Its [CSS spring documentation](https://motion.dev/docs/css) distinguishes visual duration from the complete spring settlement time. These are useful future comparison points for Lucent's fixed-duration transitions versus any later spring design. Context7 resolved the official `/websites/motion_dev` documentation and queried transition configuration on September 8, 2026. No performance parity, native-host behavior or interruption guarantee is inferred from this bounded documentation check. Further refinements must evaluate concrete behavior and cost, preserve retained ownership and accessibility, and record any new adopted influence before implementation.

## CREDITS implications

`CREDITS.md` already lists Avalonia, Flutter, and CSSWG with conceptual references, but the existing influence descriptions do not fully record this proposed transition design. Before implementation adopts these influences, extend the active ledger with:

- CSS Transitions Level 1 Working Draft dated January 8, 2026: before/after sampling and cancellation as conceptual guidance, explicitly excluding browser cascade compatibility and shortened-reversal conformance.
- CSS Transitions Level 2 and Media Queries Level 5: consulted September 8, 2026, starting-style distinction and motion-preference semantics; record the exact published draft identity at adoption. W3C specification/document terms apply.
- Avalonia official control-transition documentation: consulted September 8, 2026, type-directed property declarations. Retain the existing Avalonia MIT identity; do not claim uninspected runtime internals or source reuse.
- Flutter API documentation: consulted September 8, 2026, current-value retargeting, ticker ownership, and accessibility policy separation. The API pages reported a nonuseful `0.0.0` footer, so no Flutter release number is inferred. Pin the corresponding source commit if implementation consults or translates code; Flutter's existing BSD-3-Clause ledger remains relevant.

This note adds no dependencies. If code is copied later, repository policy separately requires file-level attribution, exact upstream commit, and license notice. A conceptual ledger update cannot substitute for those obligations.
