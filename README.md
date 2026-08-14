# Lucent

**Compiled declarative UI for .NET.**

Lucent is an experimental C#-superset language and UI framework for building cross-platform desktop applications on Avalonia. It aims to provide React-like components, compiler-managed fine-grained updates, CSS-native styling, and normal .NET interoperability without exposing a virtual DOM or requiring XAML.

> [!IMPORTANT]
> Lucent is in the design and proof-of-concept stage. The repository contains a narrow compiler and a direct-native Avalonia control experiment; there is no general-purpose compiler, stable runtime, package, or compatibility promise yet.

```csharp
component Counter()
{
    private readonly State<int> count = new(0);

    Fragment Render()
    {
        return StackPanel {
            class: "counter";

            TextBlock {
                Text: $"Count: {count.Value}";
            }

            Button {
                class: "primary";
                Content: "Increment";

                Click: (sender, e) => {
                    count.Update(count.Value + 1);
                };
            }
        };
    }
}
```

The intended compilation model is:

```text
.lui source
    -> Lucent syntax and semantic model
    -> component and reactive IR
    -> generated C#
    -> Roslyn
    -> Lucent runtime
    -> Avalonia controls
```

## Design direction

Lucent is built around a few opinionated choices:

- Use normal C# for logic and purpose-built syntax where UI would otherwise become awkward.
- Give components class-like ownership while keeping `Render()` a function of current inputs, state, slots, and context.
- Compile reactive dependencies into direct, fine-grained Avalonia updates.
- Prefer explicit structure over hidden convenience.
- Use CSS as an authoring format, with a typed compiled representation where practical.
- Keep native Avalonia controls as the default rendering surface and allow a small optional Lucent control library only where it centralizes meaningful behavior.
- Make editor support part of the proof of concept, not a later cleanup project.

The goal is not C# punctuation wrapped around XAML semantics. Lucent owns a small amount of UI-specific syntax so the compiler can understand component identity, lifetime, slots, control flow, styling, and reactivity directly.

## Documentation

- [Project vision](docs/VISION.md)
- [Language and Avalonia design review](docs/DESIGN_REVIEW.md)
- [Language model](docs/LANGUAGE.md)
- [Compiler and runtime architecture](docs/ARCHITECTURE.md)
- [Styling and design tokens](docs/STYLING.md)
- [Tooling and developer experience](docs/TOOLING.md)
- [Proof-of-concept roadmap](docs/ROADMAP.md)
- [Design decisions and open questions](docs/DECISIONS.md)
- [Documentation index](docs/README.md)

## Repository status

This repository contains a shared [Lucent compiler](src/Lucent.Compiler), [CLI](src/Lucent.Compiler.Cli) and project-aware [MSBuild](src/Lucent.Compiler.MSBuild) adapters, a basic [language server](src/Lucent.LanguageServer) and [VS Code extension](editors/vscode), Counter and native-control TodoMVC examples, and a runnable Avalonia host in [`src/Lucent.Poc`](src/Lucent.Poc). The POC generates compiled C# from `.lui` under `obj` during the normal build. The editor currently provides diagnostics and native-control/member hover; go-to-definition works for project-source controls and members.

Run the desktop POC with:

```powershell
dotnet run --project src/Lucent.Poc/Lucent.Poc.csproj
```

See [POC 0001 notes](docs/poc/0001-desktop-window.md) for the host's exact scope, findings, and open issues.

Generate and verify the Counter output with:

```powershell
dotnet run --project src/Lucent.Compiler.Cli/Lucent.Compiler.Cli.csproj -- generate --input examples/counter/Counter.lui --output src/Lucent.Poc/Generated/CounterComponent.g.cs
dotnet run --project src/Lucent.Compiler.Cli/Lucent.Compiler.Cli.csproj -- verify --input examples/counter/Counter.lui --output src/Lucent.Poc/Generated/CounterComponent.g.cs
```

Prepare and test the experimental VS Code extension with:

```powershell
Push-Location editors/vscode
npm install
npm test
npm run prepare-server
Pop-Location
```

See [POC 0002](docs/poc/0002-counter-compiler.md) for the original compiler checkpoint and [POC 0003](docs/poc/0003-shared-compiler-tooling.md) for the generalized binder, bounded C# islands, source mapping, MSBuild adapter, and editor foundation.

See [POC 0004](docs/poc/0004-native-controls-todo.md) for the direct native-control TodoMVC, explicit and implicit Avalonia content, target-type primitive conveniences, keyed row identity, project-aware metadata binding, and current limits.

See [CONTRIBUTING.md](CONTRIBUTING.md) before proposing syntax or architecture changes. Lucent has deliberately deferred several design choices, and examples should not quietly turn those possibilities into promises.
