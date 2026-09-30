"use strict";

const childProcess = require("node:child_process");
const { verifyManagedToolFiles, hashFile } = require("./managed-tool");
const fs = require("node:fs");
const path = require("node:path");
const { isDeepStrictEqual } = require("node:util");

const HASH = /^[0-9a-f]{64}$/;
const ACTIONS_DIGEST = /^sha256:[0-9a-f]{64}$/;
const COMMIT = /^[0-9a-f]{40}$/;
const MAX_ARCHIVE_BYTES = 512 * 1024 * 1024;
const MAX_HELPER_OUTPUT = 128 * 1024;

function samePath(left, right) {
    const a = path.resolve(left);
    const b = path.resolve(right);
    return process.platform === "win32" ? a.toLowerCase() === b.toLowerCase() : a === b;
}

function verifyHelperFiles(helperDirectory, manifest, sourceCommit, signal) {
    return verifyManagedToolFiles(helperDirectory, manifest, sourceCommit, "Lucent.Tooling.Cache", signal);
}
function exactRequirement(requirement) {
    if (requirement?.schemaVersion !== 1 || requirement.kind !== "project-requirements"
        || requirement.state !== "package" || requirement.semanticReady !== false
        || !HASH.test(requirement.compiler?.sha256) || !COMMIT.test(requirement.compiler?.sourceCommit)
        || typeof requirement.compiler?.informationalVersion !== "string"
        || !Array.isArray(requirement.projects) || !requirement.projects.length) return null;
    const versions = new Set(requirement.projects.map(project => project?.sdk?.version));
    if (versions.size !== 1 || [...versions][0] == null || requirement.projects.some(project =>
        project.state !== "package" || project.sdk?.id !== "Lucent.Lui.Sdk"
        || project.sdk?.repositoryCommit !== requirement.compiler.sourceCommit
        || !HASH.test(project.sdk?.packageSha256))) return null;
    return [...versions][0];
}

function selectApprovedEntry(requirement, approvedEntries, clientRelease) {
    const version = exactRequirement(requirement);
    if (!version) return { status: "incompatible", reason: "The evaluated projects do not name one matching package release." };
    if (!Array.isArray(approvedEntries) || !clientRelease?.language || !clientRelease.protocol) {
        throw new Error("Approved release entries or client compatibility are unavailable.");
    }
    const minimum = clientRelease.protocol.minimum;
    const maximum = clientRelease.protocol.maximumInclusive;
    if (!Number.isInteger(minimum?.major) || minimum.major < 0
        || !Number.isInteger(minimum.minor) || minimum.minor < 0
        || maximum?.major !== minimum.major
        || !Number.isInteger(maximum.minor) || maximum.minor < minimum.minor) {
        throw new Error("Client protocol compatibility is invalid.");
    }
    const releaseEntries = approvedEntries.filter(entry => entry.releaseVersion === version);
    if (!releaseEntries.length) return { status: "no-anchor", version };
    const matches = releaseEntries.filter(entry => {
        const identity = entry.server?.identity;
        const actions = entry.anchor?.githubActions;
        return entry.sdk?.id === "Lucent.Lui.Sdk"
            && entry.sdk.version === version
            && HASH.test(entry.sdk.packageSha256)
            && requirement.projects.every(project => project.sdk.packageSha256 === entry.sdk.packageSha256)
            && identity?.sourceCommit === requirement.compiler.sourceCommit
            && identity.compiler?.sha256 === requirement.compiler.sha256
            && identity.compiler?.informationalVersion === requirement.compiler.informationalVersion
            && identity.language?.id === clientRelease.language?.id
            && identity.language?.version === clientRelease.language?.version
            && identity.language?.featureLevel === clientRelease.language?.featureLevel
            && identity.protocol?.id === clientRelease.protocol?.id
            && identity.protocol.major === minimum.major
            && Number.isInteger(identity.protocol.minor)
            && identity.protocol.minor >= minimum.minor
            && identity.protocol.minor <= maximum.minor
            && HASH.test(entry.server?.artifact?.sha256) && HASH.test(entry.server?.filesSha256)
            && Number.isSafeInteger(entry.server.artifact.bytes) && entry.server.artifact.bytes > 0
            && entry.server.artifact.bytes <= MAX_ARCHIVE_BYTES
            && HASH.test(entry.anchor?.descriptorSha256)
            && entry.anchor.sourceCommit === identity.sourceCommit
            && ["bundled-catalog", "github-actions-receipt"].includes(entry.anchor.kind)
            && actions?.repository === "RichiCoder1/lucent"
            && actions.workflow === ".github/workflows/tests.yml"
            && actions.headSha === identity.sourceCommit
            && Number.isSafeInteger(actions.runId) && actions.runId > 0
            && Number.isSafeInteger(actions.runAttempt) && actions.runAttempt > 0
            && Number.isSafeInteger(actions.artifact?.id) && actions.artifact.id > 0
            && actions.artifact.name === `complete-release-${actions.runId}-${actions.runAttempt}`
            && ACTIONS_DIGEST.test(actions.artifact.digest);
    });
    if (matches.length !== 1) return { status: "incompatible", version, reason: "No unique approved server matches the evaluated compiler and client." };
    return { status: "approved", version, entry: matches[0] };
}

