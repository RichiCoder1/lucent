param([string]$OutputDirectory = "$PSScriptRoot/evidence/milestone-1")

$ErrorActionPreference = 'Stop'
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$solution = "$PSScriptRoot/NativeStack.sln"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
$work = Join-Path ([IO.Path]::GetTempPath()) ("native-stack-milestone-1-" + [Guid]::NewGuid())

function Get-Sha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($Path)))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-InputIdentity {
    $paths = @(
        Get-ChildItem "$PSScriptRoot/NativeStackProbe" -File -Recurse |
            Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]'
        Get-ChildItem "$PSScriptRoot/UiaExternalHelper" -File -Recurse |
            Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]'
        Get-ChildItem $PSScriptRoot -File |
            Where-Object { $_.Extension -in '.ps1', '.props' -or $_.Name -in 'global.json', 'NativeStack.sln' }
    ) | Sort-Object FullName -Unique
    $files = @($paths | ForEach-Object {
        $relative = $_.FullName.Substring($PSScriptRoot.Length).TrimStart('\', '/').Replace('\', '/')
        [ordered]@{ path = $relative; sha256 = Get-Sha256 $_.FullName }
    })
    $lines = @($files | ForEach-Object { "$($_.path) $($_.sha256)" })
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $hash = ([BitConverter]::ToString($hasher.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $hasher.Dispose() }
    [ordered]@{ sha256 = $hash; fileCount = $lines.Count; files = $files }
}

function Get-WorkingTreeIdentity {
    $status = @(& git -C $PSScriptRoot status --porcelain -- .)
    $patch = @(& git -C $PSScriptRoot diff --binary HEAD -- .) -join "`n"
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $patchHash = ([BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($patch)))).Replace('-', '').ToLowerInvariant() }
    finally { $hasher.Dispose() }
    [ordered]@{ dirty = $status.Count -gt 0; status = $status; trackedPatchSha256 = $patchHash }
}

function Assert-ArtifactProof([string]$Directory, [string[]]$ExpectedNames) {
    $proof = Get-Content (Join-Path $Directory proof.json) -Raw | ConvertFrom-Json
    $files = @(Get-ChildItem $Directory -File | Where-Object Name -ne 'proof.json' | Sort-Object Name)
    $proofNames = @($proof.artifacts | ForEach-Object name | Sort-Object)
    if (-not $proof.ok -or (Compare-Object $ExpectedNames $files.Name) -or (Compare-Object $ExpectedNames $proofNames)) { throw "Invalid artifact set: $Directory" }
    foreach ($artifact in $proof.artifacts) {
        $path = Join-Path $Directory $artifact.name
        if (-not (Test-Path $path) -or (Get-Sha256 $path) -ne $artifact.sha256) { throw "Artifact hash mismatch: $path" }
    }
    return $proof
}

function Assert-MatchingArtifacts($Fresh, $Retained, [string]$Name) {
    $freshLines = @($Fresh.artifacts | ForEach-Object { "$($_.name) $($_.sha256)" } | Sort-Object)
    $retainedLines = @($Retained.artifacts | ForEach-Object { "$($_.name) $($_.sha256)" } | Sort-Object)
    if (Compare-Object $freshLines $retainedLines) { throw "$Name fresh artifacts do not match retained evidence." }
}

function Assert-ImeEvidence([string]$Path, [bool]$FocusCase) {
    $events = @(Get-Content $Path | ConvertFrom-Json)
    $japanese = -join [char[]](0x65E5, 0x672C, 0x8A9E)
    $tesuto = -join [char[]](0x3066, 0x3059, 0x3068)
    $valid = if ($FocusCase) {
        $events[-1].Kind -eq 'close' -and $events[-1].Committed -eq '' -and
        @($events | Where-Object { $_.Kind -eq 'editing' -and $_.Preedit -eq $tesuto }).Count -gt 0 -and
        @($events | Where-Object { $_.Kind -eq 'focus-lost' -and $_.Committed -eq '' -and $_.Preedit -eq '' }).Count -gt 0
    } else {
        @($events | Where-Object { $_.Kind -eq 'editing' -and $_.Text.Length -gt 0 }).Count -gt 0 -and
        @($events | Where-Object { $_.Kind -eq 'editing' -and $_.Text -eq '' }).Count -gt 0 -and
        @($events | Where-Object { $_.Kind -eq 'input' -and $_.Text -eq $japanese }).Count -gt 0 -and
        @($events | Where-Object { $_.Kind -eq 'focus-lost' }).Count -gt 0 -and
        @($events | Where-Object { $_.Kind -eq 'focus-gained' }).Count -gt 0 -and
        @($events | Where-Object { $_.Kind -eq 'close' }).Count -gt 0
    }
    if (-not $valid) { throw "IME evidence matrix failed: $Path" }
    [ordered]@{ path = [IO.Path]::GetFullPath($Path); sha256 = Get-Sha256 $Path; valid = $true }
}

