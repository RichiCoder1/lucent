# M3 bounded UI Automation matrix

This is the complete M3 fixture contract. It is intentionally limited to the retained controls already present in the Issue Browser shell; it is not a virtualized-list or full application certification matrix.

| Fixture | UIA control type / name | Required properties | Required patterns and commands | Required events / lifetime |
| --- | --- | --- | --- | --- |
| root | Window / `Lucent Issue Browser` | enabled, control/content element | host WindowPattern and TransformPattern plus fragment root | `WM_GETOBJECT` always returns the same live root provider; no runtime ID requirement |
| group | Group / semantic name | enabled, focus state | none | structure changes for additions/removals |
| text | Text / semantic name | enabled, content/control element | none | no Value, Invoke, SelectionItem, Selection, or Scroll |
| text field | Edit / semantic name | value, enabled, focus | Value; Focus; `SetValue` rejects controls/newlines and stale identity | value property and focus events |
| button | Button / semantic name | enabled, focus | Invoke; Focus | focus event; no Value/Selection/Scroll |
| list | List / semantic name | enabled | Selection | structure event and no Invoke/Value/Scroll |
| list item | ListItem / semantic name | selected, enabled, focus | single SelectionItem; Focus; disabled actions return `UIA_E_ELEMENTNOTENABLED` | selected property and focus events; no Value/Invoke/Scroll |
| status | StatusBar / semantic name | enabled | none | structure/property changes only; no actions |
| scroll viewport | Pane / semantic name | enabled, focus, vertical offset/view size | Scroll (`Small/LargeIncrement/Decrement`, `NoAmount`, bounded vertical percent); Focus | focus and scroll property events; no Value/Invoke/Selection |

Runtime IDs are `[3, compositionEpoch, elementId]`: stable for a mounted retained element, never reused across compositions, and independent of semantic generation. Providers are cached by that pair, pruned when absent from the latest snapshot, disconnected on pruning/disposal, and subsequently return `UIA_E_ELEMENTNOTAVAILABLE`. The declared matrix has **zero suppressions**. A future suppression must carry a nonempty reason in the deterministic semantic dump and a negative external UIA test before it is accepted.

ABI evidence is the Windows SDK `10.0.26100.0` `UIAutomationCore.idl` (provider IIDs, vtable order, `VARIANT`/`SAFEARRAY` ownership) and `UIAutomationCoreApi.h` (`UiaReturnRawElementProvider`, event, host-provider, and disconnect signatures). SDL3's `SDL_RegisterEvents`/`SDL_PushEvent` contract is the wakeup evidence; no UIA callback executes Core from `WndProc`.
