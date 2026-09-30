"use strict";

const assert = require("node:assert/strict");
const crypto = require("node:crypto");
const { EventEmitter } = require("node:events");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { PassThrough } = require("node:stream");
const { test } = require("node:test");
const { verifyHelperFiles, selectApprovedEntry, resolveCachedServer, importApprovedArchive, importDownloadedRelease } = require("./server-cache");

const sourceCommit = "a".repeat(40);
const digest = bytes => crypto.createHash("sha256").update(bytes).digest("hex");

function fixture(t) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "lucent-cache-adapter-"));
    t.after(() => fs.rmSync(root, { recursive: true, force: true }));
    const helperDirectory = path.join(root, "helper");
    fs.mkdirSync(helperDirectory);
    const files = ["Lucent.Tooling.Cache.dll", "Lucent.Tooling.Cache.deps.json", "Lucent.Tooling.Cache.runtimeconfig.json"]
        .map(fileName => {
            const bytes = Buffer.from(`approved ${fileName}`);
            fs.writeFileSync(path.join(helperDirectory, fileName), bytes);
            return { fileName, bytes: bytes.length, sha256: digest(bytes) };
        });
    const helperManifest = { schemaVersion: 1, sourceCommit, entryPoint: "Lucent.Tooling.Cache.dll", files };
    const archive = Buffer.from("known server ZIP bytes");
    const archivePath = path.join(root, "server.zip");
    fs.writeFileSync(archivePath, archive);
    const identity = {
        sourceCommit,
        compiler: { sha256: "b".repeat(64), informationalVersion: `0.2.0+${sourceCommit}` },
        language: { id: "lui", version: "preview", featureLevel: "preview-1" },
        protocol: { id: "lucent-lui", major: 1, minor: 0 }
    };
    const entry = {
        releaseVersion: "0.2.0",
        sdk: { id: "Lucent.Lui.Sdk", version: "0.2.0", packageSha256: "e".repeat(64) },
        server: { artifact: { bytes: archive.length, sha256: digest(archive) }, filesSha256: "c".repeat(64), identity },
        anchor: { kind: "bundled-catalog", sourceCommit, descriptorSha256: "d".repeat(64),
            githubActions: { repository: "RichiCoder1/lucent", workflow: ".github/workflows/tests.yml",
                runId: 123, runAttempt: 1, headSha: sourceCommit,
                artifact: { id: 456, name: "complete-release-123-1", digest: `sha256:${"f".repeat(64)}` } } }
    };
    const requirement = {
        schemaVersion: 1, kind: "project-requirements", state: "package", semanticReady: false,
        compiler: identity.compiler && { ...identity.compiler, sourceCommit },
        projects: [{ state: "package", sdk: { id: "Lucent.Lui.Sdk", version: "0.2.0", repositoryCommit: sourceCommit, packageSha256: "e".repeat(64) } }]
    };
    const clientRelease = {
        language: identity.language,
        protocol: { id: "lucent-lui", minimum: { major: 1, minor: 0 }, maximumInclusive: { major: 1, minor: 0 } }
    };
    const cacheRoot = path.join(root, "cache");
    const serverPath = path.join(cacheRoot, "generations", entry.server.artifact.sha256, "server", "Lucent.Lui.LanguageServer.dll");
    const options = { requirement, approvedEntries: [entry], clientRelease, cacheRoot, helperDirectory,
        helperManifest, dotnetPath: process.execPath, sourceCommit, archivePath };
    return { root, options, entry, serverPath };
}

function respondingSpawn(onRequest) {
    return (_program, _args, _options) => {
        const child = new EventEmitter();
        child.stdin = new PassThrough();
        child.stdout = new PassThrough();
        child.stderr = new PassThrough();
        child.kill = () => { child.emit("exit", 1); child.emit("close", 1); };
        let input = "";
        child.stdin.on("data", chunk => {
            input += chunk.toString();
            for (let end; (end = input.indexOf("\n")) >= 0;) {
                const line = input.slice(0, end);
                input = input.slice(end + 1);
                onRequest(child, line);
            }
        });
        return child;
    };
}

