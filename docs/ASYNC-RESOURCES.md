# Explicit asynchronous resources

Use `owner.Async(source, load, name)` for component-owned reads such as a lookup or preview. The source runs synchronously on the UI owner thread and tracks reactive inputs. The fetcher receives that captured input and a cancellation token; its incidental reactive reads do not create dependencies. Components still mount synchronously.

```csharp
namespace Example;
using System;
using System.Threading;
using System.Threading.Tasks;
using Lucent.Core;

public component Preview(
    Func<string> address,
    Func<string, CancellationToken, Task<string>> fetch
) {
    readonly AsyncValue<string> preview = owner.Async(address, fetch, "preview");
    string status = preview.IsPending ? "Loading preview..."
        : preview.Error != null ? "Preview unavailable"
        : preview.Value ?? "No preview";

    void Retry() {
        preview.Refresh();
    }

    <Column>
        <Text>{status}</Text>
        <Button onInvoke={Retry}>Refresh</Button>
    </Column>
}
```

The readonly declaration retains one resource for each mount. It does not start work during recipe construction. Reading `Value`, `HasValue`, `IsPending`, `IsCancelled`, or `Error` starts a dirty resource. A source change cancels the previous generation; only the latest generation may commit, through the owner's reactive queue. A producer that ignores cancellation may continue executing, but its late result is ignored.

`Refresh()` invalidates the current generation without requiring a source change. The next read starts it again. It keeps the last successful value available while refreshing; use `IsPending` and `Error` separately when showing stale content. The overload with `staleValue` also provides data before the first success. Failures appear in `Error` and can be retried. Source changes and refresh clear the previous failure when the new generation starts.

Only the source's synchronous reads are tracked. Put every reactive dependency that should trigger a new request into the source (a tuple or record can capture several inputs). The fetcher should use the captured values after awaiting, without reading UI-owned signals from worker threads. Keep blocking work out of its synchronous portion.

Unmounting disposes the resource and cancels its current generation. Keep accepted saves and other operations that must finish under an application or workspace owner; do not give them the lifetime of a replaceable preview component. This convenience reuses `AsyncValue` and adds no implicit await, async component mounting, or new `.lui` grammar.

Focused Core tests cover explicit dependency tracking, stale results, owner-thread completion, retry, and disposal. The packed SDK consumer includes an authored `.lui` source-change/error/retry/unmount scenario for NativeAOT verification.