function abortError() { return Object.assign(new Error("Cache operation was canceled."), { name: "AbortError" }); }

function runHelper(request, { helperPath, dotnetPath, signal, spawn = childProcess.spawn }) {
    return new Promise((resolve, reject) => {
        if (signal?.aborted) { reject(abortError()); return; }
        const child = spawn(dotnetPath, [helperPath], {
            cwd: path.dirname(helperPath), stdio: ["pipe", "pipe", "pipe"], windowsHide: true
        });
        let output = "";
        let errors = "";
        let outputBytes = 0;
        let errorBytes = 0;
        let settled = false;
        let cancelTimer;
        const deadline = setTimeout(() => { timedOut = true; cancel(); }, 5 * 60 * 1000);
        let timedOut = false;
        const finish = (error, result) => {
            if (settled) return;
            settled = true;
            clearTimeout(deadline);
            clearTimeout(cancelTimer);
            signal?.removeEventListener("abort", cancel);
            error ? reject(error) : resolve(result);
        };
        const cancel = () => {
            if (settled || cancelTimer) return;
            cancelTimer = setTimeout(() => { child.kill(); finish(abortError()); }, 2000);
            try { child.stdin.write("cancel\n"); } catch { /* Exit/error handler owns a closed pipe. */ }
        };
        child.stdin.on("error", () => {});
        child.stdout.on("data", chunk => {
            outputBytes += chunk.length;
            if (outputBytes > MAX_HELPER_OUTPUT) { child.kill(); finish(new Error("Cache helper output exceeds its limit.")); return; }
            output += chunk.toString("utf8");
        });
        child.stderr.on("data", chunk => {
            errorBytes += chunk.length;
            if (errorBytes > MAX_HELPER_OUTPUT) { child.kill(); finish(new Error("Cache helper error output exceeds its limit.")); return; }
            errors += chunk.toString("utf8");
        });
        child.once("error", error => finish(error));
        // stdout may still deliver buffered bytes after exit; close follows stdio closure.
        child.once("close", code => {
            if (signal?.aborted) { finish(abortError()); return; }
            if (timedOut) { finish(new Error("Cache helper timed out.")); return; }
            let result;
            try { result = JSON.parse(output); }
            catch { finish(new Error("Cache helper returned no supported result.")); return; }
            if (code !== 0 || result?.status === "error") {
                const error = new Error(`Cache helper rejected the server: ${result?.code ?? "failed"}`);
                error.code = result?.code ?? "helper_failed";
                finish(error);
            } else finish(null, result);
        });
        signal?.addEventListener("abort", cancel, { once: true });
        try { child.stdin.write(JSON.stringify(request) + "\n"); }
        catch (error) { child.kill(); finish(error); return; }
        if (signal?.aborted) cancel();
    });
}

function validateHelperResult(result, entry, cacheRoot) {
    const sha = entry.server.artifact.sha256;
    const generation = path.resolve(cacheRoot, "generations", sha);
    const serverPath = path.join(generation, "server", "Lucent.Lui.LanguageServer.dll");
    if (result?.schemaVersion !== 1 || result.status !== "verified"
        || result.archiveSha256 !== sha || result.filesSha256 !== entry.server.filesSha256
        || !isDeepStrictEqual(result.identity, entry.server.identity)
        || typeof result.generationPath !== "string" || !samePath(result.generationPath, generation)
        || typeof result.serverPath !== "string" || !samePath(result.serverPath, serverPath)
        || !fs.statSync(serverPath).isFile()) throw new Error("Cache helper returned a different server generation.");
    return { status: "selected", serverPath, identity: entry.server.identity, archiveSha256: sha, provenance: entry.anchor };
}

async function execute(entry, operation, options, archivePath) {
    const { cacheRoot, helperDirectory, helperManifest, dotnetPath, sourceCommit, signal, spawn } = options;
    if (!path.isAbsolute(cacheRoot) || !path.isAbsolute(helperDirectory) || !path.isAbsolute(dotnetPath)) {
        throw new Error("Cache and helper paths must be absolute on the workspace host.");
    }
    const helperPath = await verifyHelperFiles(helperDirectory, helperManifest, sourceCommit, signal);
    const request = {
        schemaVersion: 1, operation, cacheRoot, dotnetPath,
        expected: { artifact: entry.server.artifact, filesSha256: entry.server.filesSha256, identity: entry.server.identity },
        anchor: entry.anchor
    };
    if (operation === "install") request.archivePath = archivePath;
    const result = await runHelper(request, { helperPath, dotnetPath, signal, spawn });
    return validateHelperResult(result, entry, cacheRoot);
}

