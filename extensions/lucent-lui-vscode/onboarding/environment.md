# Check the workspace host

**Lucent: Check Environment** inspects installed .NET tooling and static
configuration. It works in Restricted Mode and runs on the workspace host,
including when that host is remote. Open a file-based workspace folder first.

The check does not restore packages, build, evaluate project code, contact feeds,
install tools, or change settings. It reports editor, build, restore and native
capabilities separately. **Not checked** means the capability has not been proved;
it does not mean a check passed. Installed SDKs do not establish which SDK a project
will select.

The extension needs the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
to run its bundled tools. Building a Lucent project also requires the SDK selected
by that project's `global.json`. Native publication has additional Windows tooling
requirements; these do not block otherwise working editing.

**Lucent: Check Windows Native Prerequisites** is an optional, explicit check of
Visual Studio installer registration for Windows x64 C++ tools and Windows SDK
components on the workspace host. It works in Restricted Mode and does not
evaluate a project. Missing discovery or an inconclusive observation remains
**Not checked**. A successful observation does not establish component-file
health, standalone SDK availability, project compatibility or a successful
NativeAOT publish. Editing and managed development remain independent.

**Lucent: Check NuGet Feed Configuration** explicitly reads effective NuGet source
configuration and package source mapping without contacting feeds. Choose a
workspace folder, package ID and exact version; no project evaluation runs and a
suggested release version is not your project's evaluated package pin.

**Lucent: Check Anonymous Feed Access** additionally contacts eligible configured
destinations after Workspace Trust and a destination/privacy confirmation. It
uses no credentials, credential plugins, default credentials, cookies or proxies,
and does not restore or download package contents. An anonymous view cannot prove
private package availability, configured authentication or future restore success.
Source identities are reported only as numbered observations; URLs, source names,
paths and credentials are excluded. Unsupported configuration, changed inputs or
unavailable observations remain explicit. Both checks run only when requested and
support cancellation and the same report review/copy flow.

The report opens as an untitled JSON document. To share it, run **Lucent: Review
and Copy Environment Report**, review the displayed report, then choose **Copy
Report**. Configuration credentials and document contents are excluded. No report
is uploaded automatically.
