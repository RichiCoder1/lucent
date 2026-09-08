# Headless tests and Windows presentation

Selected September 7, 2026. Execution order: [#103](https://github.com/RichiCoder1/lucent/issues/103), then [#101](https://github.com/RichiCoder1/lucent/issues/101). The owner closed #91 after nonrecurrence; its intermittent Archive exit has no confirmed root cause.

## Execution status

#103 and #101 are delivered, including the separately packaged headless harness, representative compiled `.lui` tests, and opt-in Windows presentation. Issue Browser exercises stock control presentation and `.lui` row commands in both menu modes. See [Windows presentation](../WINDOWS-PRESENTATION.md) for the selected API and limits.

Focused desktop checks cover Lucent popup depth and outside-owner placement, plus native keyboard, pointer and UIA command invocation, preserved selection, Escape dismissal and owner shutdown with a menu open. Diagnosis separated Windows menu navigation from Lucent's Home/End behavior: native tests use arrow keys and verify foreground activation. Opt-in diagnostics distinguish cancellation from semantic command results. The native return path also completes owner clipboard requests and schedules a frame. Headless contracts cover command invocation without popup layout; native evidence comes from the published application. Detailed results and delivery identities belong in #101.

## Headless interaction tests

Extract shared test setup into `Lucent.Testing`, with optional real rasterization in `Lucent.Testing.Skia`. Each application owns its UI thread, production application session, composition and input router. Test operations marshal to that owner; test clocks and platform services are controlled explicitly. Bounded queue draining reports failure rather than waiting indefinitely for self-reposting callbacks. The harness cannot preempt arbitrary blocking user code.

Start with compiled `.lui` editor, menu and debounce scenarios. Assertions use semantics, focus, selection, geometry and resolved presentation; pixel captures are optional. Keep representative low-level contracts. Include the new suite in ordinary local/CI verification and distribute the harness separately from application runtime packages.

## Windows presentation

Preserve customizable Lucent rendering as the default. An explicit Windows option may prefer native menus for the supported flat command/separator shape. Custom `.lui` content retains the Lucent popup rather than losing content or changing application commands. Recheck command availability at invocation, preserve the original application owner and right-click selection, and release native resources on every dismissal path.

Improve Lucent menus as well: a distinct themed surface, visible edge, restrained rounding, readable state colors and separators, and popup depth without clipping the shadow. Offer platform-oriented scrollbar defaults through the existing style system; application styles retain precedence. This slice does not replace Lucent scroll viewports with native child controls or introduce a general native-widget hierarchy.

Verify menu conversion and themes headlessly first. Retain focused Windows checks for actual native accessibility, popup placement outside owner bounds, keyboard/pointer dismissal, command targeting, and owner shutdown. Record final supported limits and evidence in the tickets; do not infer native transport behavior from headless tests.

## Nested menus

[#104](https://github.com/RichiCoder1/lucent/issues/104) tracks nested menus with safe-triangle pointer intent, including diagonal travel, left-opening placement, bounded grace timing and independent keyboard navigation. Current menus are flat; #101 does not claim submenu behavior. Issue Browser will remain the stock-theme demonstration for both presentation paths.
