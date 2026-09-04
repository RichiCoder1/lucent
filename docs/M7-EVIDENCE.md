# M7 `.lui` cutover evidence

The M7 implementation candidate is commit `0b6b08e347f426a223b50694c40c9bc9041e2ad2`. The package and external records below bind that exact source commit.

## Surface inventory

- The Issue Browser owns seven ordinary project `.lui` documents: `IssueBrowser`, `Header`, `FilterBar`, `Loading`, `Error`, `IssueRow`, and `Details`.
- Its C# application sources contain no `ComponentRecipe` declaration. C# retains startup, theme, state, async transport, fixture data, and helper expressions.
- `HandwrittenParityFixture` remains test-only evidence. No application dual composition path, runtime reader, registry, installer, updater, or component manifest exists.

## Cross-language tooling

- evaluated `ProjectReference` source declarations and references participate in cross-language rename and Find All References;
- paired `.lui` tag names and compiler-owned keyed-`foreach` local provenance lower to exact authored edits;
- the VS Code extension exposes Lucent results from `.lui` and C# documents only in workspaces containing `.lui` files;
- source-map ambiguity, unmappable locations, overlap, stale identities, and stale local epochs reject publication.

## Gates

| Gate | Result |
| --- | --- |
| Debug/Release warning-clean compiler and language-server builds and corpora | Pass |
| VS Code extension tests, including exact activation selectors and C# no-result behavior | Pass |
| `pwsh -NoProfile -File tools/Verify-LuiSdk.ps1` | Pass |
| `pwsh -NoProfile -File tools/Measure-LuiTooling.ps1 -Verify` | Pass within all frozen budgets |
| Fresh targeted cross-project/source-map review | Pass |
| `pwsh -NoProfile -File tools/Verify-M1.ps1` | Pass; formatting, managed and NativeAOT contracts, package inventory, architecture negatives, copied-publish pixel walks, and virtualized UIA proof passed |
| `pwsh -NoProfile -File tools/Verify-M6.ps1 -Mode Candidate` | Pass with acceptance `clean-automated-candidate` |
| Windows Sandbox clean-machine package run | Pass with networking disabled, no development SDK, exact inventory, and successful application launch |
| Parent visual, Accessibility Insights, and bounded international-input reviews | Pass against the exact candidate |
| `pwsh -NoProfile -File tools/Verify-M6.ps1 -Mode Final ...` | Pass with acceptance `final-gates-recorded` |

## Evidence records

- Package SHA-256: `94fa89068f6d84f341aa976d736fb333f8475c0ce34f3c3bc5fe0c9c4bac0e1a`
- Manual gate SHA-256: `59b4777f6cca5dd7f121931b65235e629e5a65fba1c1a9b33db9d959f7f82842`
- Clean-machine gate SHA-256: `540c7e1206f8cb2417b764f56c6dd30c458564aeeecf1678f92b0d464381028c`
- Final evidence SHA-256: `28500b0c643a51dbaaa181906f725b72c7918c3ceb144db4ecd437856c3688de`

Narrator and real-language IME certification remain supplemental and are not claimed. Fresh dual closure review remains required before closing #55, #41, and #26.
