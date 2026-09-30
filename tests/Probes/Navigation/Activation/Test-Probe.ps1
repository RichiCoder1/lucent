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
function Start-ProbeConsole([string] $name, [string[]] $arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($exe)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    return [pscustomobject]@{
        Name = $name
        Process = $process
        Output = $process.StandardOutput.ReadToEndAsync()
        Error = $process.StandardError.ReadToEndAsync()
    }
}
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
    $primary = Start-ProbeConsole 'primary' @('--primary', $key, $ready, $result)
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while (-not (Test-Path -LiteralPath $ready) -and -not $primary.Process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 25 }
    if (-not (Test-Path -LiteralPath $ready)) { throw 'Activation primary did not become ready.' }
    $secondary = Start-ProbeConsole 'secondary' @('--secondary', $key)
    if (-not $secondary.Process.WaitForExit(8000)) { throw 'Activation secondary did not complete within eight seconds.' }
    $evidence.secondaryExit = $secondary.Process.ExitCode
    if (-not $primary.Process.WaitForExit(12000)) { throw 'Activation primary did not complete within twelve seconds.' }
    $evidence.primaryExit = $primary.Process.ExitCode
    if ($primary.Process.ExitCode -ne 0 -or $secondary.Process.ExitCode -ne 0) { throw 'A probe process exited unsuccessfully.' }
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
    foreach ($console in @($secondary, $primary)) {
        if ($null -ne $console) {
            if (-not $console.Process.HasExited) { $console.Process.Kill(); $console.Process.WaitForExit() }
            $console.Output.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $run ($console.Name + '.stdout.txt'))
            $console.Error.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $run ($console.Name + '.stderr.txt'))
            $console.Process.Dispose()
        }
    }
    $evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $run 'evidence.json')
}
