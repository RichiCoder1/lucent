# Editor generation experiment

This isolated A0 experiment exercises the shared preparatory `GeneratorDriver` engine from a real `MSBuildWorkspace` project while `.lui` AdditionalDocuments change in memory. It is not Lucent language-server integration and does not prove LSP diagnostics, navigation, rename protocol behavior, project-graph parity, named components, routes, packaging or NativeAOT acceptance.

The wrapper restores an otherwise empty SDK fixture, copies the future-grammar support input to an artifact-only `.lui` path, and runs one editor host. The host discovers the real .NET 10 JSON generator from the evaluated analyzer references. A path-specific pre-compilation projection maps declarations back to the AdditionalDocument path with `#line`; the shared engine supplies the current unsaved AdditionalDocument snapshot instead of the disk AdditionalText.

The executable checks an unsaved property edit, deletion, rename, mapped declaration location, real JSON output invalidation, cancellation, and epoch-based stale-publication rejection. It also reuses the returned driver for an unchanged snapshot and an edit, records incremental step reasons, and measures the three process-local runs. These timings are feasibility observations, not an editor latency claim.

Run from the repository root:

```powershell
./tests/Probes/ApplicationAuthoring/Editor/Run-EditorProbe.ps1
```

Exact commands, exits, the console log and measurements are written under `artifacts/a0-editor`.

## Observed evidence

On 2026-09-14 the wrapper exited `0` with SDK 10.0.401 and `Microsoft.CodeAnalysis, Version=5.9.0.0`. The fixture restore, shared-host build, editor-probe build and executable all exited `0`. The evaluated project supplied `System.Text.Json.SourceGeneration` product `10.0.12+95017c711e6afc1085133d440e42b4bd78155701`, SHA-256 `BDEF5CE75FA321ACF8D2C2A5B2A9F9EB2A30EF4A28B1694FBCA978176C8DD6C9`. Five real JSON outputs were present before and after an unsaved property edit, changed from `Name` to `Title`, disappeared after in-memory deletion, and returned after an in-memory rename. The projected `Person` declaration mapped to line 4 of the current original AdditionalDocument path; the renamed result no longer mapped to the old path.

One process measured 592.85 ms for its first preparation, 34.23 ms for an unchanged reused-driver run, and 10.09 ms for the unsaved edit. The unchanged run reported ten cached incremental steps and the edit reported fifteen modified steps. These single-process observations include JIT and host state and are recorded only as an initial cost bound. `measurements.json` retains the exact observed values and generator reference identity.

The cancellation fixture blocked inside a cooperative generator, canceled the older epoch after a newer request began, and verified that neither canceled nor completed-stale work replaced the newer publication. Reuse with changed ordered generator instances or driver options is rejected. Reuse updates AdditionalTexts, parse options and the analyzer-options provider; the changed-options fixture observes the new value. A thrown generator exception is rejected even when Roslyn represents it through a generator result rather than an error-severity diagnostic.

## Remaining integration blockers

- Lucent's actual `LuiProjectContext.EvaluateProjectAsync` and `RenameSnapshotAsync` independently create editor solutions, remove `.lui` AdditionalDocuments, and build manual projections. This probe does not change either path or exercise LSP requests, so it is workspace feasibility rather than language-server parity.
- The SDK proof currently transports preparatory sources as AdditionalFiles. That makes them visible to every final generator, which does not establish the required binding-only isolation for arbitrary generators.
- The reusable engine validates ordered generator types and refreshes document, parse and option inputs, but it does not yet own the production cache key for analyzer binary hashes, metadata references, project graph, compiler options or source versions. The probe's AdditionalDocument adapter is synchronous because its texts are already resident; production collection must remain asynchronous and cancelable.
- Cancellation depends on generators observing Roslyn's token. A non-cooperative analyzer can still block an in-process editor host, so trust, timeout and process-isolation policy remain open.
- `#line` proves the Roslyn declaration location for this projection. It does not prove Lucent source-map diagnostics, definition, cross-language rename, referenced-project invalidation or renamed-file protocol behavior.
- The fixture uses projected support declarations and the real JSON API. It does not include the actual LUI parser/lowering, named companion identity, routes, final-output comparison, packaging or NativeAOT execution.
