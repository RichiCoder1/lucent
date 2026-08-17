# Plan 007 example quality evidence

These deterministic native captures are review evidence, not screenshot
goldens or pixel-diff tests.

## Authority and environment

- Repository base: `74a6c8f7cad464a07b075aa2fd4cb9d93e6df71c` plus the uncommitted
  Plan 007 working tree.
- OS: Windows 11 Pro `10.0.26200`; .NET SDK `9.0.317`; Avalonia `12.1.1`.
- Capture API: `Avalonia.Media.Imaging.RenderTargetBitmap`.
- Dynamic resource API: `Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension`
  retained directly in coded `Avalonia.Styling.Setter.Value`; the native test
  proves resource replacement without reparsing or rebuilding.
- Focus API: Fluent's public `SystemControlFocusVisualPrimaryBrush`,
  `SystemControlFocusVisualSecondaryBrush`, `SystemControlFocusVisualMargin`,
  `SystemControlFocusVisualPrimaryThickness`, and
  `SystemControlFocusVisualSecondaryThickness` resources. The examples use a
  2 px teal primary outline, 2 px separation margin, and no secondary halo;
  Fluent/AvaloniaEdit templates and automation peers remain native.
- Design authority hashes:
  - `PRODUCT.md`: `c3e5592a51b97b9cdc61aa054fe272a22fb8927197da871078078a78428d41ea`
  - `DESIGN.md`: `63f657d266fae237c8310f79ce51c85aa1ffc8aa61cb116a2b26686cf1e2c6bb`
  - `design/tokens.css`: `9d7bbbeec3b0e79fa29da2cfcc659d660bd35eb72bd66c152a7afbc26011694c`
  - `design/lucent-icon.svg`: `89605580a77dfa73772b332afcf3bacdd159f5dbbfbac32a92fe6be7abe926ed`
  - `design/lucent-icon-monochrome.svg`: `07d454c6caec3b2c44fc80394394d8aa1b9c850c32d15c4ec58d545a32e08fb`
  - `.impeccable/mocks/workbench/dark-ribbon.webp`: `0212dac87a8d3ea96beafbf491dac8385a1763f1f2c6af2d207b8d0030b0d8d8`

## Captures

| Profile | Logical size | Theme | State / reachability evidence | File | SHA-256 |
| --- | --- | --- | --- | --- | --- |
| counter-light | 420Ã—300 | light | incremented count; action visible | `captures/counter-light.png` | `50ff3a9174bb03e59df7d0644b49eccba71b6b04728492aa4a6341aeabcdc10e` |
| counter-dark-focus | 420Ã—300 | dark | focus target reaches primary action | `captures/counter-dark-focus.png` | `1e9bcd0d9cd89f739226991684af7238a677c15fd609dae111d21bf179b817ed` |
| todo-light-populated | 900Ã—760 | light | populated rows, filters, progress | `captures/todo-light-populated.png` | `4b41bb7b1be0ef5822cdad588c2af287160b9156ae1fb31a4074fb75b97bc994` |
| todo-dark-empty | 700Ã—560 | dark | completed-state row and actions | `captures/todo-dark-empty.png` | `a7c71e9b030c4f80e0a89b94faeaab9e2ca819d408ee58ccfe870590b70442aa` |
| pulse-dark-results | 820Ã—760 | dark | populated async result state | `captures/pulse-dark-results.png` | `12a619a61b6c5694cd6dcce4381d67929084d1e1bd3f29b6bbf4eda35985ca25` |
| pulse-light-error | 600Ã—560 | light | real failed refresh after stale loading state, recovery action | `captures/pulse-light-error.png` | `7cf1bc6d36fa87b86b10f2a671ae73ccd02048ecdeb0d5af9613c6ab1d53a243` |
| workbench-light-shell | 1280Ã—800 | light | bounded Grid shell, editor, problems, status | `captures/workbench-light-shell.png` | `4f52105d75d3a96f54f0cabdcba8ebdb12e97425814887f65a496fe6881f9472` |
| workbench-dark-palette | 960Ã—680 | dark | palette and diagnostic edge | `captures/workbench-dark-palette.png` | `02497ca0b2881486391fe3aef31dc2fb35bf88ef440396c21efabfc633cc7285` |

Each profile is deterministic and launched from the repository root with its
documented `--quality-capture <profile>` command in Plan 007. All eight files
are non-empty PNGs with the required dimensions and non-zero pixel diversity.
The logical 100% pass checked the primary action, focus target reachability,
minimum logical sizes, and no blank capture. The user explicitly approved
deferring physical Windows 200% display scaling because this runner cannot
control that setting; no 200% evidence is claimed. A configured 200% desktop
review remains a manual follow-up for clipping, focus geometry, and primary
action reachability.

## Mark integration and small-size review

