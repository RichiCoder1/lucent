# Lucent architecture and code review

Reviewed September 4, 2026 against `cc1a0ae79f266cc34eb89d42047ba6020bae0153`. The existing uncommitted roadmap and links-and-notes draft were read and preserved. This is a read-only source audit; recommendations below have not been implemented.

## Assessment

Lucent has a viable foundation for the proposed links-and-notes application. Retained ownership, transactional mounting, portable Core/platform separation, compile-time `.lui` lowering, and explicit stale-result rejection are worth preserving. The current implementation is substantially better exercised for a small Issue Browser than for a responsive application with a durable editing session.

Proceed with the application direction, with a small corrective slice first and several explicit additions to the first plan. The most consequential additions are `.lui` composability and live-data semantics, ordered durable writes and negotiated close, editor-session ownership across responsive arrangements, and constrained paragraph measurement. Input dispatch also has a measured allocation problem that should be addressed before a substantially denser UI.

The user's clarified direction is **`.lui` first for application and reusable UI authoring**. C# remains the underlying framework contract and the appropriate home for application services, models, and advanced control implementation. New UI capabilities should be designed and exercised from `.lui` as they land. General authoring conveniences can follow later; missing composition capabilities cannot all be relegated to that follow-up.

## Current defects and API traps

Ordering considers impact and effort. High impact is not a claim that every entry blocks continued work. Effort is relative: S = focused local change, M = a module or several adapters, L = a cross-cutting contract. Risk describes the proposed change. Confidence distinguishes reproduced behavior from source inspection.

| ID | Finding | Impact | Effort | Change risk | Confidence | Timing |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | Valid `<` comparisons and generic calls fail in `.lui` condition headers | High authoring impact | S/M | Medium | Reproduced | Corrective slice |
| C2 | An auto-sized empty text field collapses when focused | High basic-control impact | S | Low | Reproduced | Corrective slice |
| C3 | A throwing wake observer can strand completed async work | Medium; requires observer failure | S | Medium | Reproduced | Corrective slice |
| C4 | Editor component discovery disagrees with compiler eligibility | Medium tooling impact | S | Low/medium | High, source | Corrective slice |
| C5 | Every app's UIA root is named Lucent Issue Browser | Medium external-app impact | S | Low | High, source | Corrective slice |
| C6 | Same-key replacement has no current-item propagation contract | High data-binding impact | M | Medium | Reproduced | Before application data binding |
| C7 | Pointer dispatch repeatedly searches and hashes the whole tree | High scaling impact | M/L | Medium/high | Measured | Layout foundations |
| C8 | UIA point lookup ignores ancestor clipping | Medium accessibility impact | M | Medium | High, source; no live UIA reproduction | Responsive scrolling work |
| C9 | Language-server launch errors bypass the extension's RPC failure path | Medium onboarding impact | S | Low/medium | High, source | Corrective slice or independent setup |

### C1. Repair structural expression boundaries

