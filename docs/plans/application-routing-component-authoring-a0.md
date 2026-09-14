# A0 compiler and ownership feasibility

Status: in progress, September 14, 2026. [A0 #302](https://github.com/RichiCoder1/lucent/issues/302)
has initial executed generator-phase evidence. It has **not passed** the integrated
language, editor, identity, routing and packaged NativeAOT gate. A1–A7 remain gated.

## Candidate architecture

The promising candidate combines pre-compilation projection with one host-owned
preparatory generator-driver pass. Early authored types and component identity
declarations are visible to external generators. Their ordinary outputs are added only
to Lucent's binding compilation. Normal final compilation emits each source once.
Generator identity, hint names and exact output content must match preparation; a mismatch
fails rather than running another pass. The SDK and editor must share these boundaries.

The phase probe confirms why both parts are needed: the real JSON generator sees early
declarations, but a sibling generator's `CompilationProvider` cannot see its ordinary
generated APIs. No reflection-based component activation or preliminary assembly build
is required for the bounded driver experiment.

Production selection remains conditional on cold SDK evaluation, compiler-host compatibility,
diagnostics, input identity, cancellation, editor cost and packaged execution. The SDK
must not obtain generator inputs by recursively invoking itself; captured trees must never
be added as final `Compile` inputs. An SDK upgrade and a runtime-target upgrade are separate.

## Executed phase probe

Source: [tests/Probes/ApplicationAuthoring](../../tests/Probes/ApplicationAuthoring/README.md).
Production source baseline: `f3e4b784661ae96f04c0f9a48ab5b367a94681be`.

| Host/input | Observed identity |
| --- | --- |
| Repository SDK | 10.0.401 |
| SDK Roslyn assembly | 5.9.0.0 |
| Compiler product | 5.9.0-1.26423.113, commit e34a38d2ae1fc26406a317517196e55c68ff83ab |
| Production compiler/workspace package family | 5.0.0, unchanged |
| JSON reference pack | 10.0.12 |
| Actual JSON generator assembly | 10.0.14.42308 |

`./tests/Probes/ApplicationAuthoring/Run-PhaseProbe.ps1` exits 0. Its locked Core/compiler
prerequisite builds have zero warnings and errors. `artifacts/a0-phase-wrapper.log` records
the run locally. The probe passes early declaration visibility, absence of sibling-output
visibility, five exactly matching JSON outputs across preparation/final generation, final
assembly emission and JSON execution, in-memory input replacement, and deletion.

The actual LUI compiler rejects a state initializer without the generated APIs and accepts
the same explicitly typed initializer and method with preparatory JSON trees. Its emitted
component mounts against real Core and projects the expected serialized model into text
semantics. This uses today's static `Components.JsonView` factory; it is not proof of the
proposed named component surface.

The subsequent `Combined.lui.input` experiment also passes: model and attributed JSON
context precede a component in the same input, a bounded Roslyn projection supplies their
ordinary declarations to generation, and current LUI parsing/lowering consumes the masked
component source and real generated APIs. Final assembly emission succeeds. This proves
one combined input shape, not general source ordering or complete authored source mapping.

A prior fixture attempted `var` at component scope and correctly received LUI2023; current
LUI requires explicit field types. The accepted interoperability test does not require
changing that rule, so the fixture uses `Person`. A probe assertion originally escaped as
an unhandled exception; the harness now catches failures and returns exit 1 without an
exception dialog. The separate SDK-host sources are excluded from this probe's compile
items to keep each experiment independent.

One successful wrapper run measured about 250 ms for the initial generator phase and
285 ms for bounded preparation/final generation plus emission/execution. These include
process-local JIT/cache effects and are not language-server or keystroke measurements.

## Remaining gate evidence

| Required contract | Current boundary |
| --- | --- |
| Ordinary types colocated with UI and across files | Combined input with preceding types passes; full shared LUI document projection and cross-file tooling remain unproven |
| External generated APIs used by LUI | Real JSON initializer/method binding and mounted execution pass; cold SDK/LSP integration remains |
| Lucent route/state generated APIs | Existing generators have been inspected; the all-LUI routed fixture remains |
| Named partial identity and companion ownership | Separate prototype in progress; no production named component emitter exists yet |
| Editor parity | In-memory driver replacement/deletion pass; actual cold/unsaved LSP diagnostics, navigation, rename, cancellation and latency remain |
| Cold SDK and output matching | Separate SDK-host experiment in progress, including changed/missing/extra output negatives |
| Packaged NativeAOT application | Not yet run for this candidate; prior formatter/package results do not satisfy this gate |

The all-LUI application and companion variants remain separate acceptance fixtures.
No degraded generated-symbol binding, save-before-bind requirement, repeated-generation
convergence, or extra authored C# bridge is accepted as a substitute.
