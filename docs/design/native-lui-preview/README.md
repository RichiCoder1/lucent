# Native .lui preview design handoff

This folder is an interactive design reference and an implementation specification.
It contains no production preview implementation.

1. Read [component-preview-authoring.md](component-preview-authoring.md) for the
   user-approved component-first direction: follow/pin, automatic defaults, optional
   `.lui` preview data/variants, generated hosting and a tight edit loop. It supersedes
   the earlier catalog-first UX and adds 12 acceptance scenarios.
2. Read [implementation-guide.md](implementation-guide.md) for controls, capability
   boundaries, lifecycle, focus, failures and 14 interaction acceptance scenarios.
3. Open [index.html](index.html) in a browser. Switch Active editor to try follow/pin,
   choose Authored variants to reveal optional data states, or open UserCard to
   see missing-data guidance. Also compare 360/520/960px, themes, failures and
   delivery stages. Review controls are not product UI; all are simulations.
4. Use [implementation-audit.md](implementation-audit.md) to find existing source
   owners and message handlers. Revalidate its baseline before editing runtime code.
5. Read [review-resolution.md](review-resolution.md) for the reconciled Sol 6.1
   xhigh / Claude Opus 5.5 High recommendations and
   [validation.md](validation.md) for exactly what the mockup checks establish.
   [opus-review.md](opus-review.md) preserves the independent advisory review.

For the initial refinement, implement the guide's A–C items only where the actual
host contract supports them. Do not expose Interact, unsaved-source freshness or
Inspect simply because the HTML can simulate them. Implementation has separately
reported a Stop/settings fix; avoid duplicating it. Unsaved-source fidelity remains
unproven at handoff. The owner accepted saved-source interactive delivery first
on October 4; unsaved compiler fidelity remains a separate #330 investigation.

The core copy set is this README, `index.html`, `implementation-guide.md`,
`component-preview-authoring.md`,
`implementation-audit.md`, `review-resolution.md`, `opus-review.md` and
`validation.md`. JPEGs are optional visual references. `opus-brief.md`
and `opus-result.json` are consultation working records, not implementation inputs.

## Captures

- [Component-first automatic preview](component-wide-review.jpg)
- [Required preview data at narrow width](component-needs-data-review.jpg)
- [Optional variants at split width](component-variants-review.jpg)

Earlier `wide-review.jpg`, `narrow-error-review.jpg` and `inspector-review.jpg`
document the first iteration. Their catalog-first identity is superseded; use the
current HTML and component-prefixed captures for the current direction.

All sample data, source snippets and inspector values are illustrative. The HTML
can open directly from disk; no package install, build or running Lucent process
is required.
