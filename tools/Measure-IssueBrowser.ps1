param(
    [Parameter(Mandatory)][string] $AppExe,
    [Parameter(Mandatory)][string] $OutputDirectory
)

# Opt-in characterization, deliberately separate from the fixed compatibility gate.
# Prepare/publish first. Do not run concurrently with other builds or benchmarks.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class IssueBenchmarkInput {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
}
'@

$app = [IO.Path]::GetFullPath($AppExe)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $output -Force
$raw = Join-Path $output 'frames.log'
$journal = Join-Path $output 'characterization.json'
if (-not (Test-Path -LiteralPath $app -PathType Leaf)) { throw "App executable does not exist: $app" }
if ((Test-Path -LiteralPath $journal) -or (Test-Path -LiteralPath $raw)) {
    throw "Use a fresh evidence directory; refusing to overwrite existing evidence in $output."
}
$clock = [Diagnostics.Stopwatch]::StartNew()
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appDirectory = Split-Path $app -Parent
$runtimeConfigPath = Join-Path $appDirectory (([IO.Path]::GetFileNameWithoutExtension($app)) + '.runtimeconfig.json')
$runtimeConfig = $null
if (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf) {
    try { $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json }
    catch { $runtimeConfig = $null }
}
$gitRevision = 'unknown'
$gitDirtyFiles = @()
try {
    $gitRevision = (& git -C $repo rev-parse HEAD 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or !$gitRevision) { $gitRevision = 'unknown' }
    else { $gitDirtyFiles = @(& git -C $repo status --short 2>$null) }
}
catch { $gitRevision = 'unknown'; $gitDirtyFiles = @() }
$appBinaries = @(
    Get-ChildItem -LiteralPath $appDirectory -File |
        Where-Object {
            $_.Extension -in @('.exe', '.dll') -or
            $_.Name.EndsWith('.deps.json', [StringComparison]::OrdinalIgnoreCase) -or
            $_.Name.EndsWith('.runtimeconfig.json', [StringComparison]::OrdinalIgnoreCase)
        } |
        Sort-Object Name |
        ForEach-Object {
            [ordered]@{
                name = $_.Name
                lengthBytes = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        }
)
$cpu = $env:PROCESSOR_IDENTIFIER
try {
    $processor = Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop | Select-Object -First 1
    if ($processor.Name) { $cpu = $processor.Name.Trim() }
}
catch { if (!$cpu) { $cpu = 'unknown' } }
$dotnetCommand = Join-Path $repo '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetCommand -PathType Leaf)) {
    $dotnetCommand = (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1).Source
}
$dotnetSdk = 'unknown'
$dotnetRuntimes = @()
if ($dotnetCommand) {
    try {
        $sdkOutput = @(& $dotnetCommand --version 2>$null)
        if ($LASTEXITCODE -eq 0 -and $sdkOutput.Count -gt 0) { $dotnetSdk = $sdkOutput[-1].ToString().Trim() }
        $runtimeOutput = @(& $dotnetCommand --list-runtimes 2>$null)
        if ($LASTEXITCODE -eq 0) { $dotnetRuntimes = @($runtimeOutput | ForEach-Object { $_.ToString().Trim() }) }
    }
    catch { $dotnetSdk = 'unknown'; $dotnetRuntimes = @() }
}
$report = [ordered]@{
    schemaVersion = 1
    status = 'running'
    currentStage = 'initialization'
    currentScenario = $null
    currentOperationIndex = $null
    workload = 'issue-browser-characterization-v1'
    app = $app
    appSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash
    appFileVersion = if ([Diagnostics.FileVersionInfo]::GetVersionInfo($app).FileVersion) { [Diagnostics.FileVersionInfo]::GetVersionInfo($app).FileVersion } else { 'unknown' }
    appBinaries = $appBinaries
    source = [ordered]@{
        checkoutRevision = $gitRevision
        checkoutDirty = [bool]($gitDirtyFiles.Count -gt 0)
        dirtyPaths = $gitDirtyFiles
        appBuiltFromRevision = if ($env:LUCENT_PERFORMANCE_SOURCE_REVISION) { $env:LUCENT_PERFORMANCE_SOURCE_REVISION } else { 'unknown' }
        appSourceBinding = if ($env:LUCENT_PERFORMANCE_SOURCE_REVISION) { 'provided by caller' } else { 'unknown; app binary is not bound to this checkout' }
    }
    dataVersion = 'lucent-issue-browser-v1'
    dataOrdering = 'VisibleIssues preserves GitHubIssueSource order: issue number descending; UIA scroll percent 0 is the top, 100 is the end.'
    samplesPerScenario = 500
    rawLog = $raw
    resourceEvidence = [ordered]@{
        source = $raw
        fields = @('surfaceCount', 'textureCount', 'textBlobCount', 'uiaProviderCount', 'processHandleCount')
        scope = 'sampled on emitted frames; between-frame peaks may be missed'
        phaseRows = 'Application emits pre after its first frame and post during normal shutdown; the shared verifier should evaluate both'
    }
    instrumentation = 'Per-frame file diagnostics. Each client operation records action time and time through the first attributed frame; client action/wait values include synchronous UIA and frame-log polling. App frame latency excludes client dispatch and diagnostic file I/O. Extra frames and settle-drain time remain recorded separately.'
    uiAutomation = [ordered]@{
        clientMode = 'PowerShell UIAutomationClient control-tree queries and patterns'
        runnerEventSubscriptions = 'none'
        externalListeners = 'unknown'
    }
    machine = [ordered]@{
        name = [Environment]::MachineName
        processor = if ($cpu) { $cpu } else { 'unknown' }
        logicalProcessors = $env:NUMBER_OF_PROCESSORS
        os = [Environment]::OSVersion.VersionString
        osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    }
    runtime = [ordered]@{
        dotnetSdk = $dotnetSdk
        availableRuntimes = $dotnetRuntimes
        appRuntimeConfig = if ($runtimeConfigPath -and (Test-Path -LiteralPath $runtimeConfigPath)) { $runtimeConfigPath } else { 'unknown; no runtimeconfig file found' }
        appFramework = if ($runtimeConfig.runtimeOptions.framework) { $runtimeConfig.runtimeOptions.framework } elseif ($runtimeConfig.runtimeOptions.frameworks) { @($runtimeConfig.runtimeOptions.frameworks) } else { 'unknown' }
        appExecutionModel = 'unknown until loaded runtime modules are observed'
        measurementHost = "PowerShell $($PSVersionTable.PSVersion); .NET $([Environment]::Version)"
    }
    conditions = [ordered]@{
        environment = if ($env:LUCENT_PERFORMANCE_ENVIRONMENT) { $env:LUCENT_PERFORMANCE_ENVIRONMENT } else { 'unknown' }
        isolation = if ($env:LUCENT_PERFORMANCE_ISOLATION) { $env:LUCENT_PERFORMANCE_ISOLATION } else { 'unknown' }
        power = if ($env:LUCENT_PERFORMANCE_POWER) { $env:LUCENT_PERFORMANCE_POWER } else { 'unknown; not sampled or changed by this script' }
        foregroundAtReady = 'unknown until window state is observed'
        displayRefreshHz = 'unknown; not queried by this script'
        displayScale = 'unknown until window DPI is observed'
        viewport = 'unknown until actual client geometry is observed'
    }
    phases = [Collections.Generic.List[object]]::new()
    scenarios = [Collections.Generic.List[object]]::new()
    failures = [Collections.Generic.List[object]]::new()
    warmup = [ordered]@{ requested = 8; completed = 0; status = 'pending' }
    complete = $false
}
$process = $null
$window = [IntPtr]::Zero
$root = $null
$script:activeScenario = $null
$script:activeOperationIndex = $null
$script:journalFailure = $null
$script:frameOffset = [long]0
$script:frameCount = 0
$script:frameRemainder = ''

