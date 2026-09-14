# A0 application authoring probes

These are bounded feasibility experiments for [A0 #302](https://github.com/RichiCoder1/lucent/issues/302),
not implemented application-authoring support or a passing A0 gate. Production compiler,
SDK, runtime and language-server behavior is unchanged.

## Generator phase experiment

From the repository root:

```powershell
./tests/Probes/ApplicationAuthoring/Run-PhaseProbe.ps1
```

The wrapper builds the existing Core/compiler with locked dependencies and runs the
isolated probe. It references the pinned SDK's Roslyn assemblies explicitly, rather than
changing production package pins. The JSON generator is the actual .NET reference-pack
generator; override `-JsonGeneratorPath` when testing another recorded version.

Observed September 14, 2026: SDK 10.0.401, Roslyn assembly 5.9.0.0, compiler product
5.9.0-1.26423.113 (`e34a38d2ae1fc26406a317517196e55c68ff83ab`), reference pack 10.0.12,
JSON generator assembly 10.0.14.42308. The experimental pre-compilation API warning is
suppressed only at the explicit experiment call; this does not approve production use.

The experiment executes these assertions:

1. Early ordinary declarations reach the real JSON generator, which emits five sources.
2. A sibling generator's compilation cannot see its ordinary generated `Default` API.
3. Exactly one preparation pass supplies binding-only JSON trees. A final pass emits each
   declaration once, with matching JSON hint names and exact UTF-8 source hashes.
4. A state initializer resolves the real model type, and generated JSON serialization
   executes after final assembly emission.
5. An in-memory declaration edit changes generation; deletion removes both authored and
   generated symbols. Changed outputs fail the equality contract instead of triggering
   another pass.
6. The current Lucent compiler rejects an actual `.lui` state initializer without generated
   JSON APIs, accepts it with preparatory output, and emits a component whose mounted text
   exposes the expected serialized model. This uses the existing static factory surface.
7. `Combined.lui.input` puts model/context declarations and the component into one input.
   A bounded Roslyn projection separates the preceding types and masks their spans for
   current LUI parsing. Real JSON output and lowered component output compile together.
   This experiment supports types before the component only; general ordering, complete
   declaration source maps and editor integration remain A0/A1 work.
8. Separate model/context declaration inputs bind to each other and an ordinary C# caller.
   Removing the model removes its JSON type-info output and produces unresolved-reference
   errors, rather than leaving the previous generated type in the compilation.

`Application.declarations` and its implementation in the probe stand in for future LUI
projection; they are deliberately ordinary C# sources. `JsonView.lui` is parsed/lowered by
the actual compiler and uses an explicitly typed field, as required by today's grammar.
The driver-buffer edit is not an LSP test. Observing two drivers does not establish an SDK
build path, named component/companion semantics, route generation, or NativeAOT packaging.
The SDK-host experiment and the remaining gate cases must establish those independently.

One successful local process observed about 268 ms for the initial generator pass and
311 ms for preparation, final generation, emission and execution together. These are
single-process observations, not editor latency or a comparative performance claim.

No fixture launches a UI. Failures return a nonzero exit without triggering an unhandled
exception dialog. All build products remain under ordinary ignored output directories.
