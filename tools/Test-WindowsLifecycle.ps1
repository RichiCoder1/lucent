param([Parameter(Mandatory)] [string] $Executable)

$ErrorActionPreference = 'Stop'

function Stop-LifecycleProcess([Diagnostics.Process] $Process) {
    if ($null -eq $Process) { return }
    try {
        $Process.Refresh()
        if (-not $Process.HasExited) {
            [void]$Process.CloseMainWindow()
            if (-not $Process.WaitForExit(3000)) {
                $Process.Kill()
                $Process.WaitForExit()
            }
        }
    }
    finally { $Process.Dispose() }
}

function Read-Log([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $reader = [IO.StreamReader]::new($stream)
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    catch [IO.IOException] { return '' }
}

function Wait-Marker(
    [Diagnostics.Process] $Process,
    [string] $Output,
    [string] $Marker,
    [int] $TimeoutMilliseconds = 10000
) {
    $deadline = [Environment]::TickCount64 + $TimeoutMilliseconds
    do {
        $text = Read-Log $Output
        if ($text.IndexOf($Marker, [StringComparison]::Ordinal) -ge 0) { return $text }
        $Process.Refresh()
        if ($Process.HasExited) { break }
        Start-Sleep -Milliseconds 50
    } until ([Environment]::TickCount64 -ge $deadline)
    throw "Lifecycle fixture did not emit '$Marker'. stdout=$text"
}

function Wait-Window([Diagnostics.Process] $Process, [string] $Output) {
    [void](Wait-Marker $Process $Output 'lifecycle:ui-mounted:' 10000)
    $deadline = [Environment]::TickCount64 + 10000
    do {
        $Process.Refresh()
        if ($Process.MainWindowHandle -ne 0) { return }
        if ($Process.HasExited) { break }
        Start-Sleep -Milliseconds 50
    } until ([Environment]::TickCount64 -ge $deadline)
    throw 'Lifecycle fixture did not present a test-owned window.'
}

function Assert-Ordered([string] $Text, [string[]] $Markers) {
    $position = -1
    foreach ($marker in $Markers) {
        $next = $Text.IndexOf($marker, $position + 1, [StringComparison]::Ordinal)
        if ($next -lt 0) { throw "Lifecycle evidence lacked ordered marker '$marker'. stdout=$Text" }
        $position = $next
    }
}

function Assert-Count([string] $Text, [string] $Marker, [int] $Expected) {
    $actual = [Text.RegularExpressions.Regex]::Matches(
        $Text,
        [Text.RegularExpressions.Regex]::Escape($Marker)
    ).Count
    if ($actual -ne $Expected) {
        throw "Lifecycle evidence contained '$Marker' $actual times; expected $Expected. stdout=$Text"
    }
}
function Invoke-LifecycleMode([string] $Mode, [scriptblock] $Drive) {
    $stdout = [IO.Path]::GetTempFileName()
    $stderr = [IO.Path]::GetTempFileName()
    $process = $null
    try {
        $process = Start-Process $script:ExecutablePath `
            -ArgumentList $Mode `
            -WorkingDirectory (Split-Path $script:ExecutablePath) `
            -WindowStyle Hidden `
            -RedirectStandardOutput $stdout `
            -RedirectStandardError $stderr `
            -PassThru
        & $Drive $process $stdout
        $process.Refresh()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Stdout = Read-Log $stdout
            Stderr = Read-Log $stderr
        }
    }
    finally {
        Stop-LifecycleProcess $process
        Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
    }
}

$script:ExecutablePath = (Resolve-Path -LiteralPath $Executable).Path

