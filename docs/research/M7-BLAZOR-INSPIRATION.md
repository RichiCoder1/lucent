# M7 Blazor/Razor inspiration brief

**Scope.** Read-only research for a JSX-like `.lui` language lowering to C#. Facts are marked **Fact**; design conclusions are **Recommendation**. Sources were consulted as primary Microsoft documentation/repositories (2026-08-29 UTC retrieval; repository state can move).

## Findings

1. **Component shape (Fact).** A `.razor` component becomes a C# partial class named after the file. Markup and C# may be colocated in `@code` blocks or split into a `.razor` file plus `.razor.cs`; parameters are public properties marked `[Parameter]`. C# expressions are embedded with `@` (implicit or explicit `@(…)`). **Recommendation:** make `.lui` a typed syntax layer over ordinary partial C# classes, preserve a straightforward code-behind escape hatch, and parse expressions into C# syntax rather than treating them as strings. [Components](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/?view=aspnetcore-9.0), [Razor syntax](https://learn.microsoft.com/en-us/aspnet/core/mvc/views/razor?view=aspnetcore-9.0)

2. **Children and templates (Fact).** Unnamed child markup maps conventionally to a `RenderFragment` parameter named `ChildContent`. Named slots are additional `RenderFragment` parameters supplied by matching child elements. Generic templates use `RenderFragment<TItem>`, `@typeparam TItem`, and a `Context` name (otherwise implicit `context`); consumers can infer or explicitly supply `TItem`. **Recommendation:** lower `.lui` children to typed delegates/fragment values and named children to declared typed slots. Validate duplicate/missing slots and generic context names at compile time; do not use a dictionary of strings. [Templated components](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/templated-components?view=aspnetcore-9.0)

3. **Generated C# and mapping (Fact).** Razor SDK has explicit generation/compile phases (`RazorGenerate`, `RazorCompile`), supports source generation, and can emit generated files with `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` under `obj/.../generated`. C# enhanced `#line` directives were motivated by Razor’s inability to map generated prefixes to exact source columns and debugger sequence points. **Recommendation:** retain a source map from every `.lui` token/span to generated C#, emit `#line`/portable-PDB mappings where possible, and make generated C# inspectable in diagnostics. A single source-of-truth compiler must own parse, lowering, diagnostics, and map production. [Razor SDK targets](https://github.com/dotnet/sdk/blob/main/src/RazorSdk/Targets/Sdk.Razor.CurrentVersion.targets), [enhanced `#line` proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/enhanced-line-directives.md)

4. **Incremental/editor architecture (Fact).** Razor’s MSBuild targets explicitly separate input discovery because it runs during Visual Studio incremental builds; current Razor tooling is moving toward cohosting with Roslyn in one process to improve editor/Hot Reload reliability. **Recommendation:** expose one incremental compiler service usable by build, language server, diagnostics, and preview; cache syntax and semantic results by document/version and never maintain a second editor-only parser with divergent rules. [Razor SDK targets](https://github.com/dotnet/sdk/blob/main/src/RazorSdk/Targets/Sdk.Razor.CurrentVersion.targets), [Razor cohosting announcement](https://github.com/dotnet/razor/issues/12763)

5. **Runtime boundaries (Fact).** Blazor render modes can be static, server, WebAssembly, or auto; the docs warn component authors not to couple implementation to one mode. Auto avoids introducing a second runtime where possible. Render-fragment delegates cannot cross an interactive render-mode boundary because they are not serializable. **Recommendation — severity: high:** Lucent should have one authoritative component/runtime model and explicit hosting adapters, not parallel runtimes with subtly different state/event semantics. Reject or diagnose non-transferable closures/children at adapter boundaries. [Render modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-9.0)

6. **Binding limitations (Fact).** Razor component parameter assignment is type-checked C#; the docs also call out a limitation where mixed markup/C# in a component attribute is rejected as “complex content.” Mobile Blazor Bindings demonstrated native-control elements (`StackLayout`, `Label`, `Button`) and C# event handlers, but was experimental, Xamarin.Forms-era, and archived read-only on 2024-10-29 with no modernization plans. Published package `Microsoft.MobileBlazorBindings` is `0.5.50-preview` (2020-10-30), MIT, and depends on Xamarin.Forms `>=4.8.0.1451`. **Recommendation — severity: high:** keep `.lui` bindings as typed property/event expressions and report exact source spans; do not create a stringly `bind="path"` protocol or copy the abandoned parallel native/hybrid runtime. [Mobile Blazor Bindings README](https://github.com/dotnet/MobileBlazorBindings/blob/6b2d767a44fff94eb90489649889c66a399c00ec/Readme.md), [archive notice](https://github.com/dotnet/MobileBlazorBindings/issues/480), [NuGet package](https://www.nuget.org/packages/Microsoft.MobileBlazorBindings/0.5.50-preview)

## Suggested M7 contract

- `.lui` syntax should have explicit component tags, typed attributes, expression-valued attributes, one unnamed child slot, and named/generic slots.
- Lower to ordinary C# component partials plus typed fragment/delegate values; generated output is an implementation detail but available on demand.
- Diagnostics should report `.lui` locations first, with generated file/line as secondary detail; preserve spans through every lowering step.
- Build and editor use the same incremental front end and generated intermediate representation. Hosting/runtime differences belong below that contract.
- Treat `RenderFragment`-like content as an in-process capability. Crossing process/host boundaries requires an explicit serializable model, never implicit delegate serialization.

## Versions, commits, and licensing notes for `CREDITS.md`

- ASP.NET Core docs: `view=aspnetcore-9.0` pages (Microsoft Learn; documentation content, no source commit pinned in the URL).
- `dotnet/sdk`: `main` Razor SDK target file, consulted at the retrieved repository state; MIT-licensed .NET project. Pin a commit when recording a dependency rather than copying moving `main` URLs.
- `dotnet/csharplang`: enhanced `#line` proposal on `main`, consulted as a language proposal/reference (not a runtime dependency); MIT-licensed repository.
- `dotnet/razor`: issue #12763, consulted for the cohosting status (announcement says Visual Studio 2026 18.3); MIT-licensed repository.
- `dotnet/MobileBlazorBindings`: final README commit `6b2d767a44fff94eb90489649889c66a399c00ec` (2024-10-29), source MIT license (`.NET Foundation Contributors`); archived project, not recommended as a dependency.

## Gaps

The official Razor pages describe behavior and SDK targets but do not constitute a complete public compiler API contract. Mobile Blazor Bindings’ historical README does not establish a durable binding ABI. M7 should pin a chosen .NET SDK/Roslyn version and add executable golden tests for lowering, source maps, slot typing, and incremental invalidation before committing syntax.

## Sources kept / dropped

Kept: Microsoft Learn component, templated-component, Razor syntax, and render-mode pages; dotnet SDK Razor targets; C# enhanced-line proposal; dotnet/razor cohosting issue; Mobile Blazor Bindings README/archive/license; NuGet package metadata. Dropped: search-result duplicates, third-party commentary, and community MAUI forks because they are not authoritative for the historical Microsoft experiment.
