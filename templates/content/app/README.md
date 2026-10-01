# LucentAppProject

This Windows app uses Lucent __LUCENT_VERSION__ and .NET SDK __DOTNET_SDK_VERSION__.
It targets `win-x64` and Windows SDK 10.0.26100.0, matching the current Windows host package.
Generation does not restore packages or run scripts. Configure the authenticated
Lucent GitHub package feed outside this project; never commit its credentials.
See https://github.com/RichiCoder1/lucent/blob/main/docs/PACKAGES.md.
The supported install/edit/test/publish journey is documented at
https://github.com/RichiCoder1/lucent/blob/main/docs/GETTING-STARTED.md.

Run these commands explicitly from this directory:

```powershell
dotnet restore
dotnet build -c Debug --no-restore -p:PublishAot=false
dotnet run -c Debug --no-build --no-restore -p:PublishAot=false
dotnet publish -c Release -r win-x64 -p:PublishAot=true
```

Commit the `packages.lock.json` created by the first restore. Use
`dotnet restore --locked-mode` in CI. Keep this project's exact Lucent and SDK
pins together when selecting another verified release.

`MainView.lui` owns its counter and editor session per mount. The `[Owned]`
session is disposed with the component. The button uses typed packaged Lucide
artwork. No router, service container, window activation registration, or editor
settings are needed for this example. A native publish needs the Windows native
toolchain described in the Lucent package documentation.

For managed debugging, install Microsoft's C# extension (`ms-dotnettools.csharp`)
in Microsoft-distributed Visual Studio Code. Review its setup and proprietary
debugger licensing restrictions:
https://github.com/dotnet/vscode-csharp/blob/main/debugger.md and
https://github.com/dotnet/vscode-csharp/blob/main/docs/debugger/Microsoft-.NET-Core-Debugger-licensing-and-Microsoft-Visual-Studio-Code.md.
The `coreclr` launch route and mapped `.lui` breakpoint hit are awaiting the actual
fresh-editor walkthrough. Lucent extension 0.3.7 adds `.lui` breakpoint placement;
the official 107.1 VSIX predates that correction.

After the Debug build, create your own `.vscode/launch.json` if desired. Use
`"type": "coreclr"`, `"request": "launch"`, `"cwd": "${workspaceFolder}"`, and
`"program": "${workspaceFolder}/bin/Debug/net10.0-windows10.0.26100.0/win-x64/LucentAppProject.dll"`.
The template replaces `LucentAppProject` with your project name. Launch the managed
DLL with its adjacent Debug symbols, set a breakpoint on `count += 1;` in
`MainView.lui`, and press **Increment**. Confirm an authored-source stop and inspect
`count`; an unbound breakpoint does not prove mapping. Templates generate no IDE
configuration. The NativeAOT published executable uses a separate native debugging
route and is not the target of this managed configuration.
