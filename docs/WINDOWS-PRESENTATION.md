# Windows presentation

Lucent keeps its retained controls and theme as the default. Opt into Windows standard context menus at application startup:

```csharp
LucentApplication.CreateBuilder()
    .UseWindows(new WindowsWindowOptions {
        MenuPresentation = WindowsMenuPresentation.PreferNative
    });
```

An unstyled `.lui` `Menu` containing standard `MenuItem` and `MenuSeparator` elements can use this path. It shares the same application commands, target and availability predicates. The Windows adapter translates a Core-owned descriptor and invokes the retained semantic command after tracking ends. Right-click does not select the underlying application item.

Explicit menu/item styles, custom layout/content and nested menus use Lucent's popup. Labels that need native shortcut/control-character interpretation also retain Lucent rendering. Native menus display the Windows system appearance and accessibility implementation; the application theme does not recolor them. Availability shown while the native tracker is open is a snapshot; invocation checks the retained command again. The host does not run an additional SDL application loop while Windows tracks the menu.

Lucent-rendered menus remain separate popup windows that may extend outside their owner. Stock presentation includes a theme-aware surface, border, rounded corners, separators, hover and keyboard-focus states. A transparent outer margin carries a restrained shadow, with content, hit testing and UIA projected into the same coordinates. High-contrast mode omits the shadow. Transparent composition depends on SDL/Windows support; it should be checked on the target graphics environment.

Native menus follow the [Windows standard keyboard interface](https://learn.microsoft.com/en-us/windows/win32/menurc/about-menus#standard-keyboard-interface): Up/Down navigate, Enter invokes and Escape dismisses. Lucent-rendered menus additionally support Home/End; native presentation does not promise those bindings.

## Scrollbar styles

`WindowsControlStyles.ScrollBar(appearance)` returns an opt-in style for the existing Lucent scrollbar: a stable gutter, a usable minimum thumb, and distinct normal/hover/pressed colors. It selects light, dark or high-contrast values. Pass the current `ThemeAppearance` and re-evaluate when it changes; append application overrides with `.With(...)`.

This is a Lucent-rendered style preset. It does not read arbitrary system metrics or create a native child scrollbar. Scroll position, paging, dragging and accessibility continue to use the existing shared viewport implementation. The framework default remains unchanged unless the application chooses the preset.

## Maintained example and limits

Issue Browser demonstrates stock themes and real context-menu actions. Run `dotnet run --project apps/Lucent.IssueBrowser -- --native-menus` to exercise the opt-in host without rewriting `.lui` commands; omit the switch for Lucent-rendered menus. Use the headless menu tests for eligibility, style and command contracts; use native desktop checks for actual focus, popup boundaries and Windows UIA.

For native-menu troubleshooting, set `LUCENT_MENU_DIAGNOSTICS=1` before launching and capture standard error. Each completed native menu reports the Windows selection ID (`0` for cancellation) and the retained command result, such as `Applied`, `Disabled`, or `Stale`. The diagnostic omits menu labels and application content. `Applied` establishes command acceptance; inspect the resulting application state to verify its effect and presentation.

Nested submenu authoring and safe-triangle pointer travel are tracked in [#104](https://github.com/RichiCoder1/lucent/issues/104). Flat menu support does not establish submenu behavior.
