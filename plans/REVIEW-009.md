# Plan 009 / 009a review record

> **Historical scope**: this PASS applies to the pre-008a plan text. The current
> manifest, activation, precedence, and dependency amendments are reviewed in
> [`REVIEW-008A-010-CROSSCHECK.md`](REVIEW-008A-010-CROSSCHECK.md).

Plan 009 and its separately gated global-style follow-up are **PASS** after a
fresh final review.

## First adversarial review — FAIL

The fresh read-only reviewer accepted the post-Plan-008 tooling boundary,
non-executing/no-I/O completion design, explicit Avalonia theme installation,
gallery-before-migration sequence, and accessibility/lifecycle preservation.

It found one high-scope issue:

- Plan 009 combined the Shadcn/OKLCH/gallery and class-completion delivery with
  an independent global CSS/MSBuild/generated-type/analyzer initiative.

It also found four roadmap/detail issues:

- Plan 010 did not make updates to `DESIGN.md`, `design/tokens.css`, and Plan 007
  testable migration deliverables.
- The roadmap marked in-progress Plan 008 as TODO.
- The roadmap incorrectly assigned Plan 009 a Workbench gate instead of its
  dedicated gallery gate.
- Plan 007 still named Plan 009, rather than the post-theme migration/dogfood
  plans, as the Workbench release oracle.

Residual advice required theme manifests to match complete assembly identity,
not version alone.

## Resolution

- Split explicit global CSS into `009a-global-lucent-styles.md`, dependent on the
  accepted Plan 009 catalog.
- Kept Plan 009 focused on the theme design's first delivery plus the requested
  adjacent/active-theme `Class:` completion.
- Made Plan 010's named visual-authority updates and no-competing-authority check
  explicit acceptance evidence.
- Corrected Plan 008 status, release gates, the stale Plan 007 owner reference,
  and package dependencies.
- Pinned theme manifests by assembly name, version, culture, and public-key token.

## Second adversarial review — FAIL

The fresh reviewer confirmed the high-scope split and all prior roadmap fixes,
with no blocker or high finding. It found two medium precision gaps:

- Full assembly identity also requires culture.
- Plan 009a treated construction of a generated style as installation even when
  it was never added to `Application.Styles`.

The plan now keys every manifest by name, version, culture, and public-key token.
Plan 009a recognizes only bounded direct `Application.Styles.Add(new
...LucentStyles())` forms; bare construction and indirect flows do not activate
completion metadata.

## Final disposition

**PASS** — the final fresh read-only reviewer found no blocker, high, or medium
issue. It confirmed the Plan 009/009a scope split, complete assembly identity,
bounded direct installation detection, gallery-before-migration gate, corrected
Plan 008 status, Plan 010 visual-authority ownership, and stale-reference cleanup.

Residual risk is implementation-only: gallery, package-consumer, semantic
detection, precedence, and performance gates remain to be executed when these
TODO plans are implemented.
