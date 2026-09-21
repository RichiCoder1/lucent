param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')] [string] $Version,
    [string] $OutputRoot,
    [switch] $Companion,
    [switch] $BuildOnly
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $repo '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
$sample = Join-Path $repo 'apps/Lucent.AuthoringSample'
$variant = if ($Companion) { 'companion' } else { 'inline' }
$requiredPackages = @('Lucent.Core', 'Lucent.Renderer.Skia', 'Lucent.Platform.Windows', 'Lucent.Lui.Sdk')
foreach ($name in $requiredPackages) {
    if (-not (Test-Path -LiteralPath (Join-Path $feedPath "$name.$Version.nupkg"))) {
        throw "Missing candidate package: $name $Version"
    }
}
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo 'artifacts/aw' }
$run = Join-Path $OutputRoot ([Guid]::NewGuid().ToString('N').Substring(0, 12))
$fixture = Join-Path $run 'fixture'
$cache = Join-Path $run 'packages'
$publish = Join-Path $run 'publish'
New-Item -ItemType Directory -Path $fixture, $cache, $publish -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'global.json') -Destination $run
foreach ($name in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')) {
    [IO.File]::WriteAllText((Join-Path $run $name), '<Project />')
}
Get-ChildItem -LiteralPath $sample -File |
    Where-Object { $_.Extension -in '.cs', '.lui' } |
    Copy-Item -Destination $fixture
if ($Companion) {
    $companionRoot = Join-Path $repo 'apps/Lucent.AuthoringSample.Companion'
    foreach ($name in @('CounterPage.lui', 'CounterPage.lui.cs')) {
        Copy-Item -LiteralPath (Join-Path $companionRoot $name) -Destination $fixture -Force
    }
}
$csharp = @(Get-ChildItem -LiteralPath $fixture -Filter '*.cs' -File)
$expectedCsharp = if ($Companion) { @('CounterPage.lui.cs', 'Program.cs') } else { @('Program.cs') }
if (@(Compare-Object $expectedCsharp @($csharp.Name)).Count -ne 0) {
    throw "Unexpected authored C# files in the $variant application."
}
if (@(Get-ChildItem -LiteralPath $fixture -Filter '*.lui' -File).Count -lt 2) {
    throw 'The application fixture did not include its authored components.'
}
$project = Join-Path $fixture 'Lucent.AuthoringSample.csproj'
[IO.File]::WriteAllText($project, @"
<Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/$Version">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <LangVersion>preview</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <IsAotCompatible>true</IsAotCompatible>
    <SelfContained>true</SelfContained>
    <LucentLuiNamedComponents>true</LucentLuiNamedComponents>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lucent.Core" Version="[$Version]" />
    <PackageReference Include="Lucent.Platform.Windows" Version="[$Version]" />
  </ItemGroup>
</Project>
"@)
$escapedFeed = [Security.SecurityElement]::Escape($feedPath)
$config = Join-Path $run 'NuGet.Config'
[IO.File]::WriteAllText($config, @"
<configuration>
  <packageSources><clear /><add key="candidate" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="candidate"><package pattern="Lucent.*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>
"@)

function Invoke-ApplicationSmoke([string] $Executable, [string[]] $Arguments, [string] $Name) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.WorkingDirectory = $fixture
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit(45000)
        if ($timedOut -and -not $process.HasExited) {
            $process.Kill($true)
            if (-not $process.WaitForExit(10000)) { throw "$Name test process could not be stopped after its timeout." }
        }
        $output = $stdout.GetAwaiter().GetResult()
        $errorOutput = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $run "$Name.log"), $output + $errorOutput)
        "$Name exit=$($process.ExitCode)" | Add-Content -LiteralPath (Join-Path $run 'commands-and-exits.txt')
        if ($timedOut) { throw "$Name application did not complete its smoke run in 45 seconds; see $run/$Name.log" }
        if ($process.ExitCode -ne 0 -or $output -notmatch 'AUTHORING SAMPLE PASS' -or $output -notmatch 'AUTHORING SAMPLE CLEANUP') {
            throw "$Name application did not report a successful smoke run; see $run/$Name.log"
        }
        if ($Companion -and $output -notmatch 'AUTHORING COMPANION PASS') {
            throw "$Name application omitted the companion mount and cleanup proof; see $run/$Name.log"
        }
    }
    finally { $process.Dispose() }
}

$previousPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = $cache
    $build = @('build', $project, '-c', 'Release', '--nologo', "-p:RestoreConfigFile=$config")
    "dotnet $($build -join ' ')" | Set-Content -LiteralPath (Join-Path $run 'commands-and-exits.txt')
    & $dotnet @build *> (Join-Path $run 'managed-build.log')
    "managed build exit=$LASTEXITCODE" | Add-Content -LiteralPath (Join-Path $run 'commands-and-exits.txt')
    if ($LASTEXITCODE -ne 0) { throw "Package-only application build failed; see $run/managed-build.log" }
    $managed = Join-Path $fixture 'bin/Release/net10.0-windows10.0.26100.0/win-x64/Lucent.AuthoringSample.dll'
    if (-not $BuildOnly) { Invoke-ApplicationSmoke $dotnet @($managed, '--smoke') 'managed-execution' }

    $arguments = @('publish', $project, '-c', 'Release', '--nologo', '-p:PublishAot=true', '-o', $publish, "-p:RestoreConfigFile=$config")
    "dotnet $($arguments -join ' ')" | Add-Content -LiteralPath (Join-Path $run 'commands-and-exits.txt')
    & $dotnet @arguments *> (Join-Path $run 'native-publish.log')
    "native publish exit=$LASTEXITCODE" | Add-Content -LiteralPath (Join-Path $run 'commands-and-exits.txt')
    if ($LASTEXITCODE -ne 0) { throw "Package-only NativeAOT publication failed; see $run/native-publish.log" }
    $executable = Join-Path $publish 'Lucent.AuthoringSample.exe'
    if (-not $BuildOnly) { Invoke-ApplicationSmoke $executable @('--smoke') 'native-execution' }
    $hashes = [ordered]@{
        version = $Version
        variant = $variant
        execution = $(if ($BuildOnly) { 'deferred' } else { 'passed' })
        executable = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    }
    foreach ($name in $requiredPackages) {
        $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $feedPath "$name.$Version.nupkg") -Algorithm SHA256).Hash
    }
    $hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'candidate.json')
    Write-Output "Windows $variant application authoring package evidence: $run (execution: $($hashes.execution))"
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
    if (Test-Path -LiteralPath $cache) {
        $resolvedRun = (Resolve-Path -LiteralPath $run).Path
        $resolvedCache = (Resolve-Path -LiteralPath $cache).Path
        if (-not $resolvedCache.StartsWith($resolvedRun + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedCache) -ne 'packages') {
            throw "Unexpected package cache cleanup target: $resolvedCache"
        }
        Remove-Item -LiteralPath $resolvedCache -Recurse -Force
    }
}
