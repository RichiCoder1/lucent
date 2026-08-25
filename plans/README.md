# Lucent real-application roadmap

Generated on 2026-08-16 against commit `3b27cfe` and the active working-tree
proofs for CSS, `Computed<T>`, completion, and Package Pulse. The target is a
keyboard-heavy developer tool, with Avalonia interop first, a deliberately
narrow Lucent language, and one substantial dogfood application as the release
gate.

The dogfood application is **Lucent Workbench**: a small workspace inspector
that opens a .NET/Lucent project, shows a virtualized project tree and problems
list, opens `.lui` source in AvaloniaEdit, previews generated C#, and exposes
menus, a command palette, shortcuts, dialogs, settings, themes, and persisted
recent workspaces. It is not a full IDE.

## Execution order and status

| Plan | Title | Priority | Effort | Depends on | Status |
| --- | --- | --- | --- | --- | --- |
| [001](001-runtime-owner-scheduler.md) | Centralize ownership and UI scheduling | P0 | M | — | DONE |
| [002](002-reactivity-conditional-regions.md) | Bind dependencies and add conditional regions | P0 | L | 001 | DONE |
| [003](003-component-composition.md) | Make Lucent components genuinely composable | P0 | L | 001, 002 | DONE |
| [004](004-desktop-application-interop.md) | Prove the native desktop application shell | P1 | L | 003 | DONE |
| [004a](004a-typed-native-roots.md) | Expose typed native roots and lifecycle diagnostics | P1 | S | 003, 004 | DONE |
| [005](005-virtualized-collections.md) | Add scalable native collections and AvaloniaEdit | P1 | M | 003, 004 | DONE |
| [006](006-lifecycle-reliability.md) | Close lifecycle, failure, accessibility, and UI-test gaps | P1 | L | 001–005 | DONE |
| [006a](006a-loading-boundaries.md) | Add explicit loading clauses to async boundaries | P1 | S | 006 | DONE |
| [007](007-example-ux-css-quality.md) | Make every example a polished Lucent showcase | P1 | L | 001–006 | DONE |
| [007a](007a-native-compiled-bindings.md) | Use Avalonia compiled bindings at native seams | P1 | M | 005–007 | DONE |
| [008](008-language-tooling-quality.md) | Make language tooling release-ready | P1 | M | 003, 004a, 006a, 007a | DONE |
| [008a](008a-package-semantics-native-compatibility.md) | Lock package semantics and native compatibility | P1 | L | 007a, 008 | DONE |
| [009](009-shadcn-theme-css-completion.md) | Add the Shadcn theme and theme-aware CSS completion | P1 | XL | 007, 008a | DONE |
| [009a](009a-global-lucent-styles.md) | Add explicit global Lucent styles | P1 | L | 008a, 009 | DONE |
| [009b](009b-avalonia-utility-styles.md) | Add a finite Avalonia utility-style catalog | P2 | M | 008a, 009, 009a | DONE |
| [010](010-example-shadcn-migration.md) | Migrate every example to the Shadcn visual authority | P1 | L | 007, 009, 009a | DONE |
| [011](011-workbench-dogfood.md) | Complete the Workbench dogfood application | P1 | L | 005, 006, 008, 008a, 010 | DONE |
| [012](012-package-preview.md) | Package and publish the experimental preview | P1 | L | 009b, 011 | TODO |

Status values: `TODO`, `IN PROGRESS`, `DONE`, `BLOCKED`, or `REJECTED` with a
one-line reason.

Review records: [001–002](REVIEW-001-002.md),
[003](REVIEW-003.md), [001–003 cross-check](REVIEW-001-003-CROSSCHECK.md),
[004](REVIEW-004.md), [005](REVIEW-005.md), [006](REVIEW-006.md), and
[001–006 Sol/high cross-check](REVIEW-001-006-CROSSCHECK.md). Plan review:
[006a](REVIEW-006A.md), [007](REVIEW-007.md), [007a](REVIEW-007A.md),
[008](REVIEW-008.md), [009/009a](REVIEW-009.md), and
[009b](REVIEW-009B.md). The manifest/style/package seams were previously covered
by the [008a–010 cross-check](REVIEW-008A-010-CROSSCHECK.md); the rewritten
Plans 010–012 require fresh plan review before implementation.

## Dependency notes

- 001 creates the one real seam required by both production Avalonia dispatch
  and deterministic tests. Do not add a broad runtime abstraction beyond it.
- 002 moves identity and invalidation out of ad hoc generated methods before
  component composition multiplies those methods.
- 003 is the first point at which a multi-file application can be authored in
  Lucent instead of one generated native tree per file.
- 004 deliberately uses Avalonia's commands, focus, menus, dialogs, storage,
  and windowing. Lucent should project those capabilities, not replace them.
- 005 keeps structural keyed regions separate from virtualized collection
  controls. They solve different problems and should have different interfaces.
- 006 is the quality gate before distribution: cancellation, error routing,
  automation metadata, and headless UI tests must be observable contracts.
- 006a adds first-load fallback syntax without changing 006's async ownership,
  stale-refresh, error-routing, or runtime contracts.
- 007 turns the examples into one credible visual family and closes the common
  Avalonia styling gaps they expose without adding a component/theme framework.
