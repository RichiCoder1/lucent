# Applications and services

Lucent applications keep a synchronous `[STAThread]` entry point. The Windows adapter pumps asynchronous lifecycle work while retaining the window for pending close preparation. The simple `Build().Run(recipe)` form remains available for applications without services.

For services, reference `Lucent.Hosting` and use `HostedApplication` at the composition root:

```csharp
using Lucent.Core;
using Lucent.Hosting;
using Lucent.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;

[STAThread]
static int Main()
{
    var lifecycle = new HostedApplication(
        session =>
        {
            var host = HostedApplication.CreateBuilder();
            host.Services.AddSingleton(session);
            host.Services.AddSingleton<SaveService>();
            host.Services.AddScoped<InboxModel>();
            // Add configuration sources and logging providers here if needed.
            return host.Build();
        },
        (services, session) => Notes.Components.Inbox(
            services.GetRequiredService<InboxModel>(), session),
        (services, cancellationToken) => services
            .GetRequiredService<InboxModel>()
            .PrepareCloseAsync(cancellationToken));

    return LucentApplication.CreateBuilder()
        .UseWindows()
        .SetTitle("Links and notes")
        .Build()
        .Run(lifecycle);
}
```

`InboxModel` and `SaveService` above are application-owned types, not built-in persistence APIs. Register services with constructors or explicit factories; the host validates scope usage. A singleton service owns application-wide work. One application DI scope owns scoped models, and transient dependencies follow normal Microsoft DI ownership. The factory resolves the model once and passes it into `.lui`; child components receive typed parameters instead of looking up services.

```lui
namespace Notes;
using Lucent.Core;
internal component Inbox(InboxModel model, ApplicationSession session) {
    <Column>
        <Text content={() => model.Title} />
        <Text content={() => session.Status.Error?.Message ?? session.Status.Phase.ToString()} />
        <Button onInvoke={session.RequestClose}>Close / retry</Button>
    </Column>
}
```

The example illustrates the authoring boundary, not the planned application's final visual design. The executable [lifecycle fixture](../tests/Lucent.Platform.Windows.TestHost/Lifecycle.lui) exercises the same public path.

## Closing without losing accepted work

`PrepareCloseAsync` must prevent new accepted writes, await existing accepted saves and return true only when closing is safe. For an expected validation or save failure, publish recoverable application state and return false; services and UI remain alive, and the next `session.RequestClose()` retries preparation. An exception escaping preparation is unexpected and terminates the session. `session.Status` is reactive and can drive progress and disabled editing, while the application model owns recoverable error details. Requests during startup are remembered; repeated requests during preparation do not run duplicate saves.

Fatal host failure requests cancellation through the preparation token and begins terminal cleanup without waiting for preparation. Cancellation is cooperative. Preparation must observe the token, but accepted saves retain application-service ownership and still finish or report failure from service shutdown. A late preparation result cannot reopen the session; a late task fault remains observed when the task can complete independently of the closed owner context.

Keep an accepted save's lifetime in the application service. Do not link it to the cancellation token of an element, obsolete read or Generic Host stopping notification. A service can cancel stale reads freely while maintaining its independent accepted-write drain. The storage ticket defines actual persistence and retry behavior; Lucent does not silently replay writes.

After preparation succeeds, service stop is terminal. Lucent attempts service stop, owner-thread composition disposal, asynchronous model-scope disposal, and host disposal; failures remain observable from `Run`. A partially stopped host is not reopened. Fallible work needing user recovery belongs in preparation. A fatal platform failure cannot offer that recovery UI; service stop must also account for accepted work on this emergency path and report any failure to finish it.

Use `SetFailureReporter` on the application builder to send terminal and late-preparation failures to application logging or crash reporting. The callback runs independently after cleanup, cannot resume the session, and must not use disposed UI or services. It is best effort before process exit. If the callback or its dispatcher fails, Lucent writes both the original and reporting failures to its minimal standard-error fallback without replacing the exception returned by `Run`.

## Threading and ownership

The Windows event loop installs the session synchronization context. Lifecycle continuations and ordinary awaited UI commands return to its owner unless application code deliberately uses `ConfigureAwait(false)`. Generic Host service hooks, background services and container-managed async disposal are not UI-thread APIs: post UI work through the captured context (cross-thread synchronous `Send` is unsupported), and place UI resources and subscriptions in `session.Scope` or the appropriate composition scope.

Microsoft's container may continue disposal on a worker and stops disposing siblings if a service disposer throws. Service disposal should release its own resources reliably and be thread-independent. Lucent separately attempts the composition, model scope and host boundaries even when another fails; it does not replace container internals. See [ADR 0003](adr/0003-application-services-and-shutdown.md).

The supplied builder disables implicit JSON/environment configuration and default logging sinks. Add the official providers you need; prefer source-generated options binding for NativeAOT. The current package dependency and primary-source references are recorded in [CREDITS.md](../CREDITS.md).
