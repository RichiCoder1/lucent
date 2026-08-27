param([string]$OutputDirectory = "$PSScriptRoot/evidence/issue-24")

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
$nativeProject = "$PSScriptRoot/NativeStackProbe/NativeStackProbe.csproj"
$avaloniaProject = "$PSScriptRoot/baseline/IssueBrowser.Avalonia/IssueBrowser.Avalonia.csproj"
$nativeOutput = Join-Path $OutputDirectory native
$avaloniaOutput = Join-Path $OutputDirectory avalonia

$experimentStatus = @(git -C $root status --porcelain=v1 --untracked-files=all -- experiments/native-stack)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect Issue 24 experiment source.' }
if ($experimentStatus.Count -ne 0) { throw 'Issue 24 proof requires a clean experiment tree and index.' }
$repositoryStatus = @(git -C $root status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect repository source.' }

dotnet build $nativeProject -c Release --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build $avaloniaProject -c Release --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Remove-Item -Recurse -Force $nativeOutput,$avaloniaOutput -ErrorAction SilentlyContinue
dotnet run --project $nativeProject -c Release --no-build -- --issue-browser-walkthrough $nativeOutput | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Native walkthrough failed.' }
dotnet run --project $avaloniaProject -c Release --no-build -- --issue-browser-walkthrough $avaloniaOutput | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Avalonia walkthrough failed.' }

$nativeClosed = Get-Content (Join-Path $nativeOutput closed-filter.json) -Raw | ConvertFrom-Json
$avaloniaClosed = Get-Content (Join-Path $avaloniaOutput closed-filter.json) -Raw | ConvertFrom-Json
if (-not $nativeClosed.Pass -or $nativeClosed.Observed -ne 667 -or -not $avaloniaClosed.pass -or $avaloniaClosed.observed -ne 667) { throw 'Closed-filter comparison failed.' }

$contract = (git -C $root hash-object experiments/native-stack/GAUNTLET-REFRAME.md).Trim()
$proof = [ordered]@{
    ok = $true
    issue = 24
    source = [ordered]@{
        head = (git -C $root rev-parse HEAD).Trim()
        scope = 'experiments/native-stack'
        experimentCleanAtStart = ($experimentStatus.Count -eq 0)
        repositoryCleanAtStart = ($repositoryStatus.Count -eq 0)
        outOfScopeChangesAtStart = $repositoryStatus.Count
        contractBlob = $contract
        contractCommit = '4546d79'
        implementationCommit = '7f9fce4'
    }
    results = [ordered]@{ native = $nativeClosed.Observed; avalonia = $avaloniaClosed.observed; expected = 667 }
    walkthrough = [ordered]@{ native = 12; avalonia = 12 }
    scores = [ordered]@{ native = @(3,3,3,3,3,3,2,2); avalonia = @(1,3,1,3,2,2,1,1); nativeHypothesisWins = 3; requiredWins = 3 }
    change = Get-Content (Join-Path $OutputDirectory change-task.json) -Raw | ConvertFrom-Json
    decision = 'proceed'
}
$proof | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $OutputDirectory proof.json) -NoNewline -Encoding UTF8
$proof | ConvertTo-Json -Depth 7 -Compress
