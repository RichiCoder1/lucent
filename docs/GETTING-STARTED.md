# Start a Lucent Windows application

This guide covers local Windows x64 and Microsoft Visual Studio Code. The reference
package release is `0.3.0-dev.107.1`, published from
`8e8c0788e5bd675546852a4e7b5c96ccfcf7fe36` by
[CI107](https://github.com/RichiCoder1/lucent/actions/runs/36784978383).
Use one complete, successfully published release set and keep its exact package,
SDK and language-tool identities together. No Lucent checkout is required.

The fresh installed-editor journey, including the managed debugger instructions
below, is awaiting its actual walkthrough in
[#249](https://github.com/RichiCoder1/lucent/issues/249). Template consumers, tests
and NativeAOT publication already have separate package evidence. Those checks do
not establish an editor breakpoint hit or this starter's window startup/close.

## Obtain a verified release

The supported distribution is authenticated GitHub Packages plus verified CI
artifacts or explicitly selected local packages and VSIX. Lucent does not currently
claim public NuGet.org or VS Code Marketplace publication. The extension identity
is `lucent.lucent-lui`; that identity alone is not proof of publisher ownership.

Install .NET SDK `10.0.401` with the release's `rollForward: disable` policy and
Microsoft-distributed Visual Studio Code. Install the verified release's
`extension.vsix` through **Extensions: Install from VSIX**. The VSIX includes its
language server, not the .NET SDK. Use the exact artifact and hashes recorded by
the complete release descriptor; authenticate the fixed repository, workflow, run
and artifact through GitHub before trusting that descriptor. CI artifacts expire,
so preserve the authenticated bundle if you need the local/offline route. See
[release-set verification](RELEASE-SETS.md).

For GitHub Packages, configure the authenticated Lucent source and source mapping
as described in [package consumption](PACKAGES.md#consumption). Keep credentials
in user configuration or the supported credential environment variable; never
commit them. Install the selected template version explicitly:

```powershell
dotnet new install Lucent.Templates@0.3.0-dev.107.1
```

Alternatively, place the authenticated bundle's `.nupkg` files in a local feed,
configure that feed for `Lucent.*`, and install the exact template artifact:

```powershell
dotnet new install C:\LucentFeed\Lucent.Templates.0.3.0-dev.107.1.nupkg
```

Replace `C:\LucentFeed` with your chosen directory. Local template installation
does not establish an offline restore: every selected ecosystem dependency must
also exist in your configured feed/cache. Keep nuget.org available for those
dependencies when using the online fallback.

## Create, restore and edit

```powershell
dotnet new lucent-app -n HelloLucent -o HelloLucent
cd HelloLucent
dotnet restore
dotnet build -c Debug --no-restore -p:PublishAot=false
```

Generation performs no restore or post-action. Inspect the generated files before
restoring, retain the resulting `packages.lock.json`, and use
`dotnet restore --locked-mode` for subsequent reproducible restores. The app targets
`net10.0-windows10.0.26100.0` and `win-x64`; its Windows host requires the supported
Windows version specified by that target.

Open the `HelloLucent` folder in VS Code. Restricted Mode permits syntax highlighting
and static environment checks. Review **Workspace Trust** before enabling semantic
services: MSBuild evaluation may execute project tooling. Run **Lucent: Select
Project**, choose `HelloLucent.csproj`, and open `MainView.lui`. The **Lucent** status
item reports whether matching language tools are ready. Use **Output → Lucent LUI**
and **Problems** for failures. Do not configure a checkout server path.

If the required tools are approved but absent, explicitly choose **Lucent: Install
Matching Language Tools**, or **Lucent: Import Server Archive** for a verified
offline archive. Online acquisition uses VS Code's GitHub authentication provider;
neither operation changes package pins. The current approved catalog includes
107.1. An unknown release needs a compatible extension/catalog update. See
[editor setup](../extensions/lucent-lui-vscode/onboarding/setup.md).

Change an authored expression in `MainView.lui`, inspect completion/hover and any
diagnostics, then save before building. Run the managed Debug output explicitly:

```powershell
dotnet run -c Debug --no-build --no-restore -p:PublishAot=false
```

## Debug an authored expression

This route uses Microsoft's **C#** extension (`ms-dotnettools.csharp`) and its
`coreclr` debugger in Microsoft-distributed VS Code. Lucent supplies source mappings
and `.lui` breakpoint placement; the external extension owns the debugger. Review
Microsoft's [debugger setup](https://github.com/dotnet/vscode-csharp/blob/main/debugger.md)
and [debugger licensing restrictions](https://github.com/dotnet/vscode-csharp/blob/main/docs/debugger/Microsoft-.NET-Core-Debugger-licensing-and-Microsoft-Visual-Studio-Code.md).
The debugger is proprietary and is not licensed for arbitrary VS Code forks.

The mapped breakpoint route is awaiting the actual fresh-editor walkthrough.
Lucent extension 0.3.7 adds the `.lui` breakpoint contribution; the official CI107
VSIX predates that correction. Do not treat installation of that older VSIX as
proof of the corrected breakpoint experience.

Build Debug with the command above. If you choose to debug, create your own
`.vscode/launch.json` in `HelloLucent` with this configuration. Templates do not
generate IDE configuration. The `program` is the managed DLL and adjacent Debug
symbols, not the NativeAOT published executable:

```json
{
  "version": "0.2.0",
  "configurations": [{
    "name": "HelloLucent managed Debug",
    "type": "coreclr",
    "request": "launch",
    "program": "${workspaceFolder}/bin/Debug/net10.0-windows10.0.26100.0/win-x64/HelloLucent.dll",
    "cwd": "${workspaceFolder}",
    "console": "internalConsole",
    "justMyCode": true,
    "stopAtEntry": false
  }]
}
```

Set a breakpoint on `count += 1;` in `MainView.lui`, select this configuration,
start debugging, and press **Increment** in the window. Confirm that the debugger
stops at the authored `.lui` statement and inspect `count`. An unbound breakpoint
is not a pass. Stop debugging or close the app normally. Replace `HelloLucent` in
the configuration if you used another project name.

## Run a headless test and publish

From the parent directory, create and run one separate test starter:

```powershell
dotnet new lucent-tests -n HelloLucent.Tests -o HelloLucent.Tests
cd HelloLucent.Tests
dotnet restore
dotnet run -c Release --no-restore -- --minimum-expected-tests 1
```

Its semantic action test runs without a desktop window. `--Skia` adds an optional
production-renderer capture test when creating the starter. See [templates](TEMPLATES.md).

Return to the app directory. Native publication separately requires Microsoft's
[Windows NativeAOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/),
including Desktop development with C++. The explicit native doctor reports
registered components, not a publication guarantee. Publish and then start the
actual native executable:

```powershell
dotnet publish -c Release -r win-x64 -p:PublishAot=true -o publish
.\publish\HelloLucent.exe
```

Exercise the counter and text input, then close normally. Keep the published
dependency assets/notices together. Record startup/close independently from a
successful publish; managed run and native publication are separate capabilities.
