#requires -Version 7.4
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseSet.psm1') -Force
. (Join-Path $PSScriptRoot 'New-LuiReleaseCatalog.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}

function Copy-Json($Value) {
    ConvertFrom-Json -InputObject (ConvertTo-Json -InputObject $Value -Depth 32 -Compress) -AsHashtable -Depth 32
}

function Expect-Rejected([string] $Name, [scriptblock] $Action, [string] $Pattern) {
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) {
            throw "Fixture '$Name' failed for the wrong reason: $($_.Exception.Message)"
        }
        Write-Output "PASS rejection: $Name"
        return
    }
    throw "Fixture was incorrectly accepted: $Name"
}

$transportNamespace = 'Lucent.ReleaseCatalogTransportFixture' + [Guid]::NewGuid().ToString('N')
$transportSource = @"
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace $transportNamespace
{
    public sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _mode;
        private int _requestCount;

        public List<string> AuthorizationHeaders { get; } = new List<string>();
        public List<string> RequestUris { get; } = new List<string>();
        public StalledStream StalledBody { get; private set; }

        public RecordingHandler(string mode) => _mode = mode;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString() ?? string.Empty);
            RequestUris.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
            var requestNumber = Interlocked.Increment(ref _requestCount);

            if (_mode == "redirect" && requestNumber == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri("https://objects.githubusercontent.com/lucent-fixture.zip?signature=synthetic");
                return Task.FromResult(redirect);
            }
            if (_mode == "redirect" && requestNumber == 2)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(new byte[] { 0x43, 0x41, 0x54 });
                return Task.FromResult(response);
            }
            if (_mode == "stall" && requestNumber == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                StalledBody = new StalledStream();
                response.Content = new StreamContent(StalledBody);
                return Task.FromResult(response);
            }
            if (_mode == "deferred" && requestNumber == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new DeferredStream())
                });
            }

            throw new InvalidOperationException("Unexpected catalog fixture request.");
        }
    }

    // Completes only after the consumer registers a continuation. Calling GetResult
    // on its incomplete ValueTask fails independently of machine or disk speed.
    public sealed class DeferredStream : Stream, IValueTaskSource<int>
    {
        private ManualResetValueTaskSourceCore<int> _pending;
        private Memory<byte> _buffer;
        private bool _started;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_started) return new ValueTask<int>(0);
            _started = true;
            _buffer = buffer;
            return new ValueTask<int>(this, _pending.Version);
        }
        public int GetResult(short token) => _pending.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => _pending.GetStatus(token);
        public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            _pending.OnCompleted(continuation, state, token, flags);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                new byte[] { 0x43, 0x41, 0x54 }.AsMemory().CopyTo(_buffer);
                _pending.SetResult(3);
            });
        }
    }

    public sealed class StalledStream : Stream
    {
        private int _readStarted;
        private int _bytesReturned;
        public bool ReadStarted => Volatile.Read(ref _readStarted) != 0;
        public int BytesReturned => Volatile.Read(ref _bytesReturned);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Interlocked.Exchange(ref _readStarted, 1);
            if (Interlocked.CompareExchange(ref _bytesReturned, 1, 0) == 0)
            {
                if (buffer.Length == 0) { Interlocked.Exchange(ref _bytesReturned, 0); return 0; }
                new byte[] { 0x41 }.AsMemory().CopyTo(buffer);
                return 1;
            }
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }
}
"@
$transportTypes = Add-Type -TypeDefinition $transportSource -PassThru
$transportHandlerType = $transportTypes | Where-Object Name -ceq 'RecordingHandler'

$sourceCommit = 'a' * 40
$repository = [pscustomobject]@{ full_name = 'RichiCoder1/lucent'; id = 184905123 }
$run = [pscustomobject]@{
    id = 36686517408L
    run_attempt = 2
    run_number = 101
    status = 'completed'
    conclusion = 'success'
    event = 'push'
    head_branch = 'main'
    head_sha = $sourceCommit
    workflow_id = 7654321L
    repository = $repository
    head_repository = $repository
}
$workflow = [pscustomobject]@{ id = $run.workflow_id; path = '.github/workflows/tests.yml' }
$identity = Assert-LuiActionsRun $run $workflow $run.id $run.run_attempt
Assert-True ($identity.repository -ceq 'RichiCoder1/lucent' -and $identity.workflow -ceq '.github/workflows/tests.yml') 'Fixed GitHub identity was not retained.'
Assert-True ($identity.runNumber -eq 101 -and $identity.runAttempt -eq 2 -and $identity.headSha -ceq $sourceCommit) 'Authenticated run identity fields were not retained.'
Write-Output 'PASS authenticated run identity shape'

