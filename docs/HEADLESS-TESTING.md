# Testing components without a desktop

Reference `Lucent.Testing` from a test project. Continue compiling `.lui` with `Lucent.Lui.Sdk`; the harness mounts the same generated component recipes used by the application. Tests may use MSTest or another runner. No window is created and no desktop input is injected.

```lui
namespace Example;
public component SaveButton(Action save) {
    <Button onInvoke={save} style={Style.Empty.Height(40)}>Save</Button>
}
```

```csharp
var saves = 0;
await using var app = await HeadlessApplication.StartAsync(
    Example.Components.SaveButton(() => Interlocked.Increment(ref saves)));
await app.KeyAsync(new(KeyCommandKind.Down, Key.Tab));
await app.KeyAsync(new(KeyCommandKind.Down, Key.Enter));
Assert.AreEqual(1, Volatile.Read(ref saves));
```

Use `Lucent.Core` and `Lucent.Testing` in the C# file. Each harness application runs on its own owner thread. Input and observation operations marshal to that thread and settle queued work before returning. Create reactive models on that owner and inspect their live state there; do not pass a caller-owned reactive graph into a component closure. Prefer snapshots or plain result values for assertions on the test-runner thread.

## Choose what to exercise

The base harness supplies deterministic text metrics for structural tests. It does not claim real font shaping or pixel fidelity. Reference `Lucent.Testing.Skia` for real text geometry, caret hit testing, and optional PNG captures with the production renderer. Pixel baselines are not required for behavior assertions.

```csharp
await using var rendered = await SkiaHeadlessApplication.StartAsync(
    Example.Components.SaveButton(() => { }),
    new HeadlessApplicationOptions { Viewport = new(320, 120, 1.5f) });
byte[] png = await rendered.CapturePngAsync();
```

The companion namespace is `Lucent.Testing.Skia`. Captures use physical pixel dimensions (480 by 180 in this example); viewport coordinates and injected input stay in logical units.

Configure viewport size, scale, appearance, and theme in `HeadlessApplicationOptions`. Use `AdvanceAsync` to advance the controlled clock; use `InvokeAsync` for owner-thread setup or queries that need direct production capabilities. Tests should explicitly complete asynchronous dependencies. Draining the queue does not complete arbitrary network or background operations.

The work limit contains queued callback/effect loops and reports unsettled work. It cannot preempt a callback that blocks forever, or an endlessly repeating timer inside a single clock advance. Keep user callbacks short, use controlled dependencies, and retain a test-runner timeout for arbitrary code failures.

## Run the maintained examples

```powershell
./tools/Test-Repository.ps1 -Project Lucent.Testing.Tests
```

This suite is included in the ordinary managed CI job. It covers compiled `.lui`, editing, menu interaction, controlled debounce, rendering, and harness lifetime/error behavior. A separate package-only consumer checks SDK and harness distribution without relying on source-project references.

Keep native desktop checks for Windows focus/capture, native resize loops, popup placement outside the owner, system clipboard, and UIA providers. Headless semantics are not a Windows accessibility test, and synthetic text input does not certify an IME.
