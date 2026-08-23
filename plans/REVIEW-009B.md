# Plan 009b review record

Plan 009b is **PASS** after addressing the first adversarial findings and a
fresh final read-only review.

## First adversarial review — FAIL

The fresh read-only reviewer accepted the finite opt-in scope, Plan 009/009a
seams, explicit installation, immutable metadata, no-I/O/no-execution tooling
contract, exclusion of non-composable side spacing, and honest rejection of
browser-only Tailwind behavior.

It found two medium precision gaps:

- The plan required escaped-colon parsing and source-range agreement but did not
  explicitly include the current `CssProjectTokenIndex`, whose identifier-only
  scanner would truncate `.hover\:bg-primary` to `hover`.
- The package's installable style type and metadata identity were unspecified,
  so it was unclear how Plan 009a's direct-install detection would activate the
  matching Plan 009 completion metadata.

## Resolution

- Route `CssProjectTokenIndex` through the shared decoded class tokenizer and
  require an exact escaped-source-span regression test.
- Name the public installable type
  `global::Lucent.Styles.Utilities.LucentStyles`; ship runtime styles and safely
  inspectable metadata in the same exact-versioned assembly; reuse Plan 009a's
  direct semantic installation form; and keep package references, construction,
  registries, and utility-specific activation insufficient.

## Final disposition

**PASS** — the fresh final reviewer found no blocker, high, or medium issue. It
confirmed that the project index now participates in the escaped-colon contract,
the package style/metadata identity reuses Plan 009a's bounded direct-install
seam, the catalog remains finite and Avalonia-native, conflict behavior is
deterministic rather than class-order-based, and Plan 010 keeps the utilities
opt-in.

Residual risk is implementation-only: literal-colon matching must be proven
through public Avalonia selector behavior. Plan 009b correctly stops rather than
adding private matching or a broader parser if that proof fails.
