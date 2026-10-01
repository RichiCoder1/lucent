param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [string] $AssetsPath,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $AssetsPath) {
    $AssetsPath = Join-Path $root 'tests/Lucent.Preview.Fixtures/obj/project.assets.json'
}
if (-not $OutputPath) {
    $OutputPath = Join-Path $root "tests/Lucent.Preview.Fixtures/bin/$Configuration/net10.0"
}

function Assert-PreviewFixtureDependencies([string] $AssetsPath, [string] $OutputPath) {
    $forbiddenFixtureDependencies = @('Lucent.Preview', 'Lucent.Testing', 'Lucent.Testing.Skia')
    $assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
    $resolved = @($assets.libraries.psobject.Properties.Name | ForEach-Object { ($_ -split '/')[0] })
    if ($resolved -notcontains 'Lucent.Core') { throw 'Compiled preview fixture does not resolve Core.' }
    foreach ($name in $resolved) {
        if ($forbiddenFixtureDependencies -contains $name) {
            throw "Compiled preview fixture resolves a forbidden dependency: $name"
        }
    }
    foreach ($framework in $assets.project.restore.frameworks.psobject.Properties.Value) {
        foreach ($reference in $framework.projectReferences.psobject.Properties.Name) {
            $name = [IO.Path]::GetFileNameWithoutExtension($reference)
            if ($forbiddenFixtureDependencies -contains $name) {
                throw "Compiled preview fixture has a forbidden evaluated project reference: $name"
            }
        }
    }
    $depsPath = Join-Path $OutputPath 'Lucent.Preview.Fixtures.deps.json'
    $deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
    $runtime = @($deps.libraries.psobject.Properties.Name | ForEach-Object { ($_ -split '/')[0] })
    if ($runtime -notcontains 'Lucent.Core') { throw 'Compiled preview fixture runtime does not include Core.' }
    foreach ($name in $runtime) {
        if ($forbiddenFixtureDependencies -contains $name) {
            throw "Compiled preview fixture runtime resolves a forbidden dependency: $name"
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $OutputPath 'Lucent.Preview.Fixtures.dll') -PathType Leaf)) {
        throw 'Compiled preview fixture assembly is missing.'
    }
    foreach ($file in Get-ChildItem -LiteralPath $OutputPath -Recurse -File -Filter '*.dll') {
        if ($forbiddenFixtureDependencies -contains $file.BaseName) {
            throw "Compiled preview fixture output includes a forbidden assembly: $($file.BaseName)"
        }
    }
}

Assert-PreviewFixtureDependencies $AssetsPath $OutputPath
[ordered]@{ ok = $true; previewFixture = 'evaluated graph and runtime output' } | ConvertTo-Json -Compress
exit 0
