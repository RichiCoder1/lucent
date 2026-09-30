# Navigation restoration and Windows activation

Status: implementation plan for [#205](https://github.com/RichiCoder1/lucent/issues/205),
[#221](https://github.com/RichiCoder1/lucent/issues/221), and
[#222](https://github.com/RichiCoder1/lucent/issues/222), September 29, 2026.
This document selects defaults; it does not claim implementation or execution evidence.
Finish the current correctness closeout before runtime work. Desktop/focus tests remain
paused while the owner is gaming; build and console-only work can proceed.

[#213 is closed](https://github.com/RichiCoder1/lucent/issues/213#issuecomment-5661455034)
with package-only managed/NativeAOT and application evidence. Its
[delivery record](context-navigation-execution.md) is the prerequisite, together with
[ADR 0009](../adr/0009-context-injection-and-navigation.md) and the current navigation
contracts. The private design snapshots named by the issues are absent from this
checkout. This is a new execution plan derived from public issues and current source,
not publication of those snapshots.

## Ownership and interface

Keep three modules: portable restoration in Core; application-owned persistence and
route-admission policy; an optional Windows activation adapter. Core continues to own
canonical parsing, matching, preparation, staging, publication and retirement. Windows
does not discover routes or create navigation sessions. Durable drafts, document state,
credentials, service lifetimes and editor undo remain outside restoration.

Proposed Core surface: bounded `Capture` and `Decode` operations returning immutable
data/finite results, plus `NavigationSession.Restore(validatedPlan)` returning the
existing `NavigationOperation`. Capture takes explicit persistence policy and optional
statically registered state codecs. Decode binds the plan to the exact current route
table and application scope key. No generic object serializer, reflection discovery,
file paths, platform types or persistence timers enter Core.

Restoration is off by default. Enabling it requires an application scope key, an
explicit allowlist/predicate for persistable routes, and one safe fallback route.
The scope key identifies the application, workspace/account boundary and application
route-contract revision. A different scope key invalidates the whole snapshot.
Applications must not put credentials in that key or persist credential-bearing routes.

## Version 1 format and bounds

Use strict UTF-8 JSON with a fixed schema identifier and integer version. Implement
the envelope using explicit readers/writers or source-generated metadata. Ship the
location mode first; journal mode is a second implementation step under #221 using
the same versioned envelope.

```json
{"schema":"lucent.navigation","version":1,"scope":"app-workspace-routes-v1","mode":"location","active":{"definition":"item","location":"/items/1"}}
```

Journal mode replaces `active` with `activeKey` and an ordered `entries` array. Each
entry contains `key`, `definition`, `location`, and optional typed `state`. Keys are
snapshot-local integers from 1 through 64, unique within the envelope. Do not serialize runtime
journal IDs: allocate fresh IDs on import and explicitly map validated snapshot keys
to them, including interaction state. IDs remain stable during later Back/Forward
within that runtime session. No cross-process identity guarantee is introduced.

| Limit | Version 1 decision |
| --- | --- |
| Whole payload | 256 KiB maximum, enforced while reading and writing; reject excess before allocating an unbounded document |
| JSON | Maximum depth 8; no comments, trailing commas, duplicate properties or trailing data; reject unknown envelope fields and unknown modes/versions |
| Retained history | Default 32 entries, hard maximum 64 and never greater than the destination session capacity |
| Location | Exact `RouteLocation` canonical text; current table limits apply, including its default 2,048 UTF-8 bytes |
| Scope and definition keys | Nonblank, maximum 128 UTF-8 bytes each |
| State | Maximum 4 KiB per entry in total; only explicitly registered codec identity/version pairs |
| Interaction codec | At most 16 viewport targets; target IDs use the existing 128-byte bound; finite nonnegative logical offsets only |

Capture a contiguous window around the active entry, favoring preceding entries,
then fill remaining capacity with forward entries. Filter disallowed entries while
preserving order. If the current route is not persistable, return `NoSnapshot`; the
application clears its previous navigation snapshot instead of resuming an older
private route next launch. A payload exceeding the byte budget returns `TooLarge`,
without a partial write or invented route truncation.

Capture on the owner thread outside staging, publication and retirement; the ordinary
trigger is idle after retirement. If close catches pending asynchronous preparation,
capture the committed branch without persisting or canceling that pending intent.
Take a fresh capture
of the active entry's registered interaction targets; its previously retained state
may predate current scrolling/focus. Persist only committed entries, never pending
intent. Applications coalesce asynchronous writes after committed navigation and
capture once more before normal close disposes the outlet. Use atomic replacement
and explicit write generations so an older completion cannot overwrite newer state.
Navigation persistence failure is recoverable and cannot cancel an accepted document
write or silently claim a successful save. Storage scheduling belongs to the application.

## Validation, replay and partial drops

Decode without mutating a session. Reject malformed/truncated JSON, wrong scope,
unknown version/mode, duplicate keys, invalid active references, over-count/over-size
input and malformed envelope structure as a whole. Reparse each location with Core,
require canonical text equality, match against the current table, and require the
matched definition identity to equal the stored identity. This prevents an old URI
from silently becoming a different route. Applications bump the scope's route-contract
revision when the same definition ID changes meaning incompatibly.

For a structurally valid journal, drop inactive entries with stale definitions,
unmatched/invalid locations or pure application route-admission policy rejection. Resource
availability and authorization remain guard responsibilities when an entry is visited.
Keep surviving order and
recompute the active index. If the active entry is invalid, reject the whole replay
and navigate to the safe fallback; never select an arbitrary neighboring document.
An unknown state codec/version or invalid bounded state drops that entry's state,
not its otherwise valid route. An over-size entry/state rejects the envelope rather
than partially parsing an unbounded value. Report only finite reason codes and counts;
do not include raw URI, payload, query values, scope keys or state in diagnostics.

Restore is startup-only: the session must be empty, idle, and have its root outlet
attached. Do not feed persisted input through the constructor's `initialLocation`,
which initializes committed history before an outlet can run Enter preparation.
Use the existing `OnMounted` application hook to start replay after required services
and route participants exist. A second restore or restore into a populated session
returns an explicit invalid-state result without mutation.

Prepare and stage the active route through the normal transaction engine, then publish
the validated journal, active route, interaction state and tree atomically. Dormant
entries are validated metadata; do not mount them or run their guards at startup.
Their guards run on ordinary Back/Forward. Add a `Restoration` origin for application
policy; keep import as an internal journal plan rather than exposing arbitrary journal
mutation. A Stay/rejection preserves the empty session and triggers one ordinary
fallback navigation. A redirect follows existing bounded redirect handling but drops
the imported journal/state and commits the redirected target as a fresh single entry.
Unexpected staging/cleanup failures retain terminal semantics and do not attempt
fallback. A newer user/activation intent supersedes replay; it must not trigger an
obsolete replay fallback when the old operation completes.

Location mode restores fresh heading/useful-target focus and authored viewport defaults.
Journal mode optionally restores only the built-in interaction codec initially: stable
authored focus IDs and viewport offsets. After committed layout, resolve surviving
targets, clamp offsets to live extents, and use the existing heading/useful fallback
when targets disappeared. Never focus hidden, disabled or foreign-root targets. Further
state codecs must be closed, statically reachable and explicitly registered with their
own finite validation; page trees, arbitrary objects, cancellation tokens, service
instances and undo buffers are always excluded.

## Windows activation contract and delivery

Select Windows App SDK App Lifecycle for protocol activation and redirection, isolated
in an opt-in `Lucent.Platform.Windows.Activation` package. Existing Windows/Core/Hosting
consumers acquire no new deployment dependency. It supports the existing single-window
model and Windows 10 1809 or newer; the base host's current OS check is unchanged.
Record the dependency and notices in `CREDITS.md` before adopting it. Pin the exact
stable Windows App SDK/CsWinRT versions only after the first NativeAOT probe proves
the adapter against Lucent's pinned .NET SDK; general NativeAOT support is not proof
of this particular adapter. Microsoft documents
[AppInstance redirection](https://github.com/microsoft/WindowsAppSDK/blob/main/specs/AppLifecycle/Instancing/AppLifecycle%20SingleMulti-Instancing.md),
[protocol delivery](https://learn.microsoft.com/en-us/windows/apps/develop/launch/handle-uri-activation-dotnet),
[NativeAOT support](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-6),
and [OS requirements](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview).

The host envelope carries bounded raw URI text, `Launch`/`Protocol` kind, cold/redirected
delivery and provenance `UntrustedExternal`. Provenance describes transport, never
authentication; redirected requests remain untrusted. Maximum raw URI is 4,096 UTF-8
bytes. Preserve original text when extracting it from Windows arguments: prove that
projection/normalization cannot erase forbidden traversal syntax before validation.
Reject a delivery whose original representation cannot meet that contract.

The application explicitly configures one lower-case scheme and one ASCII authority
host, an instance key including application/channel identity, and a policy selecting
the existing session or rejecting the request. Accept only `scheme://host/path?query`:
reject user info, ports, fragments, missing/mismatched host, foreign schemes and opaque
URIs. Extract the escaped path/query without decoding or dot-segment normalization;
Core alone validates percent escapes, Unicode, traversal, separators and route values.
Never construct a shell command from a URI. Routes display content; delete/send/pay or
other consequential operations still require an independent explicit user action.
[#165](https://github.com/RichiCoder1/lucent/issues/165) remains the sole external launcher.

| Supported first delivery | Registration and deployment |
| --- | --- |
| Unpackaged win-x64 directory | Self-contained Windows App SDK runtime beside the NativeAOT application; explicit per-user install/uninstall operation calls protocol registration APIs with an absolute executable path. Ordinary startup never registers or repairs associations. |
| MSIX full-trust win-x64 desktop application | Self-contained runtime in the package; `windows.protocol` declaration in the package manifest. MSIX installation/removal owns association changes; do not invoke unpackaged registration APIs. |

These are distinct proof targets; success in one does not establish the other.
[Microsoft's deployment matrix](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview)
describes their different initialization requirements. The initial contract excludes
sparse/external-location packages, AppContainer/UWP, elevated redirection, cross-user
delivery, cross-package-instance merging, framework-dependent runtime installation,
and single-file packaging. Application signing identities and production protocol names
are installer inputs, not framework defaults. Test with isolated fixture identities.

## Startup, redirection and shutdown ordering

1. Before building an application session, starting services or creating an SDL window,
   initialize the selected activation runtime, capture cold arguments, attach the
   redirected-activation receiver, and claim the configured AppInstance key. Cover
   the receiver-subscription/claim race with a bounded startup inbox.
2. A secondary process redirects and awaits delivery with a five-second bound. On
   success it exits before initializing the Lucent application; delivery acknowledgement
   means received, not that route guards accepted navigation. On timeout/failure it
   reports a finite failure and exits, never opens a competing window. Do not repeatedly
   retransmit an indeterminate request. A later independent launch can claim ownership
   after the former owner exits.
3. The primary holds one latest validated protocol request plus one coalesced plain
   launch request before mounting. Superseded buffered requests complete explicitly;
   there is no unbounded activation queue. Copy arguments in the receiver and post
   application policy/navigation to the existing owner dispatcher.
4. At `OnMounted`, a policy-accepted cold protocol request takes precedence over
   restoration; skip imported history and navigate normally. A rejected cold request,
   including unmatched routes or a preparation veto, opens the safe fallback once;
   terminal failures retain terminal handling. A plain launch tries restoration, then fallback. Once
   running, warm protocol requests use ordinary latest-intent navigation and current
   Leave/Enter guards. A plain launch requests attention without adding history.
5. Navigation acceptance and foreground success are separate results. After successful
   activation commit, restore a minimized window and request foreground once when
   application policy permits. Windows may refuse; record the result and request taskbar
   attention, without synthetic input or repeated focus attempts. Rejected warm activation
   leaves route/focus intact. Preserve normal route focus reconciliation after layout.
6. During close preparation, reject new activation as `Closing`; do not bypass accepted
   writes or reopen the session. A declined close resumes acceptance. Unsubscribe,
   release the instance key and tear down activation runtime only after application
   shutdown; late callbacks cannot touch disposed owners. Startup failure releases
   ownership through the same cleanup path.

[Windows foreground rules](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)
permit denial even when documented preconditions hold. Tests must distinguish route
delivery, window attention and actual foreground acquisition.

## Implementation order and focused evidence

| Step | Existing issue | Required evidence |
| --- | --- | --- |
| Location codec and startup replay | #221 | Fixed expected JSON bytes plus independent hostile/truncated/unknown-version/scope/definition tests; bounded allocation; Enter guard invoked; fallback once; explicit activation wins; late replay cannot overwrite newer intent. |
| Journal import and interaction codec | #221 | Partial inactive drops, active rejection, duplicate keys, fresh runtime IDs, capacity/byte/depth limits, invalid state drop, atomic publication, Back/Forward guards, redirect behavior, missing targets and viewport clamping; current interaction capture after scrolling. |
| Application persistence integration | #221 | Fresh temporary storage, older async write completion rejected, atomic replace/failure recovery, nonpersistable active route clears prior snapshot, close/draft-write independence; generated typed routes and restoration in a package-only managed and NativeAOT console consumer. |
| Optional activation dependency probe | #222 | Locked dependency graph, warning-clean build, exact NativeAOT executable and required assets, cold argument extraction and raw-input fidelity, instance claim/redirection/cleanup without a visible window; both deployment artifacts prepared. Stop this adapter if the proof fails; do not add reflection fallbacks. |
| Host policy and delivery | #222 | Deterministic envelope/URI/queue/owner-thread/close tests; published process race proves only one session/window initialization, bounded redirect failure and recovery after owner exit. |
| Registered Windows proof | #222 | Separately install isolated unpackaged and MSIX fixtures in an authorized test environment; actual OS protocol launch cold/warm, hostile input, guard veto, normal launch attention, minimized/foreground-denied behavior, disposal and association cleanup. Run UI/focus cases only after the owner resumes desktop testing. |

Extend the existing `NavigationSessionContracts`, `RouteOutletContracts` and
`NavigationInteractionContracts` for their owned transitions. Keep byte-format behavior
in one restoration suite and Windows transport in its adapter suite. Reuse generated
route/package consumers; do not duplicate the full behavior matrix at each layer.
Record exact source/package identities and unexecuted delivery modes in the existing
issues. Update public authoring documentation and package/architecture inventories
when implementation lands. No new issue, production scheme, registration mutation or
multi-window policy is required to begin the scoped framework work.
