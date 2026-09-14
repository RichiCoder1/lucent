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
An additional cross-file driver case passes with separate model/context inputs and an
ordinary C# caller. Removing the model removes its generated type-info output and reports
unresolved references. This remains compiler-driver evidence, not cross-language LSP proof.

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

### SDK-host experiment

[SdkHost](../../tests/Probes/ApplicationAuthoring/SdkHost/README.md) now passes a cold
one-command positive build and execution. Eleven evaluated generators and two AdditionalTexts
yield six ordinary preparation outputs: five from the real JSON generator and one external
probe. Normal C# compilation emits the matching six identities/content hashes. There is no
preliminary assembly build, recursive preparation, or convergence loop.

Changed, missing and extra outputs each fail with `PROBE9001` and exit 1 after otherwise
valid C# compilation. The failure target removes both intermediate and final assemblies;
the wrapper verifies their absence. The complete wrapper exits 0. Commands and exits are
recorded locally in `artifacts/a0-sdk-host/commands-and-exits.txt`.

The experiment exposed and corrected an intermediate-output mismatch: its MSBuildWorkspace
must receive the outer build's evaluated `BaseIntermediateOutputPath`. It still reports one
non-fatal workspace warning because `.editorconfig` is both an analyzer config and an
explicitly transported AdditionalText. That warning is retained in the evidence; this is
not a warning-free workspace claim. Production diagnostic transport and trust/recovery
behavior remain unproven. The preparation/output-comparison engine is separate from its
MSBuild/CLI adapter for the forthcoming editor experiment.

Review identified two architectural limits beyond those positive results. The probe
transports binding trees as AdditionalFiles, so ordinary final generators can observe
those extra inputs. Matching JSON output proves that fixture, not general input isolation.
The host also forwards a finite set of outer build properties; it does not yet preserve
arbitrary global analyzer options, RID/platform, defines or reference selection. The
manifest records some identities for inspection but does not prove all evaluated-input
parity. These limits must be resolved or diagnosed within the accepted bounded pipeline
before production selection; they cannot be hidden behind the successful cold fixture.

The reviewed SDK wrapper subsequently exits 0 after additional negatives: a foreign
output containing Lucent marker comments is still compared; a broken analyzer alongside
healthy SDK generators fails with `PROBE0007` and no manifest; and a missing host on a
warm rebuild removes the previously successful consumer assembly. Subprocesses use the
same selected dotnet host. Generator execution exceptions are terminal even when Roslyn
reports them without an error-severity diagnostic. These checks retain the original
changed/missing/extra output assertions and post-compile cleanup.

### Workspace/editor-host experiment

[Editor](../../tests/Probes/ApplicationAuthoring/Editor/README.md) exercises the shared
engine from a real MSBuildWorkspace with current in-memory AdditionalDocuments. Real JSON
output follows an unsaved property edit, disappears after deletion and returns after rename.
Mapped declaration paths/lines follow the authored file. Canceled and completed-stale runs
cannot publish over a newer epoch in the probe's publication harness.

The final wrapper exits 0. One process measured 592.85 ms cold, 34.23 ms for unchanged
driver reuse with ten cached steps, and 10.09 ms for an edit with fifteen modified steps.
These are isolated host observations, not end-to-end LSP latency. Reuse rejects changed
generator instances or driver options, updates analyzer options/text/parse options, and
forwards cancellation. Exact commands and measurements are in `artifacts/a0-editor`.

The real language server, cross-language rename/diagnostics, shared production cache
identity and uncooperative analyzer isolation remain unproven. The experiment does not
lower LUI markup or establish SDK input parity.

### Named companion experiment

[Companion](../../tests/Probes/ApplicationAuthoring/Companion/README.md) uses one constrained
incremental emitter, early extended partial recipe factories and one final implementation.
It dynamically compiles and executes against real Core. Ordinary C# fields initialize
before companion managed state; instance-based LUI initialization then reads that state,
followed by one ComponentContext setup bridge. Two mounts have independent state and
cleanup, cross-file methods/properties work, and initializer failure rolls back cleanup.

Invalid inputs fail with prototype diagnostics for duplicate setup, authored constructors,
`[ComponentState]` misuse and unsupported inferred-derived/readonly state. Negative tests
check that invalid final source hints are absent. Writable LUI fields use explicit `[Once]`;
the prototype does not silently reinterpret derived expressions as writable signals.

The wrapper and focused probe exit 0; dependency builds are warning-clean and all nine C#
files pass formatting. This is an identity/initialization prototype: its root is a no-op,
it does not lower markup/requirements/parameters, and it does not implement all existing
state kinds. Those limits prevent treating it as the full A0 consumer.

### Alternative isolated-emitter phase experiment

