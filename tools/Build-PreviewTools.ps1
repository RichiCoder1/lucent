param([Parameter(Mandatory)] [string] $OutputRoot)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not [IO.Path]::IsPathFullyQualified($OutputRoot)) { throw 'Use an absolute new output directory.' }
$destination = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $destination) { throw 'The preview tools output directory must be new.' }
New-Item -ItemType Directory -Path $destination | Out-Null
$projects = @{
    build = 'src/Lucent.Preview.Build/Lucent.Preview.Build.csproj'
    supervisor = 'src/Lucent.Preview.Supervisor/Lucent.Preview.Supervisor.csproj'
}
foreach ($item in $projects.GetEnumerator()) {
    $project = Join-Path $root $item.Value
    $output = Join-Path $destination $item.Key
    & dotnet publish $project -c Release --self-contained false -p:PublishAot=false -p:PublishTrimmed=false -p:RestoreLockedMode=true `
        "-p:ArtifactsPath=$(Join-Path $destination '.build')" -p:UseSharedCompilation=false -nodeReuse:false `
        -o $output *> (Join-Path $destination "$($item.Key)-publish.log")
    if ($LASTEXITCODE) { throw "Preview $($item.Key) publication failed; see $destination." }
}
Write-Output "Development preview tools: $destination"
Write-Output 'These local tools execute trusted projects. They are not an authenticated external preview release.'
