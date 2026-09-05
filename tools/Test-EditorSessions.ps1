param([Parameter(Mandatory)] [string] $Executable)

$ErrorActionPreference = 'Stop'
$path = (Resolve-Path -LiteralPath $Executable).Path
$stdout = [IO.Path]::GetTempFileName()
$stderr = [IO.Path]::GetTempFileName()
$process = $null
try {
    $process = Start-Process -FilePath $path -ArgumentList '--editor-session-proof' `
        -WorkingDirectory (Split-Path $path) -WindowStyle Hidden `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    if (-not $process.WaitForExit(20000)) { throw 'Published editor session proof timed out.' }
    $output = Get-Content -LiteralPath $stdout -Raw
    if ($process.ExitCode -ne 0 -or $output -notmatch 'editor-session-proof: PASS;') {
        throw "Published editor session proof failed: exit=$($process.ExitCode); stdout=$output; stderr=$(Get-Content -LiteralPath $stderr -Raw)"
    }
    $output.Trim()
}
finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
    Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
}
