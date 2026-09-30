param(
    [Parameter(Mandatory)] [string] $Fixture,
    [switch] $RunRegistered
)
$ErrorActionPreference = 'Stop'
if (-not $RunRegistered) { throw 'This command changes a protocol association. Pass -RunRegistered only in the reviewed isolated test environment.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../..'))
$allowed = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/windows-activation-registered')) + [IO.Path]::DirectorySeparatorChar
$proof = (Resolve-Path -LiteralPath $Fixture).Path
if (-not $proof.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture must be within the isolated activation artifact directory.' }
$manifestPath = Join-Path $proof 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.scheme -notmatch '^lucent[0-9a-f]{32}$' -or $manifest.host -ne 'navigation') { throw 'Unsupported fixture identity.' }
if ((Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash -ne $manifest.invokeScriptSha256) { throw 'Fixture run script changed after preparation.' }
$exe = Join-Path $proof 'publish/Consumer.exe'
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $manifest.executableSha256) { throw 'Fixture executable changed after preparation.' }
$settings = Get-Content -LiteralPath (Join-Path $proof 'publish/fixture.txt')
if ($settings.Count -ne 4 -or $settings[0] -ne $manifest.instanceKey -or $settings[1] -ne $manifest.scheme) { throw 'Fixture settings do not match the manifest.' }
$output = Join-Path $proof 'output'
$stop = Join-Path $proof 'stop'
if ($settings[2] -ne $output -or $settings[3] -ne $stop) { throw 'Fixture paths do not match their isolated directory.' }
if ($manifest.registrationAttempted -or (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1) -or (Test-Path -LiteralPath $stop) -or (Test-Path -LiteralPath (Join-Path $proof 'registered-evidence.json'))) { throw 'The fixture has already been attempted or has stale output; prepare a new unique fixture.' }
$scheme = $manifest.scheme
$association = "Registry::HKEY_CLASSES_ROOT\$scheme"
$userAssociation = "Registry::HKEY_CURRENT_USER\Software\Classes\$scheme"
if ((Test-Path -LiteralPath $association) -or (Test-Path -LiteralPath $userAssociation)) { throw 'The unique protocol scheme already has an association; refusing to replace it.' }
$attempted = $false
$registered = $false
$verified = $false
$cleanupError = $null
$failure = $null
function Invoke-Setup([string] $verb) {
    $process = Start-Process -FilePath $exe -ArgumentList $verb -PassThru -WindowStyle Hidden
    try {
        if (-not $process.WaitForExit(5000)) {
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
            throw "Fixture $verb exceeded five seconds."
        }
        if ($process.ExitCode -ne 0) { throw "Fixture $verb exited $($process.ExitCode)." }
    }
    finally { $process.Dispose() }
}
function Open-Uri([string] $uri) {
    $start = [Diagnostics.ProcessStartInfo]::new($uri)
    $start.UseShellExecute = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'Shell did not provide a process handle for exit verification.' }
    return $process
}
function Wait-Event([int] $number) {
    $path = Join-Path $output ('event-{0:D4}.txt' -f $number)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 25 }
    if (-not (Test-Path -LiteralPath $path)) { throw "Registered delivery $number did not arrive within ten seconds." }
    return ConvertFrom-StringData (Get-Content -LiteralPath $path -Raw)
}
function Wait-Completion([Diagnostics.Process] $process, [string] $kind, [int] $exit, [string] $failure) {
    if (-not $process.WaitForExit(8000)) { throw "Shell process $($process.Id) did not exit within eight seconds." }
    if ($process.ExitCode -ne $exit) { throw "Shell process $($process.Id) exited $($process.ExitCode), expected $exit." }
    $path = Join-Path $output ("process-$($process.Id).txt")
    $deadline = [DateTime]::UtcNow.AddSeconds(2)
    while (-not (Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 25 }
    if (-not (Test-Path -LiteralPath $path)) { throw "Shell process $($process.Id) omitted completion evidence." }
    $completion = ConvertFrom-StringData (Get-Content -LiteralPath $path -Raw)
    if ($completion.kind -ne $kind -or $completion.exit -ne "$exit" -or $completion.failure -ne $failure) { throw "Shell process $($process.Id) reported an unexpected activation result." }
}
function Stop-OwnedPrimary {
    $pidPath = Join-Path $output 'primary.pid'
    if (-not (Test-Path -LiteralPath $pidPath)) { return }
    $primaryId = 0
    if (-not [int]::TryParse((Get-Content -LiteralPath $pidPath -Raw).Trim(), [ref]$primaryId)) { throw 'Invalid primary PID evidence.' }
    $primary = Get-Process -Id $primaryId -ErrorAction SilentlyContinue
    if ($null -eq $primary) { return }
    $actualPath = $primary.Path
    if (-not [string]::Equals($actualPath, $exe, [StringComparison]::OrdinalIgnoreCase)) { throw 'The recorded PID no longer belongs to this fixture; refusing to stop it.' }
    $null = New-Item -ItemType File -Path $stop -Force
    if (-not $primary.WaitForExit(3000)) {
        Stop-Process -Id $primaryId -Force
        if (-not $primary.WaitForExit(3000)) { throw 'Owned primary did not stop after termination.' }
    }
}
function Stop-OwnedShellProcess([Diagnostics.Process] $process) {
    if ($null -eq $process -or $process.HasExited) { return }
    if (-not [string]::Equals($process.MainModule.FileName, $exe, [StringComparison]::OrdinalIgnoreCase)) { throw "Shell process $($process.Id) is not the fixture EXE; refusing to stop it." }
    $process.Kill()
    if (-not $process.WaitForExit(3000)) { throw "Owned shell process $($process.Id) did not stop." }
}
$coldProcess = $null
$invalidProcess = $null
$warmProcess = $null
$lastProcess = $null
try {
    $attempted = $true
    $manifest.registrationAttempted = $true
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath
    Invoke-Setup '--register'
    $registered = $true
    $coldUri = "${scheme}://navigation/notes/12?tab=one"
    $invalidUri = "${scheme}://navigation:9/notes/12"
    $warmUri = "${scheme}://navigation/notes/%2F?tab=two"
    $lastUri = "${scheme}://navigation/notes/13?tab=three"
    $coldProcess = Open-Uri $coldUri
    $cold = Wait-Event 1
    if ($cold.kind -ne 'Protocol' -or $cold.delivery -ne 'Cold' -or $cold.provenance -ne 'UntrustedExternal' -or [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($cold.rawBase64)) -cne $coldUri) { throw 'Cold registered delivery lost its raw protocol URI or provenance.' }
    $invalidProcess = Open-Uri $invalidUri
    Wait-Completion $invalidProcess 'RedirectFailed' 2 'RejectedInput'
    if (Test-Path -LiteralPath (Join-Path $output 'event-0002.txt')) { throw 'Rejected secondary changed the primary inbox.' }
    $warmProcess = Open-Uri $warmUri
    $warm = Wait-Event 2
    if ($warm.kind -ne 'Protocol' -or $warm.delivery -ne 'Redirected' -or $warm.provenance -ne 'UntrustedExternal' -or [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($warm.rawBase64)) -cne $warmUri) { throw 'Warm registered delivery lost its raw protocol URI or provenance.' }
    Wait-Completion $warmProcess 'Redirected' 0 'None'
    $lastProcess = Open-Uri $lastUri
    $last = Wait-Event 3
    if ($last.kind -ne 'Protocol' -or $last.delivery -ne 'Redirected' -or [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($last.rawBase64)) -cne $lastUri) { throw 'Final warm registered delivery was lost.' }
    Wait-Completion $lastProcess 'Redirected' 0 'None'
    if ($cold.pid -ne $warm.pid -or $cold.pid -ne $last.pid) { throw 'A redirected delivery created a new primary.' }
    Wait-Completion $coldProcess 'Primary' 0 'None'
    $verified = $true
}
catch {
    $failure = $_.Exception.Message
    throw
}
finally {
    try { Stop-OwnedPrimary } catch { $cleanupError = $_.Exception.Message }
    foreach ($process in @($coldProcess, $invalidProcess, $warmProcess, $lastProcess)) {
        try { Stop-OwnedShellProcess $process } catch { $cleanupError = $_.Exception.Message }
    }
    if ($attempted) {
        try { Invoke-Setup '--unregister' } catch { $cleanupError = $_.Exception.Message }
    }
    if ((Test-Path -LiteralPath $association) -or (Test-Path -LiteralPath $userAssociation)) { $cleanupError = 'The unique protocol association remains after Unregister.' }
    foreach ($process in @($coldProcess, $invalidProcess, $warmProcess, $lastProcess)) { if ($null -ne $process) { $process.Dispose() } }
    [ordered]@{ succeeded = ($verified -and $null -eq $failure -and $null -eq $cleanupError -and $registered); registered = $registered; failure = $failure; cleanupError = $cleanupError; scheme = $scheme; executableSha256 = $manifest.executableSha256 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $proof 'registered-evidence.json')
    if ($null -ne $cleanupError) { Write-Error "Registered fixture cleanup failed: $cleanupError" }
}
if ($verified) { Write-Output "Isolated registered protocol transport: PASS ($proof)" }
