# Issue Browser

<!-- impeccable:product-schema 1 -->

## Platform

Windows desktop, authored with Lucent `.lui` and the Windows host. This is a native desktop reference application, not a web surface.

## Users and purpose

Lucent authors and the project owner use Issue Browser to evaluate whether stock controls and theming can produce an attractive, usable application with minimal authoring. The primary workflow is finding an issue, reading its details, and taking an appropriate action while preserving browsing context.

## Capabilities and constraints

- Keep deterministic fixture-backed data and meaningful interaction: filtering, selection, details, status changes, retries and context menus. Live GitHub authentication, networking and mutation are outside this design slice.
- Use Lucent's stock theme and supported Windows presentation. Do not override colors or tokens in application code. Keep application styles limited to necessary layout.
- Use a resizable issue list beside a detail pane at desktop widths; narrow windows use list/detail navigation with preserved selection and browsing state.
- Demonstrate realistic content and loading, empty, failed and stale-data states. Preserve the existing 10,000-row virtualization workload.
- Develop and evaluate the design with real Lucent controls. Application-only decoration must not substitute for missing framework defaults.

## Product principles

1. Make the ordinary authoring path produce a polished application.
2. Improve shared defaults when recurring visual gaps appear.
3. Keep a separate optional minimal/reset base for custom design systems; polished controls remain the default.
4. Preserve keyboard, pointer and accessibility behavior across styling choices.
5. Keep Windows-specific presentation in the platform adapter and portable concepts in Core.

## Evidence on hand

The maintained application already exercises deterministic async loading, errors/retries, filtering, optimistic status mutation, virtualized selection, nested menus and adaptive list/detail navigation. Headless tests exercise application state and composition; published desktop tests establish actual Windows behavior. Fixture content must remain clearly fictional rather than presented as live repository data.
