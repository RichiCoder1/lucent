param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-14", [switch]$NoPublish)

$ErrorActionPreference = 'Stop'
$project = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$solution = "$PSScriptRoot/NativeStack.sln"
$exe = "$PSScriptRoot/NativeStackProbe/bin/Release/net9.0-windows/win-x64/publish/NativeStackProbe.exe"
$native = Join-Path $OutputDirectory 'native'

if (-not $NoPublish) {
    dotnet restore $solution --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet build $solution --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Remove-Item -Recurse -Force $native -ErrorAction SilentlyContinue
Remove-Item -Force (Join-Path $OutputDirectory 'proof.json') -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $native | Out-Null
$contract = & $exe --issue-14-provider-contract $native | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $contract.Ok -or $contract.EmergencySuppressions -ne 0) { throw 'Issue #14 provider contract failed.' }
$walkthrough = @(& $exe --issue-browser-walkthrough $native | ForEach-Object { $_ | ConvertFrom-Json })
if ($LASTEXITCODE -ne 0 -or $walkthrough.Count -ne 12 -or @($walkthrough | Where-Object { -not $_.pass }).Count -ne 0) { throw 'Issue-browser keyboard walkthrough failed.' }
$semantics = Get-Content (Join-Path $native 'semantics.json') -Raw | ConvertFrom-Json
if (@($semantics | Where-Object { $_.suppressionReason -ne $null }).Count -ne 0) { throw 'Final semantic dump contains emergency suppressions.' }
$controls = & $exe --controls-self-check | ConvertFrom-Json
$virtualization = & $exe --virtualization-self-check | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $controls.Ok -or -not $virtualization.Ok) { throw 'Controls or virtualization regression failed.' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('native-stack-issue14-uia-' + [Guid]::NewGuid())
New-Item -ItemType Directory $work | Out-Null
$externalSuccess = $false
try {
    $ready=Join-Path $work ready.json; $close=Join-Path $work close.signal; $result=Join-Path $work result.json
    $uiaHost=Start-Process -FilePath $exe -ArgumentList @('--issue-browser-uia-host',$ready,$close) -PassThru
    $until=[DateTime]::UtcNow.AddSeconds(10); while(-not (Test-Path $ready) -and [DateTime]::UtcNow -lt $until){Start-Sleep -Milliseconds 50}; if(-not (Test-Path $ready)){throw 'Issue #14 UIA host did not become ready.'}
    & dotnet "$PSScriptRoot/UiaExternalHelper/bin/Debug/net9.0-windows/UiaExternalHelper.dll" $ready $close $result
    if($LASTEXITCODE -ne 0 -or -not (Test-Path $result)){throw 'Issue #14 external UIA helper failed.'}
    $external=Get-Content $result -Raw | ConvertFrom-Json; if(-not $external.Valid){throw 'Issue #14 external UIA assertions failed.'}
    if(-not $uiaHost.WaitForExit(10000)){throw 'Issue #14 UIA host did not exit.'}
    $hostResult=Get-Content ($ready + '.host.json') -Raw | ConvertFrom-Json
    if(-not $hostResult.rootAbi -or $hostResult.rootPointCalls -lt 1 -or $hostResult.rootFocusCalls -lt 1 -or $hostResult.cacheCount -gt $semantics.Count -or $hostResult.maxCacheCount -gt $semantics.Count -or $hostResult.staleDisconnected -lt 1 -or $hostResult.focusEvents -lt 1 -or $hostResult.propertyEvents -lt 1 -or $hostResult.structureEvents -lt 1){throw 'Issue #14 provider ABI/cache/event assertions failed.'}
    $externalSuccess = $true
} finally { if($uiaHost -and -not $uiaHost.HasExited){$uiaHost.Kill()}; if($externalSuccess){Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue}else{Write-Host "Issue #14 UIA evidence retained: $work"} }
$visibleWork = Join-Path ([IO.Path]::GetTempPath()) ('native-stack-issue14-visible-' + [Guid]::NewGuid())
New-Item -ItemType Directory $visibleWork | Out-Null
$visibleSuccess = $false
try {
    $visibleReady=Join-Path $visibleWork ready.json; $visibleClose=Join-Path $visibleWork close.signal; $visibleResult=Join-Path $visibleWork result.json
    $visibleHost=Start-Process -FilePath $exe -ArgumentList @('--issue-browser-visible-uia-host',$visibleReady,$visibleClose) -PassThru
    $until=[DateTime]::UtcNow.AddSeconds(10); while(-not (Test-Path $visibleReady) -and [DateTime]::UtcNow -lt $until){Start-Sleep -Milliseconds 50}; if(-not (Test-Path $visibleReady)){throw 'Visible issue-browser UIA host did not become ready.'}
    & dotnet "$PSScriptRoot/UiaExternalHelper/bin/Debug/net9.0-windows/UiaExternalHelper.dll" $visibleReady $visibleClose $visibleResult
    if($LASTEXITCODE -ne 0 -or -not (Test-Path $visibleResult)){throw 'Visible issue-browser external UIA helper failed.'}
    $visibleExternal=Get-Content $visibleResult -Raw | ConvertFrom-Json
    if(-not $visibleExternal.Valid -or -not $visibleHost.WaitForExit(10000)){throw 'Visible issue-browser provider seam failed.'}
    $visibleHostResult=Get-Content ($visibleReady + '.host.json') -Raw | ConvertFrom-Json
    if(-not $visibleHostResult.visible -or -not $visibleHostResult.rootAbi -or $visibleHostResult.rootPointCalls -lt 1){throw 'Visible issue-browser host did not exercise FragmentRoot hit testing.'}
    $visibleSuccess = $true
} finally { if($visibleHost -and -not $visibleHost.HasExited){$visibleHost.Kill()}; if($visibleSuccess){Remove-Item -Recurse -Force $visibleWork -ErrorAction SilentlyContinue}else{Write-Host "Issue #14 visible evidence retained: $visibleWork"} }
$uia = & "$PSScriptRoot/run-uia-proof.ps1" -NoPublish | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $uia.host.criticalClaimsPass -or -not $uia.helper.valid) { throw 'Issue #3 UIA transport regression failed.' }
git -C "$PSScriptRoot/../.." diff --check
if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed.' }
$staged = @(git -C "$PSScriptRoot/../.." diff --cached --name-only)
if ($LASTEXITCODE -ne 0 -or $staged.Count -ne 0) { throw 'Issue #14 proof requires no staged files.' }
$proof = [ordered]@{
    ok = $true
    issue = 14
    nativeAot = $true
    providerContract = $contract
    walkthrough = [ordered]@{ passed = $walkthrough.Count; steps = @($walkthrough | ForEach-Object { $_.step }) }
    finalSemanticDump = [ordered]@{ entries = $semantics.Count; emergencySuppressions = 0 }
    regressions = [ordered]@{ issue3Uia = $uia.host.criticalClaimsPass; controls = $controls.Ok; virtualization = $virtualization.Ok }
    externalChildUia = $external
    providerRuntime = $hostResult
    visibleProviderMode = [ordered]@{ host = $visibleHostResult; external = $visibleExternal }
    manualEvidence = if (Test-Path (Join-Path $OutputDirectory 'manual-checklist.md')) {
        $manual = Get-Content (Join-Path $OutputDirectory 'manual-checklist.md') -Raw
        if ($manual -match 'manual UIA/Narrator gate — passed') { 'passed; see manual-checklist.md' } else { 'pending; see manual-checklist.md' }
    } else { 'pending; manual checklist missing' }
    noStagedFiles = $true
}
$proof | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'proof.json') -NoNewline -Encoding UTF8
$proof | ConvertTo-Json -Depth 8 -Compress
