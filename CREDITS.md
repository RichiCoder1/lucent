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

Before adding a dependency or adopting a new architectural reference:

1. Add it here or to the active roadmap's reference ledger.
2. Record the consulted version or commit and license.
3. Distinguish conceptual influence from copied or translated code.
4. Re-check NativeAOT support and distribution notices against the final published output.
