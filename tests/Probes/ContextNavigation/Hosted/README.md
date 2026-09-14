# Hosted context and navigation package probe

`Test-Package.ps1` copies this fixture into an isolated artifact directory, resolves exact
candidate Core, Hosting, and LUI SDK packages, and runs the same closed typed component under
the managed runtime and win-x64 NativeAOT. It rejects project references and records all three
package hashes with the evidence.
