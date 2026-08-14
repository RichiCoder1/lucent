# Contributing to Lucent

Lucent is still proving its core language and runtime model. Contributions should make a design claim more concrete, testable, or easier to evaluate. Broad feature work is less useful than a narrow end-to-end slice at this stage.

## Before proposing a change

Read the [project vision](docs/VISION.md), [language model](docs/LANGUAGE.md), [architecture](docs/ARCHITECTURE.md), and [decision status](docs/DECISIONS.md). Check whether the area is an accepted direction, a working choice, or explicitly deferred.

For a syntax or semantic proposal, explain:

- the UI problem it solves;
- why normal C# is not already clear enough;
- what behavior is implicit;
- how the compiler can analyze it;
- how diagnostics and completion will explain it;
- how it lowers to Avalonia;
- what smaller alternative was considered.

Prefer a focused design note and a few representative examples over a large speculative implementation.

## Documentation changes

Keep the root README short and project-facing. Put details in the topic document that owns them, and update [docs/DECISIONS.md](docs/DECISIONS.md) when commitment status changes.

Examples must label unsettled syntax as illustrative. Do not present a deferred idea as supported or final. If code begins to exist, documentation and executable behavior should change together.

## Implementation changes

The current implementation targets .NET 9. Restore, verify generated output, build, and test with:

```powershell
dotnet restore Lucent.sln
dotnet run --project src/Lucent.Compiler.Cli/Lucent.Compiler.Cli.csproj -- verify --input examples/counter/Counter.lui --output src/Lucent.Poc/Generated/CounterComponent.g.cs
dotnet build Lucent.sln --no-restore
dotnet test Lucent.sln --no-build
```

Run the native Avalonia verification from an interactive desktop session:

```powershell
dotnet run --project src/Lucent.Poc/Lucent.Poc.csproj -- --smoke-test
```

Verify the VS Code grammar, manifest, and bundled language server with:

```powershell
Push-Location editors/vscode
npm install
npm test
npm run prepare-server
Pop-Location
```

Implementation work should include the smallest useful test at the same layer:

- parser changes need syntax and diagnostic cases;
- semantic changes need symbol and type-resolution cases;
- lowering changes need readable generated-code snapshots;
- runtime changes need identity, update, and disposal tests;
- editor changes need protocol-level language-server tests;
- styling changes need parsing, validation, and Avalonia-mapping tests.

Generated output should remain deterministic and inspectable. Performance-sensitive changes should include evidence for allocations, invalidation breadth, or startup cost when those are part of the claim.

## Pull requests

Keep changes narrow and state what the change proves. Call out decisions that remain open and avoid bundling unrelated language, runtime, styling, and tooling work into one review.
