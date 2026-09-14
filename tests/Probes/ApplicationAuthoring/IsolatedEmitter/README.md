# Isolated emitter proof

This bounded A0 experiment tests one alternative to transporting preparatory source
trees as `AdditionalFiles`. It does not change the SDK-host probe or claim general SDK,
editor, package or NativeAOT support.

`ProjectEmitter` is a small analyzer assembly containing deterministic project payload
constants. Its experimental pre-compilation callback publishes the early `Person` and
`JsonContext` declarations, while its ordinary source callback publishes the final
`Application` implementation. The real .NET 10 System.Text.Json generator runs in the
same driver and consumes those early declarations.

`AdditionalFileObserver` is a separately loaded external generator. It fingerprints every
AdditionalText by file name and SHA-256 content and reports `PROBE2001` when a captured
`.binding.g.cs` appears. The executable runs two final generations:

1. The isolated emitter receives the single authored `Model.lui` AdditionalText. The
   external fingerprint contains exactly that input, real JSON outputs match preparation,
   and the emitter implementation compiles.
2. The same emitter receives the JSON preparation outputs as `.binding.g.cs` AdditionalTexts,
   modeling the old transport. The external generator sees a changed fingerprint and
   reports the transport leak.

The proof therefore exercises the relevant boundary with real JSON generation, rather
than relying on a generator that ignores AdditionalTexts. It does not prove the host can
build a content-addressed project analyzer from arbitrary payloads. A production host
would need to compile that analyzer with the selected SDK Roslyn references, key its
artifact by all source/options/analyzer/reference inputs, use a unique content-addressed
path and MVID, and retain an in-memory equivalent for editor work.

Run from the repository root:

```powershell
./tests/Probes/ApplicationAuthoring/IsolatedEmitter/Run-IsolatedEmitter.ps1
```
