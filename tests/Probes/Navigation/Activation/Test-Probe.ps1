$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$project = Join-Path $PSScriptRoot 'ActivationProbe.csproj'
$run = Join-Path $root ('artifacts/windows-activation-probe/' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $run 'publish'
$null = New-Item -ItemType Directory -Path $publish -Force
$inputFiles = @('ActivationProbe.csproj', 'Program.cs', 'packages.lock.json', 'Test-Probe.ps1')
$sourceHashes = @($inputFiles | ForEach-Object {
    $path = Join-Path $PSScriptRoot $_
    [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
})
& $dotnet restore $project --locked-mode
if ($LASTEXITCODE) { throw 'Activation probe locked restore failed.' }
& $dotnet publish $project -c Release --no-restore -o $publish -m:1 -nr:false -p:UseSharedCompilation=false
if ($LASTEXITCODE) { throw 'Activation probe NativeAOT publication failed.' }
$exe = Join-Path $publish 'ActivationProbe.exe'
$ready = Join-Path $run 'ready.txt'
$result = Join-Path $run 'result.txt'
$key = 'Lucent.ActivationProbe.' + [guid]::NewGuid().ToString('N')
$primary = $null
$secondary = $null
$evidence = [ordered]@{
    succeeded = $false
    sources = $sourceHashes
    executableSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    primaryExit = $null
    secondaryExit = $null
    failure = $null
    payload = @(Get-ChildItem -LiteralPath $publish -File -Recurse | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($publish, $_.FullName); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
try {
    $primary = Start-Process -FilePath $exe -ArgumentList @('--primary', $key, $ready, $result) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'primary.stdout.txt') -RedirectStandardError (Join-Path $run 'primary.stderr.txt')
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while (-not (Test-Path -LiteralPath $ready) -and -not $primary.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 25 }
    if (-not (Test-Path -LiteralPath $ready)) { throw 'Activation primary did not become ready.' }
    $secondary = Start-Process -FilePath $exe -ArgumentList @('--secondary', $key) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'secondary.stdout.txt') -RedirectStandardError (Join-Path $run 'secondary.stderr.txt')
    if (-not $secondary.WaitForExit(8000)) { throw 'Activation secondary did not complete within eight seconds.' }
    $evidence.secondaryExit = $secondary.ExitCode
    if (-not $primary.WaitForExit(12000)) { throw 'Activation primary did not complete within twelve seconds.' }
    $evidence.primaryExit = $primary.ExitCode
    if ($primary.ExitCode -ne 0 -or $secondary.ExitCode -ne 0) { throw 'A probe process exited unsuccessfully.' }
    if (-not (Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -Raw) -ne 'PASS: primary received secondary launch; raw URI fidelity passed') { throw 'Activation delivery or raw URI fidelity evidence is missing.' }
    foreach ($source in $sourceHashes) {
        if ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $source.path) -Algorithm SHA256).Hash -ne $source.sha256) { throw 'Probe source changed during execution.' }
    }
    $evidence.succeeded = $true
    Write-Output "Windows activation console probe: PASS ($run)"
}
catch {
    $evidence.failure = $_.Exception.Message
    throw
}
finally {
    foreach ($process in @($secondary, $primary)) {
        if ($null -ne $process) {
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
            $process.Dispose()
        }
    }
    $evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $run 'evidence.json')
}
