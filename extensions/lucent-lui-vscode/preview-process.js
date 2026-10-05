"use strict";

const childProcess = require("node:child_process");
const path = require("node:path");

function terminationFailure(message) {
    return Object.assign(new Error(message), { code: "termination-failed" });
}

function exactRecord(record, keys) {
    return record && !Array.isArray(record) && Object.keys(record).length === keys.length
        && keys.every(key => Object.hasOwn(record, key));
}

function decodeSupervisorResult(text, requestId) {
    const result = JSON.parse(text);
    const keys = ["protocolVersion", "kind", "requestId", "status", "exitCode", "termination", "treeReaped", "stdoutBytes", "stderrBytes"];
    if (!exactRecord(result, keys) || result.protocolVersion !== 2
        || result.kind !== "preview-supervisor-result" || result.requestId !== requestId
        || !["completed", "cancelled", "timeout", "output-limit", "launch-failed", "termination-failed"].includes(result.status)
        || !["natural", "cooperative", "forced", "unconfirmed"].includes(result.termination)
        || typeof result.treeReaped !== "boolean"
        || result.exitCode !== null && (!Number.isInteger(result.exitCode) || result.exitCode < -2147483648 || result.exitCode > 2147483647)
        || !Number.isSafeInteger(result.stdoutBytes) || result.stdoutBytes < 0
        || !Number.isSafeInteger(result.stderrBytes) || result.stderrBytes < 0)
        throw terminationFailure("The preview supervisor returned an invalid result.");
    if (!result.treeReaped || result.termination === "unconfirmed" || result.status === "termination-failed")
        throw terminationFailure("The preview process tree could not be confirmed stopped.");
    return result;
}

function decodeSupervisorStarted(text, requestId) {
    const record = JSON.parse(text);
    if (!exactRecord(record, ["protocolVersion", "kind", "requestId"]) || record.protocolVersion !== 2
        || record.kind !== "preview-supervisor-started" || record.requestId !== requestId)
        throw terminationFailure("The preview supervisor returned an invalid started record.");
    return record;
}

