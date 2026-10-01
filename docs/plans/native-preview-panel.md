# Native preview panel

Issue [#231](https://github.com/RichiCoder1/lucent/issues/231), following the
[orchestration boundary](native-preview-orchestration.md). The implementation and
focused verification are recorded below; external distribution remains #233.

## User experience

The preview opens beside source, with the last accepted Skia image and an explicit
status. A compact toolbar selects a registered scenario, logical size, device
scale, light/dark appearance, contrast and fixture density. Zoom changes only the
editor display. Reset starts a fresh scenario and clock. Width and height are
applied together, avoiding intermediate builds for partially typed values.

Scenario defaults remain the starting point. Presentation changes are local to
the preview session and never rewrite the application or workspace settings.
Density is fixture data: it affects components only where the authored fixture
uses it. The panel does not invent a global Core density setting.

Build errors keep the previous image visibly stale. Attributable compiler
diagnostics can open their authored source locations. All images remain
noninteractive in this slice; pointer, keyboard and committed-text forwarding
belong to #232. Native window chrome, UIA and IME parity are not claimed.

## Ownership and protocol

The coordinator continues to serialize builds and supervise trusted code. A
catalog request runs the explicit development executable and returns bounded
metadata from its registered catalog. It invokes no scenario setup, root or
theme factory. Constructing the executable's catalog still executes trusted
author code and therefore uses the same trust, freshness and process ownership
checks as rendering. There is no reflection scan or production startup.

Capture uses a fresh worker and one effective immutable presentation. Both the
host and the fixture setup context see that presentation; changing host options
alone would leave fixture density and viewport inconsistent. Catalog defaults
remain immutable. Culture, initial time and the authored theme factory remain
scenario-owned. The worker echoes its effective presentation alongside the
existing session, generation, request, input, artifact and frame identities.

The development protocol advances as one coherent version across the C# worker,
JavaScript reader and shared fixtures. Unreleased older worker versions are
rejected explicitly. Bounded dimensions, aggregate pixel count, encoded bytes,
catalog count and string lengths are checked before allocation or admission.
Zoom never changes renderer scale or protocol dimensions.

Worker protocol v2 uses explicit `preview-catalog-request`,
`preview-catalog-result`, `preview-capture-request` and `preview-frame-result`
kinds. Catalog results contain at most 64 scenarios and fit within 64 KiB.
Logical extents are 1–8192, scale and density are 0.25–4, physical dimensions
are at most 8192 each, and their product is at most 16,777,216 pixels. PNG output
is capped at 32 MiB. Physical extents use the renderer's binary32 arithmetic.
Unsupported descriptor defaults produce a bounded failure before capture.
Build and supervisor protocols retain their independent version 1 contracts.

## Panel lifecycle and message boundary

The extension owns the selected configuration and current status. The webview
uses a restrictive CSP, a fresh script nonce, no command URIs and no general
filesystem access. Authored labels and diagnostics are text, not executable HTML.
Incoming messages use a small action allowlist, exact fields, bounded values and
the current panel identity. Diagnostic actions also require the current display
revision and refer to extension-owned entries;
the webview cannot supply an arbitrary URI to open.

Frame delivery has one unacknowledged message and one replaceable pending state.
Acknowledgment includes the panel and delivery identities. Old acknowledgments
cannot release a newer delivery. A missing acknowledgment suspends delivery
instead of accumulating images. Renderer results still require independent byte,
hash, dimensions and correlation checks before they reach this transport.
Stop remains available for the current panel while newer display state is pending.
Retained catalog choices may request a fresh build after failure; the new build
must rediscover and verify the chosen scenario. This does not permit interaction
with stale pixels or execution of an old artifact.

Hiding the panel cancels active preview work and pauses rebuilds and frame
delivery. Source changes can mark retained pixels stale while hidden. Becoming
visible resumes only a preview that the user had left running; explicit Stop
stays stopped. Closing disposes watchers, timers, panel transport and process
ownership. The panel does not retain a hidden webview context or poll while idle.

## Verification

Extend the existing coverage owners rather than repeating their matrices:

- Protocol and compiled worker: catalog does not run fixtures; effective
  presentation reaches setup and actual Skia output; malformed or excessive
  requests fail; cleanup still precedes capture success.
- Editor coordinator/runtime: catalog and capture share verified build identity,
  cancellation, stale rejection and cleanup; mapped diagnostics remain data.
- Panel/controller: action validation, zoom versus renderer scale, one-flight
  transport, stale acknowledgments, hide/show, explicit Stop, close/reopen and
  failed-build recovery.
- One installed development-editor walkthrough: controls change real compiled
  pixels, stale failure remains visible, diagnostic opens authored source, and
  hide/close releases execution. Close the editor and Computer Use afterward.

VS Code's [webview lifecycle and security guidance](https://code.visualstudio.com/api/extension-guides/webview)
and [message delivery contract](https://code.visualstudio.com/api/references/vscode-api#Webview)
inform the host boundary. A successful `postMessage` alone does not prove the
webview received or displayed the frame.

## Implementation evidence — October 1, 2026

The development panel and worker v2 implement the controls above. All 176 editor
checks pass, including bounded transport, stale acknowledgments, hidden/closed
panels, diagnostic generations and scenario recovery. The compiled worker suite
passes 15 contracts and the existing scenario suite passes 18. A selected
appearance now reaches the authored theme factory before setup, through a
per-capture presentation; no Core theme behavior changed. Catalog discovery does
not invoke setup, root or theme callbacks. Distinct fixture titles make the
scenario picker usable.

Worker source hashes, warning-clean builds, process exit and independent PNG
checks are retained under `C:\Users\richa\AppData\Local\Temp\lucent-preview-worker231`.
The production JavaScript coordinator/runtime, freshly published build and
Windows supervisor tools, real SDK and compiled worker passed together under
`C:\Users\richa\AppData\Local\Temp\lucent-preview231-pipeline-1fcf8226d710417585dae32a1ecb2902`.
The requested 400-by-300 logical viewport at 1.5 scale produced a 600-by-450 PNG
with dark/high contrast and density 0.75, four catalog entries and independently
checked bytes. All 65 source lock files retained their original hashes and
existence, and the successful generation storage was empty after release.

Adversarial review found three lifecycle gaps: Refresh after panel close,
execution after removing the workspace folder, and old diagnostic locations
receiving a new generation. The fixes require an existing panel and current
workspace membership, clear retained selection when either owner disappears,
and clear mapped diagnostics synchronously when a generation changes. Focused
regressions reproduce the close and stale-diagnostic defects before correction;
the review recheck found no remaining actionable issue in those corrections.

Computer Use in the already trusted onboarding fixture displayed real compiled
light and dark cards. Display zoom changed pixels on screen without rebuilding;
the Apply action changed viewport width, device scale and fixture density in the
compiled result. A deliberate `CS0103` retained the previous image as stale;
its diagnostic button opened the authored fixture at line 3, column 37. Hiding
the preview released active generation storage. This source development client
uses the existing official 110.1 language-server override and is not an
authenticated new VSIX release. The walkthrough also exposed the image's
`display` rule overriding its initial `hidden` attribute; the explicit hidden
rule fixes the empty placeholder. The pipeline directory's `walkthrough.md`
records final editor observations and cleanup separately from automated proof.
The final reload confirmed the corrected empty placeholder and Ready language
services. Stop and closing during fresh builds left no active generation
directories. The owned editor was closed and Computer Use reset after verification.

Presentation changes still compile a fresh generation and start one-shot
workers. These checks establish behavior and ownership, not an incremental
latency target. Unsaved buffers, persistent rendering and current-generation
input remain #232; production native chrome, UIA and IME parity are unclaimed.
