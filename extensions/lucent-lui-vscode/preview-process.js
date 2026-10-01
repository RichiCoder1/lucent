"use strict";

const childProcess = require("node:child_process");
const path = require("node:path");

function terminationFailure(message) {
    return Object.assign(new Error(message), { code: "termination-failed" });
}

function decodeSupervisorResult(text, requestId) {
    const result = JSON.parse(text);
    const keys = ["protocolVersion", "kind", "requestId", "status", "exitCode", "termination", "treeReaped", "stdoutBytes", "stderrBytes"];
    if (!result || Array.isArray(result) || Object.keys(result).length !== keys.length
        || keys.some(key => !Object.hasOwn(result, key)) || result.protocolVersion !== 1
        || result.kind !== "preview-supervisor-result" || result.requestId !== requestId
        || !["completed", "cancelled", "timeout", "output-limit", "launch-failed", "termination-failed"].includes(result.status)
        || !["natural", "cooperative", "forced", "unconfirmed"].includes(result.termination)
        || typeof result.treeReaped !== "boolean"
        || result.exitCode !== null && !Number.isInteger(result.exitCode)
        || !Number.isSafeInteger(result.stdoutBytes) || result.stdoutBytes < 0
        || !Number.isSafeInteger(result.stderrBytes) || result.stderrBytes < 0)
        throw terminationFailure("The preview supervisor returned an invalid result.");
    if (!result.treeReaped || result.termination === "unconfirmed" || result.status === "termination-failed")
        throw terminationFailure("The preview process tree could not be confirmed stopped.");
    return result;
}

function runSupervised(supervisorPath, request, { signal, spawn = childProcess.spawn } = {}) {
    if (signal?.aborted) return Promise.reject(Object.assign(new Error("Preview cancelled."), { code: "cancelled" }));
    return new Promise((resolve, reject) => {
        let child;
        let settled = false;
        let stopSent = false;
        let timer;
        let stdoutBytes = 0;
        let stderrBytes = 0;
        const stdout = [];
        const complete = (error, result) => {
            if (settled) return;
            settled = true;
            clearTimeout(timer);
            signal?.removeEventListener("abort", stop);
            if (error) reject(error); else resolve(result);
        };
        const uncertain = message => {
            // Kill-on-job-close bounds descendants, but without the supervisor's
            // reaped acknowledgement the coordinator must retain files and block.
            try { child?.kill(); } catch { /* Completion remains explicitly unconfirmed. */ }
            complete(terminationFailure(message));
        };
        const stop = () => {
            if (settled || stopSent) return;
            stopSent = true;
            try { child.stdin.write("stop\n"); }
            catch { uncertain("The preview supervisor control channel failed."); }
        };
        try {
            child = spawn(supervisorPath, [], {
                cwd: path.dirname(supervisorPath), shell: false, windowsHide: true,
                stdio: ["pipe", "pipe", "pipe"]
            });
        } catch (error) { complete(error); return; }
        child.once("error", error => {
            if (child.pid) uncertain("The preview supervisor failed after launch.");
            else complete(error);
        });
        child.stdin.on("error", () => {
            // EPIPE may race a natural exit; close still must supply a valid result.
            stopSent = true;
        });
        child.stdout.on("data", chunk => {
            if (settled) return;
            const bytes = Buffer.from(chunk);
            stdoutBytes += bytes.length;
            if (stdoutBytes > 65536) uncertain("The preview supervisor exceeded its protocol bound.");
            else stdout.push(bytes);
        });
        child.stderr.on("data", chunk => {
            stderrBytes += Buffer.byteLength(chunk);
            if (stderrBytes > 65536) uncertain("The preview supervisor exceeded its diagnostic bound.");
        });
        child.once("close", code => {
            if (settled) return;
            try {
                const result = decodeSupervisorResult(Buffer.concat(stdout).toString("utf8"), request.requestId);
                if (code !== 0) throw terminationFailure("The preview supervisor exited without successful tree cleanup.");
                complete(undefined, result);
            } catch (error) {
                complete(error.code === "termination-failed" ? error
                    : terminationFailure("The preview supervisor exited without valid termination evidence."));
            }
        });
        timer = setTimeout(() => uncertain("The preview supervisor exceeded its cleanup deadline."),
            request.timeoutMs + request.graceMs + 15000);
        signal?.addEventListener("abort", stop, { once: true });
        try {
            // Keep stdin open: EOF means parent loss, not successful request delivery.
            child.stdin.write(JSON.stringify(request) + "\n");
            if (signal?.aborted) stop();
        } catch { uncertain("Could not deliver the preview supervisor request."); }
    });
}

module.exports = { runSupervised, decodeSupervisorResult };
