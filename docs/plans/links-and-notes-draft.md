# Draft plan: links and notes, application services, and responsive foundations

Status: Revised execution direction, September 4, 2026. Incorporates the architecture/code review and the decision to make `.lui` the primary UI authoring surface. Capabilities below are planned unless explicitly described as current. GitHub Issues and Project 4 own implementation scope, dependencies, status, and acceptance.

Current decisions and implementation are summarized in the [roadmap](../ROADMAP.md) and [daily-use execution](daily-use-execution.md). Light Notes is a public MIT application with exact Lucent package pins, Microsoft.Data.Sqlite storage, and the Lucent-owned managed layout engine. The candidate and readiness tables below preserve the original planning context; they are not the current execution queue.

## Outcome

Build a first-party Windows desktop application that makes it easy to capture a link or thought, add notes, find it later, and open or archive it. The application should be useful enough for regular personal use and excellent in design and interaction quality.

Use that application to strengthen Lucent's framework surface: foundational components, structured and responsive layout, service composition, and local persistence integration. Deliver the `.lui` composition and state contracts needed by each application slice as part of that work. Follow with additional authoring conveniences based on observed friction.

## Agreed direction

- A link-focused inbox also supports standalone notes. Capture and retrieval share a coherent experience.
- The initial application is local and single-user. The owner and coding agents on the owner's Windows machine are the primary audience; another contributor should be able to restore, run, and publish from documented prerequisites.
- Design and UX belong in every slice, including typography, spacing, keyboard operation, focus, resizing, and loading, empty, save, and error states.
- Grid and responsive layout belong in the first application effort. Row/Column should develop into a coherent Flex-style layout surface with shared sizing rules.
- Rich layout, text, and base presentation capabilities are explicit outcomes. Higher-level, source-owned component recipes in the style of shadcn can build on those foundations later.
- Prefer official Microsoft packages and well-maintained libraries for common infrastructure. Lucent should integrate established facilities rather than invent equivalents without a concrete reason.
- Make `.lui` the primary surface for application and reusable UI composition. C# supplies the shared framework contract, application models/services, and advanced control internals; required markup capabilities land with the feature.
- Keep the Windows-first, .NET 10, NativeAOT-compatible direction. Preserve portable Core concepts and the shared framework model behind typed C# and `.lui`.
- Use focused, risk-based verification. Pre-release API changes and explicit bounded risk are acceptable; milestone artifacts and routine full release checks are unnecessary.

## Starting point at planning

The Issue Browser remains a maintained reference application. At the reviewed baseline, Lucent provided retained composition, reactive state and ownership, typed styles, bounded rows and columns, sizing constraints, alignment, positive main-axis growth, scrolling, fixed-height virtualization, and single-line text input. Existing test suites cover managed behavior, published applications, SDK consumption, performance, and accessibility.

Grid, a complete Flex-style sizing model, responsive composition, multiline editing, DI/Generic Host integration, and application persistence were the planned additions. See the current [roadmap](../ROADMAP.md) for the implemented boundary. Existing single-line editing and international-text guarantees do not imply a complete multiline or bidirectional editor.

The current [architecture](../ARCHITECTURE.md) and [authoring ADR](../adr/0002-lui-authoring-surface.md) describe the supported boundary. Extend those documents when an implementation decision changes that boundary. Keep the [glossary](../../CONTEXT.md) for canonical terms rather than execution notes.

## Architecture review and corrective work

The [September architecture and code review](../../plans/architecture-review.md) examined `cc1a0ae`, including grammar, component composition, runtime ownership, layout/text, platform input, accessibility, and tooling. It found a viable direction plus concrete defects and first-application prerequisites. Its probes used existing Release binaries; it is evidence for the work, not a new acceptance gate.

Start with focused fixes for structural C# expression parsing (`<` and generic calls), compiler/editor component eligibility, extension launch failures, default text-field sizing, wake-observer failure containment, and accessibility application identity/clipping. These independent fixes can run alongside product design. Retained current-item propagation and `.lui` content declaration/forwarding form the next authoring foundation.

Address per-event tree searches and property hashing before scaling the UI. Characterize deep layout measurement, nested scene copying, and long-text shaping/editing as part of the relevant foundations. Preserve stale-scene checks, ownership, and immutable publication; avoid a separate speculative optimization program.

## `.lui` authoring contract for the first application

