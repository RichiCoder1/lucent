# Preview process supervisor

This Windows-only development executable starts one explicitly supplied executable
in an owned Windows job. The child starts suspended and is assigned before its
primary thread resumes. The job disallows breakaway and terminates its remaining
processes when its last handle closes. This is process ownership, not a sandbox.

Send one bounded UTF-8 JSON request line on stdin. Keep stdin open while the child
is running. Send `stop` or close stdin to request cleanup. The supervisor forwards
`stop` and closes child stdin, allows the bounded grace period, then terminates the
job if needed. A normal root exit also terminates remaining descendants.

This is a one-request executable. Its redirected console reader runs on a
background thread because Windows console reads may block synchronously. An
initial-input timeout ends the executable without launching a child; process exit
also ends that reader. The control monitor catches channel and disposed-token
errors, and never outlives an externally reusable supervisor session.

```json
{"protocolVersion":1,"kind":"preview-supervisor-request","requestId":"request-1","program":"C:\\Program Files\\dotnet\\dotnet.exe","args":["build","C:\\Projects\\Example\\Example.csproj","--no-restore","--disable-build-servers"],"workingDirectory":"C:\\Projects\\Example","logDirectory":"C:\\Temp\\preview-generation\\logs","timeoutMs":120000,"graceMs":1000,"maxOutputBytes":1048576}
```

Executable, working and log paths must be absolute and already exist. Logs are new
`stdout.log` and `stderr.log` files; existing logs are never overwritten. Log paths
must not traverse reparse points. Environment overrides are optional and bounded.
The default output allowance is 1 MiB per stream, with a hard cap of 16 MiB per
stream; total stored output never exceeds twice the selected allowance. Excess
output initiates cleanup. No application output is forwarded as protocol records.

The final stdout line has `protocolVersion`, `kind: preview-supervisor-result`,
`requestId`, `status`, nullable `exitCode`, `termination`, `treeReaped`,
`stdoutBytes` and `stderrBytes`. Status is `completed`, `cancelled`, `timeout`,
`output-limit`, `launch-failed` or `termination-failed`. Completed means the process
finished; its exit code still determines success. Termination is `natural`,
`cooperative`, `forced` or `unconfirmed`. Forced cleanup does not prove disposal
callbacks ran. Byte counts record observed output, including discarded excess.

Only `treeReaped: true` establishes that job accounting reached zero and owned log
readers completed. `termination-failed`, missing output or supervisor crash must
block replacement and generation-directory deletion. Exit code 3 reports uncertain
termination; other supervised outcomes return exit code 0 and require inspecting
the structured result.

Build tools must disable shared build/compiler servers. The supervisor cannot own
an unrelated existing server that a tool chooses to reuse.

Implementation references: Microsoft's [job object ownership documentation](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects),
[CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw)
and [AssignProcessToJobObject](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject).
Bindings use the repository's pinned Microsoft.Windows.CsWin32 package.
