# Prepared SDK emitter probe

This executable A0 probe tests the build-time boundary for prepared application authoring. A preparation host opens the evaluated consumer through `MSBuildWorkspace`, runs one preparatory pass over arbitrary early declarations and real foreign generators, and compiles a deterministic project-specific analyzer. The normal C# compilation loads that analyzer, receives the early declarations through Roslyn's pre-compilation output API, and receives the final supplied payload through ordinary source output.

Run it from the repository root:

```powershell
./tests/Probes/ApplicationAuthoring/PreparedSdk/Run-PreparedSdkProbe.ps1
```

The wrapper verifies:

- ordinary source binds to the early declaration while the real `System.Text.Json` generator sees it;
- the normal final compiler receives the same foreign generator inputs, analyzer configuration, defines, target framework, runtime identifier, project reference, and AdditionalFiles;
- arbitrary early and final payloads produce a deterministic content-addressed emitter;
- a payload edit in the same project retains immutable cached emitters but selects only the current emitter for compilation;
- an unrelated outer global property changes the full input identity without changing the payload address, and remains visible to the foreign generator;
- changed, missing, extra, analyzer-identity, and nondeterministic foreign output comparisons fail the build and remove consumer assemblies.

The command log is written under `artifacts/a0-prepared-sdk/commands.log`. Other generated projects and binaries remain under that artifact directory.

The shared editor/SDK engine's driver-state contract has a focused executable regression:

```powershell
./.dotnet/dotnet.exe run --project ./tests/Probes/ApplicationAuthoring/PreparedSdk/Reuse/PreparedSdk.Reuse.csproj -c Release --artifacts-path ./artifacts/a0-preparation-reuse
```

It supplies a fresh generator wrapper with the same analyzer hash and ordinal, updates AdditionalText and analyzer-config inputs through the reused driver, and proves that analyzer hash or ordinal changes invalidate the reusable state.

After packing matching `Lucent.Core` and `Lucent.Lui.Sdk` candidates, exercise the production package's failure boundaries with:

```powershell
./tests/Probes/ApplicationAuthoring/PreparedSdk/Run-ProductionSdkNegatives.ps1 -Feed ./artifacts/a0-package-feed -Version <candidate-version>
```

That wrapper uses the packaged preparation host and SDK targets. It verifies a same-project edit against a persistent emitter cache, a warm missing-host failure, and a deliberately nondeterministic ordinary generator mismatch. Each failure must remove the consumer assembly. Its command log is written under `artifacts/a0-production-sdk-negatives/commands.log`.

The production host leases `%LOCALAPPDATA%/Lucent/PreparationWorkspace/workspace.lock`
across complete preparation and comparison operations. All package/source host
versions share this per-user path, regardless of invocation temporary directories;
the file remains in place after its exclusive handle is disposed. Known sharing
contention waits for up to two minutes. Other I/O or access errors fail immediately.
This serializes Lucent preparation work across unrelated repositories for that user;
it does not coordinate independent IDE sessions or ordinary MSBuild writers.

Ctrl+C cancels queued acquisition. When `LUCENT_PREPARATION_CANCEL_STDIN=1`, a line
on standard input provides the same cancellation signal for process controllers.
An active MSBuild workspace evaluation finishes before cancellation releases its
lease; this does not establish bounded cancellation of active MSBuild descendants.
The workspace is disposed before releasing the lease. Preparation and comparison
remain separate phases, and a real input change between them still fails comparison.

The wrapper verifies deterministic contention, a cancelled waiter that never
enters its workspace, active cancellation/failure release, retained compiler-visible
options and AdditionalFiles metadata, stable warm identity, and final comparison.
`-ArtifactRoot <fresh-directory>` keeps outputs elsewhere and
`-UseExistingPackageCache` reuses the normal cache. `-CoreVersion` explicitly selects
a different Core dependency for a local SDK candidate; this is source evidence,
not proof of official publication.

This is an infrastructure feasibility probe. Its payload inputs are supplied fixture files rather than the Lucent compiler's named-component output, and it does not prove project-graph parity, LSP behavior, source mapping, packaging, or the full A0 gate. The production integration must also treat workspace and analyzer-load failures as build failures, preserve the original evaluated compiler inputs, and avoid exposing preparatory binding inputs as ordinary AdditionalFiles.