- Design each new UI capability through a representative `.lui` usage before settling the underlying API. Exercise composed panes, live rows, editor sessions, responsive arrangements, and commands through markup and the existing compiler/editor suites.
- Establish consistent default-content declaration and forwarding, including composition around header/body/footer content. Remove the undocumented dependence on a parameter being named `content`. Evaluate a small named-slot or sibling-fragment contract only where the designed shell needs it; avoid wrappers that exist solely to satisfy grammar and change Grid placement.
- Keep the retained runtime model. Make construction-time values versus live readers understandable in documentation and tooling. Supply current-item readers or an equivalent update seam for same-key immutable replacements, including `.lui foreach` and a defined policy for conditional pattern locals.
- Bind editing and viewport state through specific, explicitly owned session contracts. Do not remount controls merely to synchronize text or introduce general mounted-component handles.
- Use explicit application models, commands, and composition-root service injection initially. Local-state, injection, two-way-binding, and live-reader syntax conveniences follow demonstrated need; asynchronous commands still require ownership, cancellation, and error behavior now.
- Keep source maps, freshness, completion, diagnostics, formatting, and both authoring surfaces aligned when grammar or APIs change. Correctness and sufficient composition are first-plan work; hot reload, broad language expansion, and component-copy tooling remain follow-ups.

## Product scope and experience

The first complete loop is: capture a link or standalone thought, edit its title and plain-text note, retrieve it through the inbox or search, then open its link or archive it. Preserve captured content across application restarts. Provide understandable save feedback and a recoverable failure experience.

Proposed initial capture is paste or explicit entry in the application. Drag/drop, global quick capture, and browser integration are follow-on options; the exact capture interaction remains a design choice. Search initially covers saved titles, URLs, and note text. Organization starts with inbox and archive; richer organization needs a demonstrated use case.

Explore the visual direction before substantial UI implementation. Produce representative screens with realistic short and long content, including an empty inbox and an editing state. Choose typography, spacing, information hierarchy, and a consistent treatment of interactive states together.

| Available space | Proposed application arrangement |
| --- | --- |
| Wide | Navigation, inbox, and editor visible together |
| Medium | Compact navigation with inbox and editor |
| Compact | Inbox or editor with clear back navigation |

Derive breakpoints and the supported minimum window size from usable content and controls. They are not fixed by this draft. A compact layout must retain access to the same core tasks. Test movement between arrangements while a note is being edited, rather than treating each arrangement as a separate static screenshot.

## Layout and component foundation

### Shared sizing and measurement

Establish one documented model for constraints, intrinsic measurement, assigned size, overflow, and device-scale rounding. Grid, Flex-style layout, text, and scrolling must agree on that model. Define behavior when content exceeds the available size and when a scroll direction supplies unbounded space.

Define constrained paragraph requests and line/run results with logical ranges, per-line metrics, caret/selection geometry, and reusable painting before freezing layout or multiline APIs. Develop wrapped text measurement alongside layout: a change in available width changes line breaks and desired height. Measurement and painting must use compatible shaping results. Keep layout results usable by input hit testing, accessibility bounds, and diagnostic dumps.

### Grid

Proposed first-pass coverage:

- Explicit row and column tracks with fixed, content-sized, and weighted remaining-space sizing.
- Track minimum/maximum constraints, row and column gaps, explicit cell placement, and row/column spanning.
- Container and per-child alignment, nested layouts, and defined overflow behavior with long content.

Decide precise track-sizing semantics during the layout evaluation. Familiar XAML star sizing and CSS fractional tracks are references, not interchangeable specifications. Do not promise full CSS Grid compatibility. Advanced automatic placement, subgrid, and a large track-expression language are deferred.

### Flex-style layout

Retain Row and Column as convenient authoring operations over shared behavior. Target grow, shrink, basis, wrapping, gaps, and container/per-child alignment. Define min/max handling and overflow explicitly, including the effect of text's intrinsic size.

Keep the supported subset coherent and documented. Avoid separate sizing algorithms for convenience components and the general layout surface. Existing fixed-height virtualization must continue working inside constrained Grid and Flex layouts; variable-height virtualization is a separate scope decision.

### Responsive composition

Use fluid sizing for continuous changes and explicit arrangement changes for meaningful thresholds. Expose available width and height in logical layout units through typed C# and `.lui` capabilities. Prefer the constraints available to the relevant container over assumptions about monitor size.

