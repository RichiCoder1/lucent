# Credits and references

Lucent is informed by open-source UI systems and platform documentation. Conceptual influence does not imply source reuse. Any copied or translated code must carry file-level attribution, upstream commit identity, and its required license notice.

The Native validation spike's detailed reference ledger is preserved in [`docs/history/native-spike/REFERENCES.md`](docs/history/native-spike/REFERENCES.md). It includes GPUI, Solid, alien-signals, Tailwind CSS, shadcn/ui, ProGPU, SDL3, Skia, Windows UI Automation, and Windows text services.

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

Before adding a dependency or adopting a new architectural reference:

1. Add it here or to the active roadmap's reference ledger.
2. Record the consulted version or commit and license.
3. Distinguish conceptual influence from copied or translated code.
4. Re-check NativeAOT support and distribution notices against the final published output.

## M0 runtime dependency ledger

These M0 `win-x64` NativeAOT dependencies were checked on 2026-08-27 before their package references were added. The dependency choice follows the validated Native spike; `tools/Verify-M0Assets.ps1` reconciles the final publish directory.

| Dependency | Exact identity | License | NativeAOT status | Distribution notice |
| --- | --- | --- | --- | --- |
| [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | SDK `10.0.400`, runtime `10.0.11`, `win-x64` | MIT ([runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)) | Required baseline; NativeAOT compiler and trimming analyzers are enabled for M0 | Publish SDK `LICENSE.txt` and `ThirdPartyNotices.txt` as `notices/dotnet-LICENSE.txt` and `notices/dotnet-ThirdPartyNotices.txt`. |
| [SDL3-CS](https://www.nuget.org/packages/SDL3-CS/3.4.14.1) / [SDL3-CS.Windows](https://www.nuget.org/packages/SDL3-CS.Windows/3.4.14.1) | `3.4.14.1`, [`edwardgushchin/SDL3-CS@b525db5bf89a46c3416efe21b09431c28cf00b8d`](https://github.com/edwardgushchin/SDL3-CS/tree/b525db5bf89a46c3416efe21b09431c28cf00b8d); `SDL3.dll` | zlib | Spike-validated NativeAOT window, stable HWND retrieval, streaming-texture presentation | Ship `SDL3-CS.Windows/LICENSE` as `notices/SDL3-CS.txt`. |
| [Microsoft Visual C++ Runtime](https://learn.microsoft.com/cpp/windows/redistributing-visual-cpp-files) | `vcruntime140.dll` `14.44.35211.0`, SHA-256 `d5e4d9a3e835fa679450145d6a7d94e36573a509317111904d9b3712c30d9066` | Microsoft Visual Studio 2022 Redistributable Code terms | App-local dependency of the selected `SDL3.dll`; clean Windows Sandbox proved the DLL is absent from the base OS | Ship the unmodified x64 DLL and `notices/Microsoft-VCRuntime.txt`; service it with dependency updates. |
| [SkiaSharp](https://www.nuget.org/packages/SkiaSharp/4.151.1) / [SkiaSharp.HarfBuzz](https://www.nuget.org/packages/SkiaSharp.HarfBuzz/4.151.1) | `4.151.1`, [`mono/SkiaSharp@279f93f4ffa7f9fe4e9c0bc298bedc3c9e439764`](https://github.com/mono/SkiaSharp/tree/279f93f4ffa7f9fe4e9c0bc298bedc3c9e439764); transitive HarfBuzzSharp `14.2.1.1`; `libSkiaSharp.dll` | MIT bindings; Skia BSD-3-Clause | Windows native-assets package supplies the selected `win-x64` native DLL; spike NativeAOT-published this line | Ship `SkiaSharp.NativeAssets.Win32/LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` as `notices/SkiaSharp-LICENSE.txt` and `notices/SkiaSharp-NOTICES.txt`. |
| [HarfBuzzSharp](https://www.nuget.org/packages/HarfBuzzSharp/14.2.1.1) | transitive from `SkiaSharp.HarfBuzz 4.151.1`; `libHarfBuzzSharp.dll` | MIT binding and HarfBuzz | Windows native-assets package supplies selected `win-x64` native DLL; spike NativeAOT-published this line | Ship `HarfBuzzSharp.NativeAssets.Win32/LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` as `notices/HarfBuzzSharp-LICENSE.txt` and `notices/HarfBuzzSharp-NOTICES.txt`. |
| [Microsoft.Windows.CsWin32](https://www.nuget.org/packages/Microsoft.Windows.CsWin32/0.3.321) | `0.3.321`, [`microsoft/CsWin32@b0f1e799e6aa793eccffafa6ba8164ca45a9266f`](https://github.com/microsoft/CsWin32/tree/b0f1e799e6aa793eccffafa6ba8164ca45a9266f) | MIT | Source-generated P/Invoke; M0 uses `CsWin32RunAsBuildTask` and disables runtime marshalling per [its NativeAOT guidance](https://microsoft.github.io/CsWin32/docs/getting-started.html) | Build-time-only (`PrivateAssets="all"`); no package asset is shipped. |

M0 carries no copied source. CsWin32 output is generated from Microsoft Win32 metadata at build time and is not a runtime dependency.
