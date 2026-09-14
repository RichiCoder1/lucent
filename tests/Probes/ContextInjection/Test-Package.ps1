param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [ValidatePattern('^0\.3\.0-[0-9A-Za-z.-]+$')] [string] $Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = 'dotnet' }
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
$package = Join-Path $feedPath "Lucent.Core.$Version.nupkg"
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
    throw "Required package is missing: Lucent.Core $Version"
}
$artifactRoot = Join-Path $root 'artifacts/context-injection-package'
$artifacts = Join-Path $artifactRoot ([Guid]::NewGuid().ToString('N'))
$cache = Join-Path $artifactRoot 'cache'
$publish = Join-Path $artifacts 'publish'
$null = New-Item -ItemType Directory -Path $cache, $publish -Force
$candidateHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
$escapedFeed = [System.Security.SecurityElement]::Escape($feedPath)
$config = Join-Path $artifacts 'NuGet.config'
@"
<configuration>
  <packageSources><clear /><add key="lucent" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="lucent"><package pattern="Lucent.Core" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $config
$previousPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = $cache
    $consumer = Join-Path $PSScriptRoot 'Consumer/ContextInjection.Consumer.csproj'
    & $dotnet build $consumer -c Release --configfile $config -p:LucentContextProbeVersion=$Version -warnaserror
    if ($LASTEXITCODE) { throw 'Managed package-only context/injection build failed.' }
    $managed = @(& $dotnet (Join-Path $PSScriptRoot 'Consumer/bin/Release/net10.0/ContextInjection.Consumer.dll'))
    if ($LASTEXITCODE -or $managed -notcontains 'package-context-injection-native-aot=pass') {
        throw 'Managed package-only context/injection execution failed.'
    }
    & $dotnet publish $consumer -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:LucentContextProbeVersion=$Version --configfile $config -o $publish -warnaserror
    if ($LASTEXITCODE) { throw 'NativeAOT package-only context/injection publication failed.' }
    $native = @(& (Join-Path $publish 'ContextInjection.Consumer.exe'))
    if ($LASTEXITCODE -or $native -notcontains 'package-context-injection-native-aot=pass') {
        throw 'NativeAOT package-only context/injection execution failed.'
    }
    $native | Write-Output
    [ordered]@{ version = $Version; corePackageSha256 = $candidateHash } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $artifacts 'candidate.json')
    Write-Output "Context/injection package evidence: $artifacts"
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
}
