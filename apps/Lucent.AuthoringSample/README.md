# Lucent application authoring sample

This small Windows application keeps its root, shell, routed pages, route records,
models, generated JSON context, state, and behavior in `.lui`. `Program.cs` is the
only authored C# file in the all-LUI project; it selects the Windows host, provides
the startup capability, decorates the root presentation, and schedules the smoke run.

Run either maintained variant from the repository root:

```powershell
dotnet run --project apps/Lucent.AuthoringSample -- --smoke
dotnet run --project apps/Lucent.AuthoringSample.Companion -- --smoke
```

The smoke run starts on the counter route, invokes its button through the semantic
surface, navigates to the generated-JSON page, and changes the serialized model before
requesting window close. Both variants print `AUTHORING SAMPLE PASS` before shutdown.
The companion replaces only `CounterPage.lui` and adds `CounterPage.lui.cs`, where
explicit `[State]` and `Setup(ComponentContext)` add mounted state and an `OnDispose`
cleanup. Its smoke hook mounts the application in two separate compositions, checks
that the component state is independent, and requires setup and cleanup to each run
twice before it prints `AUTHORING COMPANION PASS`.

The [application authoring guide](../../docs/APPLICATION-AUTHORING.md) describes the
builder, routing, setup, companion, and service contracts. Candidate-package managed
and NativeAOT smoke checks are run with `tools/Test-ApplicationAuthoringPackage.ps1`.
