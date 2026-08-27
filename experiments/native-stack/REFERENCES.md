# Lucent Native references and credits

Maintain this ledger while implementing the spike. Record the consulted version or commit in the final evidence. Conceptual influence does not imply source reuse.

| Project or source | License | Inspiration or use | Reuse status |
| --- | --- | --- | --- |
| [GPUI](https://github.com/zed-industries/zed/tree/main/crates/gpui) | Apache-2.0 | Typed C#/code-first authoring target, retained application UI, explicit platform boundaries | Concepts only |
| [Solid](https://github.com/solidjs/solid) | MIT | Fine-grained updates, stable DOM/node identity, explicit structural regions | Concepts only |
| [React Compiler](https://react.dev/learn/react-compiler) | MIT project | Compiler-derived dependency and memoization direction | Concepts only |
| [alien-signals](https://github.com/stackblitz/alien-signals) | MIT | Push-pull graph, lazy computed values, batching, scopes, and dependency-link cleanup | Algorithm reference; do not translate code without file-level attribution |
| [Tailwind CSS](https://github.com/tailwindlabs/tailwindcss) | MIT | Finite fluent utility vocabulary and fast local composition | Vocabulary inspiration only |
| [shadcn/ui](https://github.com/shadcn-ui/ui) | MIT | Semantic tokens, visual authority, accessible component composition, and light/dark themes | Design inspiration only |
| [ProGPU](https://github.com/wieslawsoltes/ProGPU) | MIT | Explicit backend/primitives/scene/application contracts, retained scene, AOT-conscious typed paths | Architecture reference only during spike |
| [SDL3](https://github.com/libsdl-org/SDL) | zlib | Cross-platform window, input, clipboard, display-scale, text-composition services, and official C API baseline | Runtime dependency candidate |
| [SDL3-CS](https://github.com/edwardgushchin/SDL3-CS) | zlib | Managed SDL3 bindings and platform-window properties | Selected runtime dependency; compare with available third-party/generated bindings in Milestone 1 |
| [Skia](https://skia.org/) / [SkiaSharp](https://github.com/mono/SkiaSharp) | BSD-3-Clause / MIT | Renderer, text measurement/shaping integration, and headless raster surfaces | Runtime dependency candidate |
| [Windows UI Automation](https://learn.microsoft.com/windows/win32/winauto/entry-uiauto-win32) | Microsoft documentation | Provider transport, roles/patterns, focus, and `WM_GETOBJECT` integration | Platform contract |
| [Windows text services](https://learn.microsoft.com/windows/win32/tsf/text-services-framework) | Microsoft documentation | IME composition and editable-text platform behavior | Platform contract |

## Issue #2 dependency and binding record

Consulted 2026-08-26. Versions are pinned by `Directory.Packages.props` and `NativeStackProbe/packages.lock.json`; package commits below are the NuGet package metadata commits, not claims that those repositories were built locally.

| Package/source | Exact version / consulted commit | License | Issue #2 disposition |
| --- | --- | --- | --- |
| [SDL3-CS](https://www.nuget.org/packages/SDL3-CS/3.4.14.1) and [SDL3-CS.Windows](https://www.nuget.org/packages/SDL3-CS.Windows/3.4.14.1) | `3.4.14.1`; `edwardgushchin/SDL3-CS@b525db5bf89a46c3416efe21b09431c28cf00b8d` | zlib | **Selected.** `SDL_Init`, hidden-window creation, SDL property HWND retrieval, version retrieval, and native `SDL3.dll` loading pass in published NativeAOT output. |
| [ppy.SDL3-CS](https://www.nuget.org/packages/ppy.SDL3-CS/2026.722.0) | `2026.722.0`; `ppy/SDL3-CS@7f836c9f21dad8ee68e70432e5b7d38ceae47eaa` | MIT | Generated-binding fallback only; not referenced. Its mechanically generated pointer surface is attractive for AOT, but SDL3-CS was selected for its clearer native-package versioning, broader maintained examples, and stronger CI/security automation; the published proof validates that choice empirically. |
| [SDL](https://github.com/libsdl-org/SDL) | consulted `667272e71da89b7c295bc3ef86dbab5f9f3c7da6` | zlib | Official SDL-owned repository provides the C library/API. No official SDL-owned C# NuGet package or C# source binding was found in the consulted source tree; SDL3-CS is therefore third-party, not an official binding baseline. |
| [SkiaSharp](https://www.nuget.org/packages/SkiaSharp/4.151.1), [SkiaSharp.HarfBuzz](https://www.nuget.org/packages/SkiaSharp.HarfBuzz/4.151.1), and transitive native assets | `4.151.1`; `mono/SkiaSharp@279f93f4ffa7f9fe4e9c0bc298bedc3c9e439764`; HarfBuzzSharp `14.2.1.1` | MIT managed packages; Skia BSD-3-Clause; HarfBuzz MIT | Selected. `SkiaSharp.HarfBuzz` brings HarfBuzzSharp transitively; no redundant direct HarfBuzz package is referenced. Published output loads `libSkiaSharp.dll` and `libHarfBuzzSharp.dll`. |
| [Microsoft.Windows.CsWin32](https://www.nuget.org/packages/Microsoft.Windows.CsWin32/0.3.321) | `0.3.321`; `microsoft/CsWin32@b0f1e799e6aa793eccffafa6ba8164ca45a9266f` | MIT | Selected with `PrivateAssets="all"`; generated `GetDpiForWindow(HWND)` returns nonzero in the published probe. |

The published `win-x64` directory contains `NativeStackProbe.exe`, `SDL3.dll`, `libSkiaSharp.dll`, and `libHarfBuzzSharp.dll` (plus PDBs). Before any distribution, copy the actual package license/notices for those native binaries into the distribution notice set; this experiment does not distribute them.

## Attribution rules

1. Add a row before adopting a new architectural or API reference.
2. Record copied or translated code at file level with upstream path, commit, and license notice.
3. Prefer independent implementations from documented behavior.
4. Re-check package licenses and NativeAOT support before the first dependency lock.
5. Keep third-party notices with any distributed native binaries.
