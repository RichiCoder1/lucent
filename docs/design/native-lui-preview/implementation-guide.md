# Native .lui preview: interaction and visual specification

Date: October 4, 2026. Status: revised component-first design, ready for implementation planning.
Owner: Design and UI. Implementation continues in the main checkout independently.

## Start here

Open [index.html](index.html) directly in a browser; it is self-contained. Review
controls above the panel select the delivery slice, panel width, editor theme and
failure/setup states. The TaskCard, source locations, timings and inspector data
are illustrative HTML. None is compiled Lucent output or runtime evidence.

Use this document for panel interactions and [component-preview-authoring.md](component-preview-authoring.md) for the active-file, automatic preview, data/variant and edit-loop contracts. Together they are the controlling recommendation. The user-approved component-first refinement supersedes the earlier catalog-first UX. The
[Sol 6.1 xhigh source audit](implementation-audit.md) gives existing handlers and
verified source locations. [Claude Opus 5.5 High's review](opus-review.md) supplied
independent critique. [Review resolutions](review-resolution.md) resolve their
disagreements; recommendations in those reviews do not override this specification.

The main-source audit checked `c6b6482e`, equivalent to supplied `77f1c69d` for the
preview files. Revalidate source when implementing. The design worktree is older
and is not the source of truth for runtime capabilities. No production files,
roadmap files, issues, release claims or runtime contracts were changed here.

Implementation subsequently reported the Stop/settings fix in local `132fc85a`.
Treat that audit finding as baseline history and recheck before implementing it
again. Implementation also reported that unsaved-source compiler checks have not
passed stock-path/configuration fidelity. The Next slice remains a proposal;
unsaved support is not delivered or proven by this design. Later Implementation
updates report the existing explicit-fixture panel controls checked and a retained
live host under review. Compiler adapter parity remains unresolved (including
suppressed warning behavior and generated checksums). A possible saved-source-first
delivery is awaiting the owner's answer; this design does not assume that sequencing
decision or substitute this mockup for those implementation checks.

## Product outcome and boundaries

A developer opens preview beside a `.lui` component and keeps editing. The panel
follows the active component unless pinned. Eligible components get an automatic
default preview; explicit data and state variants extend that path when needed.
No handwritten scenario registry is required for the intended simple path. The
rendered component takes most of the space; optional controls stay out of its way.
Project execution, frame freshness and keyboard ownership remain explicit.

| Delivery | Established or proposed capability |
| --- | --- |
| Existing #231 | Actual compiled Skia PNG, registered scenarios, presentation controls, mapped diagnostics, saved-file rebuilds, bounded delivery and process cleanup. Image is noninteractive. |
| Proposed next #232 | Supported unsaved C#/.lui snapshots, persistent rendering, current-generation pointer/wheel/key/committed-text input, each gated on implementation proof. Source changes still rebuild/restart and reset state. |
| Proposed authoring/tooling slice | Active-file association, follow/pin, typed automatic defaults, optional `.lui` preview declarations and generated development host. Reuses existing catalogs underneath. |
| #233 | Compatible packaged tools, generated-host setup and representative external-consumer proof for ordinary app and library projects. |
| Later #243 | Runtime inspection, read-only element details and verified element source origins. Hide these controls until supported. |

The mockup's delivery switch selects saved-image/live/inspection concepts, not
shipping feature claims. All slices use the proposed component-first chrome.
Active-editor and TaskCard-data controls simulate editor context and authored
definitions; they are review aids, not controls inside the product panel.
Neither the mockup nor this design promises Hot Reload, state preservation,
native window chrome, native IME/preedit, UIA parity, WASM or a general offscreen
hosting package. Keep the existing small Hosting seams and ownership model.

## Visual composition

Operate-mode refinement of the existing VS Code webview. Inherit VS Code fonts,
foregrounds, surfaces, focus colors, form controls and compact density. No branded
hero, custom desktop shell, duplicate source editor, or permanent settings sidebar.
The mockup's tab strip is context; use VS Code's real tab in the product.

From top to bottom:

1. **Subject:** component/source name, Follow editor / Pin, and Configure preview
   data. Project/TFM is secondary context. Long names truncate with accessible text.
2. **Toolbar:** accepted viewport/settings opener; display zoom; quick appearance;
   current interaction mode; Reset; Stop/Start. A component-scoped Variant picker
   appears only when multiple previews exist. One automatic preview needs no picker.
3. **Status:** one short phase label, a concise freshness/coverage explanation, and
   Show preview output. A failure adds one actionable summary below it.
4. **Canvas:** centered image when it fits, scrollable when explicitly magnified.
   No simulated phone bezel, ruler, dot-grid decoration or window chrome.
5. **Input help:** image limitation or keyboard entry/release instruction.
6. **Frame caption:** accepted component/variant, source coverage/revision, logical size,
   render scale and appearance. Extra metadata is disclosed rather than endlessly
   extending the footer.

Use approximately 30px control targets and 32px toolbar/status rows. Text is
12–13px inherited UI text; 11px is reserved for secondary provenance. Maintain
visible focus and native editor zoom. Defaults are token-driven, not hardcoded
dark UI. Preview appearance is independent from the editor theme.

At 700px and below, the optional variant picker gets its own row, advanced
settings use two columns, and the future inspector becomes a bottom section.
Do not allow arbitrary wrapping into a pile of labeled numeric fields. At 480px
and below, secondary button labels collapse to accessible icons and errors wrap.
At 360px Stop/Start, component identity and status must remain
visible. Canvas scrolling never scrolls those controls out of reach. Long diagnostic
strings must wrap without widening the webview.

These breakpoints are design starting values. Use the panel's available width,
not the desktop's device width. At large editor zoom, collapse optional summaries
before making primary controls unreachable.

## Controls and semantic rules

### Component targeting and preview variants

**Open Component Preview** targets the active `.lui` component. Follow editor is
on by default; Pin holds the component/project/TFM while continuing to track its
source changes. Non-component editor focus keeps the last target. The catalog is
execution infrastructure and an optional advanced entry point, not the primary
user journey. See [authoring and edit-loop contracts](component-preview-authoring.md).

Typed automatic activation uses actual compiler-bound component defaults. Required
props/services produce Configure preview, never fabricated values or application
startup. Optional `.lui` preview declarations supply data, owned state and wrappers;
advanced explicit providers can still use the existing service/fixture APIs.

Use a native VS Code Quick Pick only for multiple variants of the target or genuine
project/TFM ambiguity. Never start at a project-wide catalog when the target is known.
The HTML picker demonstrates variant content/filtering; do not ship that custom UI.
Compiler-bound origins and validated existing authored origins associate entries
with a component. Do not infer activation from filenames or arbitrary constructors.

Accepted frame identity includes component, project/TFM, variant and revision. A
component change clears the displayed frame before loading; a same-component edit
may keep labeled last-good pixels. User Stop continues to stage target changes
without executing. Cleanup-blocked state survives target/pin changes.

Source actions remain extension-owned and verified against evaluated context.
A descriptor label is not permission to navigate to arbitrary paths. Component
source navigation can use the mapped document; runtime element origins remain #243.

### Presentation and zoom

Maintain three independent values:

- **Draft:** unapplied form strings. Invalid and partially typed values are allowed
  locally. Status/frame updates cannot overwrite a dirty field.
- **Requested:** the last committed presentation, component and variant for the next build.
- **Accepted:** the frame's actual echoed effective presentation and origin.

The viewport chip and frame caption describe accepted pixels. While building,
status describes the requested change (for example “Applying 800 × 600 · Dark…”).
The variant picker can already identify a requested variant, so retained same-
component pixels must keep their accepted variant label. A different component
clears the frame instead. Never attach new appearance, size, source revision or
component/variant labels to old pixels.

Opening the presentation sheet starts a draft from requested values. It contains
logical width/height, appearance, device scale, contrast and fixture density.
**Apply & restart** commits all six once; Enter submits the form. Closing the sheet
or Escape cancels its draft and returns focus to the opener. **Preview defaults**
changes the draft only; Apply remains required. Temporary hiding may discard an
unapplied draft under this same cancel contract; retain the committed request.

Validate numeric fields and the aggregate physical bounds before sending. The host
remains authoritative. Show field/combination errors rather than silently dropping
an action. Existing limits are 1–8192 logical extent, 0.25–4 scale/density, at most
8192 physical pixels per dimension and 16,777,216 aggregate physical pixels. Reuse
the protocol's binary32 calculation; do not introduce a different rounding formula.

**Appearance** is a one-click Light/Dark change to the committed presentation; it
rebuilds/resets while running. Disable this shortcut while the presentation sheet
is open to avoid overwriting an unrelated draft. High contrast remains a separate
presentation value. Fixture density is not a global Core setting: its effect
depends on fixture code consuming it.

**Fit** is the default display mode: shrink to the available canvas, never enlarge.
Show the resulting percentage. Offer explicit 50/100/150/200% and retain the
existing allowable numeric zoom range where supported. These choices change only
display magnification, never logical layout, renderer scale, component state or a
build. Fit may require below-25% display sizing for a very large accepted frame;
that is an explicit new frontend fit mode, not permission to relax renderer bounds
or send an invalid existing numeric `zoom` action. Keep scale and zoom separate in
the host view model. At 100%, one logical unit occupies one CSS pixel.

### Reset, rebuild and stopped intent

Keep one visible **Reset preview** action when running/current. Its accessible
name and tooltip say “Same component, variant and presentation; rebuilds with fresh state.”
Do not show adjacent Reset and Refresh buttons. Retain **Rebuild preview** in the
command palette and optional host menu for explicit retry/undeclared-input changes.
After failure, **Retry** rebuilds current supported source and committed controls.

For this delivery Reset still recompiles/restarts. Reusing an existing executable
without recompilation is a separate optimization requiring the same freshness,
artifact validation and cleanup proof. It is not a UX prerequisite for #232.

**Stop** immediately revokes input, then shows **Stopping…** until cleanup is
confirmed. Only then show **Stopped** and replace Stop with **Start preview**.
Unconfirmed cleanup becomes a sticky blocked state; no new owner may start.

While stopped, component/variant and presentation changes stage the next request. They must
not execute code. Label the form action **Apply for next start**; show “Changes apply
on Start.” Reset is disabled, zoom remains usable, and edits do not auto-start.
Start uses the retained selection/overrides, not freshly reread configured defaults.
This differed from the audited controller. Implementation reports the Stop/settings
fix in `132fc85a`; verify this full contract against that change before doing more
work. Any unsupported rebuilding controls must remain disabled while stopped.

Hiding stops execution and delivery. Returning rebuilds fresh state only if the
user left preview running. “Paused” or “Resume” must not imply preserved runtime
state. User Stop remains stopped through hide/show and source changes. Closing
releases panel ownership; reopening follows explicit start/setup again.

## State and feedback contract

Keep operation phase, desired running intent, frame freshness, display readiness
and input ownership separate. A green frame status is not proof of a live worker
or keyboard ownership. Existing phase names can be mapped into the following view
states without weakening backend invariants.

| View state | Display | Action and input contract |
| --- | --- | --- |
| Not configured | Specific missing tools/project-context requirement | Set up component preview through host-owned actions; no handwritten registry requirement for the intended default path. |
| Needs preview data | Name missing props/context/services for the selected component | Add preview data / Open preview definition; explicit reversible editor edit, no fabricated inputs. |
| Restricted / unsupported | Reason and appropriate host action | Manage VS Code trust or open a local Windows workspace. Never grant trust in the page. |
| Tools/SDK/version unavailable | Name the missing/incompatible prerequisite | Environment diagnostics and setup guide. No fallback to arbitrary executables. |
| Resolving / loading | Resolving active component or building its preview | Stop remains reachable. Same-component last-good pixels may remain; another component's pixels are cleared. No component input. |
| No target | “Open a .lui component to preview” | Do not redirect to an unrelated global catalog. Retain a prior target when non-component files take focus. |
| Updating / rendering | Requested change in status; previous frame keeps its provenance | Revoke input immediately. Superseding selections are allowed while running. Stop works even with an unacknowledged frame. |
| Current image #231 | “Ready · saved source”; persistent image-only help | No input. Unsaved source is not included; never imply editor-buffer freshness. |
| Current interactive #232 | “Ready · editor snapshot” only after actual supported overlays are accepted | Eligible for deliberate input capture; not automatically capturing. |
| Build/preview failure | First useful error plus Open source when mapped; previous frame if any | Retry and Output; expandable remaining diagnostics. No stale input. |
| Display failure | “The preview could not be displayed” | Host-owned display reconnect/recovery. Accepted worker pixels alone do not prove visible current content. |
| Stopping / stopped | Cleanup progress, then stopped; retained image read-only | Stage controls only once safely stopped. Explicit Start required. |
| Hidden | Execution stopped; retained desired-running intent | Show rebuilds only if previously running. No background image accumulation. |
| Cleanup unconfirmed | Persistent blocked explanation | Output/diagnostics only; coordinator-owned recovery, no unconditional “try again.” |

First-build failures use an empty surface, not a broken image or indefinite loader.
Stale frames use a visible corner label and original provenance; modest dimming is
optional. Preserve readability for comparison. Never use color or opacity alone
to communicate freshness. A small progress line communicates work without inventing
a percentage or latency guarantee.

Show one primary diagnostic and an **N more** disclosure. Source navigation is
offered only for verified locations bound to the current display revision. Clear
old diagnostic actions synchronously when source/generation identity changes; do
not silently retarget them. A file outside the allowed workspace gets text/Output,
not a fake clickable location. Output is the existing Lucent Preview channel,
separate from language-server logs. Do not expose artifact hashes or transport IDs
in primary chrome; retain them in support details.

Live regions announce meaningful phase/failure transitions once, not every frame,
pointer move or identical delivery. Frame replacement never steals focus. Stable
controls and diagnostic identities must survive status-only updates.

## Interactive input (#232)

Default view is eligible for interaction only after the live owner and current
displayed frame are both admitted. Keyboard focus and capture are distinct:

1. Tab reaches one preview-surface focus stop. Outside capture, Tab/Shift+Tab
   continue through editor controls. Enter/Space deliberately enters the sample.
2. A pointer click in the painted image enters interaction and also delivers that
   same click; do not consume the first click merely to focus. Clicks in margins
   never become component events.
3. While capturing, Tab/Shift+Tab traverse Lucent controls and ordinary Escape
   reaches app menus/dialogs. Do not reserve bare Escape globally.
4. **Shift+Escape** releases interaction to the Interact control and never reaches
   the app. Show it persistently while capturing and provide an explicit Leave
   preview action and accessible help. This is a proposed scoped binding; qualify
   it in the installed editor and honor user keybinding overrides.
5. Preserve host F6/Shift+F6, command palette and editor navigation chords. Do not
   globally prevent default on every key or promise that webview JS can intercept
   host-owned shortcuts. Register host release/focus commands where necessary and
   verify webview context-key behavior. The prototype models F6 by returning focus
   to its toolbar; it cannot model the surrounding VS Code workbench.

Coordinate the above with VS Code's Tab Moves Focus behavior; when enabled, Tab
must be able to leave the preview. An explicit, discoverable release must work even
if app code hangs or claims a key. Do not infer full screen-reader access to the
rendered Lucent app from accessible HTML controls or a PNG alt text.

On blur, another control receiving focus, source invalidation, rebuild, stop,
hide/close, trust loss, or display-channel loss: revoke eligibility/capture, cancel
held pointers, release keys and clear hover using the owned input path. Do not
silently re-enter capture when a fresh frame arrives. Merely choosing Interact mode
does not focus app text fields. Inspect and interact are mutually exclusive.

Map coordinates against the actual painted rectangle:
`logicalX = (clientX - imageLeft) * logicalWidth / paintedWidth`, with analogous Y.
Do not multiply by devicePixelRatio or render scale again. Reject ordinary margin
events; allow bounded outside-image coordinates only during owned pointer capture.
Normalize wheel delta units, allowlist keys, and carry committed text separately.
Every event is bound to current session/generation/frame/sequence. Native IME
preedit and accessibility parity remain separate work.

## Onboarding and authoring

Make one-command preview of an ordinary component the default. Resolve the active
file's evaluated project/TFM, compatible tools and generated development host.
Ask only for genuine ambiguity or missing infrastructure. Native Quick Picks,
settings and Walkthroughs handle prerequisites; no setup wizard in the webview.

The data path is separate: missing required inputs show Configure preview for that
component and a native code action to add its preview declaration. Editing the
declaration then participates in the same tight loop. Do not save buffers, change
production defaults or fabricate sample domain objects. An empty old catalog is
an implementation-stage limitation, not the intended first-run UI.

Normal app projects and component libraries must both work without extraction or
handwritten preview executables. Preserve current explicit catalogs as an advanced
path during rollout. [The authoring design](component-preview-authoring.md) defines
staging, preview-only compilation, context resolution and 12 additional acceptance
checks. Packaged external-consumer proof remains necessary for #233.

## Inspector integration (#243)

Do not render a disabled permanent “coming soon” inspector in #231/#232. When the
runtime exposes supported inspection, add a sibling Inspect toggle beside Interact.
Selecting Inspect releases app input. Pointer hover outlines an element; click
selects it without invoking the app. Offer a keyboard-navigable tree as an
alternative to pixel hit testing. Every selection identifies the same current
generation/frame, and rebuild invalidates it.

Details open only on selection: about 240px at wide widths, below the canvas at
narrow widths. Start with ancestry, bounds, relevant state/semantics and a verified
Reveal source action. Make values read-only; property editing is not part of this
design. Missing origins say unavailable. Avoid raw-object dumps and expose more
advanced diagnostics on demand. Closing details keeps mode intentional; Interact
is the explicit switch back to app behavior.

## Implementation sequence and ownership

| Order | Change | Owner / dependency |
| --- | --- | --- |
| A | Compact chrome, presentation disclosure, explicit saved-source label, meaningful empty/error states, one reset affordance | Existing panel HTML/view code; keep CSP and allowlisted actions. |
| B | Accepted component/variant provenance, whole-form validation, stable drafts/DOM, Show Output, component-scoped variant picker | Panel snapshot and controller actions. Map actual effective worker data; do not synthesize it from selection. |
| C | Sticky stopped intent, confirmed stopping state, display-loss recovery | Existing coordinator/controller lifecycle. Requires focused tests; not a cosmetic patch. |
| D | Fit mode and supported-input currentness/capture UX | Frontend view model plus #232 live owner, immutable overlays and input protocol. Prove ownership before enabling controls. |
| E | Active-file follow/pin, automatic typed defaults, optional data/variant authoring, generated host and packaged consumer path | New compiler/tooling slice alongside #232/#233; detailed increments in component-preview-authoring.md. No project parsing or activation inference in webview. |
| F | Inspect mode/details/source navigation | #243 identities, runtime tree/hit-test/source-origin support. |

Closed #231 is a capability baseline; this table does not reopen or mutate tickets.
Implementation can place small panel refinements alongside #232/#233 without
blocking overlay/session feasibility on cosmetic changes. Increment E is essential
to the final editor experience and must be tracked as substantive authoring work,
not silently dropped because the existing explicit-fixture path works. The audit maps all
existing action routes. Additional view actions require bounded host validation,
not arbitrary command IDs/URIs in page messages.

Preserve one unacknowledged frame and bounded pending state, generation validation,
supervised execution, and hidden/closed cleanup. Do not turn a design loading state
into unbounded timers, polling, images or worker replacement. Record VS Code UX
inspiration in `CREDITS.md` before adopting the implementation; this isolated
proposal does not alter dependency or credit files.

## Acceptance scenarios for the implementing agent

| ID | Given / action | Observable result |
| --- | --- | --- |
| UX-01 | Open at 360px with a long title and error | Stop/Start and component identity remain reachable; text wraps; preview scrolls independently. |
| UX-02 | Current light component, choose dark/another variant, then fail build | Old pixels retain old component/variant/appearance/size; requested change and failure are separate; all component input is disabled. |
| UX-03 | Type half of a new width while a frame arrives | Draft text and focus survive; no build occurs until Apply. |
| UX-04 | Apply 8192 logical width at 4× | Visible combination error; no request appears successful; no allocation/build is started for an invalid presentation. |
| UX-05 | Change Fit to 100%, then resize the panel | No worker reset/build; explicit 100% scrolls, Fit recalculates display only. |
| UX-06 | Stop, change component/variant/appearance, edit source, hide/show | No executable work resumes; explicit Start uses the staged request and fresh state. |
| UX-07 | Stop while a new frame is unacknowledged; cleanup fails | Stop is accepted immediately; Stopping precedes blocked state; no replacement worker or false Stopped claim. |
| UX-08 | Enter sample by click; Tab; open app menu; press Escape | First click acts; Tab traverses component focus; Escape closes the app menu. Shift+Escape returns to editor controls. |
| UX-09 | Hold pointer/key, then edit source or hide | Input cleanup occurs; old/new frames cannot receive leftover events; fresh frame does not reclaim keyboard capture. |
| UX-10 | Error source changes before Open source completes | Stale action is rejected; never navigate to a new location using an old diagnostic index. |
| UX-11 | Start unconfigured or with required component inputs missing | Specific tool/data action for that component; no guessed ID, silent settings write, fabricated data or production startup. |
| UX-12 | Enable future Inspect, select item, rebuild | No app click handler fires; details are same-generation/read-only; selection invalidates before fresh pixels appear. |
| UX-13 | Host high contrast/reduced motion and keyboard navigation | Chrome honors tokens, visible focus, named controls and meaningful announcements; no animation required to understand state. |
| UX-14 | Corrupt/failed image decode or missing delivery acknowledgement | Display is not claimed current; bounded transport remains bounded; recovery cannot interact with stale pixels. |

Run focused existing panel/coordinator/host checks for affected contracts, then one
coordinated installed-editor walkthrough. The HTML mockup is for evaluating the
design, not a replacement for native input, lifecycle or accessibility proof.

## References

- [Current native-preview documentation](../../../docs/NATIVE-PREVIEW.md).
- [Current panel plan](../../../docs/plans/native-preview-panel.md).
- [Preview scenario authoring](../../../docs/PREVIEW-SCENARIOS.md).
- [VS Code webview UX](https://code.visualstudio.com/api/ux-guidelines/webviews):
  contextual use, themeability, native actions and avoiding duplicated editor/setup UI.
- [Webview implementation guidance](https://code.visualstudio.com/api/extension-guides/webview):
  retrieved via Context7 `/microsoft/vscode-docs`; use VS Code theme variables and
  preserve lifecycle/security behavior.
- [VS Code keyboard accessibility](https://code.visualstudio.com/docs/configure/accessibility/accessibility#_tab-navigation):
  workbench focus commands and Tab Moves Focus inform the proposed capture boundary.

The component-first product direction is settled. The separate saved-source-first
versus compiler-adapter sequencing question remains pending. Shortcut qualification,
overlay fidelity, source-origin validation, typed activation, preview-profile
exclusion, display readiness and lifecycle completion remain engineering proof
obligations; they do not pass by product choice.
