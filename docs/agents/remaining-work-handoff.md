# Current work and handoff

Updated September 30, 2026. The owner authorized the remaining review corrections,
then the broader roadmap, with completed chunks committed and pushed as needed.
This file describes current work; detailed evidence belongs in the linked execution
records and issues.

## Current constraints

The owner reauthorized Computer Use on September 30, but the first app-list call
immediately reported another physical Escape interruption. A question to reset and
retry is pending; do not issue desktop input until the owner answers. The earlier
Node session was reset and the untouched Issue Browser fixture was stopped.
Earlier representative Component Browser and Issue Browser
presentation checks ran, as did a real Issue Browser journal restart.
Computer Use's Down and Right commands arrived at SDL as keypad 2 and keypad 6,
not the main arrow keys. Tab arrived correctly and moved focus. Equivalent Core
and compiled application pointer-to-arrow checks pass. The diagnostic patch was
removed after isolated NativeAOT builds; evidence is under `artifacts/issue245`.
Do not claim physical main-arrow coverage from these injected commands. Standard
Num Lock-off keypad navigation passes separate managed and NativeAOT contracts.
Notes #12's real pending-save input and reopen proof now passes on the published
93.1 consumer fixture. The prior offscreen substitution was declined during the
gaming pause and is not part of that evidence.

If the owner interrupts Computer Use after testing resumes, ask to continue while
working on independent tasks. Do not interpret the interruption as cancellation of
the whole implementation request.
When a walkthrough finishes, close its test windows and end the Computer Use
connection rather than leaving the owner to cancel it. The current helper exposes
no explicit stop method; reset the Node session after the final interaction.

## Delivery and active work

