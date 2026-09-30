# LucentTests

Headless MSTest tests using Lucent __LUCENT_VERSION__ and .NET SDK
__DOTNET_SDK_VERSION__. No desktop window or physical input is used.

After configuring the authenticated Lucent package feed, run these commands
explicitly; generation runs neither of them:

```powershell
dotnet restore
dotnet run -c Release --no-restore -- --minimum-expected-tests 1
```

The executable MSTest runner works without changing your workspace's
`global.json`. If your workspace already selects Microsoft.Testing.Platform,
you can also use `dotnet test --project LucentTests.csproj`.
Commit the first restore's lock file and use locked restore in CI. This template
does not generate SDK, NuGet, tool, or editor configuration.

The counter test invokes a semantic action and checks updated content and
retained identity. Snapshots and the application are disposed. Add `--Skia`
when creating this project to include a production-renderer pixel test; the
base harness intentionally uses deterministic text metrics. Neither harness
certifies Windows focus, IME, or native accessibility.
