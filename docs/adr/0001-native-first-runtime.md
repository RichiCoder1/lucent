# Own the native UI stack

Status: Accepted

## Decision

Lucent will replace its unreleased Avalonia implementation with a Windows-first, NativeAOT-compatible stack that owns reactivity, composition, layout, styling, controls, semantics, and platform adapters. The validation spike proved the risky Windows path and materially clearer authoring model.

The exact prior tree remains on `archive/avalonia-final`. Production code is rebuilt rather than promoted from the spike. There is no Avalonia bridge, dual backend, or compatibility path before an explicit post-1.0 commitment.

## Consequences

- Portable contracts remain independent from Windows hosting, input/IME, UIA, and presentation.
- Windows is the only production adapter until it succeeds; future platforms may force generalization but do not justify speculative APIs.
- NativeAOT and trimming are enforced from the first implementation commit.
- Pre-1.0 APIs and schemas may be replaced cleanly rather than preserved through compatibility branches.
