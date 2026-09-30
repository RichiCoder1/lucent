# Environment doctor and onboarding

Execution: [#248](https://github.com/RichiCoder1/lucent/issues/248), following the
[onboarding design](https://github.com/RichiCoder1/lucent/issues/242) and
[editor tooling lifecycle](editor-tooling-lifecycle.md). Static and trusted-project
inspection are published in 107.1. Native and feed additions pass development
delivery checks; official publication and the installed-editor journey are separate.

The current implementation slice covers static inspection, host/runtime/installed
SDK preflight, a standard editor walkthrough and setup status, explicit project
selection, workspace/configuration cancellation, and report review/copy. The local
.NET tool and the private VSIX static doctor use the same result schema. The editor
also has an explicit trusted-project report backed by the existing requirements
producer, including authoritative target/tool identity and fresh active-evidence
reuse. Standalone CLI parity is implemented and has a real development-project
proof. Explicit Windows native-prerequisite observations, feed configuration and
anonymous feed access pass focused contracts, installed-tool checks and independent
review. The complete development VSIX passes 83 release-set contracts. Official
publication remains pending for these additions; the installed-editor journey is #249.

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

The first feed adapter makes anonymous metadata observations only. It sends no
saved, environment, Windows, proxy or plugin credentials, and rejects credential-
bearing or query-bearing source/resource URLs. A reachable index and an advertised
exact version establish only the anonymous view. A 401 requires authentication;
a 403 is forbidden; a 404 may conceal a private release. Configured credential
validity and future restore readiness remain unverified. Richer authentication is
deferred to a separately scoped credential policy, not inferred from a source name.

NuGet 7.9.0's default loader can create missing files, including during a deletion
race. The optional adapter therefore owns a small version-pinned discovery step
for local Windows configuration paths. It holds existing files open without write
or delete sharing, then gives the fixed ordered path list to NuGet's immutable
parser. NuGet owns merging, clear semantics, disabled sources and package mapping.
Freshness compares both the ordered path set and bytes; a newly applicable config
invalidates the result. Unsupported hosts/paths are unavailable, not permission to
fall back to a mutating loader. NuGet also uses temporary lock files while parsing;
read-only here means no settings, workspace or package-cache mutation, not an
absence of process-owned temporary files. The helper owns a separate temporary
scratch directory and removes it after disposing settings, before reporting success.
Cleanup after forced termination is not claimed. Hard process supervision retains
the deadline even if an upstream lock operation does not honor cancellation.

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

The optional NuGet adapter and its delivery proof are implemented:

Use official NuGet configuration/protocol APIs for effective settings, disabled
sources and source mapping, with the pinned discovery boundary described above.
Keep these dependencies and network work outside the static doctor and normal
editor activation. Validate anonymous HTTP responses, redirects, cancellation and
unavailable feeds with local fixtures; report neither credentials nor raw URLs.
A probe proves only its own result, not a future restore.

Verification retains 50 Tools contracts, the 23-contract adapter checkpoint plus
two focused scratch/isolation follow-ups, and 110 editor contracts. A fresh local
tool installation passes static, native, offline feed and anonymous exact-version
checks without changing the consumer workspace or package cache. The complete
VSIX producer and 83 release-set contracts pass, including missing-notice and
undeclared-payload rejection. `artifacts/doctor-vsix-current.txt` points to retained
development evidence; these bytes are not an official release or a visual editor
walkthrough. Final independent review has no actionable findings.

Each adapter must retain cancellation and workspace-generation checks, present its
scope explicitly, and leave unperformed checks as `notChecked`.

Windows native inspection uses the fixed installed Visual Studio `vswhere` path,
not a workspace or PATH-selected executable. Bounded inventory/component queries
must identify the same complete, launchable instance with x64 C++ tools and a
Windows SDK. Missing components, unavailable discovery and an observed installation
are distinct results. Actual NativeAOT publication remains `notChecked`. Thirteen
focused native/CLI contracts, the editor suite and a real installed-tool invocation
pass; independent review found no blockers in that slice. These checks do not claim
a newly published tool or installed-extension walkthrough.

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
