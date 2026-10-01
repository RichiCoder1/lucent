# Explicit preview scenarios

`Lucent.Preview` is an in-tree development library for registering compiled
components with explicit fixture data. It does not launch an editor panel, watch
files or execute an application's entry point. Those capabilities follow in the
[native preview work](https://github.com/RichiCoder1/lucent/issues/224).

Put registrations in a separate development project that references the component
project and `Lucent.Preview`. Keep that reference out of the shipping application's
project graph. The library is currently non-packable; an official external preview
package is not claimed.

## Register compiled factories

The catalog has ordinal, case-sensitive IDs. Adding registrations and enumerating
a built catalog run no setup or component code. `Build` snapshots registrations;
later builder changes do not change that catalog.

```csharp
var catalog = new PreviewCatalogBuilder()
    .Add(
        descriptor,
        (_, _) => ValueTask.FromResult(new CardData("Empty", "No saved items.")),
        (data, _) => Components.Card(data))
    .Build();

var scenario = catalog.Get("card/empty");
```

The descriptor supplies that ID, display title, explicit project/document/component
origin, and a `PreviewPresentation`. Presentation includes logical viewport and
scale, appearance, theme factory, density, culture/UI culture and an initial
clock instant. Culture values are copied and made read-only. Density is explicit
fixture data, not a universal Core styling switch. Theme factories and other
delegates remain authored code; captured mutable values are not deep-cloned.

Use separate empty, loading, error and long-text registrations with meaningful
data. The compiled component receives those exact values; design mode does not
replace them automatically. See the compiled example catalog in
[CompiledScenarios.cs](../tests/Lucent.Preview.Tests/CompiledScenarios.cs) and its
separate [component project](../tests/Lucent.Preview.Fixtures/Lucent.Preview.Fixtures.csproj).

## Start an owned instance

Every launch uses a fresh binding, fixture and controlled clock. The same clock
must be given to fixture setup and the headless host:

```csharp
var presentation = scenario.Descriptor.Presentation;
var clock = new FakeTimeProvider(presentation.InitialTime);
PreviewScenarioBinding? binding = null;

await using var application = await SkiaHeadlessApplication.StartAsync(
    context => binding!.CreateRoot(context.Session),
    builder => binding = scenario.Bind(builder, clock, cancellationToken),
    new HeadlessApplicationOptions
    {
        Title = scenario.Descriptor.Title,
        Purpose = CompositionPurpose.Preview,
        Viewport = presentation.Viewport,
        Appearance = presentation.Appearance,
        ThemeFactory = presentation.ThemeFactory,
        Culture = presentation.Culture,
        UICulture = presentation.UICulture,
        TimeProviderFactory = () => clock,
    });

byte[] png = await application.CapturePngAsync();
```

This example uses `Microsoft.Extensions.Time.Testing.FakeTimeProvider` with
`Lucent.Testing` and `Lucent.Testing.Skia`. The catalog library depends only on
Core and accepts a `TimeProvider`; it does not depend on a testing clock.
Code that explicitly calls system time still uses system time.

The general headless builder callback runs on the application owner thread.
The harness then enforces its selected host, title, theme and purpose. Explicitly
set `Purpose = Preview`; ordinary headless tests continue to use Application.
Culture is isolated to the owner thread and its asynchronous work, without
changing process-wide defaults.

## Fixture lifetime

Async setup receives `PreviewSetupContext` and a cancellation token. Register
cleanup immediately when acquiring a resource, before awaiting more work:

```csharp
async ValueTask<CardData> Setup(PreviewSetupContext context, CancellationToken token)
{
    var source = new FakeCardSource();
    context.OnDispose(() => source.DisposeAsync());
    return await source.LoadAsync(token);
}
```

`OnStop` runs before component teardown. `OnDispose` runs afterward, including
partial setup failure. The fixture return value is not automatically disposed;
ownership is explicit. Setup registration capabilities expire when setup ends.
Callbacks should use the returned fixture or other stable values instead of
retaining the setup context.

Use `ProvideRootContext` for typed borrowed context and `CreateServiceBinding`
for an explicit `IComponentServiceSource`. The existing single-binding rule still
applies. Services stop accepting new work before component teardown and are
revoked before fixture resources are disposed. Microsoft dependency injection
continues to use the existing Hosting integration.

Cancellation is checked before and after setup and around root construction.
Await failed startup so cleanup can finish; do not detach it with a timeout that
abandons the owned task. Code that ignores cancellation cannot be forcibly stopped
by this library. Worker-process supervision belongs to #230.

Design mode is not a sandbox. Build targets, static initializers and component
code retain their normal machine capabilities. The scenario catalog performs no
assembly scan, constructor inference, production startup or implicit data injection.
