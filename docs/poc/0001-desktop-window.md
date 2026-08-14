# POC 0001: Code-only desktop window

> [!NOTE]
> This is a historical checkpoint. Native event values now require explicit
> `() =>` or `(sender, e) =>` lambdas; bare event blocks are no longer accepted.

This first executable slice originally proved only that the repository could restore, build, and launch a code-only Avalonia desktop application. POC 0002 subsequently replaced its hand-maintained Counter stand-in with compiler-generated C# while retaining the same host boundary.

## Scope

- .NET 9 desktop executable
- Avalonia 12.1.1 with the desktop backend and Fluent theme
- no XAML or Avalonia markup package
- one window containing a label and increment button
- one direct event-to-property update
- one checked-in generated-code target with explicit mount and cleanup

## Run

```powershell
dotnet restore Lucent.sln
dotnet run --project src/Lucent.Poc/Lucent.Poc.csproj
```

Run the native-window smoke path with:

```powershell
dotnet run --project src/Lucent.Poc/Lucent.Poc.csproj -- --smoke-test
```

The smoke path creates a real platform window, waits through Avalonia's loaded dispatcher priority, raises the generated button's routed click event, verifies that the existing count text becomes `Count: 1`, closes the window, and verifies application shutdown. It requires an interactive desktop session.

## What this does not prove

- parsing or compiling `.lui`
- the Lucent `Fragment Render()` contract
- generated component identity or state-member lifetime
- dependency analysis, scheduling, batching, or disposal
- CSS compilation or Avalonia property-priority behavior

## Verification

Verified on Windows with .NET SDK 9.0.316:

- NuGet restore completed with Avalonia packages pinned consistently at 12.1.1.
- Debug and Release builds completed with zero warnings and zero errors.
- The native smoke path observed `window-opened`, a loaded-priority dispatcher turn, `counter-updated`, `window-closed`, and `app-exit`, then returned exit code 0.
- `dotnet format --verify-no-changes` passed.
- NuGet reported no known vulnerabilities in direct or transitive packages.

## Insights and open issues

This section is updated as the POC exposes concrete constraints.

- Avalonia supports a completely code-only application; `Avalonia.Markup.Xaml` is not required for this substrate.
- Avalonia controls must be created after platform initialization. The application lifetime boundary therefore belongs below generated Lucent component code.
- `Generated/CounterComponent.g.cs` is now emitted by the narrow Counter compiler described in [POC 0002](0002-counter-compiler.md). Its `Mount()` method creates each control once, the click handler directly assigns `TextBlock.Text`, and `Dispose()` removes the event subscription.
- C# treats the `.g.cs` suffix as generated code and does not automatically apply the project's nullable annotation context. Lucent-generated C# must emit an explicit `#nullable enable` directive to preserve nullable diagnostics.
- The host window owns the generated component lifetime, but the component is not an Avalonia `Control`. Returning one root `Control` from `Mount()` is only sufficient for this single-root POC and is not the general Lucent `Fragment` contract.
- The Counter's classes are projected onto Avalonia controls, but `Counter.css` is deliberately not translated by hand. CSS compilation and property precedence remain unproven.
- The click path runs on Avalonia's UI thread. Background invalidation, dispatcher marshalling, batching, and post-disposal queued work remain open runtime concerns.
- The source fixture's `onClick: { ... }` form is accepted as a bounded event value and lowers to Avalonia's typed routed-event handler. It does not establish arbitrary block-valued properties or general embedded C#.
- The POC targets `net9.0` because that SDK is installed and Avalonia 12.1.1 is compatible with it. This is a bootstrap choice rather than a long-term target-framework commitment.
- The durable smoke mode exercises the real desktop backend and shutdown path. A future headless test suite would complement it for deterministic compiler/runtime behavior but would not replace native-window verification.
