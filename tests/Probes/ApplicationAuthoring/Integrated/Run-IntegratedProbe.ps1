[CmdletBinding()]
param([string] $JsonGeneratorPath)

$ErrorActionPreference = "Stop"
$probeRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $probeRoot "../../../..")).Path
$dotnet = Join-Path $repositoryRoot ".dotnet/dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw "Install the repository-pinned SDK first." }
if (-not $JsonGeneratorPath) {
    $JsonGeneratorPath = Join-Path $repositoryRoot ".dotnet/packs/Microsoft.NETCore.App.Ref/10.0.12/analyzers/dotnet/cs/System.Text.Json.SourceGeneration.dll"
}
if (-not (Test-Path -LiteralPath $JsonGeneratorPath -PathType Leaf)) { throw "Missing real JSON generator: $JsonGeneratorPath" }
$artifactRoot = Join-Path $repositoryRoot "artifacts/a0-integrated"
$resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactRoot)
if (-not $resolvedArtifacts.StartsWith($repositoryRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace artifacts outside the repository: $resolvedArtifacts"
}
if (Test-Path -LiteralPath $resolvedArtifacts) { Remove-Item -LiteralPath $resolvedArtifacts -Recurse -Force }
New-Item -ItemType Directory -Path $resolvedArtifacts | Out-Null
$commands = [System.Collections.Generic.List[string]]::new()

function Invoke-Checked([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments) {
    & $dotnet @Arguments
    $exitCode = $LASTEXITCODE
    $commands.Add("dotnet $($Arguments -join ' ') => exit $exitCode")
    if ($exitCode) { throw "dotnet command failed with exit $exitCode" }
}

Push-Location $repositoryRoot
try {
    foreach ($project in @("src/Lucent.Lui.Compiler/Lucent.Lui.Compiler.csproj", "src/Lucent.Lui.Generator/Lucent.Lui.Generator.csproj", "src/Lucent.Core/Lucent.Core.csproj")) {
        Invoke-Checked @("restore", $project, "--locked-mode")
        Invoke-Checked @("build", $project, "-c", "Release", "--no-restore")
    }
    $hostProject = Join-Path $probeRoot "Host/IntegratedHost.csproj"
    Invoke-Checked @("restore", $hostProject, "-p:RestorePackagesWithLockFile=false")
    Invoke-Checked @("build", $hostProject, "-c", "Release", "--no-restore")
    $hostPath = Join-Path $probeRoot "Host/bin/Release/net10.0/IntegratedHost.dll"
    $input = Join-Path $probeRoot "Inputs/Integrated.lui.input"
    $companion = Join-Path $probeRoot "Companion/CompanionApp.lui.cs"
    $allGenerated = Join-Path $resolvedArtifacts "all-lui/generated"
    $companionGenerated = Join-Path $resolvedArtifacts "companion/generated"

    Invoke-Checked @($hostPath, $input, $allGenerated, "AllLuiApp", $JsonGeneratorPath)
    Invoke-Checked @($hostPath, $input, $companionGenerated, "CompanionApp", $JsonGeneratorPath, $companion)

    $blockerLog = Join-Path $resolvedArtifacts "companion-cross-reference.log"
    & $dotnet $hostPath (Join-Path $probeRoot "Inputs/CompanionReference.lui.input") (Join-Path $resolvedArtifacts "blocker") "CompanionApp" $JsonGeneratorPath $companion 2>&1 | Tee-Object -FilePath $blockerLog | ForEach-Object { Write-Host $_ }
    $blockerExit = $LASTEXITCODE
    $commands.Add("dotnet IntegratedHost CompanionReference.lui.input => exit $blockerExit (expected 1)")
    if ($blockerExit -eq 0 -or -not (Select-String -LiteralPath $blockerLog -SimpleMatch "LUI2000: The name 'Organization' does not exist")) {
        throw "The companion cross-file binding check did not expose the expected current-lowerer blocker."
    }

    foreach ($consumer in @(
        @{ Name = "all-lui"; Project = (Join-Path $probeRoot "AllLui/AllLui.csproj"); Generated = $allGenerated; Exe = "AllLui.exe" },
        @{ Name = "companion"; Project = (Join-Path $probeRoot "Companion/Companion.csproj"); Generated = $companionGenerated; Exe = "Companion.exe" }
    )) {
        Invoke-Checked @("restore", $consumer.Project, "-r", "win-x64", "-p:RestorePackagesWithLockFile=false")
        $publish = Join-Path $resolvedArtifacts "$($consumer.Name)/publish"
        Invoke-Checked @("publish", $consumer.Project, "-c", "Release", "-r", "win-x64", "--self-contained", "true", "--no-restore", "-p:GeneratedRoot=$($consumer.Generated)", "-o", $publish)
        $executable = Join-Path $publish $consumer.Exe
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "NativeAOT executable missing: $executable" }
        & $executable
        $runExit = $LASTEXITCODE
        $commands.Add("$executable => exit $runExit")
        if ($runExit) { throw "$($consumer.Name) NativeAOT execution failed." }
    }
}
finally {
    Pop-Location
    $commands | Set-Content -LiteralPath (Join-Path $resolvedArtifacts "commands-and-exits.txt")
}

Write-Output "PASS: all-LUI and companion NativeAOT consumers use named lowered state identity, real markup, real JSON APIs, and existing generated route APIs; expected companion cross-reference blocker retained."
