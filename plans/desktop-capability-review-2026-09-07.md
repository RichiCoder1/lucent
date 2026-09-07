# Desktop capability review — 2026-09-07

## Scope and evidence

Fresh independent read-only source review after desktop feedback. Lucent: `7b67b4978c84a96da3804f3a95ef99f9b3bf2aa1`; Light Notes: `590d94139279311ff738cef6d9a0c89133f8cb8c`. Evidence below is source inspection, not an interactive reproduction or a newly passing test result.

Reviewed repository guidance, CONTEXT.md, architecture, language, testing and verification documents, ADRs 0001–0005, the input/text/scene/Windows host paths, the Light Notes workspace and authored shell, and relevant existing test sources. Successfully read the improve audit playbook, including its Finding format and correctness, performance, test coverage, architecture, DX and direction sections. Source and documentation were treated as review data. No builds, tests, installations, focus changes, desktop windows, commits or pushes were performed. This report is the only file produced; existing plan indexes and other workspace changes are outside its scope.

Paths prefixed `Lucent/` resolve under `D:/src/richicoder1/lucent/`; `LightNotes/` resolves under `D:/src/richicoder1/light-notes/`.

This is a focused desktop capability review, not a full compiler, security, storage engine, dependency or accessibility certification audit. The older Avalonia work is not an implementation reference. Preserve Windows-first, NativeAOT-compatible .NET 10, portable Core, CPU Skia/SDL presentation and primary `.lui` authoring. The existing verification policy is risk-based pre-release verification; this report does not replace it with release gates.

## Accepted product decisions

Updated after the user's design discussion on 2026-09-07:

1. Preserve invalid or incomplete drafts and allow navigation. Show validation inline; invalid URLs disable Open only, not unrelated actions. Ordinary intermediate input is not a persistence failure.
2. Persist recoverable drafts locally across normal close/reopen, separately from the last successfully saved valid note. Provide an explicit **Discard draft** action that removes the pending/recovery draft and restores that valid note. Discard does not undo earlier successful autosaves or introduce note history. Draft durability and actual storage failures must be reported truthfully.
3. Restore each collection's selected note, scroll position and search query. Switch immediately using cached records; refresh independently without clearing the view. Filtering the collection does not replace the current editor or discard its draft when that note is hidden.
4. First context-menu slice: a shared accessible framework foundation, standard text-editing menus and note-row menus, authored through `.lui` and commands with pointer and keyboard invocation.
5. Right-click targets the row's commands without changing the selected/open note. An explicit Open command can navigate. Menu target, keyboard focus and active document are distinct identities; the menu target needs a visible cue that does not imply a second selected note.
6. Menus **must be able to extend outside the application window in the first slice**. Client-bound overlays are not an acceptable temporary product constraint. Use Lucent-rendered popup windows, preserving `.lui` composition and theme customization. Windows owns popup hosting, placement, DPI and native lifetime; portable menu commands, invocation, focus and semantics remain framework-owned. This presentation choice is accepted; platform API feasibility still requires implementation investigation.
7. UI/focus testing is authorized again for tonight. A Computer Use connection attempt and reset both failed before any app interaction. No new interactive evidence was obtained. The earlier pause references in the original review below describe the conditions under which that source review was performed, not the current authorization.


## Future platform integration

The user explicitly deferred native Windows menu presentation to a future opt-in framework layer covering menus, scrollbars and other platform-sensitive behaviors. This work should provide platform-appropriate styles and defaults while preserving Lucent's customizable presentation. Native menu hosting is a behavior/presentation adapter, not merely a visual style; the future layer may combine theme defaults with platform adapters.

The current menu contract should keep command identity, availability, invocation target and semantics independent of its Lucent popup renderer. Consumers should not need to rewrite ordinary command definitions to adopt a future native presentation. Do not implement a second presenter or invent a general platform-profile API in this slice. Assess any presentation limits for custom `.lui` menu content when the native option is actually designed.

The product design round is settled: complete pointer editing and controlled selection, retain durable drafts with explicit discard, preserve collection continuity, investigate live sizing, add portable cursor intent, and deliver accessible Lucent popup menus outside the owner window. Implementation/API details and focused verification follow the dependency order below.

## Findings

### CORRECTNESS-01 — Make application-controlled selection authoritative

