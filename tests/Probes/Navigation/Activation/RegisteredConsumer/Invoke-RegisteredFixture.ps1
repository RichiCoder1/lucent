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
if ($manifest.schemaVersion -ne 2 -or $manifest.scheme -notmatch '^lucent[0-9a-f]{32}$' -or $manifest.packageSourceCommit -notmatch '^[0-9a-f]{40}$' -or $manifest.host -ne 'navigation') { throw 'Unsupported fixture identity.' }
if ((Get-FileHash -LiteralPath (Join-Path $proof 'candidate-descriptor.json') -Algorithm SHA256).Hash -cne $manifest.candidateDescriptorSha256) { throw 'Candidate descriptor changed after fixture preparation.' }
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
$registration = $null
$cleanup = $null
function Get-FixtureRegistration {
    # Discover the SDK's actual registration instead of reproducing its app-ID hash.
    $applications = 'Registry::HKEY_CURRENT_USER\Software\RegisteredApplications'
    $registrations = @()
    if (Test-Path -LiteralPath $applications) {
        $registeredApplications = Get-Item -LiteralPath $applications
        foreach ($application in $registeredApplications.GetValueNames()) {
            $relativePath = $registeredApplications.GetValue($application)
            if ($relativePath -isnot [string] -or -not $relativePath.StartsWith('Software\', [StringComparison]::OrdinalIgnoreCase)) { continue }
            $capabilities = "Registry::HKEY_CURRENT_USER\$relativePath\URLAssociations"
            if (-not (Test-Path -LiteralPath $capabilities)) { continue }
            $key = Get-Item -LiteralPath $capabilities
            $progId = $key.GetValue($scheme)
            if ($null -eq $progId) { continue }
            if ($progId -isnot [string] -or $progId -notmatch '^[A-Za-z0-9._-]+$') { throw 'The fixture has an unsupported handler identity.' }
            $handler = "Registry::HKEY_CURRENT_USER\Software\Classes\$progId"
            $commandKey = Join-Path $handler 'shell\open\command'
            if (-not (Test-Path -LiteralPath $commandKey)) { throw 'The registered handler has no command.' }
            $command = (Get-Item -LiteralPath $commandKey).GetValue('')
            if ($command -isnot [string] -or -not ($command.StartsWith(($exe + ' '), [StringComparison]::OrdinalIgnoreCase) -or $command.StartsWith(('"' + $exe + '" '), [StringComparison]::OrdinalIgnoreCase))) { throw 'The registered handler does not target the exact fixture EXE.' }
            $registrations += [ordered]@{ capabilities = $capabilities; progId = $progId; command = $command }
        }
    }
    if ($registrations.Count -ne 1) { throw 'Expected exactly one registered handler for the unique fixture scheme.' }
    return $registrations[0]
}
function Assert-RegistrationRemoved {
    if ($null -eq $registration) { throw 'No verified registration is available for cleanup comparison.' }
    if (Test-Path -LiteralPath $registration.capabilities) {
        if ($null -ne (Get-Item -LiteralPath $registration.capabilities).GetValue($scheme)) { throw 'The fixture URL capability remains after Unregister.' }
    }
    foreach ($classes in @('Registry::HKEY_CURRENT_USER\Software\Classes', 'Registry::HKEY_CLASSES_ROOT')) {
        if (Test-Path -LiteralPath (Join-Path $classes $registration.progId)) { throw 'The fixture handler ProgID remains after Unregister.' }
        if (Test-Path -LiteralPath (Join-Path $classes "$scheme\shell")) { throw 'The fixture scheme retains shell commands after Unregister.' }
    }
    # Windows App SDK retains the shared protocol definition, without a handler.
    return [ordered]@{ capabilityRemoved = $true; handlerRemoved = $true; schemeDefinitionRemains = (Test-Path -LiteralPath $association) }
}
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
    $broker = [Diagnostics.Process]::Start($start)
    if ($null -ne $broker) { $broker.Dispose() }
}
function Wait-Event([int] $number) {
    $path = Join-Path $output ('event-{0:D4}.txt' -f $number)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 25 }
    if (-not (Test-Path -LiteralPath $path)) { throw "Registered delivery $number did not arrive within ten seconds." }
    return ConvertFrom-StringData (Get-Content -LiteralPath $path -Raw)
}
function Open-VerifiedHandler([int] $processId) {
    $process = [Diagnostics.Process]::GetProcessById($processId)
    try {
        if (-not [string]::Equals($process.MainModule.FileName, $exe, [StringComparison]::OrdinalIgnoreCase)) { throw "Recorded process $processId is not the exact fixture EXE." }
        if ($process.Handle -eq [IntPtr]::Zero) { throw "Recorded process $processId has no waitable handle." }
        return $process
    }
    catch { $process.Dispose(); throw }
}
function Wait-Completion([Diagnostics.Process] $process, [string] $kind, [int] $exit, [string] $failure) {
    $path = Join-Path $output ("process-$($process.Id).txt")
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 25 }
    if (-not (Test-Path -LiteralPath $path)) { throw "Verified handler $($process.Id) omitted completion evidence." }
    $completion = ConvertFrom-StringData (Get-Content -LiteralPath $path -Raw)
    if ($completion.kind -ne $kind -or $completion.exit -ne "$exit" -or $completion.failure -ne $failure) { throw "Verified handler $($process.Id) reported an unexpected activation result." }
    $acknowledgment = Join-Path $output ("ack-$($process.Id).txt")
    $temporary = "$acknowledgment.$([Guid]::NewGuid().ToString('N')).tmp"
    Set-Content -LiteralPath $temporary -Value $manifest.instanceKey
    Move-Item -LiteralPath $temporary -Destination $acknowledgment
    if (-not $process.WaitForExit(8000)) { throw "Verified handler $($process.Id) did not exit within eight seconds." }
    if ($process.ExitCode -ne $exit) { throw "Verified handler $($process.Id) exited $($process.ExitCode), expected $exit." }
}
function Wait-NewHandler([string] $kind, [int] $exit, [string] $failure) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $fresh = @(Get-ChildItem -LiteralPath $output -Filter 'process-*.txt' -File | Where-Object {
            $_.BaseName -match '^process-([0-9]+)$' -and -not $script:knownHandlerIds.Contains([int]$Matches[1])
        })
        if ($fresh.Count -gt 1) { throw 'More than one unclaimed handler completed for a single shell URI.' }
        if ($fresh.Count -eq 1) { break }
        Start-Sleep -Milliseconds 25
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($fresh.Count -ne 1 -or $fresh[0].BaseName -notmatch '^process-([0-9]+)$') { throw 'Shell URI produced no single fresh handler completion.' }
    $processId = [int]$Matches[1]
    $process = Open-VerifiedHandler $processId
    $script:knownHandlerIds.Add($processId) | Out-Null
    $script:ownedProcesses += $process
    Wait-Completion $process $kind $exit $failure
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
$ownedProcesses = @()
$knownHandlerIds = [Collections.Generic.HashSet[int]]::new()
$coldProcess = $null
try {
    $attempted = $true
    $manifest.registrationAttempted = $true
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath
    Invoke-Setup '--register'
    $registered = $true
    $registration = Get-FixtureRegistration
    $coldUri = "${scheme}://navigation/notes/12?tab=one"
    $invalidUri = "${scheme}://navigation:9/notes/12"
    $warmUri = "${scheme}://navigation/notes/%2F?tab=two"
    $lastUri = "${scheme}://navigation/notes/13?tab=three"
    Open-Uri $coldUri
    $cold = Wait-Event 1
    if ($cold.kind -ne 'Protocol' -or $cold.delivery -ne 'Cold' -or $cold.provenance -ne 'UntrustedExternal' -or [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($cold.rawBase64)) -cne $coldUri) { throw 'Cold registered delivery lost its raw protocol URI or provenance.' }
    $coldProcess = Open-VerifiedHandler ([int]$cold.pid)
    $knownHandlerIds.Add($coldProcess.Id) | Out-Null
    $ownedProcesses += $coldProcess
    Open-Uri $invalidUri
    Wait-NewHandler 'RedirectFailed' 2 'RejectedInput'
    if (Test-Path -LiteralPath (Join-Path $output 'event-0002.txt')) { throw 'Rejected secondary changed the primary inbox.' }
    Open-Uri $warmUri
    $warm = Wait-Event 2
    if ($warm.kind -ne 'Protocol' -or $warm.delivery -ne 'Redirected' -or $warm.provenance -ne 'UntrustedExternal' -or [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($warm.rawBase64)) -cne $warmUri) { throw 'Warm registered delivery lost its raw protocol URI or provenance.' }
    Wait-NewHandler 'Redirected' 0 'None'
    Open-Uri $lastUri
    $last = Wait-Event 3
    if ($last.kind -ne 'Protocol' -or $last.delivery -ne 'Redirected' -or [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($last.rawBase64)) -cne $lastUri) { throw 'Final warm registered delivery was lost.' }
    Wait-NewHandler 'Redirected' 0 'None'
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
    foreach ($process in $ownedProcesses) {
        try { Stop-OwnedShellProcess $process } catch { $cleanupError = $_.Exception.Message }
    }
    if ($attempted) {
        try { Invoke-Setup '--unregister' } catch { $cleanupError = $_.Exception.Message }
    }
    if ($registered) {
        try { $cleanup = Assert-RegistrationRemoved } catch { $cleanupError = $_.Exception.Message }
    }
    foreach ($process in $ownedProcesses) { $process.Dispose() }
    [ordered]@{ succeeded = ($verified -and $null -eq $failure -and $null -eq $cleanupError -and $registered); registered = $registered; failure = $failure; cleanupError = $cleanupError; registration = $registration; cleanup = $cleanup; scheme = $scheme; executableSha256 = $manifest.executableSha256 } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $proof 'registered-evidence.json')
    if ($null -ne $cleanupError) { Write-Error "Registered fixture cleanup failed: $cleanupError" }
}
if ($verified) { Write-Output "Isolated registered protocol transport: PASS ($proof)" }
