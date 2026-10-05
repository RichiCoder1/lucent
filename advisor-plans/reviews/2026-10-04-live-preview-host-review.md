# Internal live-preview host review

October 4, 2026. Read-only adversarial review requested by Implementation before live transport integration. Two Astra 6 xhigh reviewers examined separate input/scheduling and lifecycle/test boundaries; the coordinator checked each promoted finding against source. No source edits, builds, tests, desktop actions or tracker changes were performed.

Scope: the five new `src/Lucent.Preview.Hosting/{Friends,PreviewFrameRenderer,PreviewRenderContracts,PreviewRenderSession,PreviewSessionClock}.cs` files and `tests/Lucent.Preview.Hosting.Tests/LiveSessionContracts.cs`, against main `132fc85aabbbbdc3b175d9b8b9a0ba8b8d537711`. These files are uncommitted; line numbers refer to the reviewed contents. Existing one-shot `PreviewWorker` is unchanged.

Reviewed SHA-256:

- PreviewRenderSession.cs: `133CD44E519860D1A6F739A573BC3B7B04DF91FB20A9E26B205BE8BF6717769E`
- LiveSessionContracts.cs: `EECDA72E42440B3435F65B95D3456636027ACB7A64C2E316BCAECABD1CC4FFCF`

The internal integration contract is in `C:/Users/richa/AppData/Local/Temp/lucent-preview232-render-a66340fa/LIVE-INTEGRATION.md`; maintained design context is in `docs/plans/native-preview-interactive.md` and `native-preview-scenarios.md`. Worker-reported warning-clean build and 26 passing hosting contracts are prior evidence, not independently rerun here.

## Standards and lifecycle

### P2 — Fence cancelled startup before author callbacks

`PreviewRenderSession.cs:96–100, 324, 388`. A pre-cancelled parent token synchronously sets `_stopping` during registration. The thread nevertheless starts, binds an uncancelled `_lifetime.Token`, and invokes `session.Start()` before checking `IsStopping`. Setup and synchronous root creation therefore execute despite already-requested cancellation; blocking setup can strand the cancelled start.

Reject pre-cancelled requests before owner construction, and honor owner-side stop before setup begins. Extend the existing startup cancellation contract with independently observed setup/root invocation counts of zero. Preserve cleanup after cancellation occurring during genuine startup. This follows the pre-setup cancellation fence in `docs/plans/native-preview-scenarios.md:50`.

### P2 — Retain the application context across stop-time input cleanup

`PreviewRenderSession.cs:463–474`. The temporary `EnterContext()` lease ends after `_lifetime.Cancel()`. `CleanupInput()` then invokes projection, pointer Cancel, key Up and hover callbacks outside the application's synchronization context. Normal input runs under the lease at line 392. Async work started by shutdown callbacks can consequently resume on a pool thread rather than the owner.

Keep the context installed across shutdown callbacks. Assert context identity from a stop-time author callback and its asynchronous continuation, with explicit completion/lifetime handling. Implementation has acknowledged this finding and assigned a correction; that correction is not included in the reviewed hash or accepted by this report.

### P2 — Make failure paths in lifecycle tests stop their owners

`LiveSessionContracts.cs:34, 309, 351, 401`. Startup assertions/timeouts occur before an acquired session reaches `await using`; the first setup waits on a test barrier without a parent cancellation source. The overflow test's finally only releases its callback barrier. If its expected overflow fails to happen, the owner remains alive. The cleanup-failure test also lacks fallback shutdown if acquiring the first frame fails.

Establish cancellation/cleanup immediately when startup is launched, release barriers in finally, and drain startup or stop the acquired owner. Preserve the primary assertion failure when cleanup also fails. Follow `docs/agents/verification.md`'s explicit-lifetime and first-failure requirements; no parallel test framework is needed.

## Spec correctness

### P2 — Revoke input authorization when cleanup replaces the displayed geometry

