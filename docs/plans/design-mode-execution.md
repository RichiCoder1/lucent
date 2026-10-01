# Composition purpose execution

Implementation record for [#228](https://github.com/RichiCoder1/lucent/issues/228).
The public contract and examples are in [the design-mode guide](../DESIGN-MODE.md).

## Implementation

The existing composition and mount environment carry immutable
`CompositionPurpose.Application` or `CompositionPurpose.Preview`. Applications
and headless tests default to Application; hosts opt in before construction.
Component setup receives a captured `DesignContext`, and owned surfaces inherit
their originating purpose. No mutable global, debugger detection, ambient async
context or extra `Setup` parameter is involved.

The compiler binds an ordinary `Design` symbol before considering the contextual
receiver. Executable generation and editor projection share that decision and
authored source spans. A private, collision-safe capture is initialized before
authored component fields. Retained callbacks read the captured immutable value
without retaining a temporary mount context. The editor provides contextual
completion/hover and rejects renaming the intrinsic while preserving ordinary
symbol behavior, including unsaved shadowing edits.

## Review corrections

The adversarial review identified two compiler defects that were independently
reproduced with failing tests before correction:

- An inferred authored field named `Design` was missing from the ordinary probe.
  The probe now reserves that name even when its declaration has a separate
  explicit-type diagnostic. It never gains intrinsic editor metadata.
- Exact receiver mappings split a conditional style expression, preventing its
  token/value conversion. Whole-expression mappings are retained alongside
  precise receiver mappings, and translation chooses a mapping containing the
  complete requested range.

Generated lambda names also avoid authored `context` and `_` parameters.
Receiver ranges are indexed, and generated capture names are cached for each
semantic document rather than repeatedly scanning every source-map entry.

## Verification boundary

The focused runtime contracts cover independent compositions on one reactive
graph, purpose available during construction, retained callbacks, inherited
providers and popup/dialog ownership. Headless coverage checks the Application
default and explicit Preview propagation through the Skia wrapper.

Compiler tests cover initializers, methods, setup, markup, anonymous and named
components, authored shadowing, exact source mappings and the review regressions.
Editor tests cover completion, hover, rename and unsaved reclassification.
The existing packaged SDK fixture mounts the same generated component in both
purposes and checks its captured values before and after root disposal, in
managed execution and NativeAOT.

Warning-clean builds pass, with 159 compiler tests, 52 generator tests, 17
affected editor tests, 18 focused runtime contracts and 15 headless tests.
The Core architecture preflight and affected-source formatting also pass.
Compiler/editor logs, first failures and the source manifest are retained in
`C:/Users/richa/AppData/Local/Temp/lucent-design228`; runtime reports are in
`C:/Users/richa/AppData/Local/Temp/lucent228-runtime-tests`.

Final package verification and review results are recorded in #228 before
closeout. A 67-receiver compiler example completed in 138 ms on the active local
machine; this is one characterization sample, not a speedup claim or timing gate.

This work does not start previews or supervise user code. Static scenarios are
the next slice, [#229](https://github.com/RichiCoder1/lucent/issues/229), followed by
worker supervision and editor integration. No additional desktop walkthrough is
needed for these non-visual compiler/runtime changes; the preceding onboarding
walkthrough remains tied to its recorded official package versions.