Specify which container provides responsive constraints and how those values become available. Avoid feedback loops where a child changes its parent size, which changes the child's breakpoint indefinitely. Begin with a bounded, well-defined container contract rather than general runtime CSS queries.

Define layout/paint/input/semantic participation explicitly; current `Visible` is routing-only and must not silently acquire collapse semantics. Preserve note drafts, caret/selection, undo/redo, and relevant scroll state across arrangement changes. Keep editor-session state independent from mounted platform resources and specify IME cancellation/transfer. Move focus deliberately when its previous target disappears, and keep keyboard and accessibility order understandable. Existing retained identity does not automatically guarantee arbitrary cross-parent reparenting: choose explicit state ownership and define the required composition behavior before implementing such moves.

### Text, presentation, and interaction

Build the capabilities the designed screens need as reusable Lucent features:

- Readable typography, wrapping, selection, clipboard behavior, and multiline plain-text editing with caret navigation, undo/redo, scrolling, and appropriate input and accessibility integration.
- Composable surfaces, borders, clipping, icons, and consistent hover, pressed, focused, disabled, and selected states across themes and scaling settings.
- Focus, keyboard, selection, and semantic behavior that works consistently across the components used by the application.
- Wheel/trackpad scrolling with nested viewport targeting and clamping, plus application commands such as find/capture/save exposed through `.lui`. Keep real input delivery distinct from UIA pattern invocation.
- Input and accessibility hit testing that respect the same clipping and responsive participation, with application-specific accessible identity.

Prioritize meaningful combinations and edge cases over the number of named controls. Add focused examples to the existing repository structure where they improve reuse or regression coverage. Keep application policy in the sample; resolve foundational gaps in Lucent instead of adding sample-specific layout or input workarounds.

## Ecosystem and module ownership

| Area | Starting direction | Work needed before committing to the design |
| --- | --- | --- |
| DI and application services | Microsoft.Extensions dependency injection, hosting, logging, and configuration | Map startup, shutdown, cancellation, service disposal, and UI-thread ownership onto Lucent's lifecycle. Choose an integration boundary and explicit service lifetimes. |
| Local data | SQLite through Microsoft.Data.Sqlite | Prove the selected package/native assets in a published NativeAOT application; define a small application-owned storage layer and schema-upgrade approach. |
| Layout engine | Evaluate Taffy against extending the current implementation | Exercise Grid, Flex, wrapped-text measurement, constraints, and rounding; assess .NET interop, NativeAOT packaging, ownership, and maintenance cost. No engine is selected yet. |
| Other infrastructure | Official or well-maintained libraries when they fit a concrete need | Check maintenance, licensing, compatibility, and integration cost before adding a dependency. |

Microsoft's Generic Host provides common application services and lifecycle facilities. Lucent's work is coordinating those with desktop hosting and composition. Do not equate DI scopes with every component scope by default or introduce service-location calls throughout UI code; derive the access and lifetime model from the application. Implement startup, negotiated close request, accepted-work drain, asynchronous service stop/disposal, and final window/composition teardown while retaining deterministic UI-thread ownership. Keep the window available for actionable save failures; cancelling obsolete reads must not discard accepted writes.

The application owns its data model, queries, and schema evolution. Keep disk operations off the UI's critical path and reject stale results through existing ownership/generation mechanisms. Stale-result rejection does not order side effects. Use application-owned per-record serialization/coalescing or revision-conditional writes and verify final durable state after reverse completion. Treat a save as successful only after the required durable operation completes; preserve an understandable retry path on failure. Select a modest backup/export and upgrade-recovery approach before relying on the application for valuable notes.

Microsoft.Data.Sqlite is a candidate, not a requirement imposed on every Lucent application. Higher-level data access remains open; EF Core's documented NativeAOT support is experimental and needs a concrete compatibility case before selection.

Taffy is written in Rust and implements Grid and Flexbox. An external native engine would require an explicit integration boundary: the current architecture keeps native-binding packages out of Core. Do not add a direct Core binding or silently relax architecture checks. Resolve the boundary and record a durable architectural decision if adoption warrants it. Likewise, leave the layout engine's own node handles and style representation out of the public authoring surface.

Record dependencies and adopted architectural inspirations in [CREDITS.md](../../CREDITS.md) before adoption, including version/commit, license, and distribution requirements. References below are candidates and conceptual sources, not newly adopted dependencies.

