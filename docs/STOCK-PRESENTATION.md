# Stock controls and adaptive panes

Lucent applications start with a stock surface and readable foreground. Built-in layout containers and ordinary `Text` inherit surrounding paint, so composed selectable content keeps the parent's pressed and selected colors. Apply `PresentationStyles.Surface` when a panel deliberately starts a new surface.

## Text roles and density

`PresentationStyles.Body`, `Title`, `Secondary` and `Caption` combine framework typography with themed foreground colors. Inside an interactive control, use `PresentationStyles.Typography(TextRole.Body)` or another role to select font metrics while retaining the control's inherited state color. An explicitly authored foreground takes precedence.

```lui
<Column style={PresentationStyles.Surface}>
    <Text style={PresentationStyles.Title}>Issues</Text>
    <Text style={PresentationStyles.Secondary}>Local reference workspace</Text>
    <Divider />
</Column>
```

`DensityMetrics.For(DensityPreset.Comfortable)` and `Compact` supply consistent font size, control/row height, padding and spacing. `PresentationStyles.ForDensity(...)` applies those metrics to a control or content region. The row-height metric describes a simple row: multiline virtualized rows must declare a fixed height that also accommodates their content and gaps. Density does not silently change the virtualization contract.

Stock buttons, text fields and selectables provide padding and visible interaction states; buttons and selectables also provide minimum heights. Explicit geometry and style overrides remain supported. Earlier prereleases had different editor defaults; applications that require exact text insets should specify padding explicitly. Hit testing, selection and caret geometry share that content inset.

## Optional minimal base

```csharp
LucentApplication.CreateBuilder()
    .SetPresentationMode(ControlPresentationMode.Minimal);
```

The minimal base removes ordinary stock control decoration while retaining useful geometry, focus indication, semantics and interaction feedback. It starts a custom design system without removing accessibility behavior. `ThemeContext.PresentationMode` is also reactive for compositions that own their theme context. Explicit author styles win in either mode. Native Windows menus retain their system presentation independently.

## Resizable split pane

Hoist a `SplitPaneState` into component setup, then supply both pane contents:

```lui
public component Workspace() {
    [Once] SplitPaneState panes = null!;
    Setup(owner) {
        panes = new SplitPaneState(owner, initialExtent: 360,
            minimumFirst: 240, minimumSecond: 320);
    }

    <SplitPane state={panes}
        first={[IssueList()]}
        second={[IssueDetails()]}
        label="Resize issue list" />
}
```

`IssueList` and `IssueDetails` are separately declared application components; each `.lui` file declares one component. Named content attributes accept content collections, hence the collection expressions above.

The default row arrangement places panes side by side. Use `axis: LayoutAxis.Column` for stacked panes. `PreferredExtent` retains the requested first-pane size; `EffectiveExtent` clamps it to available space. Shrinking or temporarily unmounting the split pane does not erase the preferred size. Both content regions remain mounted during dragging. Keep state above a responsive branch when switching between split and narrow navigation, and hoist editor/viewport sessions there when they must survive branch changes.

The splitter captures pointer drags and exposes an orientation-specific resize cursor. Arrow keys along its axis resize by 8 logical pixels, Shift increases the step to 40, and Home/End select the current limits. The labeled splitter exposes a finite range through Windows UI Automation and can be focused and resized without a pointer.

The [Issue Browser](../apps/Lucent.IssueBrowser/PRODUCT.md) demonstrates framework defaults with layout-only application styles. The [Windows presentation guide](WINDOWS-PRESENTATION.md) covers submenu trees and platform opt-ins.
