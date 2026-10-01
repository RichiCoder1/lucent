# Composition-scoped design mode

The host chooses why a component is mounted. Ordinary applications and headless
tests use `CompositionPurpose.Application`. An explicit preview uses
`CompositionPurpose.Preview`. The purpose is immutable for that composition and
is available before component state, setup and content are constructed.

In `.lui`, read it through contextual `Design`:

```lui
namespace Example;

public component Greeting(string name) {
    string displayName = Design.IsDesignMode && string.IsNullOrEmpty(name)
        ? "Alex Example"
        : name;

    <Text>Hello, {displayName}!</Text>
}
```

One compiled component can run in both purposes at the same time. This expression
preserves explicitly supplied data; the framework never replaces parameters or
services automatically. Named preview fixtures should supply realistic empty,
loading, error and long-text data rather than relying on a global sample-data flag.

## Select the purpose

For the [headless harness](HEADLESS-TESTING.md), opt in explicitly:

```csharp
await using var preview = await HeadlessApplication.StartAsync(
    Example.Components.Greeting(""),
    new HeadlessApplicationOptions { Purpose = CompositionPurpose.Preview });
```

`SkiaHeadlessApplication.StartAsync` accepts the same options. Neither harness
implicitly enables design mode. The options are copied when the application
starts; later option changes do not change a running composition.

Custom hosts can select the purpose with
`LucentApplication.CreateBuilder().SetPurpose(CompositionPurpose.Preview)` or the
three-argument `Composition(graph, name, purpose)` constructor. Unsupported enum
values are rejected before component construction. Changing purpose requires a
new composition.

## Authoring and lifetime

Contextual `Design` is supported in component initializers, methods, `Setup` and
markup expressions, including anonymous components. C# recipe authors use
`ComponentContext.Design` inside `Component.Define`; custom mount callbacks use
`MountContext.Design`. Ordinary C# helpers and component companions receive that
value explicitly. No additional setup parameter is required.

`DesignContext` is a read-only value containing `Purpose` and `IsDesignMode`.
Generated components capture that value before authored initialization. Retained
callbacks keep their originating purpose without retaining a temporary mount
context. This does not extend component lifetime or permit state writes after
disposal.

Nested recipes, context providers, conditional/keyed/virtualized children, menus,
submenus, dialogs, popovers and tooltips inherit their originating purpose. Two
compositions can share a reactive graph and still have different purposes.

## Ordinary names take precedence

A parameter, field, local, type or alias named `Design` keeps its normal C# meaning.
If that symbol lacks `IsDesignMode`, the compiler reports the ordinary member error;
it does not silently use the contextual value. Strings and unrelated member names
are never rewritten. Static code retains ordinary restrictions on capturing
component instance state.

Completion and hover describe the contextual value and its read-only members.
Rename is rejected for the intrinsic; ordinary authored symbols named `Design`
retain their usual navigation and rename behavior. The executable compiler and
editor projection use the same binding decision, including unsaved shadowing
changes.

## Preview boundary

Design mode is an authoring signal, not a sandbox or platform capability check.
Component code, static constructors and build targets can still access files,
network services and other application dependencies. Use explicit fake services
and normal ownership/cancellation for preview fixtures. Native window, IME,
accessibility and clipboard support remain separate host capabilities.

This signal does not itself launch a preview, discover scenarios or reload state.
Those development tools are tracked by [native preview #224](https://github.com/RichiCoder1/lucent/issues/224).