## Ordered implementation sequence

These are useful work slices, not release milestones. Ticket order expresses priority; native blocker links express prerequisites. Independent corrective fixes and design can proceed in parallel, as can hosting/storage and layout exploration after their inputs are available.

1. **Correct current defects and design the experience.** Fix the bounded parser, tooling, field, wake, and UIA issues. Produce wide/medium/compact designs with realistic content and `.lui` usage sketches. Select capture behavior, minimum usable size, and required component/presentation coverage.
2. **Establish authoring and lifetime contracts.** Add retained current-item updates and coherent `.lui` content composition. Define and implement negotiated hosting/service lifecycle and hoistable editor/viewport sessions with responsive participation. Resolve constrained paragraph measurement and the layout-engine choice through a bounded evaluation.
3. **Prove independent startup and reduce input cost.** Reuse issue #62 for the first-party application's pinned external setup, without a separate HelloLucent application or speculative multi-sample infrastructure. Establish ordered local persistence and create/read/restart behavior. Replace redundant per-event lookup/hashing while preserving stale-input rejection.
4. **Implement reusable layout, text, and desktop input.** Deliver the selected Grid/Flex subset, wrapped paragraph measurement, constrained virtualization, wheel/trackpad and commands, and multiline editing. Use `.lui` examples and realistic content sizes as the design surface. Address measured measurement/copy/shaping costs within the affected module.
5. **Build the responsive inbox/editor and durable workflow.** Compose the designed shell in `.lui`, then complete capture, edit, search, open, archive, save feedback, orderly close, and reopen. Preserve session state and keyboard access through arrangement changes.
6. **Refine for daily use and prioritize further authoring improvements.** Exercise real data upgrade/recovery and modest export/backup, long content, selected DPI/themes, and targeted accessibility. Resolve visual and interaction friction throughout earlier slices as well. Select later authoring conveniences from observed repetition; do not pre-approve a broad syntax rewrite or component registry.

The ordered ticket index below is maintained with GitHub issue links. Implementation specifications live in those tickets. The external app is now Light Notes in the public RichiCoder1/light-notes repository, MIT licensed and pinned to Lucent prerelease NuGet packages. The original planning-readiness column below is historical; GitHub owns completion and active-work status.