All four example projects embed `design/rendered/lucent-icon-32.png` as an
`AvaloniaResource`, load it through Avalonia's public `AssetLoader`, and assign
the resulting `WindowIcon` to their generated main window. The derived assets
were inspected together in [`mark-size-review.png`](mark-size-review.png),
enlarged with nearest-neighbor sampling so the source pixels remain visible.

| Size | Treatment | SHA-256 |
| --- | --- | --- |
| 16 px | monochrome; preserves the L silhouette without unreliable color separation | `b44bacf7c41ac1160289cad4a9372adc90b3240ba61070ea45842d4303280ba9` |
| 20 px | monochrome; retains the stronger small-size silhouette | `c9878981d737b618a80d34d27bb8d5d2fe9ae540d7e33c69cceb10c0107eaecd` |
| 24 px | color; teal/coral layers remain distinct | `cd1d37c97c269a000db1b5a304136679b493520c61cc74fa4a5001be1a5389a1` |
| 32 px | color; selected as the embedded native window icon | `b97431d859c9daa8db1797d79e3dcc1c54021094a05f98bd31b99859c27c6d0c` |

The review sheet SHA-256 is
`6af52dcbade84ee5edd0bf23b4b8364d010b4f1ddba34bcae99b8a3563973079`.

## Final verification gates

The final Plan 007 working tree was verified after the selector, projected
property-owner, font-weight, and eight-digit color corrections:

| Gate | Command | Result |
| --- | --- | --- |
| Solution build | `dotnet build Lucent.sln --no-restore --disable-build-servers` | passed; 0 warnings, 0 errors |
| Full .NET suite | `dotnet test Lucent.sln --no-build --no-restore --disable-build-servers` | 222 passed: compiler 111, MSBuild 12, runtime 30, language server 20, analyzer 11, Workbench 38 |
| VS Code extension | `npm test` from `editors/vscode` | 5 passed |
| Repository LSP payload | `npm run prepare-server` from `editors/vscode` | passed; server republished under `editors/vscode/server` |
| Workbench data smoke | `dotnet run --project examples/workbench/Lucent.Workbench.csproj --no-build --no-restore -- --smoke-test data` | exit 0 |
| Workbench reliability smoke | `dotnet run --project examples/workbench/Lucent.Workbench.csproj --no-build --no-restore -- --smoke-test reliability` | exit 0 |
| Package Pulse smoke | `dotnet run --project src/Lucent.Poc/Lucent.Poc.csproj --no-build --no-restore -- --smoke-test package-pulse` | exit 0; loading, success, empty, failure, stale, latest-generation, close, and exit markers observed |
| Native icon/capture load | `dotnet run --project examples/counter/Counter.csproj --no-build --no-restore -- --quality-capture counter-light` | exit 0; embedded `WindowIcon` resource loaded and deterministic capture hash remained unchanged |
| Markdown links | repository-local link check for this plan, `docs/STYLING.md`, and this record | passed; 0 broken links |
| Diff hygiene | `git diff --check` | passed; line-ending notices only, no whitespace errors |

No files were staged during these gates. The physical Windows 200% display
check remains the single explicitly approved deferral described above.

## Deliberate boundaries and reduced motion

- FluentTheme and AvaloniaEdit templates remain native; Workbench styles their
  surrounding controls and uses public Fluent focus resources instead of
  replacing templates.
- Workbench now uses a bounded native Grid (menu row, three-column work area,
  status/action edge, and palette overlay) while retaining its commands,
  sidebar, editor, problems, settings, preview, automation IDs, and headless
  flows. Unsupported dependency graph and live preview mock content was not
  added.
- Counter remains the smallest state/event proof, Todo remains TodoMVC scope,
  and Package Pulse remains a simulated async catalog rather than a package
  client. The Package Pulse error profile waits for the real failing computation
  after its initial stale/loading value and leaves a Retry action visible.
- Motion uses only the catalog's 120/180 ms timings. Setting those native
  duration resources to zero preserves final state; spatial translation/folding
  is not introduced, so reduced motion has no hidden content to suppress.
- The SVG mark assets remain the source authority; the reviewed 16/20/24/32 px
  derivatives add no icon or font dependency.

## Rebuild review

The single correction batch retained the typed CSS bridge and changed only native
composition/presentation: Workbench now uses the persisted sidebar width in its
three-column shell, a dominant editor pane, bounded project/problems panes, a
raised palette, and an explicit status edge. Package Pulse's error profile waits
for the real initial result, then renders the stale result component beside its
error and Retry action. Todo completed rows carry an explicit Completed/Active
label and semantic success styling; Counter's compact root enforces 420×300
minimum dimensions.

Physical Windows 200% display scaling remains an approved manual follow-up; these
captures are logical-size evidence only.
