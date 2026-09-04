# M7 `.lui` cutover evidence

This is the automated cutover record for issue #55. The pre-existing cutover rows were captured against `7e1b4b1` before this evidence file was added. The cross-language Find All References row records the current uncommitted working candidate and is not evidence for that commit. It does not close issues or replace the separate manual milestone reviews.

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

The current measured tooling results were: cold project load 6145 ms/30000 ms; compiler corpus 5526 ms/15000 ms; formatting corpus 5512 ms/15000 ms; incremental corpus 4875 ms/15000 ms; warm completion 152 ms/120000 ms; edit-to-diagnostic 78 ms/120000 ms; rename 38 ms/120000 ms.

## Remaining closure work

The current working candidate's final commit/hash, fresh independent review, manual Accessibility Insights/visual/IME reviews, and final package/release gates are pending parent completion; this record does not claim they ran.
