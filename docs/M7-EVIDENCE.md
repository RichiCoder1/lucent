# M7 `.lui` cutover evidence

This is the cutover record for issue #55. The implementation candidate is commit `af598557a53b5c41756e2049ac561c35ac819e75`; it includes the cross-language Find All References support required by the M7 tooling contract. The package and external records below bind that exact source commit.

## Surface inventory

- The Issue Browser owns seven ordinary project `.lui` documents: `IssueBrowser`, `Header`, `FilterBar`, `Loading`, `Error`, `IssueRow`, and `Details`.
- Its C# application sources contain no `ComponentRecipe` declaration. `IssueBrowserStructure` owns startup/theme/mount lifecycle; state, async transport, fixture data, and helper expressions remain C#.
- `HandwrittenParityFixture` remains test-only evidence for the Filter Bar and virtualized Issue Row. No application dual composition path, runtime reader, registry, installer, updater, or component manifest exists.

## Automated gates

| Gate | Command | Result |
| --- | --- | --- |
| SDK, generated inspection, clean, incrementality, and NativeAOT consumer inventory | `pwsh -NoProfile -File tools/Verify-LuiSdk.ps1` | Pass |
| Issue Browser walkthrough, deterministic dumps/parity, NativeAOT smoke/pixels, and virtualized UIA | `pwsh -NoProfile -File tools/Verify-LuiParity.ps1` | Pass |
| NativeAOT package/native-asset inventory, negative inventory checks, architecture check, and three copied-publish visual/pixel walks | `pwsh -NoProfile -File tools/Verify-M0.ps1` | Pass |
| External UIA role/pattern/event/stale-provider matrix | `pwsh -NoProfile -File tools/Invoke-UiaProof.ps1 -HostExe artifacts/lui-parity-host/Lucent.Platform.Windows.Tests.exe` | Pass (two runs; 11 descendants, 5 focus, 8 property, and 1 structure event each) |
| Compiler, generator, LSP, formatter, and frozen tooling budgets | `pwsh -NoProfile -File tools/Measure-LuiTooling.ps1 -Verify` | Pass; all seven operations were within their frozen budgets |
| Full compiler/generator/LSP contract corpora and authored C# formatting | `dotnet run --project tests/Lucent.Lui.Compiler.Tests/Lucent.Lui.Compiler.Tests.csproj --no-build`; `dotnet run --project tests/Lucent.Lui.Generator.Tests/Lucent.Lui.Generator.Tests.csproj --no-build`; `dotnet run --project tests/Lucent.Lui.LanguageServer.Tests/Lucent.Lui.LanguageServer.Tests.csproj --no-build`; `pwsh -NoProfile -File tools/Verify-Formatting.ps1` | Pass |
| Cross-language Find All References | Debug/Release warning-clean compiler and LSP builds; Debug/Release compiler and LSP corpora; `node --test extensions/lucent-lui-vscode/extension.test.cjs`; `pwsh -NoProfile -File tools/Verify-LuiSdk.ps1`; `pwsh -NoProfile -File tools/Measure-LuiTooling.ps1 -Verify` | Pass; C# and `.lui` component/property/expression references, declaration filtering, normalized URIs, stale rejection, and shutdown are covered |
| Full current-source milestone gate | `pwsh -NoProfile -File tools/Verify-M1.ps1` | Pass against `af59855`; formatting, managed and NativeAOT contracts, package inventory, architecture negatives, three copied-publish pixel walks, and virtualized UIA proof passed |
| Clean automated release candidate | `pwsh -NoProfile -File tools/Verify-M6.ps1 -Mode Candidate` | Pass; acceptance `clean-automated-candidate` for source `af59855` and package SHA-256 `62910d069d06a68750f3c1b89ce064728d2b06eae7e19e697796cc8bf3ce70e9` |
| Clean-machine package gate | `pwsh -NoProfile -File tools/Run-M6Sandbox.ps1 -CandidateZipPath artifacts/m6/lucent-win-x64.zip -CandidateEvidencePath artifacts/m6/m6-evidence.json -GateRecordPath artifacts/m6/clean-machine-gate.json` | Pass in Windows Sandbox with networking disabled, no development SDK, exact inventory, and successful application launch |
| Manual milestone gates | Parent review of the exact candidate | Pass; light/dark/high-contrast and available 100/125/150/200% visual checks, Accessibility Insights role/pattern/selection/scroll/focus walkthrough, and bounded Unicode/dead-key/composition input review |
| Final release evidence | `pwsh -NoProfile -File tools/Verify-M6.ps1 -Mode Final -ManualGateRecord artifacts/m6/manual-gate.json -CleanMachineGateRecord artifacts/m6/clean-machine-gate.json` | Pass; acceptance `final-gates-recorded`, final evidence SHA-256 `2614109dc1fe8734ab3fb10679e124ea0f3ac539454ceb01513d0888fcbf8b7f` |

The current measured tooling results were: cold project load 6145 ms/30000 ms; compiler corpus 5526 ms/15000 ms; formatting corpus 5512 ms/15000 ms; incremental corpus 4875 ms/15000 ms; warm completion 152 ms/120000 ms; edit-to-diagnostic 78 ms/120000 ms; rename 38 ms/120000 ms.

## Evidence records

- Package SHA-256: `62910d069d06a68750f3c1b89ce064728d2b06eae7e19e697796cc8bf3ce70e9`
- Manual gate SHA-256: `0a54ec175de5b7ae245fdf8dcfd41065ebe55f23c6940205077c2381950c0c47`
- Clean-machine gate SHA-256: `38a7072bc03805e680c847eabad6d021b041fe9800d0de916d784e4c7947ffb1`
- Final evidence SHA-256: `2614109dc1fe8734ab3fb10679e124ea0f3ac539454ceb01513d0888fcbf8b7f`

Narrator and real-language IME certification remain supplemental and are not claimed by this record. Fresh independent closure review remains required before closing #55, #41, and #26.
