# Remaining-work execution refinements

Status: the M7, resize, #58, and #59 slices below were completed on 2026-09-04. These are the execution refinements used for that work. Future cleanup/testing execution is tracked in [#61](https://github.com/RichiCoder1/lucent/issues/61); use the current [verification policy](verification.md).

These notes refine `remaining-work-handoff.md`; GitHub issue acceptance remains authoritative. Complete M7 before implementing resizing, #58, and #59 in that order. File the cleanup/testing issue afterward. These are implementation directions, not claims of completion. After M7, apply the [pre-release verification policy](verification.md) instead of the original full-gate-per-issue requirement.

## Resize: prove viewport coverage before choosing a sizing API

The defect spans both axes. Application styles fix widths to 800 and the main column height to 500. The authored root is mounted below `Composition.Root`; `SceneLayout` measures children along the main axis from their content. Removing explicit dimensions can restore horizontal stretch while still leaving the main column shorter than the viewport.

1. File a focused resize issue before editing. Add one application-level regression beside `VisualSurface` in `tests/Lucent.IssueBrowser.Tests/Program.cs` using the real `IssueBrowserStructure` composition.
2. Project the same composition at 800x500, a larger width and height, then back to the original size. Include non-integer DPI scaling. Assert the authored root covers its allotted viewport, header/list/row widths follow it, and the newly exposed right and bottom areas paint the expected page/header colors.
3. Preserve element identity, selection, focus, and bounded realization across resizing. Keep the list's existing height/density policy and field widths unless evidence shows they prevent the requested behavior; responsive wrapping and list redesign are separate work.
4. First evaluate the existing root-allocation and layout-property seams. If a fill capability is missing, define the smallest bounded sizing contract with this application as its consumer. Do not add viewport subscriptions or geometry assignments to application code. Do not silently assign new behavior to `MainAlignment.Stretch`: current main-axis documentation only promises start/center/end placement.
5. Keep original-size pixel/behavior parity. Any diagnostic-hash change must be explained by the intended property/provenance change, not accepted by blindly replacing the expected hash.
6. Run the affected application/Core tests and a fresh published application pixel/resize smoke. Use the full gate only if the chosen layout change creates cross-cutting risk that these checks cannot contain.

## #58: scalar expression children

Treat `<Text>{expression}</Text>` as another spelling of the existing scalar `[DefaultContent]` argument. Reuse Roslyn parsing/conversion, candidate binding, ordinary generated calls, source maps, and freshness identity.

- Support exactly one expression child. `<Text>\n  {value}\n</Text>` treats surrounding indentation/newlines like other structural children. Retained non-whitespace literal children remain text. Test expression-only, expression-plus-comment, `prefix {value}`, `{value} suffix`, and two expressions with parser/formatter round trips.
- Reject mixed literal/expression content, multiple expressions, structural siblings (element, if, or foreach), ambiguous default-content targets, and expression-produced `ComponentContent`. When children supply the selected default-content parameter, reject an explicit attribute with that parameter name, including names other than `content`, with a deterministic source diagnostic.
- Preserve the distinction between a construction-time scalar and an explicitly live delegate: do not implicitly stringify values or make plain expressions reactive.
- Prove generated-call and managed/NativeAOT parity with the equivalent named argument. Cover normal conversions, hard type errors, preserved nullable-warning diagnostics, incomplete editor input, exact diagnostic/source spans, rename/references inside the expression, and stable document/range formatting. Keep warning-as-error enforcement at the existing build/gate layer.
- Use existing parser/compiler/generator/LSP fixtures and the SDK/NativeAOT consumer proof. Recheck affected tooling budgets when there is a credible performance impact. Avoid a separate grammar or runtime evaluator; unrelated renderer/UI gates are not routine requirements for this compiler slice.

## #59: application lifecycle

The builder must own a lifecycle, not merely wrap the existing entrypoint's three calls. Resolve the recipe/state/theme creation order against the actual Issue Browser before freezing a public interface.

- `UseWindows()` explicitly selects the Windows adapter. Core must not reference Windows types or discover hosts at runtime. No DI or service registry is part of this slice. Preserve the existing title and exit behavior with an explicit builder title (for example SetTitle), an integer Run result, and cleanup before propagating a host exception to the entrypoint.
- First prove `Run(ComponentRecipe)` using the existing `ComponentRecipe.Create` operation: the recipe root already owns a scope and its composition context exposes the theme. Create Issue Browser state when that root mounts and mount the generated UI through the existing recipe operation. Add an application-context API only if this real consumer demonstrates a missing capability. Allocate exactly one graph, composition, and effective theme context, with cleanup for recipe/mount failure, host startup failure, and ordinary close.
- Prefer a one-shot built application: reject a second Run deterministically, while a new builder produces a fresh lifecycle. A built-but-never-run application should own no live scopes or platform resources. Validate the explicit host before allocating runtime resources.
- Prove `SetTheme(Func<ThemeAppearance, Theme>)` against the real consumer. Refactor the current multi-argument AppTheme.Create into an appearance-oriented factory. Keep exactly one writer of the effective theme: first evaluate keeping density as application-state-driven style values through existing live bindings, leaving the application lifecycle to own appearance-to-theme updates. If density remains a theme layer, explicitly prove its composition/update ordering before implementation; two effects that independently overwrite ThemeContext.Theme are unacceptable. Preserve light/dark/high-contrast, density restyling, and platform-settings behavior.
- The migrated Issue Browser entrypoint must no longer create a graph/composition or call raw `WindowsBootstrap`; do not move that ceremony into another application helper and call the issue complete.
- Test lifecycle through the public application entry seam and test-owned host adapters where useful. Retain NativeAOT, package inventory, actual Windows close/startup behavior, and external UI proofs.

## Cleanup/testing follow-up

Inventory and map existing assertions before selecting a test framework or replacing runners. Migrate one representative group first and demonstrate that its meaningful negative cases still fail. Preserve test-owned published NativeAOT/UIA hosts, source/package identity checks, and a single local/CI entry path. Evaluate Appium only where it reaches the needed Windows observations. Clarify "FastPass" before evaluating it. Delete old documents/runners only after durable content, assertions, artifacts, and inbound links have explicit replacements.