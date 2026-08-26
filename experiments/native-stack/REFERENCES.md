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
| [SDL3](https://github.com/libsdl-org/SDL) | zlib | Cross-platform window, input, clipboard, display-scale, and text-composition services | Runtime dependency candidate |
| [SDL3-CS](https://github.com/edwardgushchin/SDL3-CS) | zlib | Managed SDL3 bindings and platform-window properties | Runtime dependency candidate |
| [Skia](https://skia.org/) / [SkiaSharp](https://github.com/mono/SkiaSharp) | BSD-3-Clause / MIT | Renderer, text measurement/shaping integration, and headless raster surfaces | Runtime dependency candidate |
| [Windows UI Automation](https://learn.microsoft.com/windows/win32/winauto/entry-uiauto-win32) | Microsoft documentation | Provider transport, roles/patterns, focus, and `WM_GETOBJECT` integration | Platform contract |
| [Windows text services](https://learn.microsoft.com/windows/win32/tsf/text-services-framework) | Microsoft documentation | IME composition and editable-text platform behavior | Platform contract |

## Attribution rules

1. Add a row before adopting a new architectural or API reference.
2. Record copied or translated code at file level with upstream path, commit, and license notice.
3. Prefer independent implementations from documented behavior.
4. Re-check package licenses and NativeAOT support before the first dependency lock.
5. Keep third-party notices with any distributed native binaries.
