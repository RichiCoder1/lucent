# Windows activation feasibility probe

Run `./Test-Probe.ps1` on Windows with the repository's pinned .NET SDK and native
compiler. This opt-in probe publishes two hidden console processes; it does not
create app windows, synthesize input, register protocols, install the runtime,
or alter the existing application instance. It stays out of the normal solution
and managed test lane. Evidence goes under `artifacts/windows-activation-probe`.

The probe pins Windows App SDK Foundation 2.3.12 and C#/WinRT 2.3.1, with a locked
self-contained dependency closure. It verifies NativeAOT initialization, native
URI projection round trips, cross-process launch redirection, bounded waits,
and clean process exits. A versioned application adapter is not implemented by
this fixture. OS-registered protocol delivery, Windows foreground policy and
MSIX delivery remain separate proof targets.

Keep instance-key ownership on one native thread. An initial asynchronous-main
experiment delivered its request, then exited with `0xC0000409` when key release
ran on another thread. Microsoft's implementation retains an owned native mutex.
Keeping registration, the bounded console wait and cleanup on the same thread
passed with both process exit codes zero. Do not turn that requirement into a
delay or suppress the exit failure. The eventual adapter must enforce ownership
and marshal cleanup back to its owner, including failed startup.

Use `IProtocolActivatedEventArgs.Uri.OriginalString` as the .NET projection's raw
input boundary. The fixture crosses the native URI ABI for four traversal/escape
cases before comparing exact text; `AbsoluteUri`, `LocalPath`, or `ToString()`
would not establish that contract. This does not replace real registered-protocol
tests, which can exercise additional shell/package normalization.