function startSupervised(supervisorPath, request, { signal, spawn = childProcess.spawn,
    clock = { setTimeout, clearTimeout } } = {}) {
    let resolveStarted, rejectStarted, resolveCompletion, rejectCompletion;
    const started = new Promise((resolve, reject) => { resolveStarted = resolve; rejectStarted = reject; });
    const completion = new Promise((resolve, reject) => { resolveCompletion = resolve; rejectCompletion = reject; });
    // Bounded callers only await completion; an unobserved started rejection is still handled.
    void started.catch(() => {});
    let child, settled = false, stopSent = false, launchRecord, finalRecord;
    let timer, stdoutBytes = 0, stderrBytes = 0, pending = "";
    const decoder = new TextDecoder("utf-8", { fatal: true });
    const clearTimer = () => { if (timer !== undefined) clock.clearTimeout(timer); timer = undefined; };
    const complete = (error, result) => {
        if (settled) return;
        settled = true;
        clearTimer();
        signal?.removeEventListener("abort", stop);
        if (!launchRecord) rejectStarted(error ?? Object.assign(new Error("Preview stopped before launch."), { code: result.status }));
        if (error) rejectCompletion(error); else resolveCompletion(result);
    };
    const uncertain = message => {
        if (settled) return;
        // Killing the job owner is a crash-path safety net, never evidence of tree reaping.
        try { child?.kill(); } catch { /* The generation remains quarantined. */ }
        complete(terminationFailure(message));
    };
    const deadline = (milliseconds, message) => {
        clearTimer();
        timer = clock.setTimeout(() => uncertain(message), milliseconds);
    };
    const cleanupDeadline = () => deadline(request.graceMs + 15000, "The preview supervisor exceeded its cleanup deadline.");
    const stop = () => {
        if (settled || stopSent) return completion;
        stopSent = true;
        cleanupDeadline();
        try { child.stdin.write("stop\n"); }
        catch { uncertain("The preview supervisor control channel failed."); }
        return completion;
    };
    const handle = { started, completion, stop };
    if (signal?.aborted) {
        complete(Object.assign(new Error("Preview cancelled."), { code: "cancelled" }));
        return handle;
    }
    if (request.protocolVersion !== 2 || request.kind !== "preview-supervisor-request"
        || !["bounded", "live"].includes(request.mode)
        || !Number.isInteger(request.timeoutMs) || request.timeoutMs < 1 || request.timeoutMs > 600000
        || !Number.isInteger(request.graceMs) || request.graceMs < 0 || request.graceMs > 10000) {
        complete(Object.assign(new Error("Unsupported preview supervisor request."), { code: "launch-failed" }));
        return handle;
    }
    const admitLine = line => {
        if (finalRecord) throw terminationFailure("The preview supervisor returned data after its final record.");
        const record = JSON.parse(line);
        if (record?.kind === "preview-supervisor-started") {
            if (launchRecord) throw terminationFailure("The preview supervisor returned duplicate started records.");
            launchRecord = decodeSupervisorStarted(line, request.requestId);
            resolveStarted(launchRecord);
            if (!stopSent) {
                clearTimer();
                if (request.mode === "bounded") deadline(request.timeoutMs + request.graceMs + 15000,
                    "The preview supervisor exceeded its bounded execution deadline.");
            }
        } else {
            finalRecord = decodeSupervisorResult(line, request.requestId);
            if (!launchRecord && !["cancelled", "launch-failed"].includes(finalRecord.status))
                throw terminationFailure("The preview supervisor finished without a started record.");
            if (!stopSent) cleanupDeadline();
        }
    };
    try {
        child = spawn(supervisorPath, [], {
            cwd: path.dirname(supervisorPath), shell: false, windowsHide: true,
            stdio: ["pipe", "pipe", "pipe"]
        });
    } catch (error) { complete(error); return handle; }
    child.once("error", error => {
        if (child.pid) uncertain("The preview supervisor failed after launch.");
        else complete(error);
    });
    child.stdin.on("error", () => {
        // EPIPE can race a natural exit. Allow its final record and close to prove cleanup.
        if (!settled && !stopSent) { stopSent = true; cleanupDeadline(); }
    });
    child.stdout.on("error", () => uncertain("The preview supervisor output channel failed."));
    child.stderr.on("error", () => uncertain("The preview supervisor diagnostic channel failed."));
    child.stdout.on("data", chunk => {
        if (settled) return;
        const bytes = Buffer.from(chunk);
        stdoutBytes += bytes.length;
        if (stdoutBytes > 65536) { uncertain("The preview supervisor exceeded its protocol bound."); return; }
        try {
            pending += decoder.decode(bytes, { stream: true });
            let newline;
            while ((newline = pending.indexOf("\n")) >= 0) {
                const line = pending.slice(0, newline).replace(/\r$/, "");
                pending = pending.slice(newline + 1);
                admitLine(line);
            }
            if (finalRecord && pending.length) throw terminationFailure("The preview supervisor returned data after its final record.");
        } catch { uncertain("The preview supervisor returned invalid ordered protocol records."); }
    });
    child.stderr.on("data", chunk => {
        if (settled) return;
        stderrBytes += Buffer.byteLength(chunk);
        if (stderrBytes > 65536) uncertain("The preview supervisor exceeded its diagnostic bound.");
    });
    child.once("close", code => {
        if (settled) return;
        try {
            pending += decoder.decode();
            if (pending.length || !finalRecord || code !== 0)
                throw terminationFailure("The preview supervisor exited without valid termination evidence.");
            complete(undefined, finalRecord);
        } catch { uncertain("The preview supervisor exited without valid termination evidence."); }
    });
    deadline(15000, "The preview supervisor exceeded its launch deadline.");
    signal?.addEventListener("abort", stop, { once: true });
    try {
        // EOF is parent loss: retain this channel for the whole owned lifetime.
        child.stdin.write(JSON.stringify(request) + "\n");
        if (signal?.aborted) stop();
    } catch { uncertain("Could not deliver the preview supervisor request."); }
    return handle;
}

function runSupervised(supervisorPath, request, options) {
    if (request.mode !== "bounded") return Promise.reject(Object.assign(new Error("Bounded supervisor mode is required."), { code: "launch-failed" }));
    return startSupervised(supervisorPath, request, options).completion;
}

module.exports = { startSupervised, runSupervised, decodeSupervisorResult, decodeSupervisorStarted };