async function resolveCachedServer(options) {
    const selection = selectApprovedEntry(options.requirement, options.approvedEntries, options.clientRelease);
    if (selection.status !== "approved") return selection;
    try { return await execute(selection.entry, "verify", options); }
    catch (error) {
        if (error.name === "AbortError") return { status: "cancelled" };
        return { status: "cache-missing", version: selection.version, reason: error.code ?? "verification_failed" };
    }
}

async function importApprovedArchive(options) {
    const selection = selectApprovedEntry(options.requirement, options.approvedEntries, options.clientRelease);
    if (selection.status !== "approved") return selection;
    const { archivePath, signal } = options;
    if (typeof archivePath !== "string" || !path.isAbsolute(archivePath)) {
        return { status: "no-anchor", reason: "Offline archive is not an absolute host path." };
    }
    try {
        if (fs.lstatSync(archivePath).isSymbolicLink() || !fs.statSync(archivePath).isFile()) {
            return { status: "no-anchor", reason: "Offline archive is not a regular host file." };
        }
        const actual = await hashFile(archivePath, MAX_ARCHIVE_BYTES, signal);
        if (actual.bytes !== selection.entry.server.artifact.bytes
            || actual.sha256 !== selection.entry.server.artifact.sha256) {
            return { status: "no-anchor", reason: "Offline archive digest has no approved release anchor." };
        }
        return await execute(selection.entry, "install", options, archivePath);
    } catch (error) {
        if (error.name === "AbortError") return { status: "cancelled" };
        return { status: "cache-missing", version: selection.version, reason: error.code ?? "installation_failed" };
    }
}

async function importDownloadedRelease(options) {
    const { downloaded } = options;
    try {
        const selection = selectApprovedEntry(options.requirement, options.approvedEntries, options.clientRelease);
        if (selection.status !== "approved") return selection;
        if (downloaded?.status !== "downloaded" || !isDeepStrictEqual(downloaded.entry, selection.entry)
            || typeof downloaded.completeArchivePath !== "string" || !path.isAbsolute(downloaded.completeArchivePath)
            || fs.lstatSync(downloaded.completeArchivePath).isSymbolicLink()
            || !fs.statSync(downloaded.completeArchivePath).isFile()
            || !Number.isSafeInteger(downloaded.artifactBytes) || downloaded.artifactBytes <= 0
            || downloaded.artifactBytes > 512 * 1024 * 1024) {
            return { status: "incompatible", reason: "Downloaded artifact differs from the approved release." };
        }
        const { cacheRoot, helperDirectory, helperManifest, dotnetPath, sourceCommit, signal, spawn } = options;
        if (!path.isAbsolute(cacheRoot) || !path.isAbsolute(helperDirectory) || !path.isAbsolute(dotnetPath)) {
            throw new Error("Cache and helper paths must be absolute on the workspace host.");
        }
        const helperPath = await verifyHelperFiles(helperDirectory, helperManifest, sourceCommit, signal);
        const entry = selection.entry;
        const actions = entry.anchor.githubActions;
        const request = {
            schemaVersion: 1, operation: "install-release", cacheRoot, dotnetPath,
            expected: { artifact: entry.server.artifact, filesSha256: entry.server.filesSha256, identity: entry.server.identity },
            anchor: { kind: entry.anchor.kind, sourceCommit: entry.anchor.sourceCommit,
                descriptorSha256: entry.anchor.descriptorSha256, runId: actions.runId,
                runAttempt: actions.runAttempt, artifactId: actions.artifact.id, artifactDigest: actions.artifact.digest },
            completeRelease: {
                archivePath: downloaded.completeArchivePath, stagingRoot: path.join(cacheRoot, ".staging"),
                artifactBytes: downloaded.artifactBytes, artifactDigest: actions.artifact.digest,
                descriptorSha256: entry.anchor.descriptorSha256, releaseVersion: entry.releaseVersion,
                sourceCommit: entry.anchor.sourceCommit, sdkPackageSha256: entry.sdk.packageSha256,
                runId: actions.runId, runAttempt: actions.runAttempt,
                expectedServer: { artifact: entry.server.artifact, filesSha256: entry.server.filesSha256, identity: entry.server.identity }
            }
        };
        const result = await runHelper(request, { helperPath, dotnetPath, signal, spawn });
        return validateHelperResult(result, entry, cacheRoot);
    } catch (error) {
        if (error.name === "AbortError") return { status: "cancelled" };
        return { status: "cache-missing", reason: error.code ?? "installation_failed" };
    }
}

module.exports = { verifyHelperFiles, selectApprovedEntry, resolveCachedServer, importApprovedArchive, importDownloadedRelease };
