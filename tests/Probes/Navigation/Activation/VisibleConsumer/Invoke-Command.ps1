param(
    [Parameter(Mandatory)] [string] $Fixture,
    [ValidateSet('home', 'second', 'launch')] [string] $Request = 'home'
)
$ErrorActionPreference = 'Stop'
$fixturePath = (Resolve-Path -LiteralPath $Fixture).Path
$manifest = Get-Content -LiteralPath (Join-Path $fixturePath 'manifest.json') -Raw | ConvertFrom-Json
$executable = Join-Path $fixturePath 'publish/Consumer.exe'
if ($manifest.packageVersion -cne '0.3.0-dev.101.1' -or $manifest.packageSourceCommit -cne '6270d75762c9318d3a64eb35cf512f6631fb485e' -or $manifest.transport -cne 'direct-command' -or (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -cne $manifest.executableSha256) { throw 'Visible fixture identity does not match its manifest.' }
$launch = [Diagnostics.ProcessStartInfo]::new($executable)
$launch.UseShellExecute = $false
if ($Request -ne 'launch') {
    # Windows App SDK's GenerateCommandLine protocol encoding. This is a direct
    # executable invocation; no OS protocol registration or shell dispatch occurs.
    $launch.ArgumentList.Add("----ms-protocol:$($manifest.scheme)://navigation/$Request")
}
$process = [Diagnostics.Process]::Start($launch)
try {
    Write-Output "Direct-command request '$Request' started PID $($process.Id); output: $(Join-Path $fixturePath 'output')"
}
finally { $process.Dispose() }
