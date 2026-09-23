# Fable review disposition — test-quality plans

Reviewed September 22, 2026 using the installed Claude CLI, requested model `fable`, effort `high`. The successful result reports `claude-fable-5-1` as the review model. Source access was limited to Read/Glob/Grep; no reviewer builds or edits were enabled.

Fable's verdict on the initial three drafts was **ready with revisions**. The [verbatim review](test-quality-fable-review.md) and [prompt](test-quality-fable-prompt.txt) are retained here; raw run metadata remains in the local `test-quality-fable-result.json` artifact. The coordinator applied the following revisions. The final revision has not received a second Fable pass; none is claimed or required for this handoff.

| Feedback | Disposition |
| --- | --- |
| Formatter cleanup depends unnecessarily on a separately owned helper | Accepted. Plan 003 C1 owns the whole change; local compilation reuse is optional. No 004 dependency remains. |
| Prototype Unicode input was described imprecisely | Corrected. It is a runtime argument; migrate actual distinct comment, escaped-newline and brace cases into a `.lui` production-formatting specimen. |
| Real grammar execution adds dependencies and CI installation cost | Accepted. Defer the tokenizer toolchain and keep current structural checks for this package. A future proposal must account for those costs explicitly. |
| Multi-suite orchestration is too large for the immediate benefit | Accepted. Required 004 H3 removes implicit Native and unused TestHost publication. Multi-suite reuse is optional H4, justified by actual use/cost first. |
| Stress cases should not introduce an unplanned opt-in mechanism | Accepted. C6 defaults to keeping them and recording measured cost; a follow-up proposal is sufficient if cost proves material. We do not rely on the review's broader claim that no characterization convention exists anywhere. |
| Use existing asset fixture helper and exact inventory membership | Accepted. C5 reuses New-CaseProject, uses case-insensitively distinct paths and checks exactly nine independent entries. |
| Diagnostic helper and headless migration are speculative | Accepted. Keep diagnostics local unless real reuse emerges; headless use is guidance rather than a tracked migration. |
| Preserve CRLF and meaningful stroke samples | Accepted. H1 uses the exact sent string; H2 uses stable thick-stroke samples and checks unpainted regions. |
| Shorten AGENTS.md pointer | Accepted. Plan 005 A1 is now two sentences; detail stays in the existing verification policy. |
| Preserve reactive cell-kind and sole-emitter assertions | Accepted explicitly in C4; remove only incidental private spelling already subsumed by behavior. |
| Update baseline | Verified local HEAD as a2e74acd448b488c22ecb71ec5b12d6de436110a and re-anchored the three plans. Additional working-tree changes remain, so the plans do not repeat the review's clean-tree claim. |

Required delivery: plan 005; plan 003 C1–C5 plus the C6 keep/follow-up decision; plan 004 H1–H3. H4 requires only a proceed/defer recommendation, not a runner rewrite. Grammar tooling stays deferred. Existing NativeAOT, UIA, independent emitter, negative asset, lifetime and source-map boundaries are preserved.

No new full-suite, coverage-percentage, test-count or mutation gate was introduced. Only advisory documents were changed in this task. Implementation should start after its current work and verify affected behavior once, reusing unaffected passing evidence.
