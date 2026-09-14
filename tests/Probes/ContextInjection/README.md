# Context and injection package probe

This probe compiles the requirement declaration in a separate library assembly, then builds and runs a package-only consumer under managed .NET and NativeAOT. It verifies exact typed context and service resolution, setup ordering, component cleanup before borrowed-service disposal, and the absence of an internal-access dependency.

Run `./Test-Package.ps1 -Feed <package-directory> -Version <exact-dev-version>` after packing `Lucent.Core`. The script uses only the supplied package for Lucent runtime code and writes disposable evidence under `artifacts/context-injection-package`.
