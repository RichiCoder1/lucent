param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-dev\.[0-9A-Za-z.-]+$')] [string] $Version
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../..'))
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
$proof = Join-Path $root ('artifacts/windows-activation-package/' + [Guid]::NewGuid().ToString('N'))
$packageCache = Join-Path (Split-Path -Parent $proof) ((Split-Path -Leaf $proof) + '.nuget-packages')
$publish = Join-Path $proof 'publish'
$null = New-Item -ItemType Directory -Path $publish -Force
Copy-Item (Join-Path $PSScriptRoot 'Program.cs') $proof
Copy-Item (Join-Path $root 'global.json') $proof
'<Project />' | Set-Content (Join-Path $proof 'Directory.Build.props'), (Join-Path $proof 'Directory.Build.targets'), (Join-Path $proof 'Directory.Packages.props')
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishAot>true</PublishAot>
    <SelfContained>true</SelfContained>
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <WindowsAppSdkDeploymentManagerInitialize>false</WindowsAppSdkDeploymentManagerInitialize>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lucent.Platform.Windows.Activation" Version="[$Version]" />
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $proof 'Consumer.csproj')
$escapedFeed = [Security.SecurityElement]::Escape($feedPath)
@"
<configuration><packageSources><clear /><add key="lucent" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources><packageSourceMapping><packageSource key="lucent"><package pattern="Lucent.*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping></configuration>
"@ | Set-Content (Join-Path $proof 'NuGet.config')
$previousPackages = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = $packageCache
$activationSources = @(
    Get-ChildItem -LiteralPath (Join-Path $root 'src/Lucent.Platform.Windows.Activation') -File |
        Where-Object { $_.Extension -in @('.cs', '.csproj', '.json', '.md') }
    Get-ChildItem -LiteralPath (Join-Path $root 'src/Lucent.Platform.Windows.Activation/notices') -File
    Get-ChildItem -LiteralPath (Join-Path $root 'src/Lucent.Platform.Windows.Activation/buildTransitive') -File
    @('WindowsBootstrap.cs', 'WindowsWindowOptions.cs', 'WindowsWindowAttention.cs') |
        ForEach-Object { Get-Item -LiteralPath (Join-Path $root "src/Lucent.Platform.Windows/$_") }
    @('Directory.Packages.props', 'eng/Packages.props') |
        ForEach-Object { Get-Item -LiteralPath (Join-Path $root $_) }
)
$evidence = [ordered]@{
    succeeded = $false
    baseCommit = (& git -C $root rev-parse HEAD).Trim()
    sourceFiles = @($activationSources | ForEach-Object { [ordered]@{
        path = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    } })
    consumerSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Program.cs') -Algorithm SHA256).Hash
    scriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    packages = @(Get-ChildItem -LiteralPath $feedPath -Filter '*.nupkg' | ForEach-Object { [ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    publishedExecutableSha256 = $null
    invalidExit = $null
    primaryExit = $null
    secondaryExit = $null
    failure = $null
}
$primary = $null
$invalid = $null
$secondary = $null
function Start-Console([string] $name, [string[]] $arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $publish 'Consumer.exe'))
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
Push-Location $proof
try {
    & $dotnet restore Consumer.csproj --configfile NuGet.config
    if ($LASTEXITCODE) { throw 'Activation package-only restore failed.' }
    & $dotnet publish Consumer.csproj -c Release --no-restore -o $publish -warnaserror -m:1 -nr:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE) { throw 'Activation package-only NativeAOT publish failed.' }
    $evidence.publishedExecutableSha256 = (Get-FileHash -LiteralPath (Join-Path $publish 'Consumer.exe') -Algorithm SHA256).Hash
    foreach ($asset in 'Consumer.exe', 'Microsoft.WindowsAppRuntime.dll', 'notices/WindowsAppSDK-LICENSE.txt', 'notices/CsWinRT-LICENSE.txt', 'notices/CsWinRT-NOTICE.txt') {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $asset) -PathType Leaf)) { throw "Activation package output omitted $asset" }
    }
    $ready = Join-Path $proof 'ready.txt'
    $result = Join-Path $proof 'result.txt'
    $key = 'Lucent.PackageProbe.' + [Guid]::NewGuid().ToString('N')
    $primary = Start-Console 'primary' @('--primary', $key, $ready, $result)
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while (-not (Test-Path -LiteralPath $ready) -and -not $primary.Process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 25 }
    if (-not (Test-Path -LiteralPath $ready)) { throw 'Activation package primary did not become ready.' }
    $invalid = Start-Console 'invalid' @('--secondary', $key, ('x' * 5000))
    if (-not $invalid.Process.WaitForExit(8000) -or $invalid.Process.ExitCode -ne 2) { throw 'Oversized secondary did not reject before redirection.' }
    $evidence.invalidExit = $invalid.Process.ExitCode
    if ($primary.Process.HasExited -or (Test-Path -LiteralPath $result)) { throw 'Rejected secondary changed the primary lifecycle.' }
    $secondary = Start-Console 'secondary' @('--secondary', $key)
    if (-not $secondary.Process.WaitForExit(8000)) { throw 'Activation package secondary exceeded redirect bound.' }
    if (-not $primary.Process.WaitForExit(12000)) { throw 'Activation package primary did not finish.' }
    if ($primary.Process.ExitCode -ne 0 -or $secondary.Process.ExitCode -ne 0) { throw 'An activation package process exited unsuccessfully.' }
    $evidence.primaryExit = $primary.Process.ExitCode
    $evidence.secondaryExit = $secondary.Process.ExitCode
    if ((Get-Content -LiteralPath $result -Raw) -ne 'PASS: package-only NativeAOT primary received redirect') { throw 'Activation package delivery proof is missing.' }
    $evidence.succeeded = $true
    Write-Output "Activation package-only NativeAOT redirection: PASS ($Version, $proof)"
}
catch {
    $evidence.failure = $_.Exception.Message
    throw
}
finally {
    foreach ($console in @($secondary, $invalid, $primary)) {
        if ($null -ne $console) {
            if (-not $console.Process.HasExited) { $console.Process.Kill(); $console.Process.WaitForExit() }
            $console.Output.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $proof ($console.Name + '.stdout.txt'))
            $console.Error.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $proof ($console.Name + '.stderr.txt'))
            $console.Process.Dispose()
        }
    }
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
    $evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $proof 'evidence.json')
}