The [review execution record](../plans/comprehensive-review-execution.md) owns the
verification details. The latest source batch includes navigation `57245db1`,
activation fixtures `7c4be47b`, architecture preflight `a4b1ba96` and the
deterministic native thread contract `e5bbada1`;
[CI 93](https://github.com/RichiCoder1/lucent/actions/runs/36653949217) passed managed,
package verification and publication. `0.3.0-dev.93.1` is published.
[CI 94](https://github.com/RichiCoder1/lucent/actions/runs/36660194305) passed all
three jobs and published `0.3.0-dev.94.1`, including the outlet sizing, projection
and final journal-ownership corrections. CI 95, 96 and 97 failed before publication.
Their failures exposed dependency-lock drift, strict dependency-free metadata
traversal, XML byte encoding, and the Windows formatter command-length limit.
Those corrections are committed. CI 97 passed formatting and produced all ten
packages, server and VSIX, then exposed optional XML-item access in the architecture
preflight and a wrong-thread test that allowed Task inlining. The preflight fix is
committed as `a4b1ba96`; the dedicated-thread correction passes managed and
NativeAOT execution in `e5bbada1`. CI 98 passed those package/native checks and the
managed/NativeAOT journal consumers, then failed an optional expected-field access
in asset verification. `4d25844c` handles both optional dimensions and application
flags under strict PowerShell. The full asset proof passes against immutable 93.1
packages under `artifacts/issue245/assets-strict-ci93`; this verifies the script
correction, not publication of the new batch.

CI 99 passed managed tests, the full native asset proof and headless package
consumers, then failed after publishing the package-only Issue Browser because
SDK-only notice records have no runtime `output` property. `a50abdc8` reads that
optional field safely under strict PowerShell. The extracted production loop
accepts the 17 runtime notices and still rejects a missing required notice.
The failed log is retained as `artifacts/issue245/ci99-package-job-109762477403.log`;
publication was skipped and 94.1 was the latest published version at that point.

CI 100 at `5f683ed1` passed managed verification and reached the activation
package consumer, then failed compilation. The fixture stored its NuGet cache
inside the generated C# project, allowing the default source glob to include
CsWinRT package sources and conflict with its referenced runtime. Preserve
`artifacts/issue245/ci100-package-job-109776699986.log` (SHA-256
`3B25DA5C603F838FC9A90E9FAD8550BE371F38AA04E7B2E35FD9D2010ADA3976`).
Both activation fixtures now keep the cache beside the generated project. The
corrected consumer compiles only `Program.cs`, and an isolated development-package
NativeAOT proof passed: primary and valid secondary exited 0; the oversized
secondary exited 2. Evidence: `artifacts/issue245/ci100-package-diagnosis.md` and
`artifacts/issue245/ci100-activation-globfix-5f683ed/proof-manifest.json`.
CI 100 publication was skipped; do not describe 100.1 as published or use it for
Notes adoption.

[CI 101](https://github.com/RichiCoder1/lucent/actions/runs/36686517408) at
`6270d75762c9318d3a64eb35cf512f6631fb485e` passed managed, package verification and
publication. `0.3.0-dev.101.1` is now published. The complete artifact's GitHub digest
and complete descriptor were independently verified; #245 is closed. The approved
editor catalog and retained acquisition evidence are under
`artifacts/issue247-cache-acquisition-design/ci101-catalog`. Generated template
consumers are now exercising that bundle.
The failed CI 95–100 evidence above remains historical and must be preserved.

| Work | Current boundary | Next action |
| --- | --- | --- |
| Notes #11, Lucent #320 and #322 | Delivered and closed; Notes CI and Lucent CI 91 passed | Preserve exact-source evidence; no new work required |
| Authoring #321 | Published in 93.1, closed, Project Done | No new work required |
| Default-content reader binding #325 | Published in 93.1, closed, Project Done | No new work required |
| Notes #12 | Published correction and real delayed-write Ctrl+S/typing/reopen pass; closed and Project Done | Presentation adoption remains separate |
| Presentation #323 | Framework correction is published in 101.1; Notes adoption is in progress, including the unchanged 520-pixel regression | Verify official Notes consumer and finish browser walkthrough |
| Repeated work #324 | All retained improvements published in 94.1; measured deferrals recorded; closed and Project Done | No new work required |
| Restoration #221 | Reviewed app persistence and pending-state capture committed as `57245db1`; native restart reopens issue #9915; official 101.1 package-only NativeAOT replay app prepared | Execute final package-only replay when Computer Use resumes |
| Activation #222 | Optional adapter has 22 passing model contracts; reviewed fixture corrections published; official 101.1 NativeAOT fixture and unsigned MSIX/Sandbox input prepared | Isolated native registration/foreground coverage remains |
| Release identity #245 | CI 101 passed all three jobs and published 101.1; actual complete artifact authenticated; closed | No remaining issue work |
| Templates #246 | Exact descriptor-bound local template package passes generated builds, semantic/Skia tests, package-only library and NativeAOT publication; 11-package inventory and CI wiring implemented | Commit/publish and confirm official template publication |
| Editor lifecycle #247 | Reviewed requirements/cache/import and authenticated online acquisition pass final V6 development proof | Commit/publish; visual VS Code sign-in remains part of the later onboarding journey |
| Windows keypad #326 | Published in 101.1 and closed; three focused contracts pass in both managed and NativeAOT runs | No remaining issue work; no physical main-arrow claim |

The activation probe now explicitly uses STA like Lucent applications. Both
processes pass and registration/cleanup remain on the same thread. The initial
async-main experiment crashed on cross-thread mutex release; preserve that failed
evidence. The [restoration/activation plan](../plans/navigation-restoration-activation.md)
and [journal implementation design](../plans/navigation-journal-implementation.md)
define the next contracts. No protocol registration or runtime installation has
been performed on the owner's machine.
The official 101.1 activation fixture is at
`artifacts/windows-activation-registered/09d23de4c31a449e83e99d06148c9f34`;
its unsigned MSIX and unlaunched Sandbox input are recorded under
`artifacts/windows-activation-sandbox/64eb810555e14a84966c2ee4e3ff117c`.
The package-only Issue Browser replay build is under
`artifacts/issue221-package101-replay`, with exact package/executable hashes in
`prepared.json`. None of these preparation steps ran a window or registered a protocol.

Light Notes now restores and builds against `0.3.0-dev.94.1`. Its new
command buttons, Field-aware TextArea, placeholder colors and metadata contrast
were exercised on 93.1 with native typing and a real bounded SQLite writer delay. The
latest text survived a new process. Evidence is in
`artifacts/notes12-native/computer-use-proof.md` and Notes' consumer evidence.
Its source update remains uncommitted until the next framework package fixes the
known constrained-height failure. Diagnostic DLL substitution isolated the cause
but is not consumer delivery proof; do not close #323 with that boundary outstanding.
101.1 is now available. Its two pin edits are prepared in
`artifacts/notes101-adoption/pins.patch`; automatic approval review rejected the
separate-repository write because the visible authorization covered walkthroughs.
The owner approval question is pending; no Notes edit has been applied by that attempt.

Fresh Component Browser NativeAOT presentation observations are recorded under
`artifacts/review323-native-final/walkthrough.md`. The final Issue Browser binary
and two-process journal proof are under `artifacts/issue221-native-reviewed`.
Calendar alignment, slider geometry, menu/submenu placement, bounded dialog width,
password toggling, editable ComboBox reuse, table sorting and picker cancellation
were exercised. This is representative desktop evidence, not every state, IME or
screen-reader certification. Commit `03116311` corrects compact header wrapping and
sizes the status-filter popup to stock labels, bounded by the available width.
Fourteen Core list contracts and two browser contracts pass; fresh NativeAOT browser
binaries are under `artifacts/review323-layout-final`. Their visual checks remain
pending the owner's pause. Overlapping Component Browser publish attempts produced
an inconclusive prepared-output failure; the subsequent single serial publish
matched generated routes and completed. Both attempts are preserved. Computer Use
main-arrow delivery remains unverified.

Pushed commit `c4373c2f` patches the VSIX packager's two vulnerable transitive
dependencies. Locked packaging passes and its npm audit reports zero vulnerabilities;
this is not an audit of the framework's other dependency trees. It is queued for the
next successful publication. #245's [release contract](../RELEASE-SETS.md) preserves exact pins and
authenticated distribution. The reported PowerShell escaping review finding was
retracted after AST/file-byte confirmation and real producer execution; no speculative
correction was applied. #246's composed proof against authenticated CI101 is under
`artifacts/templates-proof/ci101-36686517408-1-fix2`. It passes five warning-clean
generated builds, one semantic test, two Skia tests, package-only library interaction
and artwork, and a NativeAOT app publish without launch. The aggregate manifest binds
reused passing slices and preserved failures. Corrections avoid an app-name token
colliding with `LucentApplication`, give the library a packable prerelease default,
and fix the consumer fixture's root and generated component namespaces. Templates
now participate in the 11-package release inventory and CI's package-consumer
receipt. Final `artifacts/templates-proof/ci101-release-final/evidence.json` proves
installation of the exact descriptor-bound template package, all generated builds
and semantic/capture/library checks, and NativeAOT publication without launch.
Its candidate is explicitly local development, combining CI101 inputs and the
new template package; official template publication awaits the next CI run.
#247's bundled delivery
passes the actual producer checks at
`03116311`, recorded in `artifacts/issue247-producer-03116311/evidence.json` as a dirty
development snapshot, not release evidence. The VSIX is 14,968,359 bytes with 184
server files; its extracted verifier and exact server identity both pass. Evaluated
project matching and immutable cache/import now have passing development proof in
`artifacts/issue247-producer-6270d757`: 71 release fixtures and the real packaged
JavaScript adapter installing/reverifying through its packaged helper. The helper
has 8 passing contracts and the extension had 45 at that checkpoint. Code Review
accepted held-open stdin, cancellation and stdout-close corrections. V4, under
`artifacts/issue247-producer-6270d757-v4`, closes the reviewed custom lock-file and
cancellation gaps. Two focused producer contracts pass, including mid-discovery
mutation and stdin cancellation with child-process exit. Its actual packaged
adapter/helper imports and reuses the authenticated 101.1 server for a real
package project. V4 source and artifact review passed.

The explicit online command now uses VS Code GitHub authentication and fixed
Actions endpoints for a shipped-catalog-approved release. It preserves the current
server, checks project inputs after download and cleans its owned staging data.
The final 60 Node contracts pass. The catalog producer's total transfer deadline,
redirect credential handling and partial-file cleanup corrections passed focused
tests and review. All 13 helper, 72 release and 26 catalog contracts pass. V5's
packaged JavaScript/helper downloaded the real approved CI101 complete artifact,
verified and installed its matching server, reused the immutable cache and cleaned
the owned download. V6 adds the reviewed cache-before-sign-in shortcut; its packaged
runtime sources match the tested source and reuse V5's unchanged network/helper
proof. Evidence is under `artifacts/issue247-producer-6270d757-v6` and its linked V5
proof. The integrated review found no remaining blocker. These are development
proofs, not published releases. See
[the lifecycle plan](../plans/editor-tooling-lifecycle.md) for supported scope.

Environment doctor #248 is the next implementation slice. Its worker owns only
`src/Lucent.Tools` and `tests/Lucent.Tools.Tests`; these are in-progress sources,
not part of the #246/#247 publication checkpoint. Keep default checks static,
offline and read-only; explicit project checks must reuse verified requirements
evaluation rather than becoming a second MSBuild evaluator.

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