- **Evidence:** `Lucent/src/Lucent.Core/InputBehaviors.cs:101–103` selects the retained item before invoking the application callback. `Composition.cs:317–321` selects only the target when there is no semantic List ancestor. `Components.cs:319–327` copies the application predicate into mutable control state through an effect. `LightNotes/src/LightNotes/Navigation.lui:67–68` mounts Inbox and Archive Selectables beneath a Column, with mutually exclusive predicates.
- **Impact:** Clicking Archive immediately selects its control. If navigation declines while busy or fails while saving, `ShowArchived` stays unchanged, so its effect has no changed dependency to restore Archive to false. Inbox remains selected. This explains a concrete path to the reported double highlight; it is an ownership defect rather than merely styling.
- **Effort:** M, including mounted-shell regression tests.
- **Risk:** MED; selection semantics, UIA and uncontrolled selection need clear separation.
- **Confidence:** HIGH for the mechanism; no interactive reproduction performed.
- **Fix sketch:** Separate selection requests from committed application selection for controlled Selectables. Cover rejected, delayed and failed callbacks with both semantic and pointer invocation, including standalone and list-contained controls.

### CORRECTNESS-02 — Retain invalid drafts independently from navigation

- **Evidence:** `LightNotes/src/LightNotes/NoteWorkspace.cs:296`, `323`, `385` and `590` require `SaveCurrentAsync` before selecting a note, changing collections, capturing or archiving. `516–528` throws on an incomplete URL or empty title. `563–574` rethrows the save failure; `650–665` replaces the one selected draft/session document set.
- **Impact:** Ordinary intermediate editing states block unrelated actions. Retry cannot make an invalid URL valid. The current model protects the draft from loss but has no independent per-document draft ownership for navigating away.
- **Effort:** L, including failure/recovery and shutdown checks.
- **Risk:** HIGH; draft identity, ordering, accepted writes and recovery interact.
- **Confidence:** HIGH.
- **Fix sketch:** Implement the accepted retained-draft policy, separating inline validation from storage failures. Persist invalid drafts across restart as accepted above; do not silently coerce/discard input or claim durability before the recovery write succeeds. Keep accepted writes application-owned and test navigation away/back, correction, retry and close.

### CAPABILITY-01 — Complete single-line pointer editing

- **Evidence:** `Lucent/src/Lucent.Core/TextField.cs:535–562` focuses all fields but only hit-tests/captures when `state.IsMultiline`; `567–569` restricts drag extension similarly. `tests/Lucent.Core.Tests/TextFieldContracts.cs:49–62` explicitly expects focus without caret hit testing. `TextAreaContracts.cs:57` covers multiline placement and dragging.
- **Impact:** Capture, search, title and URL fields cannot position the caret or drag-select using the mouse.
- **Effort:** M.
- **Risk:** MED; grapheme boundaries, preedit cancellation, pointer capture and horizontal scrolling must agree.
- **Confidence:** HIGH.
- **Fix sketch:** Use the shared paragraph hit-test/selection contract for both editor modes, preserving mode-specific newline behavior. Cover padding, overflow, empty text and grapheme clusters. The code calls `route.Focus()`; this finding does not establish the separate reported plain-click focus loss.

### CORRECTNESS-03 — Use text metrics for single-line caret and selection geometry

- **Evidence:** `Lucent/src/Lucent.Core/SceneLayout.cs:839–848` uses `inner.Y` and `inner.Height` for NoWrap caret geometry; `790–801` does the same for selection. The wrapped branch uses `text.CaretBounds` at `856`. `TextFieldContracts.cs:75–79` checks inner-box geometry rather than a text-metric invariant.
- **Impact:** Tall controls produce tall carets and selections regardless of font metrics, and vertical text alignment can disagree with caret placement. This explains the oversized single-line caret.
- **Effort:** S–M.
- **Risk:** MED; the geometry also feeds IME and accessibility.
- **Confidence:** HIGH.
- **Fix sketch:** Share text-derived geometry across editor modes with an explicit empty-text line-height rule. Verify varying font/control heights and alignment, not only consistency between paint and exported rectangles.

### PERFORMANCE-01 — Investigate presentation during native live sizing

- **Evidence:** `Lucent/src/Lucent.Platform.Windows/WindowsBootstrap.cs:154–175` waits/polls and drains events before projection/presentation at `229–284`; resize schedules a later frame at `369–374`. No live-sizing event-watch/message-hook render path was found. `LightNotes/tests/LightNotes.Desktop.Tests/PublishedResponsiveTests.cs:206–226` calls `SetWindowPos` and waits for settled size.
- **Impact:** Existing resize tests do not establish that new pixels appear while a user holds and drags a native border. Outer-loop-only presentation is a credible explanation for the reported frozen surface and black newly exposed region.
- **Effort:** M investigation; M–L correction depending on evidence.
- **Risk:** HIGH; reentrancy, resource recreation, UIA and owner work must remain safe.
- **Confidence:** MED for root cause; HIGH for the coverage gap.
- **Fix sketch:** With coordinated desktop access, use bounded event/frame instrumentation and a coordinated physical resize to establish whether frame-loop progress stops inside native sizing. Select a supported integration from that evidence. Neither a GPU migration nor replacement of SDL follows from the current finding.

