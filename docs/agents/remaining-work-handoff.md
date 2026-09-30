# Current work and handoff

Updated September 29, 2026. The owner authorized the remaining review corrections,
then the broader roadmap, with completed chunks committed and pushed as needed.
This file describes current work; detailed evidence belongs in the linked execution
records and issues.

## Current constraints

The owner explicitly resumed UI and focus testing. Coordinate physical desktop
ownership between workers; builds and model checks may continue independently.
Notes #12's real input proof remains pending until exercised. The prior offscreen
substitution was declined during the gaming pause; do not describe it as a pass.

If the owner interrupts Computer Use after testing resumes, ask to continue while
working on independent tasks. Do not interpret the interruption as cancellation of
the whole implementation request.

## Delivery and active work

The [review execution record](../plans/comprehensive-review-execution.md) owns the
verification details. Main is pushed through `09ef196b2f136fe8fb6d5934bb1b61e6ab5eafef`;
[CI 93](https://github.com/RichiCoder1/lucent/actions/runs/36653949217) passed managed,
package verification and publication. `0.3.0-dev.93.1` is published.

| Work | Current boundary | Next action |
| --- | --- | --- |
| Notes #11, Lucent #320 and #322 | Delivered and closed; Notes CI and Lucent CI 91 passed | Preserve exact-source evidence; no new work required |
| Authoring #321 | Published in 93.1; frozen package proof and CI pass; CI 92's stale ambiguity assertion is corrected in `9fde6b46` | Close delivery tracking |
| Default-content reader binding #325 | Published in 93.1; 154 compiler cases and the original implicit-icon Component Browser build pass | Close delivery tracking |
| Notes #12 | `f61ae316`; CI, model/storage and NativeAOT maintenance checks pass | Focused Ctrl+S/editing continuity after desktop permission resumes |
| Presentation #323 | `caaba2d7`; Core/browser contracts and headless images pass | Adopt the new public package in Light Notes, then its deferred visual/input proof |
| Repeated work #324 | Parser, protocol line-index and font allocation improvements published with measured comparisons; source-map indexing and broad preparation caching deferred by measurements | Finish Notes filtering and native/UIA decisions |
| Restoration #221 | Location slice `e67f7ac6` has 62 passing codec/session checks and no required corrections from independent Code Review | Journal/state implementation, public startup/package-only proof, then app persistence and interaction proof |
| Activation #222 | Optional Foundation/C#/WinRT NativeAOT probe passes hidden-process redirection and raw URI projection | Implement isolated adapter and lifecycle policy; registered protocol, MSIX and window/foreground proof remain pending |

The activation probe now explicitly uses STA like Lucent applications. Both
processes pass and registration/cleanup remain on the same thread. The initial
async-main experiment crashed on cross-thread mutex release; preserve that failed
evidence. The [restoration/activation plan](../plans/navigation-restoration-activation.md)
and [journal implementation design](../plans/navigation-journal-implementation.md)
define the next contracts. No protocol registration or runtime installation has
been performed on the owner's machine.

Light Notes is upgrading from `0.3.0-dev.86.1` to published `0.3.0-dev.93.1`.
Its new command buttons, Field-aware TextArea, placeholder colors and measured
metadata-contrast correction are prepared separately; do not claim consumer
integration based only on Lucent source builds.

After these corrections, continue navigation #205, onboarding #242, native
preview #224, diagnostics #243 and transfer #244 according to their technical
dependencies. The [roadmap](../ROADMAP.md) and GitHub issue acceptance are
current authority. Do not revive older instructions to stop before #205.

## Workspace and evidence preservation

- Lucent: `D:/src/richicoder1/lucent`; Light Notes: `D:/src/richicoder1/light-notes`.
- Preserve unrelated `.codex/`, `.dotnet-home/`, `%SystemDrive%/`, `advisor-plans/`,
  `docs/plans/windows-sandbox-testing.md` and `docs/research/` content. Preserve
  Notes' pre-existing status-only formatting changes. Stage exact owned files.
- Serialize shared-tree builds and coordinate commits between workers. Fresh
  binary tests must run a nonzero expected case count; ordinary `dotnet test`
  without the repository's MTP invocation has previously discovered zero cases.
- Keep ignored evidence and failed comparisons. An active-machine measurement is
  characterization, not a native frame-budget or manual UI pass. Follow
  [verification policy](verification.md) and [test scope](../TESTING.md).
- If `D:` disappears after restart, inspect attachment of `C:/DevDrive/Dev.vhdx`
  before changing anything. Do not format, repartition or relax ACLs.

The performance batch #313–319 is complete, including physical held-border resize
proof, and published through `0.3.0-dev.89.1`. Its
[execution record](../plans/performance-execution.md) retains failed and passing
measurements. Earlier component, authoring and navigation deliveries remain in
their execution records and issues. The prior chronological handoff is preserved
[in Git history](https://github.com/RichiCoder1/lucent/blob/09ef196b2f136fe8fb6d5934bb1b61e6ab5eafef/docs/agents/remaining-work-handoff.md);
its superseded instructions are historical, not active work orders.
