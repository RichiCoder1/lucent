# Plan 007a review

Plan 007a completed against `e099eae` with the user-approved bindings-only
adjustment: Avalonia 12.1.1 `ListBox` item templates remain non-recycling because
the native control does not pass existing roots through `Build(data, existing)`.

## Result

- Standards review: **PASS** (`ad072163-9cb0-4676-bf95-eb9ea6b04787`).
- Spec review: **PASS** (`f56e9f8a-2efe-4833-85b2-2c79f33cd62f`).
- Warning-free solution build.
- 272 .NET tests passed: compiler 149, MSBuild 13, runtime 33, language server
  21, analyzers 11, and Workbench 45.
- VS Code extension: 5 tests passed and `prepare-server` passed.
- Workbench data/reliability and Package Pulse native smokes passed.
- Markdown links and `git diff --check` passed.

## Accepted boundary

`binding(...)` is explicit and native-only. Item paths use inherited
`DataContext`; component parameters and readonly ordinary fields use explicit
sources with owned replacement/disposal. Delayed slot content diagnoses
explicit-source bindings. No runtime type, adapter, reflection path, custom
presenter, dependency, or row owner was added.
