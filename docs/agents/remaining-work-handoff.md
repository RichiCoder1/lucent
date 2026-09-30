# Current work and handoff

Updated September 29, 2026. The owner authorized the remaining review corrections,
then the broader roadmap, with completed chunks committed and pushed as needed.
This file describes current work; detailed evidence belongs in the linked execution
records and issues.

## Current constraints

The owner explicitly resumed UI and focus testing. Coordinate physical desktop
ownership between workers; builds and model checks may continue independently.
Notes #12's real pending-save input and reopen proof now passes on the published
93.1 consumer fixture. The prior offscreen substitution was declined during the
gaming pause and is not part of that evidence.

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
| Authoring #321 | Published in 93.1, closed, Project Done | No new work required |
| Default-content reader binding #325 | Published in 93.1, closed, Project Done | No new work required |
| Notes #12 | Published correction, CI, model/storage and NativeAOT maintenance checks pass; real delayed-write Ctrl+S/typing and reopen pass | Record native evidence and close tracking with the consumer update |
| Presentation #323 | New controls adopted in Notes against 93.1; default-size native proof passes; Field grouping exposed a route-outlet minimum-height defect | Publish framework sizing fix `9f3da89a`, update consumer, rerun constrained-size proof and finish browser walkthrough |
| Repeated work #324 | Parser, protocol and font improvements published; projection buffer-copy removal committed; broader caches and Notes filtering deferred by measurements | Publish projection improvement; defer UIA change because real clients remain listening, with no production experiment retained |
| Restoration #221 | Journal/state implementation and package-only location startup proof complete; review reproduced extreme imported scroll and stale focus requests | Finish correction review/publication, then app persistence and interaction proof |
| Activation #222 | Optional adapter and bounded lifecycle policy implemented locally; source/model checks in progress | Correct rejected-warm/startup ordering, package-only proof, then native registration/foreground coverage |

The activation probe now explicitly uses STA like Lucent applications. Both
processes pass and registration/cleanup remain on the same thread. The initial
async-main experiment crashed on cross-thread mutex release; preserve that failed
evidence. The [restoration/activation plan](../plans/navigation-restoration-activation.md)
and [journal implementation design](../plans/navigation-journal-implementation.md)
define the next contracts. No protocol registration or runtime installation has
been performed on the owner's machine.

Light Notes restores, builds and publishes against `0.3.0-dev.93.1`. Its new
command buttons, Field-aware TextArea, placeholder colors and metadata contrast
were exercised with native typing and a real bounded SQLite writer delay. The
latest text survived a new process. Evidence is in
`artifacts/notes12-native/computer-use-proof.md` and Notes' consumer evidence.
Its source update remains uncommitted until the next framework package fixes the
known constrained-height failure; do not close #323 with that failure outstanding.

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