Expect-Rejected 'failed-run' {
    $changed = Copy-Json $run
    $changed.conclusion = 'failure'
    Assert-LuiActionsRun $changed $workflow $run.id $run.run_attempt
} 'successful main-branch'
Expect-Rejected 'unfinished-run' {
    $changed = Copy-Json $run
    $changed.status = 'in_progress'
    Assert-LuiActionsRun $changed $workflow $run.id $run.run_attempt
} 'successful main-branch'
Expect-Rejected 'pull-request-event' {
    $changed = Copy-Json $run
    $changed.event = 'pull_request'
    Assert-LuiActionsRun $changed $workflow $run.id $run.run_attempt
} 'successful main-branch'
Expect-Rejected 'non-main-branch' {
    $changed = Copy-Json $run
    $changed.head_branch = 'feature'
    Assert-LuiActionsRun $changed $workflow $run.id $run.run_attempt
} 'successful main-branch'
Expect-Rejected 'fork-head' {
    $changed = Copy-Json $run
    $changed.head_repository.full_name = 'SomeoneElse/lucent'
    Assert-LuiActionsRun $changed $workflow $run.id $run.run_attempt
} 'successful main-branch'
Expect-Rejected 'wrong-workflow-file' {
    $changedWorkflow = Copy-Json $workflow
    $changedWorkflow.path = '.github/workflows/release.yml'
    Assert-LuiActionsRun $run $changedWorkflow $run.id $run.run_attempt
} 'successful main-branch'
Expect-Rejected 'wrong-attempt' {
    Assert-LuiActionsRun $run $workflow $run.id 1
} 'successful main-branch'

$completeArtifact = [pscustomobject]@{
    id = 811L
    name = "complete-release-$($run.id)-$($run.run_attempt)"
    digest = 'sha256:' + ('c' * 64)
    size_in_bytes = 2048L
    expired = $false
    workflow_run = [pscustomobject]@{
        id = $run.id
        repository_id = $repository.id
        head_repository_id = $repository.id
        head_sha = $sourceCommit
    }
}
$completeMetadata = Assert-LuiActionsArtifact $completeArtifact $run $completeArtifact.name
Assert-True ($completeMetadata.id -eq 811 -and $completeMetadata.digest -ceq $completeArtifact.digest) 'Complete artifact metadata was not retained.'
Assert-True ($completeArtifact.name -ceq "complete-release-$($run.id)-$($run.run_attempt)") 'Complete artifact name did not bind the attempt.'
Write-Output 'PASS authenticated artifact identity shape'

