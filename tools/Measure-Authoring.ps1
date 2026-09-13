param(
    [string] $ResultsPath,
    [string] $LogDirectory,
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnet = 'dotnet'
}
if (!$ResultsPath) {
    $ResultsPath = Join-Path $root 'artifacts/authoring-build-measurements.json'
}
if (!$LogDirectory) {
    $LogDirectory = Join-Path $root 'artifacts/authoring-build-measurements'
}

$coreProject = Join-Path $root 'src/Lucent.Core/Lucent.Core.csproj'
$coreAssemblyPath = Join-Path $root 'src/Lucent.Core/bin/Release/net10.0/Lucent.Core.dll'
if ($Configuration -ne 'Release') {
    $coreAssemblyPath = Join-Path $root "src/Lucent.Core/bin/$Configuration/net10.0/Lucent.Core.dll"
}
if (!(Test-Path -LiteralPath $coreProject -PathType Leaf)) {
    throw "Core project not found: $coreProject"
}

$null = New-Item -ItemType Directory -Path $LogDirectory -Force

function Invoke-MeasuredBuild([string] $Name, [string[]] $Arguments) {
    $logPath = Join-Path $LogDirectory "$Name.log"
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $output = @(& $dotnet @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()
    $output | Set-Content -LiteralPath $logPath
    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode. See $logPath"
    }
    [ordered]@{
        name = $Name
        timingKind = 'process-wall-clock'
        measurementScope = 'Lucent.Core build process including project references and analyzers'
        milliseconds = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds)
        exitCode = $exitCode
        command = ($dotnet + ' ' + ($Arguments -join ' '))
        log = $logPath
    }
}

$buildArguments = @(
    'build',
    $coreProject,
    '--no-restore',
    '-c',
    $Configuration,
    '-warnaserror'
)

$rebuild = Invoke-MeasuredBuild 'core-rebuild' ($buildArguments + @('-t:Rebuild'))
$incremental = Invoke-MeasuredBuild 'core-incremental' $buildArguments

if (!(Test-Path -LiteralPath $coreAssemblyPath -PathType Leaf)) {
    throw "Core assembly not found after measurement: $coreAssemblyPath"
}

$assembly = [Reflection.Assembly]::LoadFrom($coreAssemblyPath)
$styleFluency = $assembly.GetType('Lucent.Core.StyleFluency', $true, $false)
$recipeFluency = $assembly.GetType('Lucent.Core.GeneratedAuthorRecipeStyleFluency', $true, $false)
$publicStaticDeclared = [Reflection.BindingFlags]::Public -bor `
    [Reflection.BindingFlags]::Static -bor `
    [Reflection.BindingFlags]::DeclaredOnly
$styleMethods = @($styleFluency.GetMethods($publicStaticDeclared))
$recipeMethods = @($recipeFluency.GetMethods($publicStaticDeclared))
# StyleFluency retains three source-authored Padding convenience methods.  The
# remaining methods on that partial type, and all methods on the generated
# recipe helper, are emitted from the shared authoring descriptor.
$styleFluencyConvenienceMethods = 3
$styleFluencyGeneratedMethods = $styleMethods.Count - $styleFluencyConvenienceMethods
$groupAttributeName = 'Lucent.Core.StylePropertyGroupAttribute'
$propertyTypeName = 'Lucent.Core.Property`1'
$groups = @(
    $assembly.GetTypes() | Where-Object {
        $_.GetCustomAttributesData().AttributeType.FullName -contains $groupAttributeName
    }
)
$propertyFields = @(
    $groups | ForEach-Object {
        $_.GetFields($publicStaticDeclared) | Where-Object {
            $_.IsInitOnly -and $_.FieldType.IsGenericType -and
            $_.FieldType.GetGenericTypeDefinition().FullName -eq $propertyTypeName
        }
    }
)

$sdkOutput = @(& $dotnet --version 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not determine the SDK version.'
}
$candidate = @(& git -C $root rev-parse HEAD 2>$null)
$candidate = ($candidate -join '').Trim()
$gitStatus = @(& git -C $root status --porcelain --untracked-files=normal 2>$null)
$dirty = $gitStatus.Count -ne 0

$measurement = [ordered]@{
    schema = 1
    measuredAtUtc = [DateTime]::UtcNow.ToString('O')
    candidate = $candidate
    workingTreeDirty = $dirty
    sdk = (($sdkOutput -join [Environment]::NewLine).Trim())
    configuration = $Configuration
    coreProject = $coreProject
    coreAssembly = $coreAssemblyPath
    coreAssemblyBytes = (Get-Item -LiteralPath $coreAssemblyPath).Length
    builds = @($rebuild, $incremental)
    generated = [ordered]@{
        attributedGroupTypes = $groups.Count
        attributedGroupTypeNames = @($groups.FullName | Sort-Object)
        propertyFields = $propertyFields.Count
        generatedTypeDeclarations = 2
        authoringTypeDeclarations = 2
        existingStyleFluencyPartialTypeDeclarations = 1
        newGeneratedHelperTypeDeclarations = 1
        styleFluencyType = $styleFluency.FullName
        styleFluencyDeclaredPublicStaticMethods = $styleMethods.Count
        styleFluencyHandwrittenConvenienceMethods = $styleFluencyConvenienceMethods
        styleFluencyGeneratedPublicStaticMethods = $styleFluencyGeneratedMethods
        styleFluencyMethodNames = @($styleMethods.Name | Sort-Object -Unique)
        recipeFluencyType = $recipeFluency.FullName
        recipeFluencyDeclaredPublicStaticMethods = $recipeMethods.Count
        recipeFluencyGeneratedPublicStaticMethods = $recipeMethods.Count
        generatedPublicStaticMethodCount = $styleFluencyGeneratedMethods + $recipeMethods.Count
        recipeFluencyMethodNames = @($recipeMethods.Name | Sort-Object -Unique)
    }
}

$resultsDirectory = Split-Path $ResultsPath -Parent
if ($resultsDirectory) {
    $null = New-Item -ItemType Directory -Path $resultsDirectory -Force
}
$measurement | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ResultsPath
Write-Output "Authoring build measurements: $ResultsPath"