### CORRECTNESS-04 — Restore each collection's independent browsing state

- **Evidence:** `LightNotes/src/LightNotes/NoteWorkspace.cs:321–327` reloads and selects `VisibleItems[0]` on every collection change. `650–658` switches editor documents. `627–631` reloads the full store even though `_allItems` already supports local filtering. `340` sets global busy; `185` makes edit availability depend on it.
- **Impact:** Returning to a collection loses selected-note continuity, and switching waits for storage and temporarily disables editable controls. The accepted per-collection query/scroll policy has no corresponding model yet.
- **Effort:** M after draft ownership is settled.
- **Risk:** MED; deletion/archive, filtering, refresh failure and dirty drafts need explicit fallback behavior.
- **Confidence:** HIGH.
- **Fix sketch:** Own selected identity, query and viewport by collection, immediately derive from cached records, then refresh independently. Define deterministic fallback when a remembered note no longer belongs to the view.
- **Qualification:** `CollectionLoading.lui:33` gates the literal loading panel on `!IsReady`, and `_ready` is not reset when switching collections. The reported flash is not established as that panel by source inspection; status/disabled-state churn or another transition needs observation.

### CAPABILITY-02 — Provide portable cursor intent for interactive controls

- **Evidence:** `Lucent/src/Lucent.Platform.Windows/WindowsServices.cs:169–209` uses a boolean text/default cursor choice. `WindowsBootstrap.cs:261–265` bases that choice on `IsTextInputAt`; `src/Lucent.Core/Input.cs:7–14` supplies only Enabled/Visible input properties.
- **Impact:** Clickables cannot request the desired hand affordance through the framework or `.lui`.
- **Effort:** S–M.
- **Risk:** LOW–MED; disabled/overlapping targets and cursor-handle lifetime must agree with input eligibility.
- **Confidence:** HIGH.
- **Fix sketch:** Resolve a small portable cursor-intent value from the actual eligible hit target and map it in Windows. Provide intentional clickable defaults and author overrides without exposing SDL types.

### DIRECTION-01 — Build one accessible context-menu capability

- **Evidence:** `Lucent/src/Lucent.Core/Input.cs:97–160` has neither F10 nor a context-menu key, and WindowsInputAdapter maps the corresponding bounded key set. No ContextMenu/Popup/MenuItem implementation was found in Core/Windows. `docs/ARCHITECTURE.md` excludes behavior-body generation from `.lui`; `docs/LUI-LANGUAGE.md:49` defers named slots until a concrete composition need proves them.
- **Impact:** Consumers cannot express the accepted text-editing and note-row menus through ordinary framework composition; app-local overlays would have to invent keyboard routing, clipping escape, focus return, dismissal and accessibility ownership.
- **Effort:** L, coarse design estimate.
- **Risk:** HIGH; interaction and popup lifetime are shared framework boundaries.
- **Confidence:** HIGH that the capability is absent; Lucent-rendered popup presentation is now accepted; its platform implementation remains to be proved.
- **Fix sketch:** Define portable invocation, anchoring, actions/availability, focus restoration, dismissal, keyboard navigation and semantic roles, then trial both accepted menu consumers in `.lui`. General slots or universal recipe augmentation should follow demonstrated need, not precede the bounded menu design.

### DX-01 — Reconcile stale language exclusions with implemented capabilities

- **Evidence:** `Lucent/docs/LUI-LANGUAGE.md:53` still says programmatic controlled text synchronization is deferred, while later editor-session sections and ARCHITECTURE.md describe application-owned synchronization. The color/brush section excludes border/radius while later documentation and the current consumer use Border and CornerRadius.
- **Impact:** Consumer authors can incorrectly infer that supported features require custom C# or are prohibited, undermining `.lui`-first authoring.
- **Effort:** S.
- **Risk:** LOW.
- **Confidence:** HIGH.
- **Fix sketch:** Make the supported/deferred surface internally consistent without broadening runtime scope solely to match prose.

## Menu presentation assessment

The portable menu contract should not decide HWND, SDL or native-menu identities. Windows-only hosting is allowed by the architecture; framework-rendered presentation is also consistent with existing Lucent-owned controls. The choice needs evidence rather than a speculative API commitment.

