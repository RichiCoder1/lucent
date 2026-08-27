# Lucent Native adversarial plan review

Reviewed on 2026-08-26 against merge commit `fb7698688f217a3ded49ec97e58b52d9f7ae87ef`, the four Native support documents, GitHub Project 4, and the complete bodies of issues #2–#17.

## Review lanes

- **Sol (`gpt-5.6-sol`)** — plan, ticket, evidence, and dependency-contract review. Initial verdict: **FAIL** until the first gate proved the integrated native dependency graph rather than unrelated pieces.
- **Claude Opus 5** — architecture falsification and repository cross-check. Verdict: **PASS for starting issue #2**, with corrections required before later slices.
- **Claude Fable 5** was requested first, but the configured provider returned `credits_required`; the user explicitly approved Opus 5 as the substitute. No Fable review result is claimed.

## Corrections applied

- Milestone 1 now proves SDL or bounded Win32 fallback, Skia presentation, shaping/fallback, practical real-IME editing, UIA COM transport, and NativeAOT together.
- Retained-scene work follows the platform feasibility proof instead of hardening in parallel with it.
- Practical IME includes preedit/commit/cancel, candidate positioning, focus loss, surrogate pairs, and a real non-Latin Windows IME.
- Headless/native structural and semantic parity is explicit.
- Text shaping covers ligatures, combining marks, bidi/RTL, fallback, mixed script, emoji, missing fonts, culture, and DPI.
- Runtime reactivity keeps only an explicit future-compiler registration seam and adds unrelated-write, stale-generation, and disposal negatives.
- Structural regions must prove joint effect, focus/capture, semantic, scene, and async cleanup.
- Theme animation depends on the scheduler and tests reduced-motion transitions and idle behavior.
- The Avalonia comparison uses a frozen shared walkthrough, source identities, and rubric, with `.lui`/CSS versus C# called out as a confound rather than credited entirely to the runtime.
- Accessibility requires an enumerated walkthrough, automated provider checks, and zero emergency suppressions at the final gate.
- Performance, virtualization, memory, sample count, percentiles, idle, and source/machine identities are predeclared.
- Final NativeAOT evidence includes actual transitive native assets, licenses, attribution, and notices.
- The SDL-to-Win32 retry is a separate conditional issue (#19), not an unbounded expansion of the SDL ticket.

## Deliberate non-change

Opus proposed a calendar or line-count ceiling. That was not adopted because the product decision explicitly allows a substantial framework core when it remains coherent and materially simplifies application code. Instead, "disproportionate machinery" is operationally defined by failure to satisfy the bounded platform proof without delegated controls or a second general UI stack.

## Result

The corrected plan is ready to begin issue #2. Each milestone still requires a recorded proceed/stop decision; this review does not pre-approve Milestones 2 or 3.

## Milestone 2 authoring reframe

The first authoring gate later recorded STOP at commit `3d0da02`. That result is
retained unchanged. Parent review found that the comparison application bypassed
the product interface it intended to test: it owned a Skia renderer and pixel
geometry, manually synchronized computed rows into virtualization, and assembled
input, focus, rendering, and semantics in application code while the typed style
and retained-element modules remained isolated proofs.

The approved reframe therefore does not lower the old threshold or proceed to
Milestone 3. It inserts two new gates:

1. Milestone 2A must produce a bounded application-facing composition,
   reactivity, styling, behavior, projection, and diagnostics interface and
   rewrite the issue browser through it.
2. Milestone 2B then runs a newly frozen comparison weighted toward the actual
   hypothesis: reactive state, structural composition, async/lifecycle,
   styling/state variants, and change locality.

The review deliberately rejects three shortcuts: crediting engine primitives
that application code does not use, treating a declarative file format alone as
good styling ergonomics, and adding a virtual DOM/CSS engine to manufacture
parity. Issues #20–#24 track the bounded reframe. Milestone 3 remains blocked.

Milestone 2A subsequently passed after three Sol xhigh review rounds. The final
round verified real pointer/key/focus behavior, composition-owned same-key
updates and clipping, reusable SDL translation, a clean source manifest,
published NativeAOT identity, all twelve walkthrough steps, and zero forbidden
application seams. This authorizes only the registered Milestone 2B comparison.

Milestone 2B then passed its frozen Closed-filter task and rubric. Parent scoring
gave Native 20 and Avalonia 14; Sol xhigh independently confirmed all category
scores and gate arithmetic, correcting only the non-blocking hunk counts to
eight Native versus eleven Avalonia. Milestone 3 remains the viability gate,
not a foregone adoption decision.
