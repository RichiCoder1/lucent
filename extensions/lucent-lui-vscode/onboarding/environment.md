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

The report opens as an untitled JSON document. To share it, run **Lucent: Review
and Copy Environment Report**, review the displayed report, then choose **Copy
Report**. Configuration credentials and document contents are excluded. No report
is uploaded automatically.
