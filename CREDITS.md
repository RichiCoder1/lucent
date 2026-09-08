# Credits and references

Lucent is informed by open-source UI systems and platform documentation. Conceptual influence does not imply source reuse. Any copied or translated code must carry file-level attribution, upstream commit identity, and its required license notice.

The Native validation spike's complete conceptual reference ledger remains available at the immutable [703d8e2 history tree](https://github.com/RichiCoder1/lucent/tree/703d8e267c6590603350db7819aa553822a30b87/docs/history/native-spike/). The active ledger below records current architectural references, adopted dependencies, and distribution attribution.

[OpenTelemetry .NET](https://github.com/open-telemetry/opentelemetry-dotnet/tree/164d8a59ae4f8ae4d9b498981f1a2ea046d0513f) `core-1.15.0` (Apache-2.0) informs Lucent's optional application-owned export of standard .NET activities and metrics. This is conceptual and API guidance only; no source is reused. Lucent's deterministic diagnostic dumps remain its own framework contract, and Lucent does not configure exporters or transmit telemetry.

## Active architectural references

These sources were consulted on 2026-08-27. They are conceptual references only; no source is reused.

| Source | Consulted identity | License or status | Influence |
| --- | --- | --- | --- |
| [Flutter](https://github.com/flutter/flutter/tree/53c174684f2fe66522393013f4b88518d7caa1ad) | `53c174684f2fe66522393013f4b88518d7caa1ad` | BSD-3-Clause | Framework/engine/embedder ownership, semantics, and text-input boundary |
| [Jetpack Compose / AndroidX](https://github.com/androidx/androidx/tree/a3b352883a0709bc25f8217df1a526290f754d96/compose) | `a3b352883a0709bc25f8217df1a526290f754d96` | Apache-2.0 | Layered primitives, ordered composition, semantics tree |
| [Qt 6 QPA](https://doc.qt.io/qt-6/qpa.html) | Qt 6 documentation, consulted 2026-08-27 | Component-specific LGPL/GPL/commercial terms; no dependency | Platform-service inventory and warning against exposing unstable adapter internals |
| [Slint](https://github.com/slint-ui/slint/tree/14c19d762af672fdc3934e4f490c1db97c20615f) | `14c19d762af672fdc3934e4f490c1db97c20615f` | Component-specific GPL-3.0/commercial terms; no dependency | AOT compiler/core/backend/renderer separation and renderer-cost evidence |
| [GPUI](https://github.com/zed-industries/zed/tree/8166e3d7b8b42d8aaf4d4dee7fcd25ab4ec65105/crates/gpui) | `8166e3d7b8b42d8aaf4d4dee7fcd25ab4ec65105` | Apache-2.0 where marked; verify file headers before reuse | Stable identity, platform text handling, accessibility tree updates |
| [StyleX](https://github.com/facebook/stylex/tree/5f7acaa4b332e2cf8352e95e5bc83efd70904fd4) | `5f7acaa4b332e2cf8352e95e5bc83efd70904fd4` | MIT | Static extraction, canonical property slots, deterministic ordered composition |
| [Panda CSS](https://github.com/chakra-ui/panda/tree/8a71bff3dc805e247be70d120cf485c8f14f104b) | `8a71bff3dc805e247be70d120cf485c8f14f104b` | MIT | Typed tokens, semantic aliases, finite build-time conditions |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia/tree/0442ba19098e6642185431c41c23f7138a270e0c) | `0442ba19098e6642185431c41c23f7138a270e0c`; prior implementation used 12.1.1 | MIT | Named value sources, explicit inheritance metadata, transition diagnostics; not selectors/XAML |
| [shadcn/ui](https://github.com/shadcn-ui/ui/tree/683a5a9b370acdb7785a0529434e6a3b8c7e0441) | `683a5a9b370acdb7785a0529434e6a3b8c7e0441` | MIT | Paired semantic foreground/surface tokens and source-owned components |
| [Design Tokens Community Group](https://github.com/design-tokens/community-group/tree/16c902d9327c18290e956a21130c445f1b88c40f) | `16c902d9327c18290e956a21130c445f1b88c40f`; Format Module 2025.10 | Community Group report, not a W3C Standard | Potential build-time token interchange; never executable runtime input |
| [Windows UI Automation SDK](https://learn.microsoft.com/windows/win32/winauto/uiauto-serversideprovider) | Windows SDK `10.0.26100.0` `UIAutomationCore.idl` and `UIAutomationCoreApi.h`, consulted 2026-08-29 | Windows SDK platform contract; no copied source | Exact UIA provider IIDs, vtable slots, HRESULT, VARIANT and SAFEARRAY ownership |
| [Windows text services](https://learn.microsoft.com/windows/win32/tsf/text-services-framework) | Microsoft documentation, consulted 2026-08-29 | Windows platform contract; no copied source | IME composition and editable-text platform behavior |

## Language, compiler, and authoring references

These sources were consulted on 2026-08-30 for the .lui design. They are conceptual/tooling references only unless separately listed as an adopted package dependency. Historical research remains available in the immutable spike tree linked above.

| Source | Consulted identity | License or status | Influence |
| --- | --- | --- | --- |
| [Roslyn](https://github.com/dotnet/roslyn/tree/e79586494f629704a0fd18b7afb840144fd5e673) / `Microsoft.CodeAnalysis.CSharp` | commit `e79586494f629704a0fd18b7afb840144fd5e673`; package API `4.14.0` | MIT | Incremental generator, `AdditionalText`, diagnostics, C# expression binding, generated spans |
| [Roslyn C# features](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.Features/4.14.0) / `Microsoft.CodeAnalysis.CSharp.Features` | package `4.14.0` | MIT | Official Roslyn `CompletionService` for C# expression-island completion in the separate editor-process-only LSP; it never ships with Lucent applications |
| [Roslyn MSBuild workspace](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/4.14.0) / `Microsoft.CodeAnalysis.Workspaces.MSBuild` | package `4.14.0` | MIT | Evaluated project loading for the separate LSP; it is editor-process-only and never ships with Lucent applications |
| [Roslyn C# workspace](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.Workspaces/4.14.0) / `Microsoft.CodeAnalysis.CSharp.Workspaces` | package `4.14.0` | MIT | C# project support for the separate LSP's evaluated MSBuild workspace; it is editor-process-only |
| [MSBuild Locator](https://www.nuget.org/packages/Microsoft.Build.Locator/1.9.1) / `Microsoft.Build.Locator` | package `1.9.1` | MIT | Registers the installed MSBuild instance for the separate LSP before workspace loading; it is editor-process-only |
| [MSBuild](https://www.nuget.org/packages/Microsoft.Build.Tasks.Core/17.14.28) / `Microsoft.Build`, `Microsoft.Build.Framework`, `Microsoft.Build.Tasks.Core`, `Microsoft.Build.Utilities.Core` | packages `17.14.28` | MIT | Security-serviced MSBuild workspace transitive dependency set for the separate LSP; it is editor-process-only |
| [.NET cryptography XML](https://www.nuget.org/packages/System.Security.Cryptography.Xml/10.0.11) / `System.Security.Cryptography.Xml` | package `10.0.11` | MIT | Security-serviced MSBuild workspace transitive dependency for the separate LSP; it is editor-process-only |
| [Razor](https://github.com/dotnet/razor/tree/58ec96978ef4e5823b54e960b9fd64cff45d7e68) | commit `58ec96978ef4e5823b54e960b9fd64cff45d7e68`; ASP.NET Core 9/10 docs | MIT | Partial C# components, typed content, generated inspection, cohosted project semantics, source mapping lessons |
| [Mobile Blazor Bindings](https://github.com/dotnet/MobileBlazorBindings/tree/6b2d767a44fff94eb90489649889c66a399c00ec) | final archived commit `6b2d767a44fff94eb90489649889c66a399c00ec`; package `0.5.50-preview` | MIT; archived experiment | Native component markup and caution against a parallel platform/runtime abstraction |
| [GPUIX](https://github.com/remorses/gpuix/tree/09e0caeb1812eece10a3a8a7200ef18567610267) | commit `09e0caeb1812eece10a3a8a7200ef18567610267` | Apache-2.0 | JSX-shaped GPUI authoring and compile-time lowering inspiration; no hooks/runtime adoption |
| [Solid](https://github.com/solidjs/solid/tree/f47845f9cc16ecbb316aa6560c7161f45af9a3d8) | `solid-js` 1.9.15; commit `f47845f9cc16ecbb316aa6560c7161f45af9a3d8` | MIT | Run-once component setup with narrow compiled reactive updates and owner cleanup |
| [alien-signals](https://github.com/stackblitz/alien-signals/tree/c00e63969bf261fc5dce31fae70cb9a90912b06e) | 3.2.1; commit `c00e63969bf261fc5dce31fae70cb9a90912b06e` | MIT | Push/pull dependency validation and nested effect-scope ownership |
| [QML](https://doc.qt.io/qt-6.11/qtqml-syntax-basics.html) | Qt 6.11 object-declaration documentation | LGPL/GPL/commercial framework terms; no dependency | Concise typed property-block syntax and warning against dynamic runtime object semantics |
| [CSSWG](https://github.com/w3c/csswg-drafts/tree/f89f7a1a0138b072051e65323f49c737152880fb) | commit `f89f7a1a0138b072051e65323f49c737152880fb` | W3C specification terms | Familiar vocabulary plus evidence that flex, background layers, overflow, and alignment semantics must not be implied by names alone |

The authoring design also revisited the already-recorded GPUI, Avalonia, Compose, Flutter, Slint, StyleX, Panda CSS, shadcn/ui, and DTCG identities above. No source is copied. Exact implementation package versions and NativeAOT/editor-host compatibility must be rechecked before package references are added.

## Development tooling

| Tool | Exact identity | License | Use |
| --- | --- | --- | --- |
| [CSharpier](https://github.com/belav/csharpier/tree/1.3.0) | .NET tool `csharpier` `1.3.0` | MIT | Repository-local, build-time-only formatting of authored C# and project files. Generated files remain excluded by the tool's normal generated-code boundary; no CSharpier asset ships with Lucent applications. |

Before adding a dependency or adopting a new architectural reference:

1. Add it here or to the active roadmap's reference ledger.
2. Record the consulted version or commit and license.
3. Distinguish conceptual influence from copied or translated code.
4. Re-check NativeAOT support and distribution notices against the final published output.

## Runtime dependency ledger

These win-x64 NativeAOT dependencies were checked on 2026-08-27 before their package references were added. The dependency choice follows the validated Native spike; the asset verifier reconciles the final publish directory.

| Dependency | Exact identity | License | NativeAOT status | Distribution notice |
| --- | --- | --- | --- | --- |
| [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | SDK `10.0.400`, runtime `10.0.11`, `win-x64` | MIT ([runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)) | Required baseline; NativeAOT compiler and trimming analyzers are enabled for runtime projects | Publish SDK `LICENSE.txt` and `ThirdPartyNotices.txt` as `notices/dotnet-LICENSE.txt` and `notices/dotnet-ThirdPartyNotices.txt`. |
| [SDL3-CS](https://www.nuget.org/packages/SDL3-CS/3.4.14.1) / [SDL3-CS.Windows](https://www.nuget.org/packages/SDL3-CS.Windows/3.4.14.1) | `3.4.14.1`, [`edwardgushchin/SDL3-CS@b525db5bf89a46c3416efe21b09431c28cf00b8d`](https://github.com/edwardgushchin/SDL3-CS/tree/b525db5bf89a46c3416efe21b09431c28cf00b8d); `SDL3.dll` | zlib | Spike-validated NativeAOT window, stable HWND retrieval, streaming-texture presentation | Ship `SDL3-CS.Windows/LICENSE` as `notices/SDL3-CS.txt`. |
| [Microsoft Visual C++ Runtime](https://learn.microsoft.com/cpp/windows/redistributing-visual-cpp-files) | `vcruntime140.dll` `14.44.35211.0`, SHA-256 `d5e4d9a3e835fa679450145d6a7d94e36573a509317111904d9b3712c30d9066` | Microsoft Visual Studio 2022 Redistributable Code terms | App-local dependency of the selected `SDL3.dll`; clean Windows Sandbox proved the DLL is absent from the base OS | Ship the unmodified x64 DLL and `notices/Microsoft-VCRuntime.txt`; service it with dependency updates. |
| [SkiaSharp](https://www.nuget.org/packages/SkiaSharp/4.151.1) / [SkiaSharp.HarfBuzz](https://www.nuget.org/packages/SkiaSharp.HarfBuzz/4.151.1) | `4.151.1`, [`mono/SkiaSharp@279f93f4ffa7f9fe4e9c0bc298bedc3c9e439764`](https://github.com/mono/SkiaSharp/tree/279f93f4ffa7f9fe4e9c0bc298bedc3c9e439764); transitive HarfBuzzSharp `14.2.1.1`; `libSkiaSharp.dll` | MIT bindings; Skia BSD-3-Clause | Windows native-assets package supplies the selected `win-x64` native DLL; spike NativeAOT-published this line | Ship `SkiaSharp.NativeAssets.Win32/LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` as `notices/SkiaSharp-LICENSE.txt` and `notices/SkiaSharp-NOTICES.txt`. |
| [HarfBuzzSharp](https://www.nuget.org/packages/HarfBuzzSharp/14.2.1.1) | transitive from `SkiaSharp.HarfBuzz 4.151.1`; `libHarfBuzzSharp.dll` | MIT binding and HarfBuzz | Windows native-assets package supplies selected `win-x64` native DLL; spike NativeAOT-published this line | Ship `HarfBuzzSharp.NativeAssets.Win32/LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` as `notices/HarfBuzzSharp-LICENSE.txt` and `notices/HarfBuzzSharp-NOTICES.txt`. |
| [Microsoft.Windows.CsWin32](https://www.nuget.org/packages/Microsoft.Windows.CsWin32/0.3.321) | `0.3.321`, [`microsoft/CsWin32@b0f1e799e6aa793eccffafa6ba8164ca45a9266f`](https://github.com/microsoft/CsWin32/tree/b0f1e799e6aa793eccffafa6ba8164ca45a9266f) | MIT | Source-generated P/Invoke; Lucent uses `CsWin32RunAsBuildTask` and disables runtime marshalling per [its NativeAOT guidance](https://microsoft.github.io/CsWin32/docs/getting-started.html) | Build-time-only (`PrivateAssets="all"`); no package asset is shipped. |

This ledger carries no copied source. CsWin32 output is generated from Microsoft Win32 metadata at build time and is not a runtime dependency.

## Test tooling

The headless testing surface introduced for #103 takes architectural inspiration from [Avalonia's headless platform](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform), consulted September 7, 2026 (Avalonia, MIT). It mounts production components, simulates input through production routing, and makes real Skia rendering optional. No Avalonia code or dependency is included. Controlled time uses the existing MIT-licensed `Microsoft.Extensions.TimeProvider.Testing` 10.9.0 package.

- [MSTest and Microsoft.Testing.Platform](https://github.com/microsoft/testfx/tree/v4.4.0), selected through `MSTest.Sdk` 4.4.0, provide discoverable .NET tests, filtering, reports, and NativeAOT-compatible test execution. Test-only dependencies; no source is copied. Framework/platform source is MIT-licensed; optional extensions retain their upstream package terms.
- [Axe.Windows](https://github.com/microsoft/axe-windows), package 2.4.2 (MIT), provides Windows accessibility rule scans through its supported Automation API. It is test-driver tooling, not a Lucent runtime dependency. Its automated scans do not represent the manual tab-stop portion of Accessibility Insights FastPass.
- [FlaUI](https://github.com/FlaUI/FlaUI) / FlaUI.UIA3 5.0.0 (MIT) is the selected test-only desktop interaction driver. It is not a Lucent runtime dependency.

## Application hosting

[Microsoft.Extensions.Hosting 10.0.11](https://www.nuget.org/packages/Microsoft.Extensions.Hosting/10.0.11) and its Microsoft.Extensions DI, configuration, logging, options, and diagnostics dependencies are adopted by the optional `Lucent.Hosting` adapter (MIT). Core remains dependency-free. Consulted on 2026-09-05: [Generic Host](https://learn.microsoft.com/dotnet/core/extensions/generic-host), [DI ownership guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines), and [NativeAOT Worker Services](https://learn.microsoft.com/dotnet/core/extensions/workers#native-aot). These are API and ownership references; no source is copied. The adapter disables default configuration/logging providers and replaces console lifetime with desktop-controlled startup/stop. NativeAOT and trimming are verified against the published lifecycle consumer; The adapter copies the .NET MIT license as `notices/Microsoft.Extensions-LICENSE.txt` and the package third-party notices as `notices/Microsoft.Extensions.Hosting-NOTICES.txt`.

## Layout evaluation and inspiration

[Taffy 0.14.0](https://github.com/DioxusLabs/taffy/releases/tag/v0.14.0) (MIT) informs the internal Grid/Flex, leaf-measurement and dirty-propagation design in [ADR 0004](docs/adr/0004-layout-and-paragraphs.md). The isolated [layout probe](tests/Probes/Layout/) pins it as a test-only Rust dependency and verifies a small NativeAOT C-ABI boundary; no Taffy binary ships with Lucent, and Core acquires no native dependency. The probe's Rust dependencies are pinned in Cargo.lock and retain their upstream licenses. This is architectural inspiration and API use, not copied or translated production source. Consulted 2026-09-05: the release, [MIT license](https://github.com/DioxusLabs/taffy/blob/v0.14.0/LICENSE), and [TaffyTree ownership/measurement API](https://docs.rs/taffy/0.14.0/taffy/tree/struct.TaffyTree.html). Redistributing probe binaries requires the corresponding upstream notices; this repository retains probe sources only.

## Optional reactive event integration

[R3 1.3.1](https://github.com/Cysharp/R3/tree/1.3.1) (Cysharp, MIT) supplies debounce/event-stream operators for the optional Lucent.Reactive.R3 adapter. Its explicit TimeProvider support enables deterministic timing tests. R3 does not replace Lucent's dependency-tracked retained graph, enter portable Core's dependencies, or own accepted application writes. NativeAOT compatibility of the selected adapter path is verified by its consumer proof rather than assumed for every R3 operator. The adapter ships the upstream license as `notices/R3-LICENSE.txt`.

## Desktop interaction references

Consulted September 8, 2026 for submenus and accessible panes: Costa Alexoglou's [Better Context Menus With Safe Triangles](https://www.smashingmagazine.com/2023/08/better-context-menus-safe-triangles/) (Smashing Magazine, August 21, 2023; copyrighted article, no source copied) informs directional pointer-intent geometry using actual child placement. Lucent uses a bounded portable decision helper and platform event replay, not the article's SVG/React implementation. Microsoft's [RangeValue control-pattern guidance](https://learn.microsoft.com/windows/win32/winauto/uiauto-implementingrangevalue) informs the splitter's finite numeric range and Windows UIA adapter. Neither reference introduces a dependency.

Consulted September 6, 2026: [Avalonia TextPresenter](https://github.com/AvaloniaUI/avalonia-docs/blob/main/api/avalonia/controls/presenters/textpresenter.mdx) (MIT) for explicit caret visibility and configurable blink intervals, and [Flutter Scrollbar](https://api.flutter.dev/flutter/material/Scrollbar-class.html) (BSD-3-Clause) for shared scroll position, thumb dragging, track paging, and replaceable hover/drag styling. These are behavioral/architectural references; no source is copied and neither framework is a dependency. Windows caret timing uses [GetCaretBlinkTime](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getcaretblinktime); existing SDL supplies system cursors and timed event waits on the owner thread.

Consulted September 7, 2026 for live sizing and popup hosting: [SDL3 AppFreezeDuringDrag](https://wiki.libsdl.org/SDL3/AppFreezeDuringDrag) and [SDL_CreatePopupWindow](https://wiki.libsdl.org/SDL3/SDL_CreatePopupWindow) (SDL zlib documentation) define the supported exposed-event redraw path and parent-owned popup-menu lifetime used by the existing SDL3 dependency. Microsoft's [server-side UIA provider guidance](https://learn.microsoft.com/windows/win32/winauto/uiauto-serversideprovider) and [UiaDisconnectProvider](https://learn.microsoft.com/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiadisconnectprovider) define destroy-then-disconnect cleanup and the re-entrant WM_GETOBJECT constraint. [Avalonia Popup](https://github.com/AvaloniaUI/avalonia-docs/blob/main/controls/feedback/popup.md) and [Win32 WindowImpl](https://github.com/AvaloniaUI/Avalonia/blob/0442ba19098e6642185431c41c23f7138a270e0c/src/Windows/Avalonia.Win32/WindowImpl.cs) (MIT), [SkiaSharp SKCanvasView API](https://github.com/mono/SkiaSharp-API-docs/blob/main/SkiaSharpAPI/SkiaSharp.Views.Maui.Controls/SKCanvasView.xml) (MIT), and [Uno Skia Desktop](https://github.com/unoplatform/uno/blob/master/doc/articles/features/using-skia-desktop.md) (Apache-2.0) were compared conceptually for popup, resize and host ownership. No source is copied and no new dependency is introduced; the detailed comparison is in [desktop-host-research.md](docs/plans/desktop-host-research.md).

## Text rasterization references

Consulted September 7, 2026 for #101: Microsoft [TrackPopupMenuEx](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-trackpopupmenuex) and [UIA menu support](https://learn.microsoft.com/windows/win32/winauto/uiauto-supportmenucontroltype) inform the opt-in standard Windows menu adapter. SDL's [transparent window properties](https://wiki.libsdl.org/SDL3/SDL_CreateWindowWithProperties) and [Windows transparency notes](https://wiki.libsdl.org/SDL3/README-windows), with [SkiaSharp mask filters](https://github.com/mono/SkiaSharp/blob/main/documentation/docfx/guides/effects/mask-filters.md), inform alpha-composited Lucent popup shadows. These use existing SDL (zlib) and SkiaSharp (MIT) dependencies; no reference implementation is copied. Presentation stays local to each popup; no shared window class or system-wide appearance settings are changed.

Consulted September 6, 2026 for the offscreen text-rasterization review. These are primary Skia documentation and source references; no source is copied and no new dependency is introduced.

| Source | Consulted identity | License or status | Influence |
| --- | --- | --- | --- |
| [Skia: The Raster Tragedy at Low-DPI](https://docs.skia.org/docs/dev/design/raster_tragedy/) | Skia documentation, consulted 2026-09-06 | BSD-3-Clause project; no dependency | Grayscale versus LCD coverage, gamma, hinting, and color-fringe tradeoffs |
| [SkSurfaceProps API](https://api.skia.org/SkSurfaceProps_8h.html) and [SkFont API](https://api.skia.org/classSkFont.html) | Skia API documentation, consulted 2026-09-06 | BSD-3-Clause project; no dependency | Pixel geometry declarations and the distinction between LCD edging and fractional glyph positioning |
| [Skia surface test](https://skia.googlesource.com/skia/+/762b0e485189/gm/surface.cpp), [SkCanvas layer implementation](https://skia.googlesource.com/skia/+/2daf164f6d51/src/core/SkCanvas.cpp), and [SkFont implementation](https://skia.googlesource.com/skia/+/34c67453a5d032b2f5416564a8c80aa5dca05c9f/src/core/SkFont.cpp) | Skia source commits 762b0e485189, 2daf164f6d51, and 34c67453a5d032b2f5416564a8c80aa5dca05c9f | BSD-3-Clause project; no source reused | Opaque-target requirements, saved-layer pixel geometry, and separate edging/subpixel-positioning flags |