Tracking issue: [#63 — .lui-first links and notes](https://github.com/RichiCoder1/lucent/issues/63). All execution tickets are sub-issues in [Project 4](https://github.com/users/RichiCoder1/projects/4), with native blocker relationships. The table is priority order; dependencies permit parallel work. GitHub owns live status.

| Order | Ticket | Blocked by | Readiness at planning |
| --- | --- | --- | --- |
| 1 | [#64 Parse comparisons and generic calls in structural headers](https://github.com/RichiCoder1/lucent/issues/64) | None | Ready after blockers |
| 2 | [#65 Align component discovery and handle language-server launch failures](https://github.com/RichiCoder1/lucent/issues/65) | None | Ready after blockers |
| 3 | [#66 Preserve intrinsic text-field size when an empty field gains focus](https://github.com/RichiCoder1/lucent/issues/66) | None | Ready after blockers |
| 4 | [#67 Prevent observer failures from suppressing async host wakes](https://github.com/RichiCoder1/lucent/issues/67) | None | Ready after blockers |
| 5 | [#68 Correct application accessibility identity and clipped point lookup](https://github.com/RichiCoder1/lucent/issues/68) | None | Ready after blockers |
| 6 | [#69 Design the links-and-notes experience and representative .lui composition](https://github.com/RichiCoder1/lucent/issues/69) | None | Ready after blockers |
| 7 | [#70 Propagate current payloads through retained keyed regions](https://github.com/RichiCoder1/lucent/issues/70) | None | Ready after blockers |
| 8 | [#71 Define consistent component content declaration and forwarding](https://github.com/RichiCoder1/lucent/issues/71) | [#64](https://github.com/RichiCoder1/lucent/issues/64), [#69](https://github.com/RichiCoder1/lucent/issues/69) | Ready after blockers |
| 9 | [#72 Integrate application services with negotiated asynchronous shutdown](https://github.com/RichiCoder1/lucent/issues/72) | [#67](https://github.com/RichiCoder1/lucent/issues/67) | Ready after blockers |
| 10 | [#73 Preserve editor sessions and define responsive participation](https://github.com/RichiCoder1/lucent/issues/73) | [#66](https://github.com/RichiCoder1/lucent/issues/66), [#69](https://github.com/RichiCoder1/lucent/issues/69) | Ready after blockers |
| 11 | [#74 Resolve constrained paragraph measurement and Grid/Flex integration](https://github.com/RichiCoder1/lucent/issues/74) | [#69](https://github.com/RichiCoder1/lucent/issues/69) | Ready after blockers |
| 12 | [#75 Replace repeated tree lookup and hashing during event dispatch](https://github.com/RichiCoder1/lucent/issues/75) | None | Ready after blockers |
| 13 | [#62 Establish pinned independent setup for the links-and-notes app](https://github.com/RichiCoder1/lucent/issues/62) | [#69](https://github.com/RichiCoder1/lucent/issues/69) | Needs repository/license decisions |
| 14 | [#76 Add ordered local persistence and durable startup](https://github.com/RichiCoder1/lucent/issues/76) | [#72](https://github.com/RichiCoder1/lucent/issues/72), [#62](https://github.com/RichiCoder1/lucent/issues/62) | Ready after blockers |
| 15 | [#77 Implement shared Grid, Flex and constrained paragraph layout](https://github.com/RichiCoder1/lucent/issues/77) | [#74](https://github.com/RichiCoder1/lucent/issues/74), [#71](https://github.com/RichiCoder1/lucent/issues/71), [#73](https://github.com/RichiCoder1/lucent/issues/73), [#75](https://github.com/RichiCoder1/lucent/issues/75) | Refine after upstream work |
| 16 | [#78 Add wheel scrolling and .lui application commands](https://github.com/RichiCoder1/lucent/issues/78) | [#69](https://github.com/RichiCoder1/lucent/issues/69), [#73](https://github.com/RichiCoder1/lucent/issues/73) | Ready after blockers |
| 17 | [#79 Implement a reusable multiline editor over owned sessions](https://github.com/RichiCoder1/lucent/issues/79) | [#77](https://github.com/RichiCoder1/lucent/issues/77), [#73](https://github.com/RichiCoder1/lucent/issues/73), [#78](https://github.com/RichiCoder1/lucent/issues/78) | Refine after upstream work |
| 18 | [#80 Build the responsive .lui inbox and editor shell](https://github.com/RichiCoder1/lucent/issues/80) | [#62](https://github.com/RichiCoder1/lucent/issues/62), [#71](https://github.com/RichiCoder1/lucent/issues/71), [#70](https://github.com/RichiCoder1/lucent/issues/70), [#77](https://github.com/RichiCoder1/lucent/issues/77), [#78](https://github.com/RichiCoder1/lucent/issues/78), [#68](https://github.com/RichiCoder1/lucent/issues/68) | Refine after upstream work |
| 19 | [#81 Complete durable capture, editing, search, open and archive](https://github.com/RichiCoder1/lucent/issues/81) | [#80](https://github.com/RichiCoder1/lucent/issues/80), [#79](https://github.com/RichiCoder1/lucent/issues/79), [#76](https://github.com/RichiCoder1/lucent/issues/76) | Refine after upstream work |
| 20 | [#82 Refine daily-use design, recovery and accessibility](https://github.com/RichiCoder1/lucent/issues/82) | [#81](https://github.com/RichiCoder1/lucent/issues/81) | Refine after upstream work |
| 21 | [#83 Select the next .lui improvements from application friction](https://github.com/RichiCoder1/lucent/issues/83) | [#82](https://github.com/RichiCoder1/lucent/issues/82) | Refine after upstream work |

Design #69 and independent corrective tickets can start immediately. Layout decisions #74, hosting #72, session work #73 and input performance #75 can progress on separate tracks as their blockers clear. Issue #62 established Light Notes independently; its repository, name, MIT license and package-consumption decisions are resolved.

## Verification approach

Use [TESTING.md](../TESTING.md) and the [pre-release verification policy](../agents/verification.md). Tests should observe user-visible or public-contract behavior and use existing suites and seams where possible.

- **Layout and responsiveness:** focused geometry and interaction checks for track sizing, spans, growth/shrinkage, wrapping, nested scrolling, long text, and widths immediately around breakpoints. Check selected DPI scales and both resize directions where they affect geometry or state.
- **Editing and persistence:** meaningful editing regressions and temporary-database integration tests for create/update/retrieve/archive, restart, schema upgrades, and failure handling. Include reverse-order saves verified by rereading the durable store, close during accepted writes, async service shutdown/failure, and preservation of draft/caret/selection/undo/scroll during responsive transitions.
- **Hosting and dependencies:** focused published NativeAOT startup, shutdown, failure cleanup, and package-inventory checks when integration or dependencies change.
- **Application interaction:** use the existing FlaUI driver for key capture/edit/find/restart paths where automation is practical. Use Axe.Windows for targeted scans and retain precise existing UIA contract tests. Automated scans do not claim a manual Accessibility Insights walkthrough.
- **Design:** inspect representative real application states and responsive transitions. Add targeted visual and keyboard checks to the affected work; broad manual walkthroughs remain release decisions.
- **Authoring and performance:** run affected compiler/SDK checks for new authoring capabilities and measure performance only for credible layout, text, or tooling risks. Cover default-content forwarding, same-key replacement, and structural expressions through `.lui`. Measure event allocation, deep layout, and long text only where relevant. Reuse passing evidence when intervening changes cannot affect it.

Choose relevant checks per change, retain warning and NativeAOT/trimming correctness, and record limitations honestly. Do not introduce fresh milestone artifacts, mandatory full-suite runs for every slice, or duplicate test harnesses.

## First-chunk success

The owner can capture and edit a link or standalone note, close and reopen the published application, find the content, and open or archive it. The core tasks work in the designed wide, medium, and compact arrangements without losing drafts or making navigation inaccessible. The visual and interaction design is suitable for regular use.

The application consumes Lucent independently and demonstrates a practical DI and local-storage pattern. Its UI and reusable application components are authored primarily in `.lui`; layout, text, presentation, commands, and session capabilities remain ordinary shared framework contracts. Durable writes retain ordering and close preserves accepted edits. Remaining limitations are documented, and observed authoring friction informs the next work.

## Deferred scope

Sync, collaboration, automatic page extraction, rich-text editing, elaborate organization, browser integration, global capture, advanced Grid features, full CSS compatibility, and a comprehensive high-level component catalog are follow-on work. Drag/drop and other desktop controls can be reconsidered when a selected flow justifies their cost.

Stable public API compatibility, a 1.0 promise, production GPU rendering, additional operating systems, and exhaustive international-input/accessibility certification retain their existing deferred status. Responsive design here targets resizable Windows application and container space; it does not imply a mobile-platform commitment.

## Open decisions to resolve during design and implementation planning

| Decision | How to resolve it |
| --- | --- |
| Visual direction, exact capture flow, breakpoints, and minimum size | Compare a small set of realistic screen designs and exercise the capture/edit/find flow. |
| Layout engine and precise Grid/Flex semantics | Use the bounded engine evaluation, current Core constraints, and text/scrolling examples. |
| Responsive constraint API and state ownership | Work through a focused editor while crossing arrangement boundaries; identify lifecycle changes explicitly. |
| DI/hosting API and module boundary | Demonstrate an injected application service from startup through failure and orderly close. |
| Storage access, migrations, and recovery | Prove the small real data model and upgrade/restart/failure paths with the selected packages. |
| Application name, sample repository shape, and license | Resolve before external repository creation/publication; preserve the existing samples direction. |
| Exact authoring QoL follow-up | Select from repeated, observed friction rather than a speculative rewrite. |

## Reference material

These primary sources informed the draft. Recheck exact versions and compatibility when selecting dependencies.

- [Microsoft responsive layouts and Grid sizing](https://learn.microsoft.com/en-us/windows/apps/develop/ui/layouts-with-xaml).
- [Microsoft responsive design techniques](https://learn.microsoft.com/en-us/windows/apps/design/layout/responsive-design).
- [Taffy layout engine](https://github.com/DioxusLabs/taffy).
- [.NET Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host).
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/).
- [EF Core NativeAOT limitations](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries).

## Experience design

The [experience design](../design/links-and-notes/README.md) expands this plan into responsive arrangements, keyboard and save flows, and clearly marked proposed `.lui` composition. See the [visual board](../design/links-and-notes/VISUAL.md) for the initial wide, medium, and compact designs. Exact APIs and final visual tokens remain implementation decisions.
