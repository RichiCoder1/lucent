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

This is an infrastructure feasibility probe. Its payload inputs are supplied fixture files rather than the Lucent compiler's named-component output, and it does not prove project-graph parity, LSP behavior, source mapping, packaging, or the full A0 gate. The production integration must also treat workspace and analyzer-load failures as build failures, preserve the original evaluated compiler inputs, and avoid exposing preparatory binding inputs as ordinary AdditionalFiles.
