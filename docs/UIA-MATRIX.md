# Bounded UI Automation matrix

This is the current bounded fixture contract.

| Fixture | UIA control type / name | Required properties | Required patterns and commands | Required events / lifetime |
| --- | --- | --- | --- | --- |
| root | Window / `Lucent Issue Browser` | enabled, control/content element | host WindowPattern and TransformPattern plus fragment root | `WM_GETOBJECT` always returns the same live root provider; no runtime ID requirement |
| group | Group / semantic name | enabled, focus state | none | structure changes for additions/removals |
| text | Text / semantic name | enabled, content/control element | none | no Value, Invoke, SelectionItem, Selection, or Scroll |
| text field | Edit / semantic name | value, enabled, focus | Value; Focus; `SetValue` rejects controls/newlines and stale identity | value property and focus events |
| text area | Edit / semantic name | committed value, enabled, focus | Value; Text/Text2 with document, selection, visible, point and caret ranges; grapheme-safe movement and clipped line geometry; Select and ScrollIntoView | text/selection, value and focus events; ranges expire with their owning element |
| button | Button / semantic name | enabled, focus | Invoke; Focus | focus event; no Value/Selection/Scroll |
| list | List / semantic name | enabled | Selection | structure event and no Invoke/Value/Scroll |
| list item | ListItem / semantic name | selected, enabled, focus | single SelectionItem; Focus; disabled actions return `UIA_E_ELEMENTNOTENABLED` | selected property and focus events; no Value/Invoke/Scroll |
| status | StatusBar / semantic name | enabled | none | structure/property changes only; no actions |
| scroll viewport | Pane / semantic name | enabled, focus, vertical offset/view size | Scroll (`Small/LargeIncrement/Decrement`, `NoAmount`, bounded vertical percent); Focus | focus and scroll property events; no Value/Invoke/Selection |

Runtime IDs are `[3, compositionEpoch, elementId]`: stable for a mounted retained element, never reused across compositions, and independent of semantic generation. Providers are cached by that pair, pruned when absent from the latest snapshot, disconnected on pruning/disposal, and subsequently return `UIA_E_ELEMENTNOTAVAILABLE`. The declared matrix has **zero suppressions**. A future suppression must carry a nonempty reason in the deterministic semantic dump and a negative external UIA test before it is accepted.

ABI evidence is the Windows SDK `10.0.26100.0` `UIAutomationCore.idl` (provider IIDs, vtable order, `VARIANT`/`SAFEARRAY` ownership) and `UIAutomationCoreApi.h` (`UiaReturnRawElementProvider`, event, host-provider, and disconnect signatures). SDL3's `SDL_RegisterEvents`/`SDL_PushEvent` contract is the wakeup evidence; no UIA callback executes Core from `WndProc`.

TextArea publishes an immutable display-text snapshot, including active preedit, with matching paragraph geometry. Value remains committed text. Range selection and scrolling are rejected while preedit is active. Text ranges use UTF-16 offsets normalized to grapheme boundaries after document changes; they do not track application document revisions. Formatting attributes beyond the plain-text read-only state are unsupported. This is a bounded plain-text provider, not rich-text or real-language IME certification.

Controlled list selection belongs to the application. A `Select` command reports `Applied` only when the item is selected after synchronous reactive work settles. `Requested` means the callback ran but selection remains unapplied (including declined or asynchronous requests); it does not change the selected snapshot or emit a selection event by itself. The Windows SelectionItem adapter returns `UIA_E_INVALIDOPERATION` for an unapplied request. Later application state changes publish their actual selection normally.
