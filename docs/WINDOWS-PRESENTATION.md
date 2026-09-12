# Windows presentation

Lucent keeps its retained controls and theme as the default. Opt into Windows standard context menus at application startup:

```csharp
LucentApplication.CreateBuilder()
    .UseWindows(new WindowsWindowOptions {
        MenuPresentation = WindowsMenuPresentation.PreferNative
    });
```

An unstyled `.lui` `Menu` containing standard `MenuItem`, `MenuSeparator` and `MenuSubmenu` elements can use this path. It shares the same application commands, target and availability predicates. The Windows adapter translates a Core-owned descriptor and invokes the retained semantic command after tracking ends. Right-click does not select the underlying application item.

Explicit menu/item styles and custom layout/content use Lucent's popup. An unsupported subtree keeps the entire menu on the Lucent path. Labels that need native shortcut/control-character interpretation also retain Lucent rendering. Native menus display the Windows system appearance and accessibility implementation; the application theme does not recolor them. Availability shown while the native tracker is open is a snapshot; invocation checks the retained command again. The host does not run an additional SDL application loop while Windows tracks the menu.

Lucent-rendered menus remain separate popup windows that may extend outside their owner. Stock presentation includes a theme-aware surface, border, rounded corners, separators, hover and keyboard-focus states. A transparent outer margin carries a restrained shadow, with content, hit testing and UIA projected into the same coordinates. High-contrast mode omits the shadow. Transparent composition depends on SDL/Windows support; it should be checked on the target graphics environment.

Native menus follow the [Windows standard keyboard interface](https://learn.microsoft.com/en-us/windows/win32/menurc/about-menus#standard-keyboard-interface): Up/Down navigate, Enter invokes and Escape dismisses. Lucent-rendered menus additionally support Home/End; native presentation does not promise those bindings.

## Native file and folder selection

Inject the portable `IFilePicker` capability into application state. In a Windows
application lifecycle, construct `WindowsFilePicker(session.Composition)` and
pass it to the root recipe. Component Browser's lifecycle and Storage example
show this wiring without a global service locator.

`OpenFilesAsync`, `SaveFileAsync` and `PickFolderAsync` return typed
`Selected`, `Canceled`, `Unsupported` or `Failed` results. Filters, initial
directory and suggested filename are explicit options. Selected items contain
a display name and location URI. A save result only chooses a destination;
the application still owns writing, permissions, overwrite policy and recovery.

The Windows host serializes requests and owns the native COM dialog on its STA
thread. Windows pumps dialog input while ordinary Lucent application callbacks
resume after dismissal. Cancellation closes the dialog on that same thread;
owner shutdown cancels queued work and the active request. Use the cancellation
token for application-owned lifetimes. A picker without a mounted Windows host
reports `Unsupported`. Other hosts can inject their own implementation.

## Scrollbar styles

`WindowsControlStyles.ScrollBar(appearance)` returns an opt-in style for the existing Lucent scrollbar: a stable gutter, a usable minimum thumb, and distinct normal/hover/pressed colors. It selects light, dark or high-contrast values. Pass the current `ThemeAppearance` and re-evaluate when it changes; append application overrides with `.With(...)`.

This is a Lucent-rendered style preset. It does not read arbitrary system metrics or create a native child scrollbar. Scroll position, paging, dragging and accessibility continue to use the existing shared viewport implementation. The framework default remains unchanged unless the application chooses the preset.

## Renderer resets

Owner and popup windows handle SDL renderer events independently. A render-target reset requests a redraw; a device reset releases the CPU surface and streaming texture and recreates them on the next presentation without remounting application components. If recreation fails, the original failure follows the application shutdown path. An unrecoverable device-loss event is terminal. Application shaping, layout and draw callbacks are never retried as device recovery.

This follows SDL's [renderer event contract](https://wiki.libsdl.org/SDL3/SDL_EventType). The focused presenter tests inject reset notifications against a real hidden SDL software renderer, verify subsequent presentation and retained editor state, and inject an allocation failure to check cleanup and bounded attempts. They do not simulate a physical GPU failure.

## Maintained example and limits

Issue Browser demonstrates stock themes and real context-menu actions. Run `dotnet run --project apps/Lucent.IssueBrowser -- --native-menus` to exercise the opt-in host without rewriting `.lui` commands; omit the switch for Lucent-rendered menus. Use the headless menu tests for eligibility, style and command contracts; use native desktop checks for actual focus, popup boundaries and Windows UIA.

For native-menu troubleshooting, set `LUCENT_MENU_DIAGNOSTICS=1` before launching and capture standard error. Each completed native menu reports the Windows selection ID (`0` for cancellation) and the retained command result, such as `Applied`, `Disabled`, or `Stale`. The diagnostic omits menu labels and application content. `Applied` establishes command acceptance; inspect the resulting application state to verify its effect and presentation.

## Submenus

Use `<MenuSubmenu menu={() => StatusMenu(model)}>Set status</MenuSubmenu>` inside a `Menu`; declare `StatusMenu` as another `.lui` component that returns a `Menu`. A submenu owns a lazy child factory and an optional `enabled` predicate. Standard native projection evaluates a bounded snapshot of eligible branches before entering the Windows tracker (at most eight levels and 512 entries).

Lucent owns one popup window per open level, so child menus can extend outside the application. Placement flips at screen edges. Right opens a child and Left returns to its parent, independently of the physical flip direction. Escape dismisses one level; invoking a leaf dismisses the whole chain before running the application callback. Empty children become unavailable; a nonempty group of disabled commands remains inspectable. Expanded state is exposed through UI Automation.

Pointer intent uses the actual placed child bounds to preserve diagonal travel toward a submenu. The grace period is bounded to 300 ms; reversal, leaving the safe area, entering the child, or expiry stops deferral. Windows-native menus delegate their navigation and pointer behavior to Windows. See [stock presentation](STOCK-PRESENTATION.md) for shared control roles and adaptive panes.
