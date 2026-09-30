param([Parameter(Mandatory)] [string] $Nonce)
$ErrorActionPreference = 'Stop'
$guestInput = 'C:\LucentInput'
$guestOutput = 'C:\LucentOutput'
$session = $null
$package = $null
$signTool = $null
$certificate = $null
$installedPackage = $null
$identityValidated = $false
$installAttempted = $false
$signatureVerified = $false
$shellProcesses = @()
$runError = $null
$cleanupErrors = [Collections.Generic.List[string]]::new()
$verified = $false

function Wait-Event([int] $number) {
    $path = Join-Path $session.guestAppOutput ('event-{0:D4}.txt' -f $number)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 25 }
    if (-not (Test-Path -LiteralPath $path)) { throw "Packaged protocol event $number did not arrive within ten seconds." }
    return ConvertFrom-StringData (Get-Content -LiteralPath $path -Raw)
}
function Open-Uri([string] $uri) {
    $start = [Diagnostics.ProcessStartInfo]::new($uri)
    $start.UseShellExecute = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'Shell omitted a process handle for exit verification.' }
    $script:shellProcesses += $process
    return $process
}
function Wait-Completion([Diagnostics.Process] $process, [string] $kind, [int] $exit, [string] $failure) {
    if (-not $process.WaitForExit(8000)) { throw "Packaged process $($process.Id) did not exit within eight seconds." }
    if ($process.ExitCode -ne $exit) { throw "Packaged process $($process.Id) exited $($process.ExitCode), expected $exit." }
    $path = Join-Path $session.guestAppOutput ("process-$($process.Id).txt")
    $deadline = [DateTime]::UtcNow.AddSeconds(2)
    while (-not (Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 25 }
    if (-not (Test-Path -LiteralPath $path)) { throw "Packaged process $($process.Id) omitted completion evidence." }
    $completion = ConvertFrom-StringData (Get-Content -LiteralPath $path -Raw)
    if ($completion.kind -ne $kind -or $completion.exit -ne "$exit" -or $completion.failure -ne $failure) { throw "Packaged process $($process.Id) reported an unexpected activation result." }
}

try {
    if ($env:USERNAME -ne 'WDAGUtilityAccount' -or -not [string]::Equals($env:USERPROFILE, 'C:\Users\WDAGUtilityAccount', [StringComparison]::OrdinalIgnoreCase)) { throw 'MSIX signing and installation require the disposable Windows Sandbox guest identity.' }
    $session = Get-Content -LiteralPath (Join-Path $guestInput 'session.json') -Raw | ConvertFrom-Json
    $marker = (Get-Content -LiteralPath (Join-Path $guestInput 'sandbox-marker.txt') -Raw).Trim()
    if ($session.schemaVersion -ne 1 -or $session.nonce -ne $Nonce -or $marker -ne $Nonce -or $session.runId -notmatch '^[0-9a-f]{32}$' -or $session.protocolScheme -notmatch '^lucent[0-9a-f]{32}$' -or $session.packageIdentity -ne ('Lucent.Probe.' + $session.protocolScheme.Substring('lucent'.Length)) -or $session.publisher -ne 'CN=Lucent Activation Fixture') { throw 'Sandbox input identity or marker is invalid.' }
    $expectedOutput = 'C:\Users\WDAGUtilityAccount\AppData\Local\Temp\LucentActivation\' + $session.protocolScheme
    if (-not [string]::Equals($session.guestAppOutput, $expectedOutput, [StringComparison]::OrdinalIgnoreCase)) { throw 'Packaged fixture output is outside its isolated guest directory.' }
    $packageInput = Join-Path $guestInput 'fixture.msix'
    $signToolInput = Join-Path $guestInput 'signtool.exe'
    $guestScript = Join-Path $guestInput 'Invoke-MsixGuest.ps1'
    if ((Get-FileHash -LiteralPath $packageInput -Algorithm SHA256).Hash -ne $session.packageSha256 -or (Get-FileHash -LiteralPath $signToolInput -Algorithm SHA256).Hash -ne $session.signToolSha256 -or (Get-FileHash -LiteralPath $guestScript -Algorithm SHA256).Hash -ne $session.guestScriptSha256) { throw 'Sandbox input hash mismatch.' }
    if (Get-AppxPackage -Name $session.packageIdentity) { throw 'Unique MSIX identity already installed in guest.' }
    if (Test-Path -LiteralPath ("Registry::HKEY_CLASSES_ROOT\" + $session.protocolScheme)) { throw 'Unique protocol already associated in guest.' }
    $identityValidated = $true
    $work = Join-Path $env:TEMP ('LucentMsixGuest-' + $session.runId)
    $null = New-Item -ItemType Directory -Path $work -Force
    $package = Join-Path $work 'fixture.msix'
    $signTool = Join-Path $work 'signtool.exe'
    Copy-Item -LiteralPath $packageInput -Destination $package
    Copy-Item -LiteralPath $signToolInput -Destination $signTool
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $session.publisher -KeyUsage DigitalSignature -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    if ($null -eq $certificate) { throw 'Guest signing certificate creation returned no certificate.' }
    $certFile = Join-Path $work 'signer.cer'
    $null = Export-Certificate -Cert $certificate -FilePath $certFile
    $null = Import-Certificate -FilePath $certFile -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
    & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint $package
    if ($LASTEXITCODE) { throw 'Guest SignTool signing failed.' }
    & $signTool verify /pa $package
    if ($LASTEXITCODE) { throw 'Guest SignTool signature verification failed.' }
    $signatureVerified = $true
    $installAttempted = $true
    Add-AppxPackage -Path $package -ErrorAction Stop
    $installedPackage = @(Get-AppxPackage -Name $session.packageIdentity | Where-Object { $_.Name -eq $session.packageIdentity })
    if ($installedPackage.Count -ne 1) { throw 'Guest package installation did not establish one exact identity.' }
    $installedPackage = $installedPackage[0]
    $scheme = $session.protocolScheme
    $installedManifest = Get-AppxPackageManifest -Package $installedPackage.PackageFullName
    $protocolNodes = @($installedManifest.SelectNodes("//*[local-name()='Application' and @Executable='Consumer.exe']//*[local-name()='Protocol' and @Name='$scheme']"))
    if ($protocolNodes.Count -ne 1) { throw 'Installed package does not own the exact Consumer.exe protocol extension.' }
    $coldUri = "${scheme}://navigation/notes/12?tab=one"
    $invalidUri = "${scheme}://navigation:9/notes/12"
    $warmUri = "${scheme}://navigation/notes/%2F?tab=two"
    $lastUri = "${scheme}://navigation/notes/13?tab=three"
    $coldProcess = Open-Uri $coldUri
    $cold = Wait-Event 1
    if ($cold.kind -ne 'Protocol' -or $cold.delivery -ne 'Cold' -or $cold.provenance -ne 'UntrustedExternal' -or [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($cold.rawBase64)) -cne $coldUri) { throw 'Packaged cold URI or provenance changed.' }
    $primaryPath = (Get-Process -Id ([int]$cold.pid) -ErrorAction Stop).Path
    if (-not [string]::Equals($primaryPath, (Join-Path $installedPackage.InstallLocation 'Consumer.exe'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Cold shell URI was handled outside the installed package.' }
    $invalidProcess = Open-Uri $invalidUri
    Wait-Completion $invalidProcess 'RedirectFailed' 2 'RejectedInput'
    if (Test-Path -LiteralPath (Join-Path $session.guestAppOutput 'event-0002.txt')) { throw 'Rejected packaged secondary changed primary inbox.' }
    $warmProcess = Open-Uri $warmUri
    $warm = Wait-Event 2
    if ($warm.kind -ne 'Protocol' -or $warm.delivery -ne 'Redirected' -or $warm.provenance -ne 'UntrustedExternal' -or [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($warm.rawBase64)) -cne $warmUri) { throw 'Packaged warm URI or provenance changed.' }
    Wait-Completion $warmProcess 'Redirected' 0 'None'
    $lastProcess = Open-Uri $lastUri
    $last = Wait-Event 3
    if ($last.kind -ne 'Protocol' -or $last.delivery -ne 'Redirected' -or [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($last.rawBase64)) -cne $lastUri) { throw 'Final packaged warm URI changed.' }
    Wait-Completion $lastProcess 'Redirected' 0 'None'
    if ($cold.pid -ne $warm.pid -or $cold.pid -ne $last.pid) { throw 'Packaged redirect created a new primary.' }
    Wait-Completion $coldProcess 'Primary' 0 'None'
    $verified = $true
}
catch { $runError = $_.Exception.Message }
finally {
    if ($identityValidated -and $installAttempted) {
        foreach ($process in $shellProcesses) {
            try {
                if (-not $process.HasExited) {
                    $actual = $process.MainModule.FileName
                    if ($null -eq $installedPackage -or -not $actual.StartsWith($installedPackage.InstallLocation + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Process $($process.Id) is not owned by the exact installed package; refusing to stop it." }
                    $process.Kill()
                    if (-not $process.WaitForExit(3000)) { throw "Owned package process $($process.Id) did not stop." }
                }
            }
            catch { $cleanupErrors.Add($_.Exception.Message) }
            finally { $process.Dispose() }
        }
        try {
            $remaining = @(Get-AppxPackage -Name $session.packageIdentity | Where-Object { $_.Name -eq $session.packageIdentity })
            foreach ($ownedPackage in $remaining) { Remove-AppxPackage -Package $ownedPackage.PackageFullName -ErrorAction Stop }
            if (Get-AppxPackage -Name $session.packageIdentity) { throw 'Exact fixture package remains installed.' }
            if (Test-Path -LiteralPath ("Registry::HKEY_CLASSES_ROOT\" + $session.protocolScheme)) { throw 'Unique package protocol association remains.' }
        }
        catch { $cleanupErrors.Add($_.Exception.Message) }
    }
    if ($null -ne $certificate) {
        foreach ($store in 'Cert:\LocalMachine\TrustedPeople', 'Cert:\CurrentUser\My') {
            try {
                $path = Join-Path $store $certificate.Thumbprint
                if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -ErrorAction Stop }
                if (Test-Path -LiteralPath $path) { throw "Guest certificate remains in $store." }
            }
            catch { $cleanupErrors.Add($_.Exception.Message) }
        }
    }
    try {
        if ($identityValidated -and (Test-Path -LiteralPath $session.guestAppOutput)) { Copy-Item -LiteralPath $session.guestAppOutput -Destination (Join-Path $guestOutput 'app-evidence') -Recurse }
    }
    catch { $cleanupErrors.Add($_.Exception.Message) }
    $result = [ordered]@{
        runId = if ($null -ne $session) { $session.runId } else { $null }
        succeeded = ($verified -and $null -eq $runError -and $cleanupErrors.Count -eq 0)
        identityValidated = $identityValidated
        installAttempted = $installAttempted
        signatureVerified = $signatureVerified
        runError = $runError
        cleanupErrors = @($cleanupErrors)
        packageIdentity = if ($null -ne $session) { $session.packageIdentity } else { $null }
        packageSha256 = if ($null -ne $session) { $session.packageSha256 } else { $null }
        packageManifestSha256 = if ($null -ne $session) { $session.packageManifestSha256 } else { $null }
        publishedExecutableSha256 = if ($null -ne $session) { $session.publishedExecutableSha256 } else { $null }
        guestScriptSha256 = if ($null -ne $session) { $session.guestScriptSha256 } else { $null }
        signerThumbprint = if ($null -ne $certificate) { $certificate.Thumbprint } else { $null }
        signedPackageSha256 = if ($signatureVerified) { (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash } else { $null }
    }
    $temporary = Join-Path $guestOutput ('guest-result-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $temporary
    Move-Item -LiteralPath $temporary -Destination (Join-Path $guestOutput 'guest-result.json')
}
if ($runError -or $cleanupErrors.Count) { exit 1 }
exit 0