test("helper inventory is exact before any process can be spawned", async t => {
    const { options } = fixture(t);
    await verifyHelperFiles(options.helperDirectory, options.helperManifest, sourceCommit);
    fs.writeFileSync(path.join(options.helperDirectory, "undeclared.dll"), "bad");
    await assert.rejects(verifyHelperFiles(options.helperDirectory, options.helperManifest, sourceCommit), /undeclared/);
    fs.rmSync(path.join(options.helperDirectory, "undeclared.dll"));
    fs.writeFileSync(path.join(options.helperDirectory, "Lucent.Tooling.Cache.dll"), "changed");
    await assert.rejects(verifyHelperFiles(options.helperDirectory, options.helperManifest, sourceCommit), /size differs/);
});

test("selection requires one exact evaluated compiler and authenticated anchor", t => {
    const { options, entry } = fixture(t);
    assert.equal(selectApprovedEntry(options.requirement, [], options.clientRelease).status, "no-anchor");
    assert.equal(selectApprovedEntry(options.requirement, [entry], options.clientRelease).status, "approved");
    assert.equal(selectApprovedEntry(options.requirement, [entry, entry], options.clientRelease).status, "incompatible");
    const differentCompiler = structuredClone(options.requirement);
    differentCompiler.compiler.sha256 = "f".repeat(64);
    assert.equal(selectApprovedEntry(differentCompiler, [entry], options.clientRelease).status, "incompatible");
    const differentSdk = structuredClone(options.requirement);
    differentSdk.projects[0].sdk.packageSha256 = "f".repeat(64);
    assert.equal(selectApprovedEntry(differentSdk, [entry], options.clientRelease).status, "incompatible");
    const wrongArtifact = structuredClone(entry);
    wrongArtifact.anchor.githubActions.artifact.name = "unrelated artifact";
    assert.equal(selectApprovedEntry(options.requirement, [wrongArtifact], options.clientRelease).status, "incompatible");
    const development = structuredClone(options.requirement);
    development.state = "development-source";
    assert.equal(selectApprovedEntry(development, [entry], options.clientRelease).status, "incompatible");
});

test("offline import rejects unknown bytes without launching helper", async t => {
    const { options } = fixture(t);
    fs.writeFileSync(options.archivePath, "different server archive");
    let calls = 0;
    const result = await importApprovedArchive({ ...options, spawn: () => { calls++; throw new Error("unexpected spawn"); } });
    assert.equal(result.status, "no-anchor");
    assert.equal(calls, 0);
});

test("known offline archive installs through the verified helper contract", async t => {
    const { options, entry, serverPath } = fixture(t);
    let calls = 0;
    const spawn = respondingSpawn((child, line) => {
        const request = JSON.parse(line);
        calls++;
        assert.equal(request.operation, "install");
        assert.equal(request.archivePath, options.archivePath);
        assert.deepEqual(request.expected, entry.server);
        fs.mkdirSync(path.dirname(serverPath), { recursive: true });
        fs.writeFileSync(serverPath, "published server bytes");
        queueMicrotask(() => {
            child.stdout.write(JSON.stringify({ schemaVersion: 1, status: "verified", archiveSha256: entry.server.artifact.sha256,
                filesSha256: entry.server.filesSha256, identity: entry.server.identity,
                generationPath: path.dirname(path.dirname(serverPath)), serverPath }));
            child.emit("exit", 0);
            child.emit("close", 0);
        });
    });
    const result = await importApprovedArchive({ ...options, spawn });
    assert.equal(result.status, "selected");
    assert.equal(result.serverPath, serverPath);
    assert.equal(calls, 1);
});

