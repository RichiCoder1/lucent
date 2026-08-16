# Credits

Lucent builds on ideas and infrastructure from several open-source projects and UI ecosystems. These acknowledgements describe influence and research, not compatibility or code derivation unless stated otherwise.

## Platform and tooling

- [Avalonia](https://avaloniaui.net/) provides the native cross-platform control, rendering, input, accessibility, styling, and application-lifetime foundation that Lucent targets.
- [.NET and C#](https://dotnet.microsoft.com/) provide the runtime, type system, async model, and interoperability surface.
- [Roslyn](https://github.com/dotnet/roslyn) provides C# parsing and semantic analysis for embedded C# and compiles Lucent's generated C#.
- [MSBuild](https://github.com/dotnet/msbuild) provides project evaluation and build integration.
- [Visual Studio Code](https://code.visualstudio.com/) and Microsoft's [Language Server Protocol libraries](https://github.com/microsoft/vscode-languageserver-node) provide the current editor host and language-client plumbing.
- [CSS](https://www.w3.org/Style/CSS/) supplies the familiar authoring vocabulary behind Lucent's deliberately Avalonia-targeted styling subset.

## Design influences

Lucent combines lessons from these projects without trying to clone them:

- [QML](https://doc.qt.io/qt-6/qtqml-index.html), [GNOME Blueprint](https://jwestman.pages.gitlab.gnome.org/blueprint-compiler/), and [Slint](https://slint.dev/) demonstrate purpose-built declarative languages for native UI.
- [SwiftUI](https://developer.apple.com/xcode/swiftui/) and [Jetpack Compose](https://developer.android.com/compose) demonstrate typed composition and ordinary language control flow.
- [React](https://react.dev/) informs Lucent's component, state, context, and unidirectional-data-flow model.
- [Octane](https://octanejs.dev/) informs the compiler-first approach to targeted updates and compiler-derived dependencies.
- [Razor](https://learn.microsoft.com/aspnet/core/razor-pages/) and [Mobile Blazor Bindings](https://github.com/dotnet/MobileBlazorBindings) demonstrate .NET source-to-component and native-control pipelines.
- [StyleX](https://stylexjs.com/) informs static style analysis, deterministic output, and low runtime styling cost.
- [shadcn/ui](https://ui.shadcn.com/) informs the preference for strong primitives and source-owned higher-level components over a large opaque widget catalog.
- [Solid](https://www.solidjs.com/), particularly its Solid 2.0 design work, informs owned reactive computations, async graph semantics, stale-result handling, and structural loading and error boundaries.

The Todo example follows the familiar [TodoMVC](https://todomvc.com/) problem shape as a compact way to exercise input, filtering, keyed identity, and list updates.

## Research sources

The detailed source lists used to evaluate and shape Lucent are kept with the design work they support:

- [Avalonia feasibility review](docs/research/AVALONIA_FEASIBILITY.md), including Avalonia platform behavior, Roslyn generation, MSBuild integration, trimming, and Native AOT references.
- [Language and Avalonia design review](docs/DESIGN_REVIEW.md), including property precedence, control projection, styling, threading, binding, and accessibility references.
- [Async-first research](docs/research/ASYNC_FIRST_CLASS.md), including the Solid 2.0 RFCs and the relevant Avalonia and .NET async documentation.
- [Native-control Todo POC](docs/poc/0004-native-controls-todo.md), including the Avalonia content, items, and property-precedence references used by that slice.

## Third-party software

Direct third-party dependencies are declared in the project files and in [`editors/vscode/package.json`](editors/vscode/package.json). Their authors retain their respective copyrights and licenses. This file is an acknowledgement of the projects that shaped Lucent; it is not a replacement for their license notices.
