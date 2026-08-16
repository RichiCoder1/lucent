# Native window composition

## Question

Can a Lucent-authored dialog or secondary window be presented through native
Avalonia APIs without a handwritten `Window` adapter such as
`SettingsDialog.cs`?

## Repository constraints

- A generated component is a logical instance that owns state and cleanup; it
  is not an Avalonia control and must not inherit `Window`.
- `Mount()` returns `Fragment` because ordinary composition supports zero, one,
  or many roots.
- Lucent already permits a native `Window` as a component root. No new dialog
  syntax or wrapper control is needed.
- Avalonia remains responsible for owners, modality, results, close
  cancellation, native window relationships, and platform behavior.

These constraints come from [the architecture](../ARCHITECTURE.md),
[the accepted decisions](../DECISIONS.md), and the executable component
contract in `GeneralCSharpEmitter` and `Fragment`.

## Primary-source findings

Avalonia's existing APIs are the presentation seam:

- `Show()` presents a non-modal window.
- `Show(owner)` associates an owned non-modal window.
- `ShowDialog<TResult>(owner)` presents a modal window and completes when it
  closes; `Close(result)` supplies its result.
- `Closing` can be cancelled, while `Closed` represents completed native
  lifetime.

Avalonia validates owner visibility, prevents a window from owning itself, and
does not permit a closed window to be shown again. Lucent should propagate
those rules rather than mirror them.

Sources:

- [Avalonia dialogs](https://docs.avaloniaui.net/docs/how-to/dialogs-how-to)
- [Avalonia window management](https://docs.avaloniaui.net/docs/app-development/window-management)
- [Avalonia `Window` source](https://github.com/AvaloniaUI/Avalonia/blob/main/src/Avalonia.Controls/Window.cs)

Other declarative systems support the same broad separation. QML components
have a declared root object while creation and ownership remain explicit;
SwiftUI owns window scenes itself and is therefore a weaker fit for Lucent's
direct-native approach.

- [Qt QML `Component`](https://doc.qt.io/qt-6/qml-qtqml-component.html)
- [Qt Quick `Window`](https://doc.qt.io/qt-6/qml-qtquick-window.html)
- [SwiftUI `App`](https://developer.apple.com/documentation/SwiftUI/App)
- [SwiftUI `WindowGroup`](https://developer.apple.com/documentation/swiftui/windowgroup)

## Recommendation

Generate a strongly typed `MountRoot()` method when a component has exactly one
statically known, direct native root:

```csharp
component SettingsDialog() => Window {
    Title: "Settings";
    SettingsPane {}
};
```

```csharp
using var component = new SettingsDialogComponent();
await desktopHost.ShowDialogAsync<bool>(owner, component.MountRoot());
```

`Mount()` remains the uniform composition interface and still returns
`Fragment`. `MountRoot()` is an additional generated interop interface whose
return type is the actual native root type (`Window` above). It is generated
only when cardinality and type are statically certain; there is no reflection,
registry, cast-by-name, wrapper, or runtime window service.

For a modal dialog, `using` naturally disposes the component after close or a
failed `ShowDialog`. For a non-modal owned window, application code retains the
component until the native `Closed` event and disposes it there. A cancelled
`Closing` event must not dispose the component.

### Why not `AsControl<T>()`

A component-wide `AsControl<T>()` is a poor primary interface. `As` reads like
a repeatable, non-owning projection, while obtaining the control performs the
component's irreversible one-shot mount. Caller-selected `T` also turns a root
type already known by the compiler into a runtime assertion. Assignment from
the exact `MountRoot()` result to `Control` already handles ordinary Avalonia
methods without discarding the stronger type.

If code starts from an already mounted `Fragment`, a future
`RequireSingle<TControl>()` helper could provide checked projection, but it
must not imply that the fragment owns or disposes its logical component.

### Lifecycle analyzer feasibility

A Roslyn analyzer can improve this interface, but cannot prove an arbitrary
Avalonia lifetime. It can reliably report dropped temporary components,
missing lexical disposal, repeated mounts, and roots escaping a `using` scope.
It cannot prove that an arbitrary event eventually fires, that an unknown
method takes ownership, or that application-specific field cleanup occurs.

The generated component remains the lifetime token. A second disposable lease
would duplicate ownership while still requiring host-specific close,
detach/reattach, and transfer policy. Generated types should carry standard
`GeneratedCodeAttribute` metadata so analysis can recognize them without a
runtime marker interface or reflection.

Initial diagnostics should cover:

- a mounted local component not disposed, returned, or stored on every exit;
- `Mount()` or `MountRoot()` invoked more than once on one instance;
- a mounted root escaping a lexical `using` scope;
- `new Component().MountRoot()` losing the deterministic disposal handle.

Code fixes should be limited to safe transformations such as introducing
`using var component = ...`. Event-driven and field ownership require a human
lifetime decision. CA2000 remains useful, but its ownership-transfer model is
not precise enough to describe the generated component/root relationship.

Sources:

- [Roslyn analyzer and data-flow tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix)
- [.NET CA2000](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2000)

## Rejected options

- **Generated components inherit `Window`:** conflates logical and native
  identity, breaks ordinary composition, and still does not define owner
  disposal.
- **Lucent `ShowDialog`/window manager:** duplicates Avalonia ownership,
  modality, results, and failure behavior.
- **New dialog syntax:** unnecessary because native `Window` is already a valid
  Lucent root.
- **Reflection or a component registry:** weakens compile-time guarantees and
  violates the direct-generated contract.
- **`RequireRoot<T>()` alone:** improves validation but leaves callers with the
  same repeated untyped boundary.
- **Generic disposable mount lease initially:** creates a second lifetime object
  but cannot remove host-specific close, detach, reattach, or transfer rules.
  Reconsider only when a real seam must return root and lifetime together.
- **Automatic disposal in `MountRoot()`:** presentation has not started yet;
  modal and non-modal callers have different deterministic lifetime points.

## Falsifying evidence

Reconsider this recommendation if direct native root types cannot be stable at
compile time, if the extra generated method creates ambiguity in composition,
or if Workbench still needs adapter classes for behavior other than obtaining a
typed root. Repeated modeless ownership ceremony would justify an
application-level helper before any Lucent runtime feature.