function Save-Journal {
    $temporaryJournal = "$journal.$PID.tmp"
    $json = $report | ConvertTo-Json -Depth 20
    try {
        [IO.File]::WriteAllText($temporaryJournal, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporaryJournal -Destination $journal -Force | Out-Null
    }
    finally {
        if (Test-Path -LiteralPath $temporaryJournal) { Remove-Item -LiteralPath $temporaryJournal -Force }
    }
}
function Frames {
    if (-not (Test-Path -LiteralPath $raw)) { return $script:frameCount }
    $stream = [IO.File]::Open($raw, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        if ($stream.Length -lt $script:frameOffset) { throw 'Raw frame log was truncated during characterization.' }
        $remaining = [int]($stream.Length - $script:frameOffset)
        if ($remaining -gt 0) {
            $stream.Position = $script:frameOffset
            $bytes = [byte[]]::new($remaining)
            $read = 0
            while ($read -lt $remaining) {
                $count = $stream.Read($bytes, $read, $remaining - $read)
                if ($count -eq 0) { break }
                $read += $count
            }
            $script:frameOffset = $stream.Position
            $text = $script:frameRemainder + [Text.Encoding]::UTF8.GetString($bytes, 0, $read)
            $lines = $text.Split("`n")
            for ($index = 0; $index -lt $lines.Length - 1; $index++) {
                if ($lines[$index].TrimEnd("`r").StartsWith('frame|')) { $script:frameCount++ }
            }
            $script:frameRemainder = $lines[-1]
        }
        return $script:frameCount
    } finally { $stream.Dispose() }
}
function Mark([string] $name) {
    $report.currentStage = $name
    $report.phases.Add([ordered]@{ name = $name; elapsedMs = $clock.Elapsed.TotalMilliseconds; frames = (Frames) })
    Save-Journal
}
function Set-Stage([string] $name) {
    $report.currentStage = $name
}
function Error-Details($record) {
    $cause = $record.Exception.GetBaseException()
    [ordered]@{
        exceptionType = $cause.GetType().FullName
        hresult = ('0x{0:X8}' -f $cause.HResult)
        exception = $record.Exception.ToString()
        scriptStackTrace = $record.ScriptStackTrace
    }
}
function Wait-Until([scriptblock] $condition, [string] $failure) {
    $until = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if ($null -ne $process -and $process.HasExited) { throw "App exited with $($process.ExitCode): $failure" }
        if (& $condition) { return }
        Start-Sleep -Milliseconds 10
    } while ([DateTime]::UtcNow -lt $until)
    throw $failure
}
function Nodes { @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Automation]::ControlViewCondition)) }
function Named([string] $name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Named-All([string] $name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition))
}
function Rows {
    @(Nodes | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem })
}
function Issue-Viewport {
    # The heading, scroll pane, and inner virtual list all share the label Issues.
    $panes = @(Named-All 'Issues' | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Pane })
    if ($panes.Count -gt 1) { throw 'Expected exactly one Issues scroll pane.' }
    if ($panes.Count -eq 1) { $panes[0] }
}
function Visible-IssueNumbers {
    # Preserve actual top-to-bottom display order. Fixture/source order is descending,
    # but the journal records rows intersecting the independently queried list viewport.
    # IsOffscreen alone cannot distinguish realized overscan on every provider.
    $list = Issue-Viewport
    if ($null -eq $list) { return @() }
    $viewport = $list.Current.BoundingRectangle
    $visibleRows = @(Rows | Where-Object {
        $bounds = $_.Current.BoundingRectangle
        -not $_.Current.IsOffscreen -and $bounds.Width -gt 0 -and $bounds.Height -gt 0 -and
            $bounds.Right -gt $viewport.Left -and $bounds.Left -lt $viewport.Right -and
            $bounds.Bottom -gt $viewport.Top -and $bounds.Top -lt $viewport.Bottom
    } | Sort-Object `
        @{ Expression = { $_.Current.BoundingRectangle.Y } },
        @{ Expression = { $_.Current.BoundingRectangle.X } })
    $numbers = foreach ($row in $visibleRows) {
        $match = [regex]::Match($row.Current.Name, '#(?<number>\d+)\b')
        if ($match.Success) { [int]$match.Groups['number'].Value }
    }
    @($numbers)
}
function Node-Identity($node) {
    $bounds = $node.Current.BoundingRectangle
    [ordered]@{
        name = $node.Current.Name
        automationId = $node.Current.AutomationId
        controlType = $node.Current.ControlType.ProgrammaticName
        runtimeId = $node.GetRuntimeId() -join ','
        processId = $node.Current.ProcessId
        bounds = [ordered]@{ x = $bounds.X; y = $bounds.Y; width = $bounds.Width; height = $bounds.Height }
    }
}
function Client-Geometry {
    $client = [IssueBenchmarkInput+Rect]::new()
    if (-not [IssueBenchmarkInput]::GetClientRect($window, [ref] $client)) { throw 'GetClientRect failed.' }
    $dpi = [IssueBenchmarkInput]::GetDpiForWindow($window)
    if ($dpi -eq 0) { throw 'GetDpiForWindow returned zero.' }
    $clientWidth = $client.Right - $client.Left
    $clientHeight = $client.Bottom - $client.Top
    [ordered]@{
        dpi = $dpi
        clientWidthPixels = $clientWidth
        clientHeightPixels = $clientHeight
        clientWidthDip = $clientWidth * 96.0 / $dpi
        clientHeightDip = $clientHeight * 96.0 / $dpi
    }
}
function Scroll-Pattern {
    $node = Issue-Viewport
    $pattern = $null
    if ($null -ne $node -and $node.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref] $pattern)) { return $pattern }
    throw 'Issue Browser did not expose its scroll pattern.'
}
function Set-Size([int] $width, [int] $height, [switch] $AllowUnchanged) {
    # Convert desired client DIPs to an outer physical size, preserving the window's monitor and position.
    $before = Client-Geometry
    $client = [IssueBenchmarkInput+Rect]::new()
    $outer = [IssueBenchmarkInput+Rect]::new()
    if (-not [IssueBenchmarkInput]::GetClientRect($window, [ref] $client)) { throw 'GetClientRect failed.' }
    if (-not [IssueBenchmarkInput]::GetWindowRect($window, [ref] $outer)) { throw 'GetWindowRect failed.' }
    $scale = $before.dpi / 96.0
    $nonClientWidth = ($outer.Right - $outer.Left) - ($client.Right - $client.Left)
    $nonClientHeight = ($outer.Bottom - $outer.Top) - ($client.Bottom - $client.Top)
    $pixelsWide = [int]([Math]::Round($width * $scale) + $nonClientWidth)
    $pixelsHigh = [int]([Math]::Round($height * $scale) + $nonClientHeight)
    if (-not [IssueBenchmarkInput]::SetWindowPos($window, [IntPtr]::Zero, $outer.Left, $outer.Top, $pixelsWide, $pixelsHigh, 0x0014)) { throw 'SetWindowPos failed.' }
    $script:requestedGeometry = $null
    Wait-Until {
        $script:requestedGeometry = Client-Geometry
        [Math]::Abs($script:requestedGeometry.clientWidthDip - $width) -le 2 -and
            [Math]::Abs($script:requestedGeometry.clientHeightDip - $height) -le 2
    } "Window client size did not reach ${width}x${height} DIPs."
    if (-not $AllowUnchanged -and [Math]::Abs($script:requestedGeometry.clientWidthDip - $before.clientWidthDip) -lt 1) {
        throw "Resize to ${width} client DIPs was a no-op; actual client width remained $($script:requestedGeometry.clientWidthDip) DIPs."
    }
    [ordered]@{
        requestedClientWidthDip = $width
        requestedClientHeightDip = $height
        previousClientWidthDip = $before.clientWidthDip
        actual = $script:requestedGeometry
    }
}
function Settle {
    $until = [DateTime]::UtcNow.AddSeconds(5)
    $prior = Frames
    do {
        Start-Sleep -Milliseconds 100
        $next = Frames
        if ($next -eq $prior) { return }
        $prior = $next
    } while ([DateTime]::UtcNow -lt $until)
    throw 'Window did not settle within five seconds.'
}
function Measure-Scenario([string] $name, [scriptblock] $operation) {
    $script:activeScenario = $name
    $script:activeOperationIndex = $null
    $report.currentScenario = $name
    $report.currentOperationIndex = $null
    Set-Stage "$name.prepare"
    Settle
    Mark "$name.start"
    $scenario = [ordered]@{
        name = $name
        expectedOperations = 500
        firstFrame = (Frames) + 1
        operations = [Collections.Generic.List[object]]::new()
        complete = $false
    }
    $report.scenarios.Add($scenario)
    Save-Journal
    foreach ($index in 0..499) {
        $before = Frames
        $script:activeOperationIndex = $index
        $report.currentOperationIndex = $index
        Set-Stage "$name.operation"
        $sample = [ordered]@{
            index = $index
            status = 'running'
            firstFrame = $null
            lastFrame = $null
            attributedFrameCount = 0
            clientActionMs = $null
            requestToFirstFrameMs = $null
            settleDrainMs = $null
            endpoint = $null
            failure = $null
            errorDetails = $null
        }
        $scenario.operations.Add($sample)
        Save-Journal
        $started = [Diagnostics.Stopwatch]::GetTimestamp()
        try {
            $sample.endpoint = & $operation $index
            $sample.clientActionMs = [Diagnostics.Stopwatch]::GetElapsedTime($started).TotalMilliseconds
            Wait-Until { (Frames) -gt $before } "$name operation $index produced no frame."
            $sample.requestToFirstFrameMs = [Diagnostics.Stopwatch]::GetElapsedTime($started).TotalMilliseconds
            $firstFrameObserved = Frames
            $sample.firstFrame = $before + 1
            # Keep every frame attributed to this operation, including frames during settle drain.
            $drain = [Diagnostics.Stopwatch]::StartNew()
            Settle
            $drain.Stop()
            $sample.settleDrainMs = $drain.Elapsed.TotalMilliseconds
            $after = Frames
            $sample.lastFrame = $after
            $sample.attributedFrameCount = $after - $before
            $sample.extraFramesAfterFirst = [Math]::Max(0, $after - $firstFrameObserved)
            $sample.status = 'completed'
        }
        catch {
            $sample.status = 'failed'
            $sample.failure = $_.Exception.Message
            $sample.errorDetails = Error-Details $_
            $sample.requestToFirstFrameMs = if ($null -eq $sample.requestToFirstFrameMs) {
                [Diagnostics.Stopwatch]::GetElapsedTime($started).TotalMilliseconds
            } else { $sample.requestToFirstFrameMs }
            try { $after = Frames }
            catch {
                $after = $before
                $sample.failure += " Frame attribution also failed: $($_.Exception.Message)"
            }
            if ($after -gt $before) {
                $sample.firstFrame = $before + 1
                $sample.lastFrame = $after
                $sample.attributedFrameCount = $after - $before
            }
            $scenario.failedOperation = $index
            $scenario.lastFrame = $after
            $report.failures.Add([ordered]@{
                phase = 'operation'
                stage = $report.currentStage
                scenario = $name
                operationIndex = $index
                message = $sample.failure
                errorDetails = $sample.errorDetails
            })
            try { Save-Journal }
            catch { $script:journalFailure = "Could not persist $name operation $index failure: $($_.Exception.Message)" }
            # Keep the failed operation and stop this corpus without retrying it.
            # Later scenarios have their own verified preparation and can still run.
            break
        }
        $script:activeOperationIndex = $null
        $report.currentOperationIndex = $null
        Save-Journal
    }
    $scenario.complete = $scenario.operations.Count -eq 500 -and
        @($scenario.operations | Where-Object { $_.status -ne 'completed' }).Count -eq 0
    $scenario.lastFrame = Frames
    Mark "$name.end"
    $script:activeScenario = $null
    $script:activeOperationIndex = $null
    $report.currentScenario = $null
    $report.currentOperationIndex = $null
    Save-Journal
}

$primaryFailure = $null
try {
    Mark 'launch.start'
    $info = [Diagnostics.ProcessStartInfo]::new($app)
    $info.WorkingDirectory = $appDirectory
    $info.UseShellExecute = $false
    $info.Environment['LUCENT_PERFORMANCE_DIAGNOSTICS'] = $raw
    $process = [Diagnostics.Process]::Start($info)
    Wait-Until { $process.Refresh(); $process.MainWindowHandle -ne [IntPtr]::Zero } 'App did not expose a window.'
    $window = $process.MainWindowHandle
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($window)
    $report.processId = $process.Id
    $report.mainWindowHandle = ('0x{0:X}' -f $window.ToInt64())
    try {
        $moduleNames = @($process.Modules | ForEach-Object { $_.ModuleName })
        $report.runtime.loadedModules = @($moduleNames | Where-Object { $_ -match '^(coreclr|hostfxr|hostpolicy|clrjit)\.dll$' })
        if ($report.runtime.loadedModules -contains 'coreclr.dll') {
            $report.runtime.appExecutionModel = 'CoreCLR/JIT runtime module observed'
        }
        elseif ($runtimeConfig) {
            $report.runtime.appExecutionModel = 'unknown; runtime config exists but CoreCLR was not observed'
        }
        else {
            $report.runtime.appExecutionModel = 'unknown; no runtime config or CoreCLR module observed, so NativeAOT is not confirmed'
        }
    }
    catch {
        $report.runtime.loadedModules = @()
        $report.runtime.appExecutionModel = 'unknown; process module inventory was unavailable'
    }

    $initialSize = Set-Size 1120 760 -AllowUnchanged
    Wait-Until {
        $visible = Visible-IssueNumbers
        $visible -contains 9999 -and $visible -contains 10000 -and
            $null -ne (Named 'Issues') -and $null -ne (Named 'Search issues')
    } 'Initial Issue Browser did not show Issue 9999, Issue 10000, the Issues list, and Search issues.'
    $initialNumbers = Visible-IssueNumbers
    if (@($initialNumbers | Where-Object { $_ -in @(9999, 10000) }).Count -ne 2) {
        throw 'Initial visible issue endpoint was not Issue 9999 and Issue 10000.'
    }
    if ($initialNumbers.Count -lt 2 -or $initialNumbers[0] -ne 10000 -or $initialNumbers[1] -ne 9999) {
        throw "Initial visible issue order was not #10000 then #9999; observed $($initialNumbers -join ',')."
    }
    if ((Named-All 'Resize issue list').Count -ne 1) { throw 'The initial wide viewport did not expose exactly one issue-list splitter.' }
    $geometry = Client-Geometry
    $report.display = [ordered]@{
        refreshHz = 'unknown; not queried by this script'
        monitor = 'unknown; monitor identity not queried by this script'
        windowDpi = $geometry.dpi
        scalePercent = [Math]::Round($geometry.dpi * 100.0 / 96.0, 2)
        clientWidthPixels = $geometry.clientWidthPixels
        clientHeightPixels = $geometry.clientHeightPixels
        clientWidthDip = $geometry.clientWidthDip
        clientHeightDip = $geometry.clientHeightDip
    }
    $foreground = [IssueBenchmarkInput]::GetForegroundWindow()
    $targetIsForeground = $foreground -eq $window
    $report.conditions.foregroundAtReady = if ($targetIsForeground) { 'target app window was foreground' } else { 'target app window was not foreground' }
    $report.conditions.targetWindowForeground = $targetIsForeground
    $report.conditions.displayScale = "$($report.display.scalePercent)% from GetDpiForWindow"
    $report.conditions.viewport = "$($geometry.clientWidthDip)x$($geometry.clientHeightDip) client DIPs; $($geometry.clientWidthPixels)x$($geometry.clientHeightPixels) physical pixels"
    $report.initialEndpoint = [ordered]@{
        visibleIssueNumbers = $initialNumbers
        search = Node-Identity (Named 'Search issues')
        issueList = Node-Identity (Issue-Viewport)
        splitter = Node-Identity (Named 'Resize issue list')
        requestedWindowSize = $initialSize
    }
    $report.initialTopology = @(
        Nodes |
            Where-Object { $_.Current.Name -and $_.Current.ProcessId -eq $process.Id } |
            ForEach-Object { Node-Identity $_ }
    )
    Mark 'ready'

    Mark 'warmup.start'
    $report.warmup.status = 'running'
    Save-Journal
    for ($index = 0; $index -lt $report.warmup.requested; $index++) {
        if (-not [IssueBenchmarkInput]::PostMessage($window, 0x0100, [IntPtr]9, [IntPtr]1)) { throw "Tab warmup $index failed." }
        if (-not [IssueBenchmarkInput]::PostMessage($window, 0x0101, [IntPtr]9, [IntPtr]0)) { throw "Tab release $index failed." }
        Settle
        $report.warmup.completed++
        Save-Journal
    }
    $report.warmup.status = 'completed'
    $focusedAfterWarmup = @(Nodes | Where-Object { $_.Current.HasKeyboardFocus })
    if ($focusedAfterWarmup.Count -ne 1 -or $focusedAfterWarmup[0].Current.ProcessId -ne $process.Id) {
        throw 'Warmup did not leave exactly one focused element in the Issue Browser process.'
    }
    $report.warmup.focusedEndpoint = Node-Identity $focusedAfterWarmup[0]
    $script:priorFocus = $focusedAfterWarmup[0].GetRuntimeId() -join ','
    Mark 'warmup.end'

    $report.scenarioStartFrame = Frames
    Measure-Scenario 'focus-traversal' {
        param($index)
        if (-not [IssueBenchmarkInput]::PostMessage($window, 0x0100, [IntPtr]9, [IntPtr]1)) { throw "Tab $index failed." }
        if (-not [IssueBenchmarkInput]::PostMessage($window, 0x0101, [IntPtr]9, [IntPtr]0)) { throw "Tab release $index failed." }
        $script:focusEndpoint = $null
        $script:focusIdentity = ''
        Wait-Until {
            $focused = @(Nodes | Where-Object { $_.Current.HasKeyboardFocus })
            if ($focused.Count -ne 1 -or $focused[0].Current.ProcessId -ne $process.Id) { return $false }
            if ([string]::IsNullOrWhiteSpace($focused[0].Current.Name) -and [string]::IsNullOrWhiteSpace($focused[0].Current.AutomationId)) { return $false }
            $script:focusIdentity = $focused[0].GetRuntimeId() -join ','
            if ($script:focusIdentity -eq $script:priorFocus) { return $false }
            $script:focusEndpoint = Node-Identity $focused[0]
            $true
        } "Tab $index did not move focus to a named Issue Browser control."
        $script:priorFocus = $script:focusIdentity
        $script:focusEndpoint
    }
    $report.focusedIdentityCount = @(
        $report.scenarios[$report.scenarios.Count - 1].operations | ForEach-Object { $_.endpoint.runtimeId } | Sort-Object -Unique
    ).Count

    Set-Stage 'list-scroll.precheck'
    $scroll = Scroll-Pattern
    $scroll.SetScrollPercent(-1, 0)
    Wait-Until { [Math]::Abs($scroll.Current.VerticalScrollPercent) -lt 0.01 } 'Issue list did not return to the top before scroll characterization.'
    Settle
    $scrollStart = Visible-IssueNumbers
    if (@($scrollStart | Where-Object { $_ -in @(9999, 10000) }).Count -ne 2) { throw 'Scroll start did not expose Issue 9999 and Issue 10000.' }
    if ($scrollStart.Count -lt 2 -or $scrollStart[0] -ne 10000 -or $scrollStart[1] -ne 9999) {
        throw "Scroll start display order was not #10000 then #9999; observed $($scrollStart -join ',')."
    }
    $script:priorVisibleIssues = $scrollStart
    Measure-Scenario 'list-scroll' {
        param($index)
        $percent = if ($index -lt 250) { ($index + 1) * 0.4 } else { (499 - $index) * 0.4 }
        $previousPercent = $scroll.Current.VerticalScrollPercent
        $scroll.SetScrollPercent(-1, $percent)
        Wait-Until { [Math]::Abs($scroll.Current.VerticalScrollPercent - $percent) -lt 0.01 } "Scroll $index did not reach $percent percent."
        $observedPercent = $scroll.Current.VerticalScrollPercent
        if ([Math]::Abs($observedPercent - $previousPercent) -lt 0.01) { throw "Scroll $index was a no-op at $observedPercent percent." }
        $visibleNumbers = Visible-IssueNumbers
        if ($visibleNumbers.Count -eq 0) { throw "Scroll $index yielded no visible issue identities." }
        if (($visibleNumbers -join ',') -eq ($script:priorVisibleIssues -join ',')) { throw "Scroll $index did not change the visible issue identities." }
        $script:priorVisibleIssues = $visibleNumbers
        if ($index -eq 249 -and ($visibleNumbers.Count -lt 2 -or $visibleNumbers[-2] -ne 2 -or $visibleNumbers[-1] -ne 1)) {
            throw "End scroll did not show #2 then #1 at the bottom; observed $($visibleNumbers -join ',')."
        }
        if ($index -eq 499 -and ($visibleNumbers.Count -lt 2 -or $visibleNumbers[0] -ne 10000 -or $visibleNumbers[1] -ne 9999)) {
            throw "Return scroll did not show #10000 then #9999 at the top; observed $($visibleNumbers -join ',')."
        }
        [ordered]@{
            scrollPercent = $observedPercent
            firstVisibleIssueNumber = $visibleNumbers[0]
            lastVisibleIssueNumber = $visibleNumbers[-1]
            visibleIssueNumbers = $visibleNumbers
        }
    }

    Set-Stage 'selection-content.precheck'
    $scroll.SetScrollPercent(-1, 0)
    Wait-Until { [Math]::Abs($scroll.Current.VerticalScrollPercent) -lt 0.01 } 'Issue list did not reach the top before selection characterization.'
    Settle
    $selectionStart = Visible-IssueNumbers
    if (@($selectionStart | Where-Object { $_ -in @(9999, 10000) }).Count -ne 2) { throw 'Selection start did not expose Issue 9999 and Issue 10000.' }
    Measure-Scenario 'selection-content' {
        param($index)
        $number = if ($index % 2 -eq 0) { 9999 } else { 10000 }
        $row = @(Rows | Where-Object { $_.Current.Name -match "#$number\b" })
        if ($row.Count -ne 1) { throw "Expected one realized Issue $number row, found $($row.Count)." }
        $selection = $row[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $rowIdentity = Node-Identity $row[0]
        $previousDetailNames = @((@(Named-All 'ISSUE #9999') + @(Named-All 'ISSUE #10000')) | ForEach-Object { $_.Current.Name })
        $selection.Select()
        $detailName = "ISSUE #$number"
        Wait-Until {
            $detailNodes = Named-All $detailName
            $selection.Current.IsSelected -and $detailNodes.Count -eq 1
        } "Selection $index did not update detail content to $detailName."
        $detailNode = Named $detailName
        if ($null -eq $detailNode -or $detailNode.Current.Name -ne $detailName) { throw "Detail endpoint was not exactly $detailName." }
        [ordered]@{
            selectedIssue = $number
            selectedRow = $rowIdentity
            detail = Node-Identity $detailNode
            previousDetailNames = $previousDetailNames
        }
    }

    Set-Stage 'resize-same-breakpoint.precheck'
    $resizeRow = @(Rows | Where-Object { $_.Current.Name -match '#10000\b' })
    if ($resizeRow.Count -ne 1) { throw 'Resize preparation requires exactly one Issue 10000 row.' }
    $resizeSelection = $resizeRow[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $resizeSelection.Select()
    Wait-Until { $resizeSelection.Current.IsSelected -and (Named-All 'ISSUE #10000').Count -eq 1 } 'Resize preparation did not select Issue 10000 and its details.'
    $null = Set-Size 1120 760 -AllowUnchanged
    Settle
    Measure-Scenario 'resize-same-breakpoint' {
        param($index)
        $width = if ($index % 2 -eq 0) { 1100 } else { 1120 }
        $size = Set-Size $width 760
        Wait-Until { (Named-All 'Resize issue list').Count -eq 1 -and (Named-All 'ISSUE #10000').Count -eq 1 } "Same-breakpoint resize $index did not retain the wide splitter and Issue 10000 detail."
        $actual = Client-Geometry
        if ($actual.clientWidthDip -lt 820) { throw "Same-breakpoint resize $index landed below the 820-DIP wide breakpoint at $($actual.clientWidthDip) DIPs." }
        [ordered]@{
            requestedClientWidthDip = $width
            previousClientWidthDip = $size.previousClientWidthDip
            actual = $actual
            breakpoint = 'wide'
            splitterPresent = $true
            detail = Node-Identity (Named 'ISSUE #10000')
        }
    }
    Set-Stage 'resize-breakpoint-crossing.precheck'
    $null = Set-Size 1120 760 -AllowUnchanged
    Wait-Until { (Named-All 'Resize issue list').Count -eq 1 -and (Named-All 'ISSUE #10000').Count -eq 1 } 'Breakpoint preparation did not retain the wide splitter and Issue 10000 detail.'
    Settle
    Measure-Scenario 'resize-breakpoint-crossing' {
        param($index)
        $wide = $index % 2 -ne 0
        $width = if ($wide) { 1120 } else { 760 }
        $size = Set-Size $width 760
        Wait-Until {
            ((Named-All 'Resize issue list').Count -eq 1) -eq $wide -and
                (Named-All 'ISSUE #10000').Count -eq 1 -and
                ((Named-All 'Back').Count -eq 1) -eq (-not $wide)
        } "Resize $index did not reach the requested responsive bucket while retaining Issue 10000 detail."
        $actual = Client-Geometry
        $observedWide = $actual.clientWidthDip -ge 820
        if ($observedWide -ne $wide) { throw "Resize $index actual client width $($actual.clientWidthDip) DIPs disagreed with expected breakpoint '$($(if ($wide) { 'wide' } else { 'compact' }))'." }
        [ordered]@{
            requestedClientWidthDip = $width
            previousClientWidthDip = $size.previousClientWidthDip
            actual = $actual
            breakpoint = if ($observedWide) { 'wide' } else { 'compact' }
            splitterPresent = $observedWide
            backButtonPresent = -not $observedWide
            detail = Node-Identity (Named 'ISSUE #10000')
        }
    }
    $report.scenariosCompleted = $report.scenarios.Count -eq 5 -and
        @($report.scenarios | Where-Object { -not $_.complete }).Count -eq 0
}
catch {
    $primaryFailure = $_.Exception.Message
    $report.status = 'failed'
    $report.complete = $false
    $failedScenario = if ($report.scenarios.Count -gt 0) { $report.scenarios[$report.scenarios.Count - 1] } else { $null }
    $report.failures.Add([ordered]@{
        phase = if ($null -ne $script:activeOperationIndex) { 'operation' } else { 'setup' }
        stage = $report.currentStage
        scenario = $script:activeScenario
        operationIndex = $script:activeOperationIndex
        lastScenario = if ($null -ne $failedScenario) { $failedScenario.name } else { $null }
        message = $primaryFailure
        errorDetails = Error-Details $_
    })
    try { Save-Journal }
    catch { $script:journalFailure = "Could not persist operation failure before cleanup: $($_.Exception.Message)" }
}
finally {
    try { Mark 'close.start' }
    catch { $report.failures.Add([ordered]@{ phase = 'cleanup'; message = "Could not record close.start: $($_.Exception.Message)" }) }
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) {
                if (-not [IssueBenchmarkInput]::PostMessage($window, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) { throw 'Close request failed.' }
                if (-not $process.WaitForExit(10000)) { throw 'App did not close within ten seconds; process was left for inspection.' }
            }
            $report.appExitCode = if ($process.HasExited) { $process.ExitCode } else { $null }
            if ($process.HasExited -and $process.ExitCode -ne 0) { throw "App exited with $($process.ExitCode)." }
        }
        catch { $report.failures.Add([ordered]@{ phase = 'cleanup'; message = $_.Exception.Message }) }
        finally { $process.Dispose() }
    }
    try { Mark 'close.end' }
    catch { $report.failures.Add([ordered]@{ phase = 'cleanup'; message = "Could not record close.end: $($_.Exception.Message)" }) }
    try { Mark 'cleanup.end' }
    catch { $report.failures.Add([ordered]@{ phase = 'cleanup'; message = "Could not record cleanup.end: $($_.Exception.Message)" }) }

    if ($script:journalFailure) {
        $report.failures.Add([ordered]@{ phase = 'journal'; message = $script:journalFailure })
    }
    $report.complete = $report.scenariosCompleted -and $report.scenarios.Count -eq 5 -and
        @($report.scenarios | Where-Object { -not $_.complete }).Count -eq 0 -and
        $report.failures.Count -eq 0 -and $report.appExitCode -eq 0
    $report.status = if ($report.complete) { 'completed' } else { 'failed' }
    $report.completedAtUtc = [DateTime]::UtcNow.ToString('O')
    try { Save-Journal }
    catch {
        $script:journalFailure = if ($script:journalFailure) { "$script:journalFailure; final journal write failed: $($_.Exception.Message)" } else { "Final journal write failed: $($_.Exception.Message)" }
        $report.complete = $false
        $report.status = 'failed'
    }
}
if ($script:journalFailure) { [Console]::Error.WriteLine($script:journalFailure) }
Write-Output $journal
if (-not $report.complete -or $report.failures.Count -ne 0 -or $script:journalFailure) { exit 1 }
