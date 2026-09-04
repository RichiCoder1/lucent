# M7 `.lui` cutover evidence

M7 is sealed from source `06d50c828420839d76bf0172997418e8fe68cd40`. The subsequent evidence/documentation commit does not change the implementation candidate. Acceptance is `final-gates-recorded`; broad manual walkthroughs were explicitly waived by the owner as described below.

## Source and artifacts

| Record | SHA-256 or identity |
| --- | --- |
| Candidate source | `06d50c828420839d76bf0172997418e8fe68cd40` |
| Candidate evidence | `3e71580edb851c7ad97fd40f5195a3f9e2c3b9e6dac8937b8d4fccf2789b6978` |
| NativeAOT package | `cb1bbc69a2d5bbdb8dcb95c6035d2077b0afa94b8d89c80a4cec9c83947bc504` |
| Final evidence | `ba581a8d70989cddf709b588ed0caef36c283f0adfab88791639492f6ab79ec8` |
| Manual-gate acceptance/waiver record | `c4b2ab9b5d753399b293ef1c15c886d5ef51e209d3c0606205f5b04b01f1fb93` |
| Clean-machine record | `d1e246a313f2f27171f923eaa83a40b5d36a0f01c56e60685e9a634f1b4b65dc` |
| VSIX 0.2.0 | `a82a288ce0dac8b7077ff35b2eafc5174adcff97bf6cf2cf63da11f235d2fcca` |
| Installed language-server DLL | `c5fc7a79deff3126f30d696576c02084c3be8d1335b37eb8d4ef633996517856` |

Large records and binaries remain under `artifacts/m6` and `artifacts/handoff`. The pre-final candidate record is preserved as `artifacts/handoff/m6-candidate-evidence.json`; prior stale records are retained only for audit comparison under `artifacts/handoff/prior-m6`.

## Correctness and review

A fresh review found incomplete rename/references for a physical `.lui` component owned by multiple projects and a reload race that could throw during freshness validation. The remediation:

- aggregates compatible component symbols and occurrences across every owning project, including requests initiated from an owner-specific usage;
- preserves the requested prepare-rename URI/range;
- rejects incomplete, disjoint, overlapping, ambiguous, or incompatible declaration mappings;
- preserves ordinary C# and generated-parameter behavior;
- rejects a project-ID replacement race as stale rather than leaking an exception.

A failing linked-owner regression was reproduced before the fix. Release build/corpus, deterministic reload-race and mapping regressions, formatting, and diff checks passed afterward. A fresh independent technical review passed on the final implementation. A separate acceptance/evidence review verified the clean candidate, exact package inventory/hashes, budgets, and fresh Sandbox record.

## Executed checks

All commands below completed with exit `0` for this candidate's implementation:

- Release compiler and LSP warning-as-error builds and executable corpora; Debug compiler/generator coverage in the full gate and a separate final Debug LSP corpus.
- `node --test extensions/lucent-lui-vscode/extension.test.cjs` (4 tests).
- `pwsh -NoProfile -File tools/Verify-LuiSdk.ps1` including the NativeAOT consumer.
- `pwsh -NoProfile -File tools/Measure-LuiTooling.ps1 -Verify`; final rename 273 ms, completion 198 ms, edit-to-diagnostic 75 ms, with all frozen budgets passing.
- `pwsh -NoProfile -File tools/Verify-Formatting.ps1` and `git diff --check`.
- `pwsh -NoProfile -File tools/Verify-M1.ps1`, including managed/NativeAOT contracts, negative architecture/package checks, copied-publish pixel/focus/lifecycle checks, and external actual-app/fixture UIA proof.
- `pwsh -NoProfile -File tools/Verify-M6.ps1 -Mode Candidate`, which reran the clean full gate and generated the exact package/performance record.
- `pwsh -NoProfile -File tools/Run-M6Sandbox.ps1 -CandidateZipPath artifacts/m6/lucent-win-x64.zip -CandidateEvidencePath artifacts/m6/m6-evidence.json -GateRecordPath artifacts/m6/clean-machine-gate.json`.
- `pwsh -NoProfile -File tools/Verify-M6.ps1 -Mode Final -ManualGateRecord artifacts/m6/manual-gate.json -CleanMachineGateRecord artifacts/m6/clean-machine-gate.json`.

The performance record contains 500 input and 500 resize samples: input p95/p99 11.9900/22.9118 ms; resize p95/p99 8.3670/10.4946 ms; zero idle frames; 10,000 source rows with at most 4 realized; 16,512 bytes retained managed growth after 20 cycles. Resource and native-handle bounds passed. Sandbox verified exact inventory and launch with networking disabled and no development SDK or source mount.

## Editor installation

The server was published from the candidate and installed with a complete file-hash comparison. The VSIX was packaged with `@vscode/vsce@3.9.2`, installed through VS Code, and verified as `lucent.lucent-lui@0.2.0`. Installed code/grammar/configuration bytes match source; the manifest matches after excluding VS Code's installation metadata.

A real isolated VS Code instance loaded the installed extension and server and passed activation, completion, hover, definition, references, rename edit coverage, a deliberate unsaved formatting change, dirty-buffer type diagnostics, recovery, and source-file preservation. Evidence is `artifacts/handoff/editor-smoke-evidence.json` (completed 2026-09-04T21:30:13.064Z). No remaining bounded editor interaction required an owner-only action.

## Explicit manual-gate decision and remaining work

For this exact candidate/package, the owner answered: **"Accept automation; record the remaining manual walkthroughs as explicitly waived."** The final gate record uses `pass=true` for the accepted gate outcome and explicitly records the waiver in its notes. It does not claim fresh broad manual visual, Accessibility Insights, or live IME observations. Bounded international-input acceptance uses current deterministic Core/Windows contracts; real-language IME certification remains supplemental.

The known Issue Browser resize/content-coverage defect is the next work item; this editor seal does not claim that defect is fixed. Roadmap #26 remains open until its remaining acceptance is met. Then implement #58 and #59 and file the cleanup/testing issue described in the [handoff](agents/remaining-work-handoff.md).

Subsequent work follows the owner's [lighter pre-release verification policy](agents/verification.md), not a full milestone rerun for every issue.