# LucentAppProject

This Windows app uses Lucent __LUCENT_VERSION__ and .NET SDK __DOTNET_SDK_VERSION__.
It targets `win-x64` and Windows SDK 10.0.26100.0, matching the current Windows host package.
Generation does not restore packages or run scripts. Configure the authenticated
Lucent GitHub package feed outside this project; never commit its credentials.
See https://github.com/RichiCoder1/lucent/blob/main/docs/PACKAGES.md.

Run these commands explicitly from this directory:

```powershell
dotnet restore
dotnet build -c Release --no-restore
dotnet run
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