[IsolatedEmitter](../../tests/Probes/ApplicationAuthoring/IsolatedEmitter/README.md) passes
a bounded alternative to the SDK probe's AdditionalFile transport. A small precompiled
analyzer publishes early declarations through pre-compilation output and its final
implementation through ordinary generator output. External generators receive only the
original authored AdditionalText. Five real JSON outputs match preparation by identity
and content; an external generator fingerprinting every AdditionalText also matches.

The negative models the earlier transport: adding those five binding files changes the
observable inputs from one to six, and the external observer reports `PROBE2001`. The
wrapper exits 0 with warning-clean builds. Exact command/exit records are under
`artifacts/a0-isolated-emitter`.

This establishes the phase/input-isolation principle, not a general implementation. The
emitter currently contains fixed fixture payloads. An SDK host must still produce the
project-specific emitter from actual declaration/lowering results, use a content-addressed
assembly path to avoid stale analyzer loads, preserve evaluated inputs, compare all final
foreign outputs and prove packaged execution. The editor should use the equivalent emitter
model in memory rather than build an analyzer DLL for each edit. No production pipeline
is selected solely from this experiment.

### Integrated acceptance still required

[Integrated](../../tests/Probes/ApplicationAuthoring/Integrated/README.md) now combines
actual LUI state/handler/markup lowering with real JSON output and a call to the existing
generated route factory. A constrained Roslyn transform promotes the lowerer's state
class to the named partial class and moves its factory to `Create`; it does not substitute
an outer state adapter. All-LUI and optional-companion console consumers each publish and
execute under NativeAOT, mount twice with distinct named state objects, and expose the
expected JSON and canonical route URI in real Core text semantics. The wrapper exits 0;
commands/exits are recorded in `artifacts/a0-integrated`.

This is source/project-reference evidence, not packaged SDK evidence or an actual routed
application: it does not mount a NavigationSession/outlet or prove route/component mapping.
The companion cross-reference negative exits 1 with the expected `LUI2000` for `Organization`.
Current member binding targets a private generated helper, so simply promoting that helper
after lowering cannot make companion instance members available during binding. The next
compiler experiment must bind and emit the same early named partial identity directly.

| Required contract | Current boundary |
| --- | --- |
| Ordinary types colocated with UI and across files | Combined input and cross-file driver binding pass; full shared LUI document projection and cross-file tooling remain unproven |
| External generated APIs used by LUI | Real JSON initializer/method binding and mounted execution pass; cold SDK/LSP integration remains |
| Lucent route/state generated APIs | Generated route factory call and JSON-backed state execute under NativeAOT; actual route/component association and navigation remain |
| Named partial identity and companion ownership | Companion ownership and transformed real markup/state execute; direct named binding, cross-file member semantics and remaining state kinds remain |
| Editor parity | MSBuildWorkspace unsaved edits, deletion, renamed origin, cancellation and measured reuse pass; actual LSP diagnostics, navigation, rename and latency remain |
| Cold SDK and output matching | Isolated host positive and changed/missing/extra negatives pass; actual Lucent SDK integration remains |
| Packaged NativeAOT application | Source/project-reference all-LUI and companion executables pass; packaged SDK routed application remains unproven |

The all-LUI application and companion variants remain separate acceptance fixtures.
No degraded generated-symbol binding, save-before-bind requirement, repeated-generation
convergence, or extra authored C# bridge is accepted as a substitute.

## Production integration boundaries identified by inspection

These are source-confirmed integration requirements, not additional executed passes.

- `LuiProjectContext.EvaluateProjectAsync` reads the current LUI AdditionalDocuments,
  then obtains its compilation from an editor clone that removes those inputs before
  building the manual component index. The new declaration preparation must run before
  that index and preserve current unsaved text. Merely installing an updated generator
  into the current clone cannot expose declarations that the clone has removed.
- `RenameSnapshotAsync` independently projects every project in the graph. It must use
  the same preparation results and authored source maps as ordinary diagnostics. Fixing
  only the diagnostic path would leave rename inconsistent.
- Existing project evaluations are published only while their captured epoch is current.
  Preparation must preserve that rule and propagate cancellation through generator runs.
  A cache hit needs the same source and AdditionalText identities/content, configuration,
  parse/compilation options, analyzer options/binary identities, references, and projection
  version; document text alone cannot establish equivalent generator inputs.
- Preparation and final comparison must identify owned outputs by generator identity,
  never by a comment marker inside generated text. All other outputs participate in exact
  identity/content comparison, including outputs whose comments resemble Lucent's.
- The existing project tooling trust policy applies: MSBuild evaluation and project
  analyzers execute trusted project code, as during a normal build. Pure syntax formatting
  remains independent of that execution. This design does not introduce an analyzer sandbox
  or change runtime NativeAOT restrictions.

Production Roslyn references remain unchanged. The isolated 5.9 experiment does not itself
establish compatibility for the shipped compiler, language server, SDK tool closure or
downstream analyzer hosts.
