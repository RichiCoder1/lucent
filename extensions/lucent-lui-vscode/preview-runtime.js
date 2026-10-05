"use strict";

const fs = require("node:fs/promises");
const path = require("node:path");
const { randomUUID } = require("node:crypto");
const { isDeepStrictEqual } = require("node:util");
const { runSupervised } = require("./preview-process");
const { decodeWorkerRequest, readVerifiedFrame, readVerifiedCatalog, readBoundedFile,
    resolvePresentation } = require("./preview-protocol");
const { parseCompilerDiagnostics } = require("./preview-diagnostics");

function inside(root, candidate) {
    const relative = path.relative(root, candidate);
    return !!relative && relative !== ".." && !relative.startsWith(".." + path.sep) && !path.isAbsolute(relative);
}

function cancelled() { return Object.assign(new Error("Preview cancelled."), { code: "cancelled" }); }

function createPreviewRuntime({ supervisorPath, buildToolPath, storageDirectory, isTrusted,
    supervise = runSupervised, onInputs = () => {}, log = () => {},
    retentionPolicy = { maxFailures: 3, maxBytes: 16 * 1024 * 1024 } }) {
    if (!Number.isInteger(retentionPolicy.maxFailures) || retentionPolicy.maxFailures < 1 || retentionPolicy.maxFailures > 16
        || !Number.isInteger(retentionPolicy.maxBytes) || retentionPolicy.maxBytes < 8192 || retentionPolicy.maxBytes > 64 * 1024 * 1024)
        throw new Error("Unsupported preview diagnostic retention policy.");
    const quarantined = new Set();
    const pending = new Set();
    const owned = new Set();
    const phaseDirectories = new Map();
    const historyDirectory = path.join(storageDirectory, "diagnostics");
    let historyTail = Promise.resolve();
    let lastHistoryTime = 0;
    function admitted(signal) {
        if (signal?.aborted) throw cancelled();
        if (!isTrusted()) throw new Error("Workspace Trust is required to execute a preview.");
    }

    async function invoke(root, program, args, phase, signal) {
        admitted(signal);
        const logDirectory = path.join(root, phase + "-" + randomUUID());
        await fs.mkdir(logDirectory);
        const directories = phaseDirectories.get(root) ?? [];
        directories.push(logDirectory);
        phaseDirectories.set(root, directories);
        admitted(signal);
        let result;
        pending.add(root);
        try {
            result = await supervise(supervisorPath, {
                protocolVersion: 2, kind: "preview-supervisor-request", mode: "bounded", requestId: randomUUID(),
                program, args, workingDirectory: path.dirname(program), logDirectory,
                timeoutMs: phase === "build" ? 600000 : 60000, graceMs: 1500,
                maxOutputBytes: 1024 * 1024,
                environment: { MSBUILDDISABLENODEREUSE: "1", DOTNET_CLI_USE_MSBUILD_SERVER: "0" }
            }, { signal });
        } catch (error) {
            if (error.code === "termination-failed") quarantined.add(root);
            throw error;
        } finally { pending.delete(root); }
        // The process adapter also enforces this; retain the invariant at the
        // ownership boundary used by injected tests and future adapters.
        if (!result.treeReaped || result.status === "termination-failed") {
            quarantined.add(root);
            throw Object.assign(new Error(`Preview cleanup is unconfirmed. Logs: ${logDirectory}`), { code: "termination-failed" });
        }
        if (result.status === "cancelled" || signal?.aborted) throw cancelled();
        if (result.status !== "completed" || result.exitCode !== 0)
            throw new Error(`Preview ${phase} failed (${result.status}, exit ${result.exitCode}). Logs: ${logDirectory}`);
        admitted(signal);
        return logDirectory;
    }

    function samePath(left, right) {
        return process.platform === "win32" ? left.toLowerCase() === right.toLowerCase() : left === right;
    }

    async function ownedRoot(artifact) {
        const root = path.resolve(artifact.root);
        const store = path.resolve(storageDirectory);
        if (!samePath(path.dirname(root), store) || !path.basename(root).startsWith("generation-")
            || !owned.has(root) || pending.has(root) || quarantined.has(root))
            throw new Error("Refusing to clean an unowned preview directory.");
        const [rootInfo, storeInfo, realRoot, realStore] = await Promise.all([
            fs.lstat(root), fs.lstat(store), fs.realpath(root), fs.realpath(store)
        ]);
        if (rootInfo.isSymbolicLink() || storeInfo.isSymbolicLink() || !rootInfo.isDirectory()
            || !samePath(path.dirname(realRoot), realStore))
            throw new Error("Refusing to clean a linked preview generation directory.");
        return root;
    }

    async function retainedEntries() {
        let directories;
        try { directories = await fs.readdir(historyDirectory, { withFileTypes: true }); }
        catch (error) { if (error.code === "ENOENT") return []; throw error; }
        if ((await fs.lstat(historyDirectory)).isSymbolicLink())
            throw new Error("Refusing to inspect linked preview diagnostic history.");
        const entries = [];
        for (const directory of directories) {
            if (!directory.isDirectory() || directory.isSymbolicLink() || !directory.name.startsWith("failure-")) continue;
            const root = path.join(historyDirectory, directory.name);
            try {
                const markerInfo = await fs.lstat(path.join(root, "retention.json"));
                if (!markerInfo.isFile() || markerInfo.isSymbolicLink() || markerInfo.size > 1024) continue;
                const marker = JSON.parse((await readBoundedFile(path.join(root, "retention.json"), 1024)).toString("utf8"));
                if (marker.protocolVersion !== 1 || marker.kind !== "preview-confirmed-diagnostic"
                    || marker.treeReaped !== true || !Number.isSafeInteger(marker.createdAt) || marker.createdAt < 0) continue;
                const files = await fs.readdir(root, { withFileTypes: true });
                if (files.some(file => !file.isFile() || file.isSymbolicLink())) continue;
                let bytes = 0;
                for (const file of files) bytes += (await fs.lstat(path.join(root, file.name))).size;
                entries.push({ root, bytes, createdAt: marker.createdAt });
            } catch (error) {
                // Incomplete/unrecognized history has no confirmed ownership marker.
                if (!["ENOENT", "EINVAL"].includes(error.code) && !(error instanceof SyntaxError)) throw error;
            }
        }
        return entries.sort((left, right) => left.createdAt - right.createdAt || left.root.localeCompare(right.root));
    }

    async function pruneHistory() {
        const entries = await retainedEntries();
        let bytes = entries.reduce((sum, entry) => sum + entry.bytes, 0);
        while (entries.length > retentionPolicy.maxFailures || bytes > retentionPolicy.maxBytes) {
            const oldest = entries.shift();
            bytes -= oldest.bytes;
            await fs.rm(oldest.root, { recursive: true });
        }
    }

    async function preserveFailure(artifact, diagnostic) {
        await fs.mkdir(historyDirectory, { recursive: true });
        if ((await fs.lstat(historyDirectory)).isSymbolicLink())
            throw new Error("Refusing to write linked preview diagnostic history.");
        const previous = await retainedEntries();
        lastHistoryTime = Math.max(Date.now(), lastHistoryTime + 1, ...previous.map(entry => entry.createdAt + 1));
        const destination = await fs.mkdtemp(path.join(historyDirectory, "failure-"));
        const summaryAllowance = Math.min(64 * 1024, Math.floor(retentionPolicy.maxBytes / 2));
        let remainingLogBytes = retentionPolicy.maxBytes - summaryAllowance - 1024;
        const report = artifact.report;
        let summary = {
            protocolVersion: 1, kind: "preview-runtime-diagnostic", generation: path.basename(artifact.root),
            diagnostic: String(diagnostic ?? "Preview operation failed.").slice(0, 2048),
            request: { sessionId: String(artifact.buildRequest.sessionId ?? "").slice(0, 128),
                generation: String(artifact.buildRequest.generation ?? "").slice(0, 64),
                requestId: String(artifact.buildRequest.requestId ?? "").slice(0, 128),
                projectPath: String(artifact.buildRequest.projectPath ?? "").slice(0, Math.min(4096, summaryAllowance / 4)),
                configuration: String(artifact.buildRequest.configuration ?? "").slice(0, 64),
                targetFramework: String(artifact.buildRequest.targetFramework ?? "").slice(0, 128),
                runtimeIdentifier: String(artifact.buildRequest.runtimeIdentifier ?? "").slice(0, 64) },
            identity: report ? { projectTargetDigest: report.projectTargetDigest,
                inputDigest: report.inputDigest, artifactDigest: report.artifactDigest } : null,
            logs: []
        };
        const phases = (phaseDirectories.get(artifact.root) ?? []).map(directory => ({ directory,
            files: ["stderr.log", "stdout.log"], prefix: path.basename(directory) + "-" }));
        // The SDK emits actual compiler diagnostics into its own bounded logs;
        // the build-tool process itself only reports the failed SDK operation.
        const sdk = { directory: path.join(artifact.root, "build"), prefix: "sdk-",
            files: ["publish.stdout.log", "publish.stderr.log", "restore.stderr.log", "restore.stdout.log"] };
        const sources = [...phases.filter(phase => path.basename(phase.directory).startsWith("worker-")),
            sdk, ...phases.filter(phase => !path.basename(phase.directory).startsWith("worker-"))];
        for (const { directory, files, prefix } of sources) {
            let directoryInfo;
            try { directoryInfo = await fs.lstat(directory); }
            catch (error) { if (error.code === "ENOENT") continue; throw error; }
            if (!directoryInfo.isDirectory() || directoryInfo.isSymbolicLink()) continue;
            for (const file of files) {
                const source = path.join(directory, file);
                try {
                    const info = await fs.lstat(source);
                    if (!info.isFile() || info.isSymbolicLink()) continue;
                    const name = prefix + file;
                    // A single pathological injected log cannot bypass the history budget.
                    const allowance = Math.min(1024 * 1024, remainingLogBytes);
                    const handle = await fs.open(source, "r");
                    let bytes;
                    try {
                        bytes = Buffer.alloc(Math.min(info.size, allowance));
                        const read = await handle.read(bytes, 0, bytes.length, 0);
                        bytes = bytes.subarray(0, read.bytesRead);
                    } finally { await handle.close(); }
                    await fs.writeFile(path.join(destination, name), bytes, { flag: "wx" });
                    remainingLogBytes -= bytes.length;
                    summary.logs.push({ file: name, truncated: info.size > bytes.length });
                } catch (error) { if (error.code !== "ENOENT") throw error; }
            }
        }
        if (Buffer.byteLength(JSON.stringify(summary)) > summaryAllowance) {
            summary = { protocolVersion: 1, kind: summary.kind, generation: summary.generation,
                diagnostic: Buffer.from(summary.diagnostic).subarray(0, 1024).toString("utf8"),
                requestId: Buffer.from(summary.request.requestId).subarray(0, 128).toString("utf8"),
                truncated: true, logs: summary.logs.slice(0, 8) };
        }
        await fs.writeFile(path.join(destination, "diagnostic.json"), JSON.stringify(summary), { flag: "wx" });
        await fs.writeFile(path.join(destination, "retention.json"), JSON.stringify({
            protocolVersion: 1, kind: "preview-confirmed-diagnostic", treeReaped: true, createdAt: lastHistoryTime
        }), { flag: "wx" });
        log(`Preview failure diagnostics: ${destination}`);
        return destination;
    }

    async function cleanup(artifact, { failed = false, diagnostic } = {}) {
        const next = historyTail.then(async () => {
            const root = await ownedRoot(artifact);
            const diagnosticDirectory = failed ? await preserveFailure(artifact, diagnostic) : undefined;
            // Pixels have already been copied into the coordinator's last-good buffer.
            // Full reports, frame files and compiler/runtime copies are generation payloads.
            await fs.rm(root, { recursive: true });
            owned.delete(root);
            phaseDirectories.delete(root);
            await pruneHistory();
            const retainedDiagnostic = diagnosticDirectory
                ? `${String(diagnostic ?? "Preview operation failed.").replace(/\s+Logs:\s+.*$/s, "").slice(0, 2048)} Retained diagnostics: ${diagnosticDirectory}`
                : undefined;
            return { diagnosticDirectory, diagnostic: retainedDiagnostic };
        });
        historyTail = next.catch(() => {});
        return next;
    }

    async function build(request, signal) {
        admitted(signal);
        await fs.mkdir(storageDirectory, { recursive: true });
        const root = await fs.mkdtemp(path.join(storageDirectory, "generation-"));
        owned.add(root);
        const outputDirectory = path.join(root, "build");
        const reportPath = path.join(outputDirectory, "report.json");
        const requestPath = path.join(root, "build-request.json");
        const selection = request.selection;
        const buildRequest = {
            protocolVersion: 1, sessionId: request.sessionId, generation: request.generation, requestId: request.requestId,
            projectPath: selection.projectPath, configuration: selection.configuration ?? "Debug",
            targetFramework: selection.targetFramework, runtimeIdentifier: "win-x64", outputDirectory,
            extraInputs: selection.extraInputs ?? []
        };
        const artifact = { root, reportPath, buildRequest };
        try {
            await fs.writeFile(requestPath, JSON.stringify(buildRequest), { flag: "wx" });
            const logs = await invoke(root, buildToolPath, ["build", "--request", requestPath, "--report", reportPath], "build", signal);
            const acknowledgement = JSON.parse((await readBoundedFile(path.join(logs, "stdout.log"), 65536)).toString("utf8"));
            if (acknowledgement.protocolVersion !== 1 || acknowledgement.kind !== "preview-build"
                || acknowledgement.status !== "succeeded" || acknowledgement.reportPath !== reportPath)
                throw new Error("Preview build returned an unexpected artifact report.");
            const report = JSON.parse((await readBoundedFile(reportPath, 16 * 1024 * 1024)).toString("utf8"));
            if (report.protocolVersion !== 1 || report.kind !== "preview-build-report" || report.status !== "succeeded"
                || !isDeepStrictEqual(report.request, buildRequest)
                || typeof report.entryPoint !== "string" || !inside(outputDirectory, report.entryPoint)
                || ![report.projectTargetDigest, report.inputDigest, report.artifactDigest].every(hash => typeof hash === "string" && /^[a-f0-9]{64}$/.test(hash)))
                throw new Error("Preview artifact identity does not match its build request.");
            artifact.report = report;
            admitted(signal);
            onInputs(report, request);
            return artifact;
        } catch (error) {
            if (error.code === "termination-failed") log(`Preview generation evidence: ${root}`);
            else {
                try { error.diagnostics = await compilerDiagnostics(artifact); }
                catch (diagnosticError) {
                    log("Preview compiler diagnostic extraction failed", diagnosticError);
                    error.diagnostics = Object.freeze([]);
                }
                const retained = await cleanup(artifact, {
                    failed: error.code !== "cancelled" && !signal?.aborted, diagnostic: error.message
                });
                if (retained.diagnosticDirectory)
                    error.message = retained.diagnostic;
            }
            throw error;
        }
    }

    async function compilerDiagnostics(artifact) {
        const directory = path.join(artifact.root, "build");
        try {
            const info = await fs.lstat(directory);
            if (!info.isDirectory() || info.isSymbolicLink()) return Object.freeze([]);
        } catch (error) { if (error.code === "ENOENT") return Object.freeze([]); throw error; }
        const logs = [];
        for (const name of ["publish.stdout.log", "publish.stderr.log", "restore.stdout.log", "restore.stderr.log"]) {
            const file = path.join(directory, name);
            try {
                const info = await fs.lstat(file);
                if (info.isFile() && !info.isSymbolicLink()) logs.push(await readBoundedFile(file, 1024 * 1024));
            } catch (error) { if (error.code !== "ENOENT") throw error; }
        }
        return parseCompilerDiagnostics(logs, artifact.buildRequest.requestId);
    }

    async function verify(artifact, signal) {
        const logs = await invoke(artifact.root, buildToolPath, ["verify", "--report", artifact.reportPath], "verify", signal);
        const result = JSON.parse((await readBoundedFile(path.join(logs, "stdout.log"), 65536)).toString("utf8"));
        const report = artifact.report;
        return result.protocolVersion === 1 && result.kind === "preview-build-verification" && result.status === "fresh"
            && ["sessionId", "generation", "requestId"].every(key => result[key] === report.request[key])
            && ["projectTargetDigest", "inputDigest", "artifactDigest"].every(key => result[key] === report[key]);
    }

    function workerIdentity(artifact, request) {
        if (artifact.buildRequest.sessionId !== request.sessionId || artifact.buildRequest.generation !== request.generation
            || artifact.buildRequest.requestId !== request.requestId)
            throw new Error("Preview artifact does not belong to this generation.");
        return { protocolVersion: 2, sessionId: request.sessionId, generation: request.generation,
            requestId: request.requestId, projectTargetDigest: artifact.report.projectTargetDigest,
            inputDigest: artifact.report.inputDigest, artifactDigest: artifact.report.artifactDigest };
    }

    async function discover(artifact, request, signal) {
        admitted(signal);
        await ownedRoot(artifact);
        const identity = workerIdentity(artifact, request);
        const outputDirectory = path.join(artifact.root, "catalog");
        await fs.mkdir(outputDirectory);
        const workerRequest = { ...identity, kind: "preview-catalog-request", requestId: randomUUID(), outputDirectory };
        const bytes = Buffer.from(JSON.stringify(workerRequest));
        decodeWorkerRequest(bytes);
        const requestPath = path.join(artifact.root, "catalog-request.json");
        await fs.writeFile(requestPath, bytes, { flag: "wx" });
        await invoke(artifact.root, artifact.report.entryPoint, ["--request", requestPath], "catalog", signal);
        const catalog = await readVerifiedCatalog(outputDirectory, workerRequest);
        admitted(signal);
        artifact.catalog = catalog;
        return catalog;
    }

    async function render(artifact, request, signal) {
        admitted(signal);
        await ownedRoot(artifact);
        const identity = workerIdentity(artifact, request);
        const scenario = artifact.catalog?.scenarios.find(entry => entry.id === request.selection.scenarioId);
        if (!scenario) throw new Error("The selected preview scenario is not registered in the current catalog.");
        const effectivePresentation = resolvePresentation(scenario, request.selection.presentation);
        const outputDirectory = path.join(artifact.root, "frame");
        await fs.mkdir(outputDirectory);
        const workerRequest = {
            ...identity, kind: "preview-capture-request", scenarioId: request.selection.scenarioId,
            presentationId: request.presentationId ?? "default", outputDirectory,
            ...Object.fromEntries(["logicalWidth", "logicalHeight", "scale", "colorScheme", "contrast", "density"]
                .map(key => [key, effectivePresentation[key]]))
        };
        const bytes = Buffer.from(JSON.stringify(workerRequest));
        decodeWorkerRequest(bytes);
        const requestPath = path.join(artifact.root, "worker-request.json");
        await fs.writeFile(requestPath, bytes, { flag: "wx" });
        await invoke(artifact.root, artifact.report.entryPoint, ["--request", requestPath], "worker", signal);
        const frame = await readVerifiedFrame(outputDirectory, { ...workerRequest, ...effectivePresentation });
        admitted(signal);
        return Object.freeze({ ...frame, effectivePresentation });
    }

    return { build, verify, discover, render, release: cleanup };
}

module.exports = { createPreviewRuntime };