| Presentation | Fit with current code | Additional ownership to establish |
| --- | --- | --- |
| Framework-rendered overlay within the existing host | Reuses the retained scene, text shaping, styles, input and semantic projection; can continue through the existing normal event loop | A real overlay layer outside ancestor clipping, z-order/hit precedence, placement/clamping, bounded menu focus, outside-click dismissal, focus return and menu semantics |
| Native-hosted menu | Can live in the Windows adapter behind the same portable invocation/action contract; not prohibited by Windows-first design | Native lifetime and callback mapping, enabled/action snapshots or live-update policy, accessibility integration, focus/IME transitions and evidence that presentation does not starve the Lucent owner loop |
| Framework-rendered menu in a separate popup window | Retains Lucent painting but is larger than a same-window overlay | Multi-window SDL routing, owner/activation relationships, cross-window focus/capture, placement/DPI, UIA roots/fragments and coordinated disposal |

The user subsequently required menus to escape client bounds and selected Lucent-rendered popup windows for the first slice. Native Windows menu presentation is deferred to the future opt-in platform layer. Popup platform API feasibility has not yet been verified. The comparison above records the ownership work each option entails, not an exemption from the accepted outside-window requirement.

### Shared resize/menu modal-loop risk

`WindowsWorkDispatcher.cs:66–82` drains session/Core work only on the SDL owner; `WindowsBootstrap.cs:178–197` processes that work and UIA from the outer loop. `WindowsUiaDispatcher.cs:6` documents that provider callbacks never enter Core from a window procedure; its default request timeout is five seconds at `24`. `WindowsUiaListener.cs:6` deliberately returns an already-created provider rather than running Core in WndProc.

Consequently, any candidate menu presentation that holds execution in a blocking or nested native loop must prove that application continuations, accepted-save completion, UIA requests, repaint and close preparation remain responsive. If they do not, queuing SDL events alone will not establish progress. Calling Core from arbitrary native callbacks is not a safe shortcut: existing routing and owner-dispatch boundaries assume controlled entry. This is a conditional design risk, not a claim that a particular unselected menu API blocks.

Live-sizing investigation should establish the reusable host-loop constraints before introducing another presentation mechanism with possible nested-loop behavior. A same-window overlay avoids deliberately adding another modal native presentation path; it does not itself fix the existing resize issue.

## Dependency order and focused evidence

1. Characterize controlled selection rejection and single-line editing in headless mounted composition tests. Fix selection authority independently of application routing; unify caret geometry and pointer editing so they share the same text coordinates.
2. Specify retained invalid-draft ownership, inline validation, persistence/restart expectations and close behavior. Implement that policy before collection continuity. Cover incomplete URL, empty title policy, navigation away/back, correction, failed storage retry and accepted-save drain.
3. Implement per-collection selection/query/scroll over the retained drafts with immediate cached transitions. Test deleted/archived remembered selections, searches with no results, and delayed/failed refresh without replacing the visible cached view.
4. Investigate live native resizing and owner-loop progress. Use the currently authorized desktop window; no interactive evidence is implied by source or synthetic geometry tests. Use findings to constrain menu presentation.
5. Add portable cursor intent as a small independent framework surface. Test eligibility and hit precedence; perform physical cursor verification during coordinated desktop work.
6. Design the shared menu contract and implement the accepted Lucent-rendered popup presentation with outside-window behavior required. Implement text-editing and note-row consumers through `.lui`/commands only after focus, dismissal, scene and semantic ownership are defined. Test invocation by pointer and keyboard, disabled commands, Escape/outside click, focus return, owner disposal, resize, and pending async saves.
7. Reconcile documentation with each delivered contract. Keep actual automated, observed and unverified evidence distinct, using repository risk-based verification rather than a blanket release suite.

## Unconfirmed or intentionally deferred

- Plain field-click focus loss remains unconfirmed. Single-line pointer editing is absent, but its handler explicitly requests focus. Do not claim the missing hit-test alone explains native focus loss.
- No new Archive crash was reported. This review does not establish that the historical issue #91 is resolved or identify its root cause.
- Collection selection reset is explicit code behavior; the reported loading flash needs separate observation.
- Full Unicode visual-bidi editing, GPU presentation, generic templates, named slots and broader platform support are intentional/deferred decisions. Their absence is not reported as a defect here.
- Existing passing geometry, semantic and published test sources do not establish live resize smoothness, single-line mouse editing or the accepted menu workflows. No prior pass was treated as current verification.