[`LuiParser.cs:541`](../src/Lucent.Lui.Compiler/LuiParser.cs#L541) and [`:598`](../src/Lucent.Lui.Compiler/LuiParser.cs#L598) include `<` among recovery stops for conditions and keys. [`IslandScanner.End:1588`](../src/Lucent.Lui.Compiler/LuiParser.cs#L1588) treats any top-level less-than token as that boundary, including a comparison or a generic type-argument opener.

An isolated parser probe returned no diagnostics for `if (count > 2)`, but a cascade beginning with “Expected ')'” for `if (count < 2)`. `if (Check<int>())` also failed. Adding another pair of parentheses around the comparison happened to avoid the stop. These were parser-only examples, so unresolved identifiers did not enter the result.

Use syntactic context to separate C# islands from markup recovery. Cover comparisons, generic invocations, nested expressions, and malformed headers that must still recover at a later element. Do not make extra parentheses a language convention to conceal the defect.

### C2. Give empty editors stable intrinsic geometry

[`Components/TextField/TextFieldControls.cs`](../src/Lucent.Core/Components/TextField/TextFieldControls.cs) removes the placeholder from projected text on focus. [`SceneLayout.cs:429`](../src/Lucent.Core/SceneLayout.cs#L429) skips shaping empty text; intrinsic measurement then has no text height. A probe of the default field in a column observed **200 × 14 before focus and 200 × 0 after focus**. Existing field tests commonly assign explicit dimensions, masking this path.

Separate placeholder painting from editor measurement or supply stable intrinsic/minimum editor metrics. Test default sizing under both Row and Column, including caret geometry and the first edit.

### C3. Make the host wake independent of observer exceptions

[`ReactiveGraph.cs:294`](../src/Lucent.Core/ReactiveGraph.cs#L294) enqueues work and directly invokes a multicast `WorkAvailable` delegate. An earlier throwing observer prevents later observers from running. [`AsyncValue.cs:157`](../src/Lucent.Core/AsyncValue.cs#L157) invokes this from an ignored continuation. Because the queue is already nonempty, later posts need not produce a fresh wake edge.

A probe with a throwing first observer and a counting second observer completed a producer with value 7. The host wake count remained zero and the async value remained pending at 0; a manual graph drain exposed 7. Prefer one host wake sink or independent observer delivery with an observable failure policy. Preserve edge coalescing and UI-thread ownership.

### C4. Share component eligibility between build and editor

[`LuiCompiler.cs:628`](../src/Lucent.Lui.Compiler/LuiCompiler.cs#L628) requires a static attributed method returning exactly `ComponentRecipe`. [`LuiProjectContext.cs:2687`](../src/Lucent.Lui.LanguageServer/LuiProjectContext.cs#L2687) checks only static plus attribute; [`ComponentSymbols:2363`](../src/Lucent.Lui.LanguageServer/LuiProjectContext.cs#L2363) uses the weaker predicate.

An attributed static method returning `int` can therefore enter editor component discovery despite being ineligible for compiler tag binding. Consolidate this small semantic rule and cover an invalid-return candidate through both compiler and completion tests. This finding concerns discovery; it does not imply every navigation operation accepts an invalid bound tag.

### C5. Pass application identity into accessibility hosting

[`WindowsUiaProvider.cs:315`](../src/Lucent.Platform.Windows/WindowsUiaProvider.cs#L315) returns the literal `Lucent Issue Browser` for the root Name property. A second application inherits that name regardless of its window title. Pass host/application identity through the provider boundary and verify a non-Issue-Browser host. No broad accessibility recertification is necessary for this fix.

### C6. Preserve identity while updating retained payloads

[`KeyedRegion.cs:97`](../src/Lucent.Core/KeyedRegion.cs#L97) calls the factory only for new keys; retained entries are reordered without receiving a replacement item. [`Components/Scrolling/VirtualizedRegion.cs:163`](../src/Lucent.Core/Components/Scrolling/VirtualizedRegion.cs#L163) has the same arrangement. A reactive row that captures the factory's item can therefore keep displaying its original record after the collection receives a new record with the same key.

A probe replacing `{ Id: 1, Title: "before" }` with `{ Id: 1, Title: "after" }` produced `source=after; rendered=before; factories=1; retained=True`. The Issue Browser already works around this by looking up the current record by key in [`Components.cs:5`](../apps/Lucent.IssueBrowser/Components.cs#L5).

This is an incomplete update contract, not evidence that retained factories should rerun. Introduce a scope-owned current-item reader or explicit update seam that preserves identity and local state. Carry it through `.lui foreach` lowering. Also characterize pattern locals in a conditional that stays on the same branch: [`LuiCompiler.cs:1594`](../src/Lucent.Lui.Compiler/LuiCompiler.cs#L1594) constructs a new choice while [`ConditionalRegion.cs:135`](../src/Lucent.Core/ConditionalRegion.cs#L135) retains the old mounted recipe. A newly captured local is not automatically delivered to that retained child.

### C7. Remove redundant work from ordinary input dispatch

[`InputRouter.cs:964`](../src/Lucent.Core/InputRouter.cs#L964) traverses the live tree, then calls `Composition.Find` and recomputes a signature for every input node. [`Composition.cs:672`](../src/Lucent.Core/Composition.cs#L672) implements a recursive search from the root, making repeated lookup quadratic in element count. [`SceneLayout.cs:635`](../src/Lucent.Core/SceneLayout.cs#L635) resolves the layout/text/input property set for each signature, then allocates serialization buffers and computes SHA-256 at [`:683`](../src/Lucent.Core/SceneLayout.cs#L683). Availability synchronization does additional searches.

An exploratory in-memory probe measured 20 pointer moves after five warmups over a static, flat, no-text scene. No rendering or repaint was inside the measured loop:

| Realized elements, including root | Mean milliseconds/move | Managed bytes allocated/move |
| --- | ---: | ---: |
| 101 | 4.49 | 3,218,188 |
| 501 | 11.68 | 15,739,559 |
| 1,001 | 13.95 | 31,394,084 |

These are diagnostic observations on this machine, not stable benchmark thresholds. The allocation volume alone warrants attention. Use indexed identity lookup and projection-affecting revisions or cached resolved state. Retain fail-closed stale-scene rejection; merely deleting validation would trade a performance defect for correctness regressions.

### C8. Align accessibility point lookup with visible geometry

[`WindowsUiaProvider.cs:176`](../src/Lucent.Platform.Windows/WindowsUiaProvider.cs#L176) snapshots raw layout bounds. [`Point:530`](../src/Lucent.Platform.Windows/WindowsUiaProvider.cs#L530) selects by those bounds and ordinal without the ancestor clip checks used by [`InputRouter.cs:1072`](../src/Lucent.Core/InputRouter.cs#L1072). A clipped or overscanned child can consequently remain a point-lookup candidate outside its visible viewport.

Carry effective clipping/participation into semantic geometry and test a partially clipped child plus a fully clipped overscan row. This is source-confirmed logic divergence; no fresh screen-reader or native UIA point-query walkthrough was performed.

### C9. Handle extension process-launch failure explicitly

[`extension.js:161`](../extensions/lucent-lui-vscode/extension.js#L161) launches `dotnet`, while [`Rpc:89`](../extensions/lucent-lui-vscode/extension.js#L89) listens for stdout and process exit but not the child process's `error` event. A missing executable therefore has no deliberate path to reject initialization and report setup guidance. The later initialization `try/catch` does not itself subscribe to that asynchronous process event.

Attach launch/runtime error handling promptly, reject pending requests, and make cleanup safe when the process never started. Add a mocked launch-error case to the existing extension tests. A stale server DLL path normally produces a process exit and is a different case; no claim of an observed VS Code host crash is made here.

## First-plan prerequisites

These are architectural extensions required by the agreed application, rather than surprise defects in capabilities already promised by the small reference app.

| ID | Addition | Impact | Effort | Change risk | Confidence | Resolve before |
| --- | --- | --- | --- | --- | --- | --- |
| A1 | Consistent `.lui` content composition and live-input contracts | High | M/L | Medium/high | High; content inconsistency reproduced | Reusable application components |
| A2 | Ordered durable mutations plus negotiated asynchronous close | High, protects accepted edits | L | High | High, source | Trusting real saved notes |
| A3 | Hoistable editor sessions and explicit responsive participation | High UX impact | L | High | High current behavior; design open | Responsive editor branches |
| A4 | Shared constrained text/layout measurement | High architectural impact | L | High | High, source | Freezing Grid and multiline contracts |
| A5 | Wheel/trackpad routing and application commands | High everyday usability | M/L | Medium | High, source | Complete capture/find/edit loop |
| A6 | Genuine independent consumer setup | Medium reproducibility impact | M | Medium | High fixture coupling; packaging choice open | External application setup |

### A1. Treat `.lui` as the first design surface

The existing [language contract](../docs/LUI-LANGUAGE.md) still calls typed C# the canonical authoring surface. Revise that product emphasis when the plan is updated: **C# supplies the semantic/runtime contract; `.lui` is the primary UI authoring experience.** A capability is not complete for the planned application merely because it can be constructed through an advanced C# control builder.

The grammar has useful fundamentals: explicit typed component declarations, ordinary C# symbol binding, target-typed expressions, retained keyed structure, typed styles, tokens, variants, and real source maps. Preserve those choices. The main gaps are composition and lifetime semantics, not a need for an entirely different language.

| Area | Current capability or limitation | Recommended treatment |
| --- | --- | --- |
| Component declarations | One component/document, one stable root, typed parameters, adjacent C# helpers | Keep initially; revisit multiple local declarations only when component families cause repeated friction |
| Default and named content | C# metadata supports default content, but `.lui` declarations reject parameter attributes; body expressions cannot splice component content | Establish explicit declaration and forwarding of default content now; prove a small named-slot design with the inbox/editor shell if needed |
| Reactivity | Style expressions bind automatically; scalar props capture values; typed readers/models are explicitly live | Keep a single retained model, expose timing in docs/tooling, and make current-item/editor synchronization contracts usable from markup |
| Structural regions | `if`/`foreach` require a single element body; direct `else if` is not parsed | Evaluate sibling fragments with Grid use cases; avoid layout wrappers solely to satisfy grammar; simple `else if` is a useful small convenience |
| Events and services | Method groups and short lambdas work; no local state or service-injection syntax | Use explicit application models/commands and constructor/composition-root injection first; require safe asynchronous command ownership before adding syntax sugar |
| Styles | Typed property bodies, variants, tokens, and composition work; shared markup style exports are deferred | Deliver all new layout/presentation properties through markup and editor tooling; promote shared style exports only if the first app demonstrates substantial duplication |
| Text and editing | Live `Text` readers exist; `TextField` exposes initial value and changes, not an external editor session | Bind a reusable session from `.lui`; do not use remounts to implement ordinary value synchronization |
| Diagnostics/tooling | Shared Roslyn project context, formatting, maps, navigation and rename exist | Repair C1/C4, add feature tests from realistic markup, and keep unsupported constructs concise and actionable |

**Content semantics need a specific correction.** [`LuiParser.cs:239`](../src/Lucent.Lui.Compiler/LuiParser.cs#L239) rejects `[DefaultContent]` on a `.lui` parameter. [`LuiCompiler.cs:1342`](../src/Lucent.Lui.Compiler/LuiCompiler.cs#L1342) emits plain parameter declarations. However, when no content metadata plan exists, [`:1451`](../src/Lucent.Lui.Compiler/LuiCompiler.cs#L1451) falls back to a literal parameter name `content`.

An in-memory compilation probe demonstrated that this wrapper and a caller with nested children compile:

```lui
public component Panel(ComponentContent content) {
    <Column content={content} />
}
```

Renaming that parameter to `children` leaves the wrapper valid but makes `<Panel><Text>hi</Text></Panel>` fail with “does not have a parameter named 'content'.” Thus, some pure `.lui` wrappers work today, but through an implicit naming rule inconsistent with the stated metadata-based contract. This is more precise than claiming wrappers are impossible.

Forwarding the same content naturally among other children is also restricted: [`LuiCompiler.cs:1570`](../src/Lucent.Lui.Compiler/LuiCompiler.cs#L1570) rejects expression children in component-content collections. A wrapper can use a `content={...}` attribute, but inserting its content between a header and footer pushes authors toward C# recipe expressions. Specify a coherent content declaration/forwarding contract, lowering through the existing immutable content types, with compiler/LSP/map tests. Do not silently retain both a magic-name rule and a conflicting metadata-only specification.

[`SingleElement:1658`](../src/Lucent.Lui.Compiler/LuiCompiler.cs#L1658) and [`LuiParser.If:557`](../src/Lucent.Lui.Compiler/LuiParser.cs#L557) also expose practical grammar limits: multi-sibling region bodies and `else if` are unavailable. These are bounded design choices, not parser bugs equivalent to C1. Grid makes unnecessary wrapper nodes more consequential because they alter direct-child placement; use that concrete case to decide whether to add fragments.

The most dangerous ergonomic ambiguity is stale state, not verbosity. `<Text>{model.Title}</Text>` is a construction-time argument; `<Text>{() => model.Title}</Text>` selects a live reader. Even a reader cannot make an immutable item captured by a retained factory become the replacement record. Explain and test both distinctions. Syntax sugar should follow a correct lifetime/update contract.

### A2. Design persistence ordering and close together

`AsyncValue` correctly rejects stale in-memory completions. It does not serialize external side effects: [`AsyncValue.cs:173`](../src/Lucent.Core/AsyncValue.cs#L173) checks generation after the producer has already run. The Issue Browser's mutation path at [`IssueBrowserState.cs:231`](../apps/Lucent.IssueBrowser/IssueBrowserState.cs#L231) and reverse-completion tests exercise displayed state, not durable write ordering.

For real notes, save A can commit after save B even if A's UI result is discarded. Cancellation cannot retroactively undo a committed write. Choose application-owned per-record serialization/coalescing or revision-conditional writes. Verify the final state by rereading/reopening storage after deliberately reversed completion. This is not a claim of existing SQLite data loss; SQLite is still planned.

Close has a related missing seam. [`Application.cs:6`](../src/Lucent.Core/Application.cs#L6) offers synchronous `Run`; [`Application.cs:122`](../src/Lucent.Core/Application.cs#L122) disposes composition after it returns. [`WindowsBootstrap.cs:228`](../src/Lucent.Platform.Windows/WindowsBootstrap.cs#L228) schedules close immediately, and teardown destroys the window before Core composition disposal. There is no close negotiation that can await accepted saves, stop hosted services, or show a recoverable failure while the window remains available.

Define startup, close request, drain/stop, and final disposal with explicit UI-thread ownership. Compose the selected hosting package's asynchronous stop/disposal into that lifecycle. Keep read cancellation distinct from accepted write completion; do not simply change `Run` to an async method and lose the thread-affinity contract.

### A3. Preserve editing sessions through responsive changes

[`ConditionalRegion.cs:139`](../src/Lucent.Core/ConditionalRegion.cs#L139) mounts a new branch and disposes the old one. [`Components/TextField/TextField.cs:87`](../src/Lucent.Core/Components/TextField/TextField.cs#L87) holds value, caret, selection, preedit, undo and redo in internal mounted state; [`Components/TextField/TextFieldComponents.cs`](../src/Lucent.Core/Components/TextField/TextFieldComponents.cs) exposes only initial text and change notification. Scroll state is similarly mount-owned at [`Components/Shared/ControlState.cs`](../src/Lucent.Core/Components/Shared/ControlState.cs).

Hoisting just the note string will preserve a draft but lose much of the editing session on a branch change. Establish a specific editor-session and viewport-state interface, with explicit ownership of platform resources and focus transfer. Preserve undo/selection/scroll where appropriate, and deliberately end or transfer IME composition according to the supported contract. Generic mounted-component handles or arbitrary reparenting are not prerequisites.

Responsive participation also needs an explicit contract. [`Input.cs:6`](../src/Lucent.Core/Input.cs#L6) documents `Visible` as routing-only. Layout and painting retain it; semantics expose unavailable nodes. [`WindowsHostContracts.cs:654`](../tests/Lucent.Platform.Windows.Tests/WindowsHostContracts.cs#L654) intentionally tests that behavior. Decide how hidden/collapsed or retained responsive regions participate in measurement, paint, input, focus and accessibility. Do not silently repurpose the existing property.

### A4. Resolve paragraph measurement before freezing layout

[`TextMeasureRequest:335`](../src/Lucent.Core/LayoutScene.cs#L335) has no available width or wrapping policy. [`ShapedText.Validate:558`](../src/Lucent.Core/LayoutScene.cs#L558) requires runs to share one baseline and vertical metrics. [`SceneLayout.cs:421`](../src/Lucent.Core/SceneLayout.cs#L421) shapes text without a width constraint. Wrapped text and multiline editing therefore require a changed measurement/result seam, not merely a new control or newline rendering.

Specify constrained paragraph measurement, line/run ranges and metrics, caret/selection geometry, hit testing, and reusable paint results. Exercise the feedback between available width and desired height before choosing/finalizing Grid/Flex semantics or an engine integration. Keep any engine boundary internal and Core-owned; native engine handles must not leak into `.lui` or portable Core contracts.

There are additional source-confirmed scaling risks worth addressing within this work, with small characterization cases rather than a separate optimization program:

| Risk | Evidence | Suggested response | Effort / change risk / confidence |
| --- | --- | --- | --- |
| Repeated subtree measurement | [`SceneLayout.cs:110`](../src/Lucent.Core/SceneLayout.cs#L110), [`:349`](../src/Lucent.Core/SceneLayout.cs#L349) | Cache resolved values and measurements by constraints within a projection; characterize deep trees | L / high / high source confidence |
| Recursive scene copying at every clip/opacity boundary, then again at scene creation | [`LayoutScene.cs:717`](../src/Lucent.Core/LayoutScene.cs#L717), [`:759`](../src/Lucent.Core/LayoutScene.cs#L759), [`:789`](../src/Lucent.Core/LayoutScene.cs#L789) | Internal ownership/freeze path; preserve public mutation isolation | M / medium / high source confidence |
| Font coverage shapes once per grapheme before final shaping | [`SkiaSceneRenderer.cs:338`](../src/Lucent.Renderer.Skia/SkiaSceneRenderer.cs#L338), [`:353`](../src/Lucent.Renderer.Skia/SkiaSceneRenderer.cs#L353) | Measure long content; prefer run/cluster-based fallback with correct missing-glyph behavior | M/L / medium / high source confidence |
| Shape cache is limited by entry count, retaining whole strings and glyph arrays | [`SkiaSceneRenderer.cs:28`](../src/Lucent.Renderer.Skia/SkiaSceneRenderer.cs#L28), [`:440`](../src/Lucent.Renderer.Skia/SkiaSceneRenderer.cs#L440) | Weighted retention budget and oversized-entry policy; consider paragraph granularity | M / low-medium / high source confidence |
| Every caret movement reparses whole-string grapheme boundaries; undo stores complete values | [`Components/TextField/TextField.cs:359`](../src/Lucent.Core/Components/TextField/TextField.cs#L359), [`:380`](../src/Lucent.Core/Components/TextField/TextField.cs#L380) | Cache boundaries now; choose buffer/history representation against an explicit note-size target | M/L / medium-high / high source confidence |

Do not require a sophisticated text buffer before demonstrating its need. Set realistic supported content sizes and measure them. The current managed performance cycle realizes at most six rows in its fixed viewport ([`Program.cs:246`](../tests/Lucent.Performance.Verifier/Program.cs#L246)); that particular scenario cannot establish behavior for deep layouts or long editable text. Other published performance scenarios were not rerun in this audit.

One integration risk needs a focused probe: [`Components/Scrolling/VirtualizedRegion.cs:135`](../src/Lucent.Core/Components/Scrolling/VirtualizedRegion.cs#L135) uses explicit viewport dimensions or the supplied window viewport before parent arrangement. Nested Grid/container constraints may consequently realize more rows than the actual viewport needs. Confidence is medium for the user-visible consequence; no nested-container reproduction was run. Include viewport-dependent realization in the layout evaluation.

### A5. Complete ordinary desktop input paths

[`WindowsInputAdapter.cs:32`](../src/Lucent.Platform.Windows/WindowsInputAdapter.cs#L32) routes mouse motion/buttons, keys, text and focus but has no mouse-wheel case. [`InputBehaviors.cs:197`](../src/Lucent.Core/InputBehaviors.cs#L197) scrolls using bounded keyboard operations. The portable key set and [`WindowsInputAdapter.cs:334`](../src/Lucent.Platform.Windows/WindowsInputAdapter.cs#L334) support a small editing shortcut set, not ordinary application commands such as Ctrl+F/Ctrl+S/Ctrl+N.

Add wheel/trackpad deltas, targeting and nested-scroll behavior, plus a command/shortcut path suitable for search and capture. Preserve text input, AltGr, focus ownership and clamping. Expose the application-facing command surface to `.lui` rather than routing through sample-specific platform code.

The desktop test at [`PublishedIssueBrowserTests.cs:109`](../tests/Lucent.Desktop.Tests/PublishedIssueBrowserTests.cs#L109) uses UIA value/selection patterns for part of its workflow. Those are useful provider checks, but do not prove wheel or keyboard-adapter delivery. Add a small real-input scenario where those paths change; keep the existing FlaUI/Axe.Windows investment.

### A6. Prove the chosen independent-consumption story

[`Consumer.csproj:3`](../tests/Lucent.Lui.Sdk.Fixtures/Consumer/Consumer.csproj#L3) references a relative checkout Debug build of Core. [`Lucent.Lui.Sdk.csproj:9`](../src/Lucent.Lui.Sdk/Lucent.Lui.Sdk.csproj#L9) packages build tooling, not a complete runtime/host distribution. The SDK proof is useful, but it does not by itself prove a fresh external application's restore/run/publish path. Its consumer also disables AOT analyzers; the separate publish proof must not be confused with analyzer coverage for a normal app.

Keep this aligned with the already planned external sample work. Choose and document a supported pinned dependency route, which may initially be a source checkout/reference rather than public runtime packages. Verify the external app from those documented prerequisites without relying on hidden prior Debug outputs. No public package release or additional repository is required merely to close this review.

## Recommended adjustments to the draft

These are direction recommendations, not additional defects or a set of newly imposed gates.

1. **Add a small corrective slice, then proceed with product work.** Address C1-C5 and the small C9 launch-error path with focused regressions. Design C6/A1 together so retained data updates and `.lui` composition improve as one coherent authoring surface. Do not schedule a full framework rewrite before the application.
2. **Bring authoring sufficiency into each foundation slice.** Before implementing a foundation, write a small representative `.lui` usage example for it: a composed inbox pane, an editor session, a responsive arrangement, and an application command. These are design examples and focused fixtures, not new milestone artifacts. C# services and control internals remain appropriate; repeated C# UI-tree escape hatches are evidence that a required authoring seam is missing.
3. **Resolve two connected contract groups early.** Layout work needs constrained paragraph measurement, responsive participation, session ownership, and measured input cost. Persistence work needs mutation ordering, close negotiation, and independent consumption. These groups can proceed independently once their public interactions are clear, then meet in the working capture/edit/reopen loop.
4. **Retain the lighter verification policy.** Add tests at the boundary where the failure occurs; reuse the existing suites. Expand geometry/performance cases around dense/deep scenes and long content, and add actual durable-order/restart checks. Keep broad appearance, international-input and release walkthroughs as explicit later decisions.

Suggested dependency order: corrective slice -> content/current-item and lifecycle/session decisions -> independent app startup plus constrained layout/text work -> responsive `.lui` inbox/editor -> durable capture/edit/search/reopen -> daily-use polish and evidence-driven authoring conveniences. Input allocation work belongs with the layout foundation, not after the finished UI becomes slow.

Keep named slots, shared style exports, and sibling fragments bounded to the first app's concrete reusable components. Defer hot reload, generic component declarations, a general dependency-injection syntax, a component registry, full CSS semantics, sync and rich text. The existing draft's focus remains appropriate.

## Evidence, coverage and limits

Three parallel read-only sweeps covered runtime/ownership/hosting, layout/text/rendering/presentation, and compiler/generator/SDK/LSP/editor tooling. The coordinating review reopened cited code, checked the application and verification setup, reconciled findings against accepted ADRs, and ran additional probes. Coverage included all primary source package areas; this was not a line-by-line proof of all native interop paths or every test/helper.

Selected existing Release suites were run using `dotnet test --project <project> --no-build --no-restore -c Release`:

| Suite | Passed |
| --- | ---: |
| Lucent.Core.Tests | 68 |
| Lucent.Renderer.Skia.Tests | 4 |
| Lucent.Platform.Windows.Tests | 16 |
| Lucent.IssueBrowser.Tests | 15 |
| Lucent.Lui.Compiler.Tests | 4 |
| Lucent.Lui.Generator.Tests | 6 |
| Lucent.Lui.LanguageServer.Tests | 9 |
| Total | 122 |

These counts are test methods; some contain substantial scenario matrices. **They ran pre-existing Release binaries, not a fresh build of the exact audited commit.** Passing them establishes the available local baseline, not absence of the findings above.

The four current-source VS Code extension tests also passed with `node --test extensions/lucent-lui-vscode/extension.test.cjs`. They do not include the process-launch failure case in C9.

Additional in-memory probes reproduced C1, C2, C3, C6 and the A1 content-name inconsistency, and measured C7. They used existing assemblies and wrote no source files. The Roslyn compilation probe suppressed the host's assembly-version unification warning for its in-memory helper; no project warning policy was changed. Source inspection supports the other findings; native UIA clipping and future durable-write behavior were not reproduced against a running application/database.

`dotnet package list --project Lucent.slnx --vulnerable --include-transitive --no-restore --format json` completed successfully and reported no vulnerable packages across the 20 solution projects against its configured feeds. This is an advisory check, not a supply-chain security certification. The VS Code extension declares no npm dependencies; npm audit had no lockfile/tree to inspect, and nothing was installed.

No source fixes, new NativeAOT build, packaging run, full performance verifier, external consumer setup, live GitHub integration, manual appearance/DPI pass, or Accessibility Insights walkthrough was performed. No new third-party library selection was made or compatibility claim freshly researched. Assessment of dependencies here concerns current boundaries and project metadata, not a recommendation of unverified latest versions.

### Considered and rejected or deferred

- Missing Grid, multiline editing, DI and persistence are already explicit planned work. The findings identify prerequisite contracts and concrete existing behavior that affect their implementation.
- Routing-only `Visible(false)` retaining paint is documented and tested; it is not an implementation violation.
- Full scene reprojection is an accepted initial choice. Repeated subtree lookup, measurement and copying within that choice remain avoidable costs.
- Construction-time scalar parameters, stable component roots, and branch disposal are deliberate. Their implications need usable update/session contracts, not a virtual-tree rewrite.
- A blanket claim that `.lui` wrappers cannot accept children was rejected after the content-name probe. The actual issue is an inconsistent declaration/forwarding contract.
- Automatic animation sampling is intentionally deferred; existing manual transition support does not imply a working host animation clock. Add motion support when a chosen interaction needs it.
- The bounded bidi/IME/accessibility contract does not constitute full international-editor certification. That limitation is not newly discovered.
- CPU rendering, NativeAOT and Windows-first hosting are not reasons to introduce a GPU rewrite or another platform now.
- No npm lockfile finding is warranted for an extension with no dependency tree. No new security blocker was established by the dependency advisory check.
- No blanket coverage percentage, duplicate test harness, milestone acceptance artifact, or mandatory full release run is recommended.
