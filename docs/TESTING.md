# Testing

Use the smallest meaningful check for the behavior you changed. The repository uses MSTest and Microsoft.Testing.Platform for named tests, filtering, and failure reports. CI and local development share [Test-Repository.ps1](../tools/Test-Repository.ps1).

## Everyday checks

```powershell
# Default managed suite; also used by Windows CI.
./tools/Test-Repository.ps1

# One project, optionally narrowed to a named behavior.
./tools/Test-Repository.ps1 -Project Lucent.Core.Tests
./tools/Test-Repository.ps1 -Project Lucent.Core.Tests -Filter 'FullyQualifiedName~ApplicationTests'

# Inspect which managed suites the current changes select.
./tools/Verify-Affected.ps1 -ListOnly
```

The affected-file helper includes relevant downstream suites and falls back to all managed tests for shared or unrecognized changes. Documentation-only changes need content and link inspection. Select additional checks by behavior; a path-based selection does not establish that published interaction or packaging works.

## Additional suites

| Suite | Use it for |
| --- | --- |
| `Published` | NativeAOT packaging, startup/close, presentation, desktop interaction, and UI Automation behavior. |
| `Sdk` | SDK package consumers, MSBuild integration, and generated NativeAOT applications. |
| `Performance` | Runtime and tooling operation measurements when investigating performance. |
| `Accessibility` | Automated Windows accessibility rule scans against the published application. |

```powershell
./tools/Test-Repository.ps1 -Suite Published
./tools/Test-Repository.ps1 -Suite Sdk
./tools/Test-Repository.ps1 -Suite Performance
./tools/Test-Repository.ps1 -Suite Accessibility
```

Tooling measurements use [LuiTooling.Measurements.json](../tools/LuiTooling.Measurements.json). Results distinguish whole-corpus timings from completion, diagnostics, and rename operation timings. Builds happen before measurement; timing limits are optional configuration, not a frozen milestone baseline.

The published suite includes `Test-WindowsLifecycle.ps1`: a `.lui` application with controlled pending work verifies close rejection/retry, accepted-work drain, asynchronous service cleanup, and startup/stop/disposal failure paths through the real Windows host. `Lucent.Hosting.Tests` covers the portable Microsoft hosting adapter.

Desktop interaction checks require an interactive Windows session. They launch and close their own application processes. Keep desktop checks separate from unrelated work that changes focus or input.

## Desktop and accessibility coverage

FlaUI UIA3 drives published application workflows through the public UI Automation surface. Direct UIA contract checks retain precise assertions for provider identity, lifetime, stale nodes, and error behavior. Axe.Windows supplies automated accessibility rule scans and inspection output; a scan does not perform the manual tab-stop portion of Accessibility Insights FastPass.

These dependencies belong to the test driver. The application remains NativeAOT-compatible and has no test-framework dependency, private test IPC, or proof mode. Tests use deterministic local data and ordinary input, accessibility, and diagnostics interfaces. See [CREDITS.md](../CREDITS.md) for dependency identities and attribution.

Follow the [pre-release verification policy](agents/verification.md) for check selection. Record a short result and any limitation in the issue. Keep generated reports, captures, and binaries under ignored `artifacts/` directories; completed evidence remains in issue records and Git history.

The published suite also runs `Test-EditorSessions.ps1`: a `.lui` editor changes arrangement after native window resizes in both directions and checks draft/selection/undo continuity, explicit focus handoff, SDL text-input lifecycle, canceled preedit and real shaping/paint. This is automated transport evidence, not real-language IME certification.

For focused input allocation measurements, run `dotnet run --project tests/Lucent.Performance.Verifier -c Release -- --input-dispatch`. It reports flat/deep pointer, key, and repeated same-target focus cases after checking that dispatch remains accepted; timing is diagnostic rather than a new release threshold.