`PreviewRenderSession.cs:252, 561–562`. Read frame A without acknowledging it, change the fixture layout, and lose then restore focus. Cleanup projects the changed layout, but `_currentFrame` remains A while backpressure prevents publishing a replacement. Pointer input carrying A's token passes the gate and hits new, unseen geometry.

Invalidate input authorization when cleanup replaces the scene. Retain the separate in-flight transport token so A can still be acknowledged and release the slot. Add an attributable target test with moved controls and focus loss/regain while A is held; an old token must not invoke the unseen target. This implements the integration contract's rule that stale frames are display-only.

### P2 — Reconcile scene freshness between synthetic releases

`PreviewRenderSession.cs:579–600`. Hold two keys on a focused behavior whose first key-up changes layout, then lose focus. Cleanup projects once. The first Up invalidates the scene; `InputRouter.DispatchKey` rejects the second as `StaleScene` (`InputRouter.cs:431–438, 1716–1741`). Cleanup ignores the returned status and clears `_pressedKeys`, reporting success while author state retains a held key. Pointer cleanup can also invalidate the scene before the key loop.

Reconcile between releases and handle rejected releases explicitly. Assert both independently recorded key-up deliveries and cleared author state when the first callback changes layout; checking only the host counter would miss the defect. Preserve stale external-input rejection rather than broadly replaying old commands.

### P2 — Initialize the authored theme factory with the requested appearance

`PreviewRenderSession.cs:72, 385–396`. The live host passes `defaults.ThemeFactory` unchanged. `Application.RunCore` first invokes it with Light (`Application.cs:322`). Setting `Theme.Appearance` only queues reactive work (`PropertyTheme.cs:288–299`); synchronous setup/root creation runs before the first flush. A dark/high-contrast request can capture Light theme values, or fail immediately if its factory rejects Light.

Use the one-shot path's existing presentation-bound factory pattern (`PreviewWorker.cs:96`). Flushing before setup alone does not prevent the incorrect initial factory invocation. Extend the live fixture with a factory that requires the requested dark/high-contrast appearance and an observed setup token value. This preserves the documented before-setup appearance contract at `docs/plans/native-preview-panel.md:113`.

## Performance and compiler disposition

One retained surface/renderer, one in-flight frame and event-driven waiting are appropriate bounded seams. The source review found no reason to replace them with a new generic hosting framework. Short whole-process CPU samples are diagnostics only; retain separate encoding, projection/shaping, raster and native-presentation measurements.

The public compiler-adapter experiment in `artifacts/preview232-adapter-experiment.md` fails saved-source parity before unsaved inputs: integrated suppressor behavior and generated-source checksum fidelity differ, and emit-only diagnostics invalidate analysis-only acceptance. No maintainable equivalent public adapter has been established. Continue saved-source live work; do not disable SDK analyzers, weaken warning policy, rewrite generated sources or use compiler internals to call the probe successful. A supported compiler input-substitution hook is a possible future dependency, not an available proven solution. Any maintained frontend is a separate ownership/scope decision.

Three standards/lifecycle findings and three spec findings remain at the reviewed hashes. These are source-confirmed failure paths, not newly executed reproductions. Correct and verify them before live transport integration; preserve the earlier passing evidence and add focused coverage at the existing host boundary.

## Follow-up after shutdown-context correction

The coordinator re-read the in-place correction. `session.EnterContext()` now encloses the entire host shutdown finally block, including input cleanup. The existing failure-cleanup test compares attachment, Cancel and key-Up synchronization contexts and the owner thread. The shutdown-context finding is resolved in source; Implementation reports the independent pre-fix failure, 26/26 post-fix tests and warning-clean build under the renderer probe's `live-context-before-results/context-before.trx` and `live-context-after-results/context-after.trx`. Those tests were not rerun by this review.

Current reviewed hashes: PreviewRenderSession.cs `1BCD081277FB1399BE77D79528A5D7F5A418CD4FEEB1F065F1EA83645DB5A8D4`; LiveSessionContracts.cs `33DCC60F62DB45D3C02C41EB0B906C62FC0816FEFEFB1D6CE5E608062DC80656`.