test("downloaded complete release is bound to the selected catalog entry and helper request", async t => {
    const { options, entry, serverPath } = fixture(t);
    const downloaded = { status: "downloaded", entry, completeArchivePath: options.archivePath,
        artifactBytes: fs.statSync(options.archivePath).size };
    const spawn = respondingSpawn((child, line) => {
        const request = JSON.parse(line);
        assert.equal(request.operation, "install-release");
        assert.equal(request.completeRelease.artifactDigest, entry.anchor.githubActions.artifact.digest);
        assert.equal(request.completeRelease.sdkPackageSha256, entry.sdk.packageSha256);
        assert.equal(request.completeRelease.archivePath, options.archivePath);
        assert.equal(request.anchor.runId, entry.anchor.githubActions.runId);
        fs.mkdirSync(path.dirname(serverPath), { recursive: true });
        fs.writeFileSync(serverPath, "published server bytes");
        queueMicrotask(() => {
            child.stdout.write(JSON.stringify({ schemaVersion: 1, status: "verified", archiveSha256: entry.server.artifact.sha256,
                filesSha256: entry.server.filesSha256, identity: entry.server.identity,
                generationPath: path.dirname(path.dirname(serverPath)), serverPath }));
            child.emit("exit", 0);
            child.emit("close", 0);
        });
    });
    const result = await importDownloadedRelease({ ...options, downloaded, spawn });
    assert.equal(result.status, "selected");
    assert.equal(result.serverPath, serverPath);
    const forged = structuredClone(downloaded);
    forged.entry.server.artifact.sha256 = "0".repeat(64);
    assert.equal((await importDownloadedRelease({ ...options, downloaded: forged,
        spawn: () => { throw new Error("forged entry must not launch helper"); } })).status, "incompatible");
});

test("concurrent cache verification accepts only exact helper results", async t => {
    const { options, entry, serverPath } = fixture(t);
    fs.mkdirSync(path.dirname(serverPath), { recursive: true });
    fs.writeFileSync(serverPath, "published server bytes");
    let calls = 0;
    const spawn = respondingSpawn((child, line) => {
        const request = JSON.parse(line);
        calls++;
        assert.equal(request.operation, "verify");
        queueMicrotask(() => {
            const reply = JSON.stringify({ schemaVersion: 1, status: "verified", archiveSha256: entry.server.artifact.sha256,
                filesSha256: entry.server.filesSha256, identity: entry.server.identity,
                generationPath: path.dirname(path.dirname(serverPath)), serverPath });
            child.stdout.write(reply.slice(0, 12));
            child.emit("exit", 0);
            child.stdout.write(reply.slice(12));
            child.emit("close", 0);
        });
    });
    const results = await Promise.all([
        resolveCachedServer({ ...options, spawn }),
        resolveCachedServer({ ...options, spawn })
    ]);
    assert.deepEqual(results.map(result => result.status), ["selected", "selected"]);
    assert.equal(calls, 2);
});

test("cancellation sends control line and suppresses a late verified result", async t => {
    const { options, entry, serverPath } = fixture(t);
    const cancellation = new AbortController();
    const lines = [];
    let requestArrived;
    const received = new Promise(resolve => { requestArrived = resolve; });
    const spawn = respondingSpawn((child, line) => {
        lines.push(line);
        if (lines.length === 1) requestArrived();
        if (line === "cancel") {
            child.stdout.write(JSON.stringify({ schemaVersion: 1, status: "verified", archiveSha256: entry.server.artifact.sha256,
                filesSha256: entry.server.filesSha256, identity: entry.server.identity,
                generationPath: path.dirname(path.dirname(serverPath)), serverPath }));
            child.emit("exit", 0);
            child.emit("close", 0);
        }
    });
    const operation = resolveCachedServer({ ...options, signal: cancellation.signal, spawn });
    await received;
    cancellation.abort();
    assert.equal((await operation).status, "cancelled");
    assert.equal(lines.length, 2);
    assert.equal(lines[1], "cancel");
});
