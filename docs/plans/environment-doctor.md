# Environment doctor and onboarding

Execution: [#248](https://github.com/RichiCoder1/lucent/issues/248), following the
[onboarding design](https://github.com/RichiCoder1/lucent/issues/242) and
[editor tooling lifecycle](editor-tooling-lifecycle.md). Implementation is underway;
this document does not claim a published doctor or a completed onboarding journey.

The current implementation slice covers static inspection, host/runtime/installed
SDK preflight, a standard editor walkthrough and setup status, explicit project
selection, workspace/configuration cancellation, and report review/copy. The local
.NET tool and the private VSIX static doctor use the same result schema. The editor
also has an explicit trusted-project report backed by the existing requirements
producer, including authoritative target/tool identity and fresh active-evidence
reuse. Standalone CLI parity is implemented and has a real development-project
proof. Optional feed diagnostics and native-prerequisite observations still belong
to #248; the fresh installed-editor journey remains #249.

## Boundaries

`Lucent.Tools` supplies the local .NET tool command `lucent doctor`. Default checks
are static, offline and read-only. They inspect configuration and installed tooling;
they do not restore, build, evaluate a project, install anything, change settings,
or contact a package feed. Native prerequisites and feed access remain explicitly
not checked until the relevant check is performed. A missing native compiler must
not label working editing or managed development as broken.

Host discovery must not run an executable from the inspected workspace or execute
an SDK selected by its `global.json`. In particular, static inspection must not
run a workspace-selected SDK merely to ask its version. Read SDK policy as data,
inspect host-provided SDK/runtime inventories, and distinguish installed versions
from proven SDK selection. Do not reproduce the SDK's complete roll-forward rules.

Explicit trusted-project checks reuse the verified server's existing
`--project-requirements` operation. They report their broader scope and never
establish a separate project-file interpretation. Optional online checks report
reachability, authentication and unavailable releases separately; absence of an
online check is not proof of feed access. Neither opt-in check performs restore
or installation.

## Results and presentation

The static versioned result format drives CLI text, JSON and VS Code presentation. Checks
carry a stable code, severity, capability, status, scope, observed evidence,
expected requirement and remedy. Capability readiness is independent: editor,
managed build/run, package access and native publication can differ. Exit states
distinguish required-check failure from an unavailable doctor invocation; optional
checks that did not run are not promoted to passes.

Reports exclude document contents and credential values. Home-directory prefixes
and credential-bearing URLs are redacted before display/export. Export is explicit
and presents the report for review before copying or saving it.

The editor's explicit project report is a separate bounded
`trusted-project-doctor` result. It reports requirements evaluation and verified
tool delivery, while leaving semantic readiness, build, native publication and
feed access unverified. It contains no project paths, raw exception messages or
credential fields. A server override remains explicitly unauthenticated as a
release artifact even when its compiler and protocol match. Cache inspection
creates no directories. Canceling the report leaves an existing language server
running; stale success and failure results are discarded.

The extension performs a small host/runtime preflight before attempting to run
the .NET doctor. This handles the missing-runtime case that the managed tool cannot
report about itself. The full doctor payload must be verified before execution,
using the same source-bound managed-tool inventory principles as the cache helper.
It runs on the workspace host, with bounded output, cancellation and a stale-result
guard when the project, workspace or active operation changes.

Use VS Code's walkthrough, status bar, Quick Pick, Output and Problems surfaces.
Avoid a separate webview. Native theme and accessibility defaults apply. The
walkthrough is an explicit entry point after installation, not a recurring modal.
Only applicable actions are offered, and one newly blocking condition produces
one notification. A status item remains available for details and recovery.

| State | Primary action |
| --- | --- |
| Untrusted | Manage Workspace Trust; retain syntax support |
| No selected project | Select a project within its workspace folder |
| Missing host/SDK/runtime | Show the required tooling and official setup guidance |
| Compiler or protocol mismatch | Install/import matching tools or inspect an explicit override |
| Offline with no compatible cache | Import an approved archive or retry acquisition |
| Installing | Cancel; retain the current server and generation |
| Active | Show current project/tool identity and environment details |
| Server stopped | Explicit restart and inspect the reported failure |
| Native prerequisites unavailable | Show native-publish setup; preserve other working capabilities |

## Delivery and checks

The CLI is a standard versioned .NET tool package. Its release inventory and the
extension's diagnostic payload must bind the exact verified bytes. Package pins,
global tool configuration and operating-system prerequisites remain outside
automatic update ownership. The fresh-consumer install/edit/debug/run/publish
journey is tracked separately in [#249](https://github.com/RichiCoder1/lucent/issues/249).

The remaining #248 work proceeds in this order:

1. Add an optional NuGet adapter for effective configuration and bounded online
   feed observations. Use official NuGet configuration/protocol APIs for hierarchy,
   disabled sources and source mapping. Keep these dependencies and network work
   outside the static doctor and normal editor activation. Validate authentication,
   redirects, cancellation and unavailable feeds with local fixtures; report neither
   credentials nor raw URLs. A probe proves only its own result, not a future restore.
2. Add optional Windows native-toolchain observations through the installed Visual
   Studio discovery mechanism. Distinguish missing components from an inconclusive
   observation, and keep native readiness independent from managed editing. Actual
   NativeAOT consumer execution remains the end-to-end proof.

Each adapter must retain cancellation and workspace-generation checks, present its
scope explicitly, and leave unperformed checks as `notChecked`. These are remaining
implementation tasks, not capabilities established by the static or editor slices.

Standalone CLI project inspection uses paired `--trusted-project <absolute.csproj>`
and `--server <absolute.server.dll>` flags. It runs the existing requirements
producer, verifies tool/input identity, and reports the producer's effective target.
Explicit server overrides remain unauthenticated as release artifacts. The default
static invocation is unchanged. Thirty-two Tools contracts cover classification,
bounded output, privacy and cancellation, including forced producer termination.
A real invocation against a coherent development server passes while leaving the
fixture's project, NuGet configuration and restore assets unchanged. Offline restore
was a separate test-preparation step. Older-server, stale-restore and mixed-compiler
failures are retained; this does not claim official publication or semantic readiness.

Use focused tests for capability classification, malformed/missing configuration,
bounded process output, cancellation, stale results and seeded-secret redaction.
Compare workspace/settings snapshots around default checks and include a real
isolated invocation: fake probes alone cannot establish process side effects.
Keep standard editor integration tests for trust gating, project selection,
deduplicated blockers, optional-native failure and disposal. Desktop/editor
walkthroughs remain separate from these non-interactive contracts.
