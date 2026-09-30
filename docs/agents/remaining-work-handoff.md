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
verification details. Main is pushed through `1cd168d73f429792cd4eaef909fbc204c564cb2f`;
[CI 93](https://github.com/RichiCoder1/lucent/actions/runs/36653949217) passed managed,
package verification and publication. `0.3.0-dev.93.1` is published.
[CI 94](https://github.com/RichiCoder1/lucent/actions/runs/36660194305) passed all
three jobs and published `0.3.0-dev.94.1`, including the outlet sizing, projection
and final journal-ownership corrections. The next local batch is not published yet.

| Work | Current boundary | Next action |
| --- | --- | --- |
| Notes #11, Lucent #320 and #322 | Delivered and closed; Notes CI and Lucent CI 91 passed | Preserve exact-source evidence; no new work required |
| Authoring #321 | Published in 93.1, closed, Project Done | No new work required |
| Default-content reader binding #325 | Published in 93.1, closed, Project Done | No new work required |
| Notes #12 | Published correction and real delayed-write Ctrl+S/typing/reopen pass; closed and Project Done | Presentation adoption remains separate |
| Presentation #323 | Notes consumes 94.1; additional CommandScope sizing fix `3c95e5f1` and two app shrink declarations pass unchanged 520-pixel regression in isolated diagnostic output | Publish correction, verify actual package consumer and finish browser walkthrough |
| Repeated work #324 | All retained improvements published in 94.1; measured deferrals recorded; closed and Project Done | No new work required |
| Restoration #221 | Final journal ownership fixes published in 94.1; app opt-in persistence passes 43 contracts and is under independent review | Finish app review, NativeAOT publication and physical restart proof |
| Activation #222 | Optional adapter committed with 22 passing model contracts; reviewed isolated registration/MSIX fixtures committed | Final coherent package proof, then native registration/foreground coverage |
| Release identity #245 | Exact server/package/client compatibility, immutable descriptors and CI completion implemented; 63 rejection/integrity contracts pass | First coherent CI publication; complete does not mean NuGet upload succeeded |

The activation probe now explicitly uses STA like Lucent applications. Both
processes pass and registration/cleanup remain on the same thread. The initial
async-main experiment crashed on cross-thread mutex release; preserve that failed
evidence. The [restoration/activation plan](../plans/navigation-restoration-activation.md)
and [journal implementation design](../plans/navigation-journal-implementation.md)
define the next contracts. No protocol registration or runtime installation has
been performed on the owner's machine.

Light Notes now restores and builds against `0.3.0-dev.94.1`. Its new
command buttons, Field-aware TextArea, placeholder colors and metadata contrast
were exercised on 93.1 with native typing and a real bounded SQLite writer delay. The
latest text survived a new process. Evidence is in
`artifacts/notes12-native/computer-use-proof.md` and Notes' consumer evidence.
Its source update remains uncommitted until the next framework package fixes the
known constrained-height failure. Diagnostic DLL substitution isolated the cause
but is not consumer delivery proof; do not close #323 with that boundary outstanding.

Fresh Component Browser and Issue Browser NativeAOT publications also pass, with
binary identities under `artifacts/review323-native/build-proof.json`. Their
walkthrough remains unperformed: Computer Use repeatedly reports a physical Escape
interruption before any application action, including after an authorized JavaScript
session reset. No alternative input mechanism was used. Owner permission remains
granted, but the helper must recover before resuming desktop actions.

Local commit `c4373c2f` patches the VSIX packager's two vulnerable transitive
dependencies. Locked packaging passes and its npm audit reports zero vulnerabilities;
this is not an audit of the framework's other dependency trees. It is queued for the
next push. #245's [release contract](../RELEASE-SETS.md) preserves exact pins and
authenticated distribution. The reported PowerShell escaping review finding was
retracted after AST/file-byte confirmation and real producer execution; no speculative
correction was applied. #246 template implementation is starting independently.

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
