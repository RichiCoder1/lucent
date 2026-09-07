# Reactive integrations

Lucent Core retains its own dependency-tracking and ownership model. The optional `Lucent.Reactive.R3` package adds maintained ecosystem scheduling where it is useful, without requiring every Core consumer to reference R3.

## Owned debounce

```csharp
using Lucent.Reactive.R3;

// Create once on the owner thread. The scope owns the scheduler.
var autosave = new OwnedDebouncedAction(owner, TimeProvider.System);

// Replace the pending callback after each edit.
autosave.Restart(TimeSpan.FromMilliseconds(750), () => saveCommand.TryExecute());

// Explicit Save or a route change can cancel the delay and flush separately.
autosave.Cancel();
```

`Restart` replaces the pending callback. It runs only after the quiet period, and only when the owner's reactive graph drains. Restart, cancellation and disposal also suppress an obsolete callback already queued for that drain. Scope disposal releases pending work. `TimeProvider` is explicit so tests can advance virtual time without sleeping.

The implementation uses R3's trailing `Debounce`, not its `ThrottleLast` sampling operation. It does not change R3's global scheduler settings. Cancellation only affects the pending callback; the application remains responsible for serializing and draining any save it has already accepted. Avoid async-void callbacks: use an owned command or service with an observable completion/failure contract.

## External callbacks

`ReactiveScope.Post(Action)` accepts a callback from any thread and returns a cancellation handle. The callback executes during the owning graph's drain; the native host already wakes for queued graph work. Cancelling the handle or disposing the scope releases an undrained callback's closure. Posting after scope disposal does nothing. Exceptions surface through the graph's existing drain error handling.

This is the small dispatch boundary used by the R3 adapter. It does not make signal access thread-safe or authorize parallel component mounting. Read and mutate reactive state on its owner thread.
