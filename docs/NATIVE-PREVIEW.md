# Development native preview

The VS Code extension can build an explicit development project and display a PNG
rendered by Lucent's real Skia renderer. Each saved generation uses a fresh worker
with a retained live rendering session. A matching readiness handshake and verified
build admit frames; confirmed process cleanup permits replacement. The preview does
not call the shipping application's entry point or run inside the language server.

This first delivery supports local Windows desktop workspaces and managed .NET
development projects. The preview libraries and tools are in-tree, non-packable
development dependencies. External package delivery, automatic component activation,
inspection and unsaved compilation have separate roadmap work.

## Try the compiled fixture

Build the tools from the repository into a new absolute directory:

```powershell
./tools/Build-PreviewTools.ps1 -OutputRoot C:/Temp/lucent-preview-tools
```

Use a VS Code extension built from the same checkout. In the trusted repository
workspace settings, configure the checked-in worker fixture:

```json
{
  "lucentLui.preview": {
    "projectPath": "tests/Probes/Preview/WorkerFixture/WorkerFixture.csproj",
    "scenarioId": "card/interactive",
    "targetFramework": "net10.0",
    "toolsDirectory": "C:/Temp/lucent-preview-tools"
  }
}
```

Run **Lucent: Start Native Preview (Development)** from the command palette.
**Refresh Native Preview** starts a new generation; **Stop Native Preview** or
closing the panel cancels the active work. Saving a watched file invalidates the
current image immediately and coalesces the next build. A failed build or scenario
leaves the previous image visibly marked out of date. Unsaved buffers are not
compiled by this slice.

The scenario button opens VS Code's Quick Pick for the executable's registered
scenarios. Selecting one uses its defaults. Open the presentation settings using
the size button, edit logical width/height, device scale, light/dark appearance,
contrast and fixture density, then apply the draft. Cancel discards that draft;
Scenario defaults fills the draft with the selected scenario's authored defaults.
While stopped, **Apply for next start** stages settings without executing work;
**Start preview** uses them. While running, **Apply & restart** creates fresh state.
The authored theme factory receives the selected appearance. Density remains
explicit fixture data, so its visual effect depends on the fixture using it.
**Display zoom** changes editor magnification without rebuilding or changing DPI.
The default **Fit** shrinks the accepted image to the available canvas without
enlarging it. Its caption identifies the actual displayed image's presentation
and generation, including when newer requested settings have not produced a frame.
**Reset** rebuilds with a fresh fixture and clock, retaining the current controls.
No setting or application source is rewritten by these controls.

Hidden panels stop executable work and resume when visible again, unless explicitly
stopped. Current live frames accept pointer, wheel, keyboard and committed text.
Click the image or press Enter on its focus stop to interact. Tab and Shift+Tab
then move between component controls; ordinary Escape reaches the component.
Shift+Escape, **Leave preview**, or **Lucent: Leave Preview Interaction** releases
interaction without waiting for component code. Focus alone does not reenter it.
F6/Shift+F6 and editor navigation chords remain host-owned. Under supported default
bindings, the next Tab after leaving moves through editor controls.

VS Code's **Tab Moves Focus** toggle is not synchronized with preview interaction
in this release; its default setting is not a reliable live toggle value. Custom
host keybindings and full IME/preedit behavior are not certified. Browser paste
forwards bounded plain committed text; native clipboard integration is separate.

A stale generation is dimmed, labeled and read-only. Input refers to the actually
displayed frame. A pointer press or wheel event may survive a newer pending image
only when native validation confirms unchanged input targets, geometry, clips,
viewport and scrollbar geometry. Keyboard and text input require the current
frame. If a release races a newer frame, it may complete only
the original surviving pointer capture or key owner. Removed or changed owners
lose the gesture; stale presses never hit-test new geometry. Frame delivery
allows one unacknowledged image and one latest
pending state; an unresponsive webview suspends delivery instead of collecting
images. Hiding and showing the panel creates a fresh delivery session.

The tools directory contains `build/Lucent.Preview.Build.exe` and
`supervisor/Lucent.Preview.Supervisor.exe`. The selected .NET SDK must be installed.
The project must lie inside the selected trusted workspace folder. Remote
workspaces and restricted workspaces cannot start executable preview work.

## Add an application scenario

Keep components in a library that the shipping application and a separate preview
executable can reference. Register explicit data and presentations using
[preview scenarios](PREVIEW-SCENARIOS.md), reference `Lucent.Preview.Hosting` only
from the development executable, and call:

```csharp
return await PreviewWorker.RunAsync(catalog, args);
```

The development executable owns the catalog. It need not start a window or invoke
production startup. `PreviewWorkerOptions.PrepareImagesAsync` is an explicit,
cancellable readiness callback for bounded one-shot capture; it is not a live
readiness contract and is rejected in live mode. Live image loading uses normal
owned application invalidation. See the checked-in
[worker fixture](../tests/Probes/Preview/WorkerFixture/Program.cs).

Configure the development `.csproj`, target framework and ordinal scenario ID in
`lucentLui.preview`. `configuration` defaults to `Debug`. `extraInputs` accepts
workspace-relative file paths for additional fixture/build dependencies that are
not already declared to MSBuild. These files participate in freshness checks.

## Freshness and process ownership

The build tool uses the selected SDK, an evaluated project graph and isolated
outputs. It records declared sources, imports, assets, references, restore state,
consumed compiler inputs and the resulting executable files. It reevaluates glob
membership and verifies input and artifact hashes before execution and again
before admitting the first live frame. Subsequent frames reuse the same verified
build; saved-input changes invalidate the session. External source globs are
watched for newly added files, alongside the previously known inputs.

Existing dependency locks are copied into each generation before restore. The
authored files remain unchanged, and their locked-mode policy still applies:
an incompatible lock fails the preview build instead of silently relaxing it.

This validates declared and evaluated inputs optimistically. Custom build tasks
and component code can still read undeclared files, environment values or the
network. Declare additional file inputs explicitly; this is not a hermetic build
or a security sandbox. Workspace Trust authorizes real project code execution.

The Windows supervisor puts each process into an owned job before allowing it to
execute. Stop or parent-channel closure requests cooperative cancellation, then
forces termination after a bounded grace period. Unconfirmed process cleanup
blocks replacement and preserves that generation's directory. Forced termination
does not imply that managed cleanup callbacks completed.

## Diagnostics

The **Lucent Preview** output channel reports actionable failures and the location
of retained generation evidence under the extension's global storage. Confirmed
stopped or superseded generations are removed after their process trees are reaped.
Confirmed failures retain compact diagnostic history with count and byte bounds;
uncertain termination directories are quarantined and never automatically pruned.

Build, worker and protocol diagnostics are separate from **Lucent LUI** language
server logs. A preview failure does not restart or dispose the language server.
Mapped compiler diagnostics offer **Open source** actions for `.lui` and C# files
inside the selected workspace. The extension verifies the physical file and owns
the target location; the webview cannot choose an arbitrary path. Diagnostic
details for files outside that workspace remain in the output log.
The [orchestration design](plans/native-preview-orchestration.md) records the
ownership and verification boundaries.

Worker protocol v2 requires a matching development worker and extension. There
is no compatibility adapter for the earlier development-only protocol. Catalog
and frame metadata are bounded to 64 KiB, with at most 64 scenarios. Captures
allow logical dimensions 1–8192, scale/density 0.25–4, at most 16,777,216 pixels,
and PNG payloads up to 32 MiB. Unsupported defaults fail before allocation.
