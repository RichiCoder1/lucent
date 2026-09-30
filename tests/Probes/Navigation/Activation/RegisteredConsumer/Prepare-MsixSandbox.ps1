param(
    [Parameter(Mandatory)] [string] $MsixBuild,
    [switch] $RunSandbox
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../..'))
$allowed = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/windows-activation-registered')) + [IO.Path]::DirectorySeparatorChar
$build = (Resolve-Path -LiteralPath $MsixBuild).Path
if (-not $build.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'MSIX build must be within the isolated activation artifact directory.' }
$msixEvidence = Get-Content -LiteralPath (Join-Path $build 'msix-evidence.json') -Raw | ConvertFrom-Json
$package = Join-Path $build 'Lucent.ActivationFixture.msix'
$manifest = Join-Path $build 'stage/AppxManifest.xml'
$fixture = Split-Path $build -Parent
$fixtureManifest = Get-Content -LiteralPath (Join-Path $fixture 'manifest.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $fixture 'candidate-descriptor.json') -Algorithm SHA256).Hash -cne $fixtureManifest.candidateDescriptorSha256) { throw 'Candidate descriptor changed after fixture preparation.' }
$program = Join-Path $fixture 'Program.cs'
if (-not [string]::Equals((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash, $msixEvidence.packageSha256, [StringComparison]::OrdinalIgnoreCase)) { throw 'MSIX package changed after build.' }
if (-not [string]::Equals((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash, $msixEvidence.manifestSha256, [StringComparison]::OrdinalIgnoreCase)) { throw 'MSIX manifest changed after build.' }
if (-not [string]::Equals((Get-FileHash -LiteralPath $program -Algorithm SHA256).Hash, $fixtureManifest.consumerSourceSha256, [StringComparison]::OrdinalIgnoreCase)) { throw 'Published handler source changed after preparation.' }
if (-not [string]::Equals((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Build-MsixFixture.ps1') -Algorithm SHA256).Hash, $msixEvidence.buildScriptSha256, [StringComparison]::OrdinalIgnoreCase)) { throw 'MSIX build script changed after packaging.' }
if ($msixEvidence.packageSourceCommit -cne $fixtureManifest.packageSourceCommit -or $msixEvidence.fixturePreparationCommit -cne $fixtureManifest.fixturePreparationCommit -or $msixEvidence.candidateDescriptorSha256 -cne $fixtureManifest.candidateDescriptorSha256) { throw 'MSIX build and prepared fixture identify different package inputs.' }
if ($fixtureManifest.schemaVersion -ne 2 -or $msixEvidence.schemaVersion -ne 2 -or $msixEvidence.signed -or $msixEvidence.installed -or $fixtureManifest.scheme -ne $msixEvidence.protocolScheme -or $msixEvidence.packageIdentity -ne ('Lucent.Probe.' + $fixtureManifest.scheme.Substring('lucent'.Length)) -or $msixEvidence.publisher -ne 'CN=Lucent Activation Fixture') { throw 'MSIX build does not have the current isolated unsigned fixture identity.' }
$guestAppOutput = 'C:\LucentOutput\app-evidence\' + $fixtureManifest.scheme
if (-not [string]::Equals($msixEvidence.guestOutputDirectory, $guestAppOutput, [StringComparison]::OrdinalIgnoreCase)) { throw "Rebuild the MSIX with guest output $guestAppOutput." }
$signToolSource = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe'
if (-not (Test-Path -LiteralPath $signToolSource -PathType Leaf)) { throw 'Pinned Windows SDK SignTool.exe is unavailable.' }
$runId = [Guid]::NewGuid().ToString('N')
$run = Join-Path $root ('artifacts/windows-activation-sandbox/' + $runId)
$sandboxRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/windows-activation-sandbox')) + [IO.Path]::DirectorySeparatorChar
if (-not [IO.Path]::GetFullPath($run).StartsWith($sandboxRoot, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $run)) { throw 'Sandbox evidence directory is not a fresh isolated child.' }
$inputFolder = Join-Path $run 'input'
$outputFolder = Join-Path $run 'output'
$null = New-Item -ItemType Directory -Path $inputFolder, $outputFolder -Force
if ((Get-Item -LiteralPath $outputFolder).Attributes -band [IO.FileAttributes]::ReparsePoint -or (Get-ChildItem -LiteralPath $outputFolder -Force | Select-Object -First 1)) { throw 'Sandbox output mapping is not a fresh ordinary directory.' }
Copy-Item -LiteralPath $package -Destination (Join-Path $inputFolder 'fixture.msix')
Copy-Item -LiteralPath $signToolSource -Destination (Join-Path $inputFolder 'signtool.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Invoke-MsixGuest.ps1') -Destination (Join-Path $inputFolder 'Invoke-MsixGuest.ps1')
$nonce = [Guid]::NewGuid().ToString('N')
$session = [ordered]@{
    schemaVersion = 2
    runId = $runId
    nonce = $nonce
    packageSourceCommit = $fixtureManifest.packageSourceCommit
    fixturePreparationCommit = $fixtureManifest.fixturePreparationCommit
    candidateDescriptorSha256 = $fixtureManifest.candidateDescriptorSha256
    hostPreparationCommit = (& git -C $root rev-parse HEAD).Trim()
    packageIdentity = $msixEvidence.packageIdentity
    publisher = $msixEvidence.publisher
    protocolScheme = $msixEvidence.protocolScheme
    instanceKey = $fixtureManifest.instanceKey
    guestAppOutput = $guestAppOutput
    unsignedInputMsixSha256 = $msixEvidence.packageSha256
    packageManifestSha256 = $msixEvidence.manifestSha256
    publishedExecutableSha256 = $fixtureManifest.executableSha256
    consumerSourceSha256 = $fixtureManifest.consumerSourceSha256
    prepareScriptSha256 = $fixtureManifest.prepareScriptSha256
    msixBuildScriptSha256 = $msixEvidence.buildScriptSha256
    guestScriptSha256 = (Get-FileHash -LiteralPath (Join-Path $inputFolder 'Invoke-MsixGuest.ps1') -Algorithm SHA256).Hash
    signToolSha256 = (Get-FileHash -LiteralPath (Join-Path $inputFolder 'signtool.exe') -Algorithm SHA256).Hash
}
$session | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $inputFolder 'session.json')
$nonce | Set-Content -LiteralPath (Join-Path $inputFolder 'sandbox-marker.txt')
$inputXml = [Security.SecurityElement]::Escape($inputFolder)
$outputXml = [Security.SecurityElement]::Escape($outputFolder)
$config = @"
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>Disable</Networking>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <PrinterRedirection>Disable</PrinterRedirection>
  <MemoryInMB>4096</MemoryInMB>
  <MappedFolders>
    <MappedFolder><HostFolder>$inputXml</HostFolder><SandboxFolder>C:\LucentInput</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$outputXml</HostFolder><SandboxFolder>C:\LucentOutput</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\LucentInput\Invoke-MsixGuest.ps1 -Nonce $nonce</Command></LogonCommand>
</Configuration>
"@
$configPath = Join-Path $run 'ActivationMsix.wsb'
$config | Set-Content -LiteralPath $configPath -Encoding utf8
if (-not $RunSandbox) {
    Write-Output "Prepared disposable MSIX Sandbox input and config without launching: $run"
    return
}

# The explicit execution path is intentionally separate from preparation. It must be
# reviewed with the host's desktop/session owner before use.
$freeMemoryKb = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
if ($freeMemoryKb -lt 8MB) { throw 'Less than eight GiB of host physical memory is free; refusing to start Sandbox.' }
$wsb = (Get-Command wsb.exe -ErrorAction Stop).Source
$sessionId = [Guid]::NewGuid().ToString()
$startAttempted = $false
$runError = $null
$stopError = $null
$connection = $null
function Invoke-Wsb([string[]] $arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($wsb)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill()
            $process.WaitForExit()
            throw "Windows Sandbox CLI exceeded thirty seconds: $($arguments[0])."
        }
        $out = $stdout.GetAwaiter().GetResult()
        $err = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Windows Sandbox CLI $($arguments[0]) failed ($($process.ExitCode)): $err $out" }
    }
    finally { $process.Dispose() }
}
try {
    $startAttempted = $true
    Invoke-Wsb @('start', '--id', $sessionId, '--config', $config)
    # The .wsb logon command needs a logged-on guest. `start` alone may create
    # only the VM; `connect` establishes the session for this exact ID.
    $connectStart = [Diagnostics.ProcessStartInfo]::new($wsb)
    $connectStart.UseShellExecute = $false
    $connectStart.ArgumentList.Add('connect')
    $connectStart.ArgumentList.Add('--id')
    $connectStart.ArgumentList.Add($sessionId)
    $connection = [Diagnostics.Process]::Start($connectStart)
    if ($null -eq $connection) { throw 'Windows Sandbox connect did not start.' }
    Start-Sleep -Milliseconds 500
    if ($connection.HasExited -and $connection.ExitCode -ne 0) { throw "Windows Sandbox connect failed ($($connection.ExitCode))." }
    $resultPath = Join-Path $outputFolder 'guest-result.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while (-not (Test-Path -LiteralPath $resultPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path -LiteralPath $resultPath)) { throw 'Guest MSIX fixture did not report within two minutes.' }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($result.runId -ne $runId -or -not $result.succeeded) { throw 'Guest MSIX proof or cleanup failed; inspect the isolated output.' }
}
catch { $runError = $_.Exception.Message }
finally {
    if ($startAttempted) { try { Invoke-Wsb @('stop', '--id', $sessionId) } catch { $stopError = $_.Exception.Message } }
    if ($null -ne $connection) { $connection.Dispose() }
    [ordered]@{ runId = $runId; sandboxSessionId = $sessionId; succeeded = ($null -eq $runError -and $null -eq $stopError); runError = $runError; stopError = $stopError } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'host-result.json')
}
if ($null -ne $runError -or $null -ne $stopError) { throw "MSIX Sandbox proof failed: $runError $stopError" }
Write-Output "Disposable MSIX Sandbox proof: PASS ($run)"