- 007a uses native compiled bindings only for explicit binding paths and safe
  recycled templates; Lucent expressions keep their existing runtime.
- 008 makes the shared compiler/LSP seam correct, bounded, and fast before its
  artifacts are packaged. It must not depend on another language server's
  in-memory workspace.
- 008a adds one non-executing package metadata seam, closes release-used native
  compatibility gaps (including bounded public `TemplateContent` or its tested
  C# fallback), and makes that contract executable before style packages depend
  on it.
- 009 adds the concrete Shadcn theme need and theme-aware `Class:` completion
  through 008a's manifest/catalog without weakening 008's cache or latency
  contracts.
- 009a separately adds honest project-global CSS runtime/build semantics and
  reuses 009's class catalog.
- 009b optionally generates a finite Avalonia-native utility catalog from those
  seams without importing Tailwind or adding runtime source scanning.
- 010 migrates every example to the accepted Shadcn visual authority without
  mixing presentation work with Workbench's real project integration.
- 011 completes the Workbench dogfood flow through existing compiler, project,
  ownership, settings, and test seams before any package interface is frozen.
- 012 packages only the system proven by Workbench. It must not stabilize
  interfaces that Workbench has not exercised.

## Release gates

Every plan must leave the solution and VS Code tests green:

```powershell
dotnet test Lucent.sln --no-restore
Push-Location editors/vscode
npm test
Pop-Location
```

Plans 004–007a, including 006a, and Plans 010–012 must also preserve or add a
user-flow gate to Lucent Workbench. Plan 006 replaces manual-only smoke coverage
with Avalonia headless interaction tests; Plan 006a extends that flow with
first-load/stale-refresh
boundary evidence; Plan 007 adds bounded visual evidence without pixel-golden
tests; Plan 008 owns protocol and performance gates for language tooling; Plan
008a owns package-manifest and native-compatibility fixtures; Plan 009 owns a
deterministic theme-gallery gate; and Plan 009a owns app/library global-style
fixtures. Plan 009b owns a separate utility gallery and manifest/runtime parity
gate before Plan 012 packages the opt-in result; it does not block Plan 010's
non-utility example migration.

## Explicit non-goals

- A custom text editor. Use AvaloniaEdit as the interop proof.
- A Lucent-specific command bus, dependency-injection container, windowing
  system, accessibility tree, or renderer.
- One-for-one wrappers around Avalonia controls.
- Browser-compatible CSS, a generic virtual DOM, alternate renderers, mobile,
  or web targets.
- Production hot reload before stable owner, component, and source-map
  identities exist.
- A broad package ecosystem before the first Workbench release gate passes.

## Ecosystem evidence

The sequence follows patterns exposed by mature desktop frameworks:

- Qt Quick separates reusable `Action` objects from menus and shortcuts, and
  gives focus scopes explicit semantics:
  <https://doc.qt.io/qt-6/qml-qtquick-controls-action.html> and
  <https://doc.qt.io/qt-6/qtquick-input-focus.html>.
- Flutter separates shortcut gestures, semantic intents, and actions, while
  focus remains its own tree:
  <https://docs.flutter.dev/ui/interactivity/actions-and-shortcuts> and
  <https://docs.flutter.dev/ui/interactivity/focus>.
- Avalonia already supplies commands/key bindings, focus, dialogs, virtualized
  collections, accessibility, and headless testing:
  <https://docs.avaloniaui.net/docs/input-interaction/mouse-and-keyboard-shortcuts>,
  <https://docs.avaloniaui.net/docs/input-interaction/focus>,
  <https://docs.avaloniaui.net/docs/how-to/dialogs-how-to>, and
  <https://docs.avaloniaui.net/docs/app-development/accessibility>.

## Findings considered and rejected

- **Build Lucent-native controls first:** rejected. ADR 0004 correctly makes
  native Avalonia controls the default; wrappers would delay interop and create
  shallow modules.
- **Implement a general router before Workbench:** rejected. Workbench needs
  selection and view composition, not a platform-neutral navigation framework.
- **Make every keyed loop virtualized:** rejected. Small structural regions and
  recycled collection containers have different lifetime contracts.
- **Prioritize hot reload:** rejected until owner identity, component metadata,
  source maps, and disposal are stable enough to patch safely.
- **Expand CSS before a concrete application need:** rejected. Plan 009 now adds
  only the bounded OKLCH and class-completion seams required by the accepted
  Shadcn workflow; Plan 009a separately gates global styles. Plan 009b adds only
  a finite opt-in Avalonia utility vocabulary and explicitly declines browser
  Tailwind compatibility.
- **Copy Akbura's surface wholesale:** rejected. Plan 008a borrows the narrow
  package-manifest and compatibility-matrix lessons without XML syntax,
  component-as-control wrappers, ordered hooks, DI/command vocabulary, embedded
  private source, or a reactive style runtime.

## Audit scope

This pass covered the compiler, generated runtime behavior, MSBuild adapter,
language server, VS Code extension, tests, examples, ADRs, design documents,
and official ecosystem documentation. It did not audit low-level parser
correctness, dependency security, performance benchmarks, macOS/Linux runtime
behavior, or unpublished Avalonia control libraries. A planned four-agent
fanout failed before execution because the newly installed conversion runtime
called `node:v8.createHook`, which Bun does not implement; the parent completed
the local and external research directly.