The other five findings remain unchanged at those hashes: pre-cancelled startup, failure-safe test cleanup, stale-frame input authorization after cleanup reprojection, rejected synthetic key releases, and initial theme-factory appearance. Their cited source locations remain valid.

## Final focused recheck — all six findings resolved

October 4, 2026 (America/Chicago). This section supersedes the open dispositions above. The coordinator rechecked only the six tracked findings against the frozen corrections; no fresh broad audit, builds, tests or UI runs were performed.

All six files match `live-review-fixed-source-identities.json` under `C:/Users/richa/AppData/Local/Temp/lucent-preview232-render-a66340fa`. The two corrected-file SHA-256 values are:

- PreviewRenderSession.cs: `13DAECCAC34865AFE526382136106EE013E3AB0500658794008B7700DCF5559C`
- LiveSessionContracts.cs: `2EC582CBD45D3D33261FC61D28022963F18AC49A1F413FB77A23AE05BC5D7DD8`

| Tracked finding | Source correction and focused assertion | Disposition |
| --- | --- | --- |
| Cancelled startup invokes author code | `StartAsync:98` rejects pre-cancelled requests before owner allocation; owner-side checks guard binding, initial theme, session start and root construction (`RunOwner:330–344`, host `:408`). `PreCancelledStartupInvokesNeitherSetupNorRoot` independently observes both callback counts as zero. Existing cancellation-during-startup cleanup remains covered. | Resolved |
| Shutdown callbacks lack the session context | The lease at host `:482` encloses cancellation, cleanup, presentation shutdown and scene disposal. The existing failure-cleanup test checks attachment/Cancel/key-Up context identity and owner thread while preserving aggregated failures. | Resolved |
| Test assertion failures abandon live owners | The test helper registers linked cancellation and the startup task before awaiting it (`LiveSessionContracts.cs:16–47`). `TestCleanup:53–85` releases registered barriers, cancels, drains startup or stops the session, disposes the source and preserves primary assertions. The initial setup and overflow barriers are registered; expected terminal failures remain explicitly asserted in their tests. | Resolved |
| Old frame authorizes unpublished geometry | `Project:544` and `CleanupInput:583` revoke `_currentFrame`; input rejects null authorization. `_inFlight` remains separate for acknowledgment. The moved-target test checks old-token rejection, no target invocation, successful old-frame acknowledgment, replacement pixels and one invocation with the new token. | Resolved |
| Cleanup loses synthetic key releases | Every synthetic release passes through `ReleaseInput:637–654`, which projects first, accepts only delivered input and limits stale reconciliation to three attempts. Other rejections become explicit failures; cleanup aggregates failures while attempting remaining releases. External input is not replayed. The public structural-change fixture observes both key-up deliveries and an empty author-held-key set. | Resolved |
| Initial theme factory receives Light for another requested appearance | The constructor binds `defaults.ThemeFactory(appearance)` at `:66–77`. The regression rejects any other factory argument and independently reads the requested token during synchronous setup. | Resolved |

The coordinator inspected the retained evidence rather than rerunning it:

- `live-review-before-results/review-before.trx` records three independent failures: cancelled setup ran once, the factory received Light/Normal, and stale input was accepted.
- `live-review-before-key-structural-results/review-before-key-structural.trx` records one delivered key-up where two were expected. The earlier lazy-property case is not used as sensitivity evidence.
- `live-review-after-second-results/review-after-second.trx` records 30 executed, 30 passed, zero failed and zero skipped/not executed. This includes 15 live and 15 existing hosting contracts.
- `live-review-after-build-second.log` records a successful build with zero warnings/errors; `live-review-final-format-check.log` records six checked files. The worker's whitespace-check result is retained in `LIVE-REVIEW-CORRECTIONS.md`.

**Conclusion:** no unresolved findings remain within this six-finding recheck. The internal host can proceed to integration on these source identities. This is not acceptance of live transport/editor wiring, unsaved compilation, NativeAOT/physical interaction, or long-lived performance; those boundaries were outside this recheck.
