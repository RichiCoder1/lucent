# M6 verifier

Use `pwsh tools/Verify-M6.ps1 -Mode PreCommit` for the automated dirty-tree subset. It records `precommit-automated-only` evidence under `artifacts/m6/`, updates the compact tracked summary, and does not claim final acceptance.

Use `pwsh tools/Verify-M6.ps1 -Mode Candidate` from a clean tree. It runs `Verify-M1.ps1` first, then performs the complete automated NativeAOT build, measurement, recursive package inventory, and zip once. Candidate evidence records the clean source commit/tree and exact lowercase SHA-256 of `artifacts/m6/lucent-win-x64.zip`; acceptance is `clean-automated-candidate`, with manual and clean-machine gates pending. Keep that candidate commit unchanged while collecting external records.

Use `pwsh tools/Verify-M6.ps1 -Mode Final -ManualGateRecord <record.json> -CleanMachineGateRecord <record.json>` from that clean candidate source. Final reads the existing candidate evidence and zip only: it does not rebuild, repackage, or remeasure. It verifies the candidate mode/acceptance, clean source commit/tree, current baseline/verifier hashes, frozen operation budgets, nonempty informational observations, recursive publish/extracted inventories, exact zip SHA-256, and strict case-sensitive gate-record schemas and values. Both gate records bind the exact candidate evidence SHA-256 as well as its source and package. The final evidence and compact summary record the gate contents and file hashes with acceptance `final-gates-recorded` and no pending gates. Missing, malformed, stale, or mismatched inputs fail closed.

The external NativeAOT `Lucent.M6.Verifier` drives only the copied, extracted application through posted Win32 keyboard messages and `SetWindowPos` resize operations. The frozen `M6-BASELINE.json` contract covers warmup, vsync, GC, latency, virtualization, memory, resources, corpora, and exact asset/license inventory. Visual review, Accessibility Insights/Narrator, real Japanese IME interaction, and clean-machine package execution remain external gates.

## Clean-machine Sandbox gate

Run this from the candidate source tree, supplying the exact candidate files:

```powershell
pwsh tools/Run-M6Sandbox.ps1 `
  -CandidateZipPath artifacts/m6/lucent-win-x64.zip `
  -CandidateEvidencePath artifacts/m6/m6-evidence.json `
  -GateRecordPath artifacts/m6/clean-machine-gate.json
```

The host rejects dirty/non-candidate evidence and a package SHA mismatch before creating a temporary input/output pair. Only the candidate zip, candidate evidence, and guest verifier are mapped into Windows Sandbox; input is read-only, output is writable, and the generated `.wsb` sets `<Networking>Disable</Networking>` and a `LogonCommand` (`wsb start` receives its documented `Disabled` spelling). There is no repository/source mount, test mode, download, feature enable, elevation, or reboot.

On Windows 11 24H2+, the host prefers the documented `wsb start --raw --config <formatted-configuration>` CLI and stops the returned session after polling the mapped result. If `wsb` is unavailable, it opens the generated `.wsb` through the registered Sandbox association and polls the same result. The CLI behavior is documented by Microsoft at [Windows Sandbox CLI](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-cli); `.wsb` mappings and `LogonCommand` are documented at [Use and configure Windows Sandbox](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file).

If no Sandbox executable is available, the script fails without changing Windows and prints this prerequisite for a separate elevated/manual operation. A newly enabled feature may require a reboot before that executable becomes available:

```text
DISM.exe /Online /Enable-Feature /FeatureName:Containers-DisposableClientVM /All /NoRestart
```

The guest proves that neither a `dotnet` command nor standard development SDK directory exists, verifies the candidate zip SHA-256 and every extracted file's relative path, byte length, and SHA-256, then launches the exact `Lucent.IssueBrowser.exe`. It waits for an HWND, requests ordinary `WM_CLOSE` through an `Add-Type` user32 declaration, and sets `launchPass` only after exit code 0. After the tested process has ended, the verifier atomically writes `clean-machine-gate.json` and a SHA-bound completion marker; the host waits for both before accepting the fixed schema consumed by `Verify-M6.ps1 -Mode Final`. `candidateEvidenceSha256`, `packageSha256`, and `sourceCommit` bind the result to the candidate.

Sandbox execution is intentionally not run on this development host when the feature is unavailable or unknown. Validate the scripts locally with PowerShell syntax checks, then run the command above on a supported Windows 11 24H2+ clean-machine host.
