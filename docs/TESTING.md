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

For reusable component/application tests without visible windows, use [the headless harness](HEADLESS-TESTING.md). `Lucent.Testing.Tests` is part of the default managed suite; `Lucent.Testing.Skia` adds optional real shaping and frame capture. Windows transport checks remain separate.

The style-driven layout slice combines Core contracts for algorithms, conditional style ownership and named window breakpoints with compiled `.lui` and consumer tests. Keep thresholds in the application's named BreakpointSet and test just below, at and above each boundary, including logical-size-preserving DPI changes and wide/narrow/wide identity restoration. Resizing an internal pane must not change a window breakpoint. Custom algorithms need invalid-output, measurement-budget, expired-context and per-container state tests; their virtualized children remain realization boundaries. `Test-HeadlessPackages.ps1` compiles parameterized styles using the packaged SDK and checks breakpoint-selected Grid/Flex geometry plus retained editor values against packaged runtime/testing libraries.

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

The VS Code client has a dependency-free Node test harness. Run it when changing the extension or its protocol/client boundary:

```powershell
node --test extensions/lucent-lui-vscode/extension.test.cjs
```

Windows CI runs this short contract check before the managed .NET suite. The managed check performs the Core architecture/public API preflight after restore/build and before the longer tests; its negative fixture proof remains after those tests.

Tooling measurements use [LuiTooling.Measurements.json](../tools/LuiTooling.Measurements.json). Results distinguish whole-corpus timings from completion, diagnostics, and rename operation timings. Builds happen before measurement; timing limits are optional configuration, not a frozen milestone baseline.

The published suite includes `Test-WindowsLifecycle.ps1`: a `.lui` application with controlled pending work verifies close rejection/retry, accepted-work drain, asynchronous service cleanup, and startup/stop/disposal failure paths through the real Windows host. `Lucent.Hosting.Tests` covers the portable Microsoft hosting adapter.

The published Windows TestHost includes two focused fixtures for the new framework seams. The layout fixture resizes a responsive `.lui` Grid between wide and compact arrangements, checks constrained paragraph line geometry, and paints at synthetic 100%, 150%, and 200% viewport scales. The input fixture focuses its native viewport and receives physical wheel and Ctrl+N/F/S events through the Windows adapter. These are framework TestHost proofs, not Light Notes application workflows; Light Notes maintains separate temporary-SQLite and workspace tests plus offline real-shell rendering/state checks and opt-in FlaUI persistence/resize/focus checks with targeted Axe.Windows scans against its published NativeAOT app. Its desktop check requires a coordinated uninterrupted input window; background UIA checks do not establish that an unoccluded screenshot was captured.

Desktop interaction checks require an interactive Windows session. They launch and close their own application processes. Keep desktop checks separate from unrelated work that changes focus or input.

`PublishedIssueBrowserTests.FlaUiNativeMenuTargetsUnselectedIssueAndPreservesSelection` covers the opt-in `--native-menus` path with keyboard, pointer and UIA invocation cases. It verifies foreground activation, discovers the actual Windows menu and its accessibility roles, invokes a command against an unselected issue, and checks selection preservation and Escape dismissal. The pointer case also checks placement outside the owner and shutdown with a menu open. Native keyboard checks use Windows arrow-key navigation. Run it as part of `Published`, or target it with `dotnet test --project tests/Lucent.Desktop.Tests -c Release --filter 'FullyQualifiedName~FlaUiNativeMenu'` after setting `LUCENT_DESKTOP_APP` to the published executable. This is a desktop test and must stay paused whenever focus testing is paused. Set `LUCENT_MENU_DIAGNOSTICS=1` to include native command outcomes in application standard error.

## Desktop and accessibility coverage

`PublishedIssueBrowserTests.FlaUiSplitPaneResizesAndRestoresAcrossNarrowNavigation` exercises the stock workspace through UIA range changes, physical keyboard input, captured dragging, narrow list/detail navigation and restoration of the preferred pane extent. `FlaUiSubmenuInvokesLeafAndDismissesOneLevel` covers nested Lucent keyboard/UIA commands and Windows-native keyboard menus. Set `LUCENT_DESKTOP_APP` to the published executable and optionally `LUCENT_DESKTOP_CAPTURES` to an artifact directory. The navigation driver supplies extended physical scan codes so SDL distinguishes arrow keys from keypad keys.

Issue Browser's managed real-Skia checks cover stock light, dark and high-contrast surfaces and constrained workspace geometry; set `LUCENT_HEADLESS_CAPTURES` to export their frames. Geometry assertions include the list and splitter's actual lower edges, not just the root scene size. These checks do not establish native focus or popup transport behavior.

FlaUI UIA3 drives published application workflows through the public UI Automation surface. Direct UIA contract checks retain precise assertions for provider identity, lifetime, stale nodes, and error behavior. Axe.Windows supplies automated accessibility rule scans and inspection output; a scan does not perform the manual tab-stop portion of Accessibility Insights FastPass.

These dependencies belong to the test driver. The application remains NativeAOT-compatible and has no test-framework dependency, private test IPC, or proof mode. Tests use deterministic local data and ordinary input, accessibility, and diagnostics interfaces. See [CREDITS.md](../CREDITS.md) for dependency identities and attribution.

Follow the [pre-release verification policy](agents/verification.md) for check selection. Record a short result and any limitation in the issue. Keep generated reports, captures, and binaries under ignored `artifacts/` directories; completed evidence remains in issue records and Git history.

The published suite also runs `Test-EditorSessions.ps1`: a `.lui` editor changes arrangement after native window resizes in both directions and checks draft/selection/undo continuity, explicit focus handoff, SDL text-input lifecycle, canceled preedit and real shaping/paint for both TextField and TextArea. The published input fixture also covers physical multiline typing, Enter, selection, undo/redo and UIA text-range geometry. This is automated transport evidence, not real-language IME certification.

For focused input allocation measurements, run `dotnet run --project tests/Lucent.Performance.Verifier -c Release -- --input-dispatch`. It reports flat/deep pointer, key, and repeated same-target focus cases after checking that dispatch remains accepted; timing is diagnostic rather than a new release threshold.

The published desktop input suite also exercises Lucent popup windows outside the owner client, UIA menu roles and clipboard commands, Escape/focus return, and closing an owner with a popup open. It identifies the popup by its same-process HWND and Menu semantics; dismissal checks observe that exact HWND without querying a destroyed provider. Cleanup closes the captured owner window and bounds the fixture process lifetime. The held-border resize test queries the responsive branch before releasing the mouse; the pointer-editing test checks physical caret placement, retained keyboard focus and drag selection in both single-line and multiline editors. Multiline selection uses UIA TextPattern; the single-line ValuePattern surface proves selection through replacement.