$normal = Invoke-LifecycleMode '--lifecycle-fixture' {
    param($process, $stdout)
    Wait-Window $process $stdout
    if (-not $process.CloseMainWindow()) { throw 'First ordinary close request was not delivered.' }
    [void](Wait-Marker $process $stdout 'lifecycle:prepare-1-rejected:' 10000)
    $process.Refresh()
    if ($process.HasExited -or $process.MainWindowHandle -eq 0) {
        throw 'Rejected close did not leave the lifecycle UI available for retry.'
    }
    if (-not $process.CloseMainWindow()) { throw 'Second ordinary close request was not delivered.' }
    [void](Wait-Marker $process $stdout 'lifecycle:prepare-2-start:' 10000)
    Start-Sleep -Milliseconds 100
    $process.Refresh()
    $pending = Read-Log $stdout
    if ($process.HasExited -or $pending.IndexOf('lifecycle:write-complete', [StringComparison]::Ordinal) -ge 0 -or $pending.IndexOf('lifecycle:service-stop:', [StringComparison]::Ordinal) -ge 0 -or $pending.IndexOf('lifecycle:model-dispose-start', [StringComparison]::Ordinal) -ge 0) {
        throw 'Repeated close bypassed the accepted write before stop or disposal.'
    }
    [void]$process.CloseMainWindow()
    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(15000)) { throw 'Accepted close did not complete in time.' }
}
if ($normal.ExitCode -ne 0) {
    throw "Normal lifecycle fixture exited $($normal.ExitCode). stdout=$($normal.Stdout) stderr=$($normal.Stderr)"
}
Assert-Ordered $normal.Stdout @(
    'lifecycle:entry:',
    'lifecycle:service-start:',
    'lifecycle:service-start-continued:',
    'lifecycle:write-accepted:',
    'lifecycle:model-create:',
    'lifecycle:root-factory:',
    'lifecycle:ui-mounted:',
    'lifecycle:prepare-1-start:',
    'lifecycle:prepare-1-rejected:',
    'lifecycle:prepare-2-start:',
    'lifecycle:write-complete:',
    'lifecycle:prepare-2-accepted:',
    'lifecycle:service-stop:',
    'lifecycle:service-stop-continued:',
    'lifecycle:ui-dispose:',
    'lifecycle:model-dispose-start',
    'lifecycle:model-dispose',
    'lifecycle:service-dispose',
    'lifecycle:run-complete:'
)
foreach ($marker in @(
    'lifecycle:write-accepted:',
    'lifecycle:write-complete:',
    'lifecycle:prepare-2-start:',
    'lifecycle:prepare-2-accepted:',
    'lifecycle:service-dispose'
)) { Assert-Count $normal.Stdout $marker 1 }

$startup = Invoke-LifecycleMode '--lifecycle-startup-failure' {
    param($process, $stdout)
    if (-not $process.WaitForExit(10000)) { throw 'Startup failure did not terminate in time.' }
}
if ($startup.ExitCode -eq 0) { throw 'Startup failure fixture unexpectedly succeeded.' }
Assert-Ordered $startup.Stdout @(
    'lifecycle:entry:',
    'lifecycle:service-start:',
    'lifecycle:service-start-continued:',
    'lifecycle:service-stop:',
    'lifecycle:service-dispose',
    'lifecycle:failure:startup failed'
)

$cleanup = Invoke-LifecycleMode '--lifecycle-cleanup-failure' {
    param($process, $stdout)
    Wait-Window $process $stdout
    if (-not $process.CloseMainWindow()) { throw 'Cleanup fixture first close was not delivered.' }
    [void](Wait-Marker $process $stdout 'lifecycle:prepare-1-rejected:' 10000)
    if (-not $process.CloseMainWindow()) { throw 'Cleanup fixture retry close was not delivered.' }
    if (-not $process.WaitForExit(15000)) { throw 'Cleanup failure did not terminate in time.' }
}
if ($cleanup.ExitCode -eq 0) { throw 'Cleanup failure fixture unexpectedly succeeded.' }
Assert-Ordered $cleanup.Stdout @(
    'lifecycle:prepare-2-accepted:',
    'lifecycle:service-stop:',
    'lifecycle:ui-dispose:',
    'lifecycle:model-dispose-start',
    'lifecycle:model-dispose',
    'lifecycle:host-dispose',
    'lifecycle:service-dispose',
    'lifecycle:failure:stop failed|scope dispose failed|host dispose failed'
)
foreach ($marker in @(
    'lifecycle:write-accepted:',
    'lifecycle:write-complete:',
    'lifecycle:prepare-2-start:',
    'lifecycle:prepare-2-accepted:',
    'lifecycle:service-dispose'
)) { Assert-Count $cleanup.Stdout $marker 1 }

[ordered]@{
    ok = $true
    executable = $script:ExecutablePath
    normal = 'two ordinary closes; first rejected, second drained; repeated close coalesced'
    startupFailure = 'stop and host disposal completed after failed startup'
    cleanupFailure = 'stop, composition, scope, and host boundaries all attempted'
    owner = 'entry, startup, root/model, prepare continuations, stop, UI disposal, and completion stayed STA'
} | ConvertTo-Json -Compress