Expect-Rejected 'wrong-artifact-name' {
    $changed = Copy-Json $completeArtifact
    $changed.name = 'complete-release-1-1'
    Assert-LuiActionsArtifact $changed $run $completeArtifact.name
} 'expected unexpired artifact'
Expect-Rejected 'expired-artifact' {
    $changed = Copy-Json $completeArtifact
    $changed.expired = $true
    Assert-LuiActionsArtifact $changed $run $completeArtifact.name
} 'expected unexpired artifact'
Expect-Rejected 'wrong-artifact-run' {
    $changed = Copy-Json $completeArtifact
    $changed.workflow_run.id = 123
    Assert-LuiActionsArtifact $changed $run $completeArtifact.name
} 'expected unexpired artifact'
Expect-Rejected 'artifact-without-github-digest' {
    $changed = Copy-Json $completeArtifact
    $changed.digest = 'b' * 64
    Assert-LuiActionsArtifact $changed $run $completeArtifact.name
} 'expected unexpired artifact'

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('lucent-catalog-fixture-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
try {
    $receiptPath = Join-Path $fixtureRoot 'managed-receipt.json'
    $managedDigest = 'sha256:' + ('d' * 64)
    $packageDigest = 'sha256:' + ('e' * 64)
    $packageArtifact = [ordered]@{ id = 812L; name = "verified-packages-$($run.id)-$($run.run_attempt)"; digest = $packageDigest; bytes = 2048L }
    $managedArtifact = [ordered]@{ id = 813L; name = "verified-managed-$($run.id)-$($run.run_attempt)"; digest = $managedDigest; bytes = 2048L }
    $managedReceipt = [ordered]@{ managedArtifact = [ordered]@{ id = $managedArtifact.id; digest = $managedDigest.Substring(7) } }
    $managedReceipt | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
    $descriptor = [ordered]@{
        status = 'complete'
        releaseSet = [ordered]@{ version = '0.3.0-dev.101.1'; sourceState = 'clean'; sourceCommit = $sourceCommit }
        provenance = [ordered]@{
            repository = 'RichiCoder1/lucent'
            workflow = '.github/workflows/tests.yml'
            sourceCommit = $sourceCommit
            runId = $run.id
            runAttempt = $run.run_attempt
            artifactId = $packageArtifact.id
            artifactDigest = $packageDigest.Substring(7)
        }
        evidence = @([ordered]@{ kind = 'managed'; artifact = [ordered]@{ fileName = 'managed-receipt.json' } })
        packages = @([ordered]@{
            id = 'Lucent.Lui.Sdk'
            version = '0.3.0-dev.101.1'
            repositoryCommit = $sourceCommit
            artifact = [ordered]@{ sha256 = 'f' * 64 }
        })
    }
    $sdkPackage = Assert-LuiCompleteDescriptor $descriptor $identity $packageArtifact $managedArtifact $fixtureRoot
    Assert-True ($sdkPackage.id -ceq 'Lucent.Lui.Sdk' -and $sdkPackage.artifact.sha256 -ceq ('f' * 64)) 'Verified SDK package identity was not retained.'
    Assert-True ($descriptor.releaseSet.version -ceq "0.3.0-dev.$($run.run_number).1" -and $identity.runAttempt -eq 2) 'Run attempt and release package version were conflated.'
    Write-Output 'PASS complete descriptor provenance correspondence'

    Expect-Rejected 'candidate-descriptor' {
        $changed = Copy-Json $descriptor
        $changed.status = 'candidate'
        Assert-LuiCompleteDescriptor $changed $identity $packageArtifact $managedArtifact $fixtureRoot
    } 'Only a complete release descriptor'
    Expect-Rejected 'dirty-source-descriptor' {
        $changed = Copy-Json $descriptor
        $changed.releaseSet.sourceState = 'dirty-development'
        Assert-LuiCompleteDescriptor $changed $identity $packageArtifact $managedArtifact $fixtureRoot
    } 'clean source tree'
    Expect-Rejected 'wrong-head-descriptor' {
        $changed = Copy-Json $descriptor
        $changed.releaseSet.sourceCommit = 'b' * 40
        Assert-LuiCompleteDescriptor $changed $identity $packageArtifact $managedArtifact $fixtureRoot
    } 'clean source tree'
    Expect-Rejected 'wrong-package-artifact-provenance' {
        $changed = Copy-Json $descriptor
        $changed.provenance.artifactId++
        Assert-LuiCompleteDescriptor $changed $identity $packageArtifact $managedArtifact $fixtureRoot
    } 'provenance differs'
    Expect-Rejected 'wrong-run-attempt-provenance' {
        $changed = Copy-Json $descriptor
        $changed.provenance.runAttempt--
        Assert-LuiCompleteDescriptor $changed $identity $packageArtifact $managedArtifact $fixtureRoot
    } 'provenance differs'
    Expect-Rejected 'wrong-workflow-provenance' {
        $changed = Copy-Json $descriptor
        $changed.provenance.workflow = '.github/workflows/other.yml'
        Assert-LuiCompleteDescriptor $changed $identity $packageArtifact $managedArtifact $fixtureRoot
    } 'provenance differs'
    Expect-Rejected 'wrong-managed-artifact-receipt' {
        $changed = Copy-Json $descriptor
        $changedReceipt = [ordered]@{ managedArtifact = [ordered]@{ id = $managedArtifact.id; digest = '0' * 64 } }
        $changedReceipt | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
        Assert-LuiCompleteDescriptor $changed $identity $packageArtifact $managedArtifact $fixtureRoot
    } 'Managed verification receipt differs'

    $redirectPath = Join-Path $fixtureRoot 'redirected-artifact.zip'
    $redirectHandler = [Activator]::CreateInstance($transportHandlerType, @('redirect'))
    Save-LuiActionsArtifact 811 'fixture-token' $redirectPath $redirectHandler ([TimeSpan]::FromSeconds(5))
    Assert-True ($redirectHandler.AuthorizationHeaders.Count -eq 2) 'Redirect transport did not issue exactly two requests.'
    Assert-True ($redirectHandler.AuthorizationHeaders[0] -ceq 'Bearer fixture-token') 'Initial artifact request omitted its bearer token.'
    Assert-True ([string]::IsNullOrEmpty($redirectHandler.AuthorizationHeaders[1])) 'Redirected artifact request retained the bearer token.'
    Assert-True ($redirectHandler.RequestUris[1].StartsWith('https://objects.githubusercontent.com/', [StringComparison]::Ordinal)) 'Artifact request did not follow the expected HTTPS redirect.'
    $redirectBytes = [IO.File]::ReadAllBytes($redirectPath)
    Assert-True ($redirectBytes.Length -eq 3 -and $redirectBytes[0] -eq 0x43 -and $redirectBytes[1] -eq 0x41 -and $redirectBytes[2] -eq 0x54) 'Redirect transport wrote unexpected artifact bytes.'
    Write-Output 'PASS redirect strips bearer authorization'

    $deferredPath = Join-Path $fixtureRoot 'deferred-artifact.zip'
    $deferredHandler = [Activator]::CreateInstance($transportHandlerType, @('deferred'))
    Save-LuiActionsArtifact 811 'fixture-token' $deferredPath $deferredHandler ([TimeSpan]::FromSeconds(5))
    $deferredBytes = [IO.File]::ReadAllBytes($deferredPath)
    Assert-True ($deferredBytes.Length -eq 3 -and $deferredBytes[0] -eq 0x43 -and $deferredBytes[1] -eq 0x41 -and $deferredBytes[2] -eq 0x54) 'Deferred ValueTask transfer wrote unexpected bytes.'
    Write-Output 'PASS incomplete ValueTask source is awaited before consuming its result'

    $existingPath = Join-Path $fixtureRoot 'existing-artifact.zip'
    $existingBytes = [byte[]]@(0x50, 0x52, 0x45)
    [IO.File]::WriteAllBytes($existingPath, $existingBytes)
    $collisionHandler = [Activator]::CreateInstance($transportHandlerType, @('redirect'))
    Expect-Rejected 'destination-collision-preserves-existing-file' {
        Save-LuiActionsArtifact 811 'fixture-token' $existingPath $collisionHandler ([TimeSpan]::FromSeconds(5))
    } 'Authenticated GitHub artifact download failed'
    $preservedBytes = [IO.File]::ReadAllBytes($existingPath)
    Assert-True ($preservedBytes.Length -eq 3 -and $preservedBytes[0] -eq 0x50 -and $preservedBytes[1] -eq 0x52 -and $preservedBytes[2] -eq 0x45) 'Destination collision damaged the existing file.'
    Write-Output 'PASS destination collision preserves existing file'

    $stalledPath = Join-Path $fixtureRoot 'stalled-artifact.zip'
    $stalledHandler = [Activator]::CreateInstance($transportHandlerType, @('stall'))
    Expect-Rejected 'stalled-body-deadline-cancels-and-cleans-partial-file' {
        Save-LuiActionsArtifact 811 'fixture-token' $stalledPath $stalledHandler ([TimeSpan]::FromMilliseconds(500))
    } 'total transfer deadline'
    Assert-True ($stalledHandler.StalledBody.ReadStarted) 'Stalled transport did not reach the response body read.'
    Assert-True ($stalledHandler.StalledBody.BytesReturned -eq 1) 'Stalled transport did not return a byte before blocking.'
    Assert-True (!(Test-Path -LiteralPath $stalledPath)) 'Timed-out artifact download left a partial file.'
    Write-Output 'PASS stalled body deadline cancels and removes a partial artifact after one byte'
}
finally {
    if ([IO.Directory]::Exists($fixtureRoot)) { [IO.Directory]::Delete($fixtureRoot, $true) }
}

Write-Output 'Release catalog fixtures passed. Synthetic metadata above tests field matching only; it is not publisher evidence.'
