# Plan 011: Complete the Workbench dogfood application

> **Executor instructions**: Treat Workbench as the release oracle, not as a
> special case. Reuse the project/compiler, ownership, settings, loader,
> accessibility, and headless-test seams already proven by Plans 001–010.
>
> **Drift check**: `git diff --stat ad0e183..HEAD -- examples/workbench src tests docs plans`

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: HIGH
- **Depends on**: 005, 006, 008, 008a, 010
- **Category**: application integration / dogfood
- **Planned at**: commit `ad0e183`

## Why this matters

Packaging should expose only interfaces exercised by a real application.
Workbench already proves the native desktop shell, editor, virtualized data,
settings, lifecycle, accessibility, and deterministic UI-test seams, but some
project and problem data remains artificial. This plan completes that
application using project references before Plan 012 freezes a preview package
surface.

## Scope

**In scope**:

- Open a real .NET/Lucent workspace and populate the existing virtualized tree
  with project files.
- Load real compiler and project diagnostics through the existing
  `IProblemLoader` interface.
- Edit `.lui`, inspect problems, navigate to source, and preview generated C#
  using Plans 008/008a's project, diagnostic, definition, and source-map seams.
- Preserve Plan 006's `OwnedComputed` loading/stale/error/retry and cancellation
  contracts while replacing only `PlaceholderProblemLoader`.
- Preserve the settings repository, serialized save coordinator, intercepted
  shutdown, accessibility peers, native controls, and Plan 005's single
  headless harness.
- Complete settings, theme selection, recent-workspace persistence, close, and
  reopen behavior needed by the walkthrough.
- Document and automate one keyboard-first walkthrough: open workspace,
  navigate tree, edit `.lui`, inspect problems, jump to source, preview generated
  C#, change theme/settings, close, and reopen the recent workspace.

**Out of scope**:

- NuGet packages, `dotnet new`, VSIX packaging, release CI, or public docs
  publication; Plan 012 owns distribution.
- A full IDE, custom text editor, hot reload, debugger, package manager, or
  workspace-wide refactoring suite.
- New loader/settings/lifecycle/test models parallel to the interfaces already
  present.
- Private compiler hooks added only for Workbench.

## Steps

### 1. Replace placeholder project data

Connect the existing workspace and document models to the real project-context
loader. Preserve stable selection, virtualization, cancellation, and recent-path
behavior. Keep filesystem errors visible and recoverable.

**Verify**: a temporary representative Lucent workspace opens, navigates, closes,
and reopens without fixture-only hooks.

### 2. Replace the placeholder problem loader

Implement the existing `IProblemLoader` using the accepted compiler/project
query seam. Keep stale diagnostics visible while refreshing, route failures
through the existing boundary/reporter, and preserve physical Retry/Refresh.

**Verify**: loading, success, stale refresh, failure, retry, cancellation, and
latest-generation behavior pass through the existing headless harness.

### 3. Connect editing, navigation, and generated preview

Wire document edits to diagnostics, source navigation, and generated-C# preview
through the same source-map and project-generation interfaces used by the LSP.
Do not reimplement semantic analysis in Workbench.

**Verify**: editing a real `.lui` file changes diagnostics and preview output;
navigation lands on the mapped source location; cancellation cannot publish a
stale generation.

### 4. Seal the walkthrough

Complete settings/theme/recent-workspace behavior and automate the keyboard-first
workflow headlessly where Avalonia supports it. Record only native platform
dialogs or behavior that genuinely requires manual verification.

**Verify**: the release record identifies which Plan 008 capabilities are
exercised through VS Code and which Workbench flows consume diagnostics,
navigation, and generated-source mapping.

## Done criteria

- [x] Workbench opens and restores a real .NET/Lucent workspace.
- [x] The existing `IProblemLoader` reports real compiler/project diagnostics
      while preserving Plan 006 lifecycle behavior.
- [x] Editing, diagnostics, source navigation, and generated-C# preview share the
      accepted compiler/LSP project-generation seams.
- [x] Settings, themes, shutdown, and recent-workspace restoration remain
      deterministic and failure-safe.
- [x] The complete keyboard-first walkthrough passes headlessly except for
      explicitly recorded native platform behavior.
- [x] Workbench uses no private hook that would be unavailable to a clean package
      consumer.
- [x] The interfaces exercised here are recorded as Plan 012's maximum
      application-facing package surface; separately gated package features
      remain bounded by their own accepted contracts.

## STOP conditions

- Workbench needs a private compiler/runtime interface solely to pass.
- Real integration bypasses the existing owner, loader, settings, accessibility,
  or test seams.
- A green flow requires skipping existing headless interaction tests.
- The requested behavior expands Workbench into a general IDE rather than the
  bounded workspace inspector defined by the roadmap.

## Maintenance notes

Workbench validates the application-facing module interfaces Plan 012 may
package. Dedicated style catalogs may additionally be packaged only through
their accepted feature gates. Project-reference access during this plan does not
make every internal type public and is not itself a compatibility promise.
