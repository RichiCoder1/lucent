# Stock-theme Issue Browser

Accepted direction, September 8, 2026. Follow the delivery of #101 with submenu support and a complete Issue Browser design that proves Lucent's default presentation.

## Design brief

The application serves Lucent authors evaluating a practical desktop workflow. Its primary task is finding an issue, reading it, and acting without losing place. Keep fixtures interactive and realistic, including the existing large-list workload; live GitHub integration is a separate future slice.

Use Lucent's stock theme with supported Windows-native presentation. Application code must not define or override palette tokens, colors, control-state appearance, typography or decorative surfaces. Application styling should only express layout needs such as spacing, pane constraints and responsive composition. Shared component roles and framework density presets are preferable to repeated per-control sizing.

The main window presents a compact command/filter area above a resizable list/detail workspace. The list emphasizes issue title and secondary metadata, with result counts and an explicit empty state. The detail pane provides readable wrapped content, contextual actions, status feedback and independent scrolling. At narrow widths, navigate between list and detail while retaining selection, filters and scroll position. All controls use shared hover, focus, pressed and disabled states.

Develop directly in Lucent. Validate desktop and narrow layouts, long content, empty/loading/error states and both light/dark defaults. Use high-contrast contracts and targeted native checks for platform behavior; broad manual certification remains a release decision.

## Execution sequence

1. **Submenus (#104):** `.lui` authoring, branch ownership and keyboard behavior, popup windows beyond the owner, actual-placement safe triangles, native menu trees and accessibility. Keep Windows tracking independent from Lucent's own keyboard and pointer handling.
2. **Shared presentation (#105):** expose the smallest coherent stock surface and text roles and consistent control density needed by the design. Preserve attractive defaults and add an explicit optional minimal/reset base that retains interaction and accessibility. Platform presentation stays opt-in and respects author overrides.
3. **Accessible split pane (#106):** stable content regions, retained preferred extent, pointer capture, keyboard resizing and a Windows UIA numeric-range contract. Clamp current geometry without erasing user intent when the window temporarily shrinks.
4. **Reference application (#107):** remove application palette overrides, compose the adaptive workspace from shared controls, refine fixture content and complete the interaction/state design. Add genuine nested actions instead of decorative submenu examples.
5. **Light Notes adoption ([light-notes#2](https://github.com/RichiCoder1/light-notes/issues/2)):** consume the verified immutable framework packages and tooling revision, preserve its existing identity and data behavior, and check note-row/editor menus and affected controls with temporary fixtures.

The implementation tickets record the final API scope. Do not expand this into a full native-widget hierarchy or a general component catalog. The no-theme-override constraint applies to Issue Browser; Light Notes keeps its own accepted design.

The design pass exposed the wrapped-row auto-height issue corrected in [#108](https://github.com/RichiCoder1/lucent/issues/108): nested auto-sized ancestors now receive the wrapped height at the constrained width. Issue Browser retains its deliberate wide/narrow filter arrangements. The separate [style-driven layout discussion](style-driven-layout-draft.md) explores a generic container and ergonomic container conditions over these existing layout properties; it is not an accepted implementation plan.

## Verification and ownership

Use deterministic headless tests for submenu lifecycle, input trajectories, timeouts, state preservation and responsive composition. Use focused Windows tests for popup chains, focus, UIA, native tracking, pointer capture and resizing. Inspect the actual rendered app in bounded visual passes.

Language-server tests should own special authoring fixtures rather than requiring decorative application styles. Preserve real-project navigation and completion coverage as the app structure changes. Record verification, limitations and publication identities in the implementation tickets.