function Assert-Hash([string]$Path, [string]$Expected) {
    $actual = Get-Sha256 $Path
    if ($actual -ne $Expected) { throw "Artifact hash mismatch: $Path" }
    [ordered]@{ path = [IO.Path]::GetFullPath($Path); sha256 = $actual; valid = $true }
}

try {
    dotnet restore $solution --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet build $solution --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $automated = & $exe --automated | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $automated.CriticalClaimsPass) { throw 'SDL host lifecycle proof failed.' }
    $text = & $exe --text-self-check | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $text.Ok) { throw 'Published text-state self-check failed.' }
    $uia = & "$PSScriptRoot/run-uia-proof.ps1" -NoPublish | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $uia.host.criticalClaimsPass -or -not $uia.helper.valid) { throw 'UIA proof failed.' }
    $sceneDirectory = Join-Path $work scene
    $scene = & "$PSScriptRoot/run-scene-proof.ps1" -OutputDirectory $sceneDirectory -NoPublish | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $scene.ok) { throw 'Scene proof failed.' }
    $layoutDirectory = Join-Path $work layout
    $layout = & "$PSScriptRoot/run-layout-proof.ps1" -OutputDirectory $layoutDirectory -NoPublish | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $layout.ok) { throw 'Layout proof failed.' }

    $issue3 = [ordered]@{
        artifacts = @(
            (Assert-Hash (Join-Path $PSScriptRoot 'evidence/issue-3-accessibility-insights.png') '6575c79327454191afa72a48738c4880317c8ad865a48afb17f635f40bc93522'),
            (Assert-ImeEvidence (Join-Path $PSScriptRoot 'evidence/issue-3-ime-ja-JP.jsonl') $false),
            (Assert-ImeEvidence (Join-Path $PSScriptRoot 'evidence/issue-3-ime-focus-ja-JP.jsonl') $true))
        automatedTextState = $text
    }
    $issue4 = Assert-ArtifactProof (Join-Path $PSScriptRoot 'evidence/issue-4') @('elements.json', 'headless-input.png', 'layout.json', 'native-framebuffer.png', 'semantics.json', 'style.json')
    $issue5 = Assert-ArtifactProof (Join-Path $PSScriptRoot 'evidence/issue-5') @('layout-text.json', 'layout-text.png')
    Assert-MatchingArtifacts $scene $issue4 'Scene'
    Assert-MatchingArtifacts $layout $issue5 'Layout'
    $scene.scene.PSObject.Properties.Remove('ArtifactDirectory')
    $layout.layout.PSObject.Properties.Remove('ArtifactDirectory')
    $issue4.scene.PSObject.Properties.Remove('ArtifactDirectory')
    $issue5.layout.PSObject.Properties.Remove('ArtifactDirectory')
    $source = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
    $workingTree = Get-WorkingTreeIdentity
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
    $evidence = [ordered]@{
        ok = $true
        decision = [ordered]@{ selectedHost = 'SDL3-CS'; outcome = 'proceed'; directWin32Fallback = 'not-run: SDL proof passed' }
        source = [ordered]@{ commit = $source; workingTree = $workingTree; inputs = Get-InputIdentity; project = 'NativeStackProbe/NativeStackProbe.csproj'; lockFileSha256 = Get-Sha256 (Join-Path $PSScriptRoot 'NativeStackProbe/packages.lock.json') }
        machine = [ordered]@{ os = [Environment]::OSVersion.VersionString; machineName = [Environment]::MachineName; processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString(); framework = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription }
        publish = [ordered]@{ executable = [IO.Path]::GetFullPath($exe); sha256 = Get-Sha256 $exe; nativeAot = $true; trimmed = $true; runtimeIdentifier = 'win-x64' }
        automated = [ordered]@{ host = $automated; textState = $text; uia = $uia; scene = $scene; layout = $layout }
        existingEvidence = [ordered]@{ issue3 = $issue3; issue4 = $issue4; issue5 = $issue5 }
        notes = 'Automated text-state checks execute from the published SDL graph. The validated Japanese IME transcripts are supplemental real SDL/Windows evidence; they are not claimed as automated OS IME delivery.'
    }
    $path = Join-Path $OutputDirectory 'proof.json'
    $evidence | ConvertTo-Json -Depth 12 | Set-Content $path -NoNewline -Encoding UTF8
    $evidence | ConvertTo-Json -Depth 12 -Compress
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
