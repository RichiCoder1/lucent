"use strict";

const childProcess = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const CAPABILITY = "environment-doctor";
const RUNTIME_MAJOR = 10;
const DEFAULT_TIMEOUT_MS = 30_000;
const MAX_TIMEOUT_MS = 120_000;
const TREE_CLEANUP_TIMEOUT_MS = 5_000;
const TREE_POLL_INTERVAL_MS = 20;
const MAX_DOCTOR_OUTPUT_BYTES = 256 * 1024;
const MAX_PREFLIGHT_OUTPUT_BYTES = 64 * 1024;
const MAX_STDERR_BYTES = 64 * 1024;
const CAPABILITIES = new Set(["build", "editor", "restore", "native"]);
const CAPABILITY_STATES = new Set(["available", "blocked", "notChecked"]);
const CHECK_CODES = new Set([
    "global-json", "dotnet-sdk", "sdk-selection", "dotnet-runtime", "native-platform",
    "native-prerequisites", "feed-config", "feed-reachability", "project-requirements"
]);
const CHECK_CAPABILITY = new Map([
    ["global-json", "build"], ["dotnet-sdk", "build"], ["sdk-selection", "build"],
    ["dotnet-runtime", "editor"], ["native-platform", "native"], ["native-prerequisites", "native"],
    ["feed-config", "restore"], ["feed-reachability", "restore"], ["project-requirements", "editor"]
]);
const CHECK_STATES = new Set(["pass", "fail", "notChecked"]);
const SEVERITIES = new Set(["info", "warning", "error"]);

function abortError() {
    return Object.assign(new Error("Environment doctor was canceled."), { name: "AbortError" });
}

function unavailable(reason) {
    return {
        schemaVersion: 1,
        kind: "environment-doctor-client",
        capability: CAPABILITY,
        status: "unavailable",
        reason
    };
}

function pathIsWithin(root, candidate) {
    const relative = path.relative(root, candidate);
    if (process.platform === "win32") {
        const normalized = relative.toLowerCase();
        return normalized === "" || (normalized !== ".." && !normalized.startsWith(`..${path.sep}`)
            && !path.isAbsolute(relative));
    }
    return relative === "" || (relative !== ".." && !relative.startsWith(`..${path.sep}`)
        && !path.isAbsolute(relative));
}

function resolveTrustedDotnetHost(workspacePath, environment = process.env) {
    const workspaceRoots = new Set([path.resolve(workspacePath)]);
    try { workspaceRoots.add(fs.realpathSync(workspacePath)); } catch { /* The caller reports an unavailable workspace. */ }
    const executable = process.platform === "win32" ? "dotnet.exe" : "dotnet";
    const candidates = [];
    for (const root of [environment.DOTNET_ROOT_X64, environment.DOTNET_ROOT]) {
        if (typeof root === "string" && path.isAbsolute(root)) candidates.push(path.join(root, executable));
    }
    for (const item of String(environment.PATH || "").split(path.delimiter)) {
        const directory = item.trim();
        const unquoted = directory.length >= 2 && directory.startsWith('"') && directory.endsWith('"')
            ? directory.slice(1, -1) : directory;
        if (unquoted && path.isAbsolute(unquoted)) candidates.push(path.join(unquoted, executable));
    }

    const seen = new Set();
    for (const candidate of candidates) {
        const absoluteCandidate = path.resolve(candidate);
        if ([...workspaceRoots].some(root => pathIsWithin(root, absoluteCandidate))) continue;
        let real;
        try {
            real = fs.realpathSync(candidate);
            if (!fs.statSync(real).isFile()) continue;
        } catch { continue; }
        const key = process.platform === "win32" ? real.toLowerCase() : real;
        if (seen.has(key)) continue;
        seen.add(key);
        if (![...workspaceRoots].some(root => pathIsWithin(root, real))) return real;
    }
    return null;
}

function waitWithinDeadline(promise, deadline) {
    const remaining = Math.max(0, deadline - Date.now());
    if (remaining === 0) return Promise.resolve({ timedOut: true });
    let timer;
    return Promise.race([
        Promise.resolve(promise).then(value => ({ timedOut: false, value })),
        new Promise(resolve => { timer = setTimeout(() => resolve({ timedOut: true }), remaining); })
    ]).finally(() => clearTimeout(timer));
}

function waitForChildClose(child) {
    if (!child || typeof child.once !== "function") return Promise.resolve({ error: true });
    return new Promise(resolve => {
        let completed = false;
        const complete = result => {
            if (completed) return;
            completed = true;
            resolve(result);
        };
        child.once("error", () => complete({ error: true }));
        child.once("close", (code, signal) => complete({ code, signal }));
    });
}

function systemTaskkillPath() {
    if (process.platform !== "win32") return null;
    const systemRoot = process.env.SystemRoot;
    if (typeof systemRoot !== "string" || !path.isAbsolute(systemRoot)) return null;
    try {
        const realRoot = fs.realpathSync(systemRoot);
        const executable = fs.realpathSync(path.join(realRoot, "System32", "taskkill.exe"));
        if (!fs.statSync(executable).isFile() || !pathIsWithin(realRoot, executable)) return null;
        return executable;
    } catch {
        return null;
    }
}

function processGroupExists(processGroupId) {
    try {
        process.kill(-processGroupId, 0);
        return true;
    } catch (error) {
        return error?.code !== "ESRCH";
    }
}

async function waitForProcessGroupExit(processGroupId, deadline) {
    while (Date.now() < deadline) {
        if (!processGroupExists(processGroupId)) return true;
        await new Promise(resolve => setTimeout(resolve, Math.min(
            TREE_POLL_INTERVAL_MS,
            Math.max(1, deadline - Date.now())
        )));
    }
    return !processGroupExists(processGroupId);
}

async function stopChild(child, { spawn, closePromise }) {
    const deadline = Date.now() + TREE_CLEANUP_TIMEOUT_MS;
    const processId = child?.pid;
    if (!Number.isInteger(processId) || processId <= 0) {
        try { child.kill("SIGKILL"); } catch { /* A test double or exited child may reject the request. */ }
        const closed = await waitWithinDeadline(closePromise, deadline);
        return !closed.timedOut;
    }

    if (process.platform === "win32") {
        const taskkill = systemTaskkillPath();
        let taskkillExitCode = null;
        if (taskkill) {
            try {
                const killer = spawn(taskkill, ["/PID", String(processId), "/T", "/F"], {
                    cwd: os.tmpdir(),
                    env: process.env,
                    shell: false,
                    windowsHide: true,
                    stdio: "ignore"
                });
                const killerResult = await waitWithinDeadline(waitForChildClose(killer), deadline);
                if (!killerResult.timedOut && !killerResult.value.error)
                    taskkillExitCode = killerResult.value.code;
                else if (killerResult.timedOut) {
                    try { killer.kill("SIGKILL"); } catch { /* The bounded cleanup deadline has expired. */ }
                }
            } catch { /* Fall back to the owned child's process handle below. */ }
        }
        if (taskkillExitCode !== 0) {
            try { child.kill("SIGKILL"); } catch { /* The child may have exited during cancellation. */ }
        }
        const closed = await waitWithinDeadline(closePromise, deadline);
        return taskkillExitCode === 0 && !closed.timedOut;
    }

    let signalSent = false;
    try {
        process.kill(-processId, "SIGKILL");
        signalSent = true;
    } catch (error) {
        if (error?.code === "ESRCH") signalSent = true;
        else {
            try { child.kill("SIGKILL"); } catch { /* The child may have exited during cancellation. */ }
        }
    }
    const closed = await waitWithinDeadline(closePromise, deadline);
    if (closed.timedOut) return false;
    return signalSent && await waitForProcessGroupExit(processId, deadline);
}

function runProcess(program, args, { spawn, signal, timeoutMs, maxOutputBytes, environment, input }) {
    return new Promise(resolve => {
        if (signal?.aborted) { resolve({ kind: "cancelled" }); return; }
        let child;
        let settled = false;
        let stopping = false;
        let deadline;
        let stdoutBytes = 0;
        let stderrBytes = 0;
        const stdout = [];
        let onAbort;
        const finish = result => {
            if (settled) return;
            settled = true;
            clearTimeout(deadline);
            signal?.removeEventListener("abort", onAbort);
            resolve(result);
        };
        let resolveChildClose;
        const childClose = new Promise(resolveClose => { resolveChildClose = resolveClose; });
        const terminate = kind => {
            if (settled || stopping) return;
            stopping = true;
            if (!child) { finish({ kind }); return; }
            void stopChild(child, { spawn, closePromise: childClose }).then(stopped => {
                finish(stopped ? { kind } : { kind: "termination-failed" });
            }, () => finish({ kind: "termination-failed" }));
        };
        onAbort = () => terminate("cancelled");
        try {
            child = spawn(program, args, {
                cwd: os.tmpdir(),
                env: environment,
                shell: false,
                detached: process.platform !== "win32",
                windowsHide: true,
                stdio: [input === undefined ? "ignore" : "pipe", "pipe", "pipe"]
            });
        } catch {
            finish({ kind: "spawn-error" });
            return;
        }
        if (!child?.stdout || !child?.stderr || typeof child.once !== "function") {
            if (child) {
                try { child.kill("SIGKILL"); } catch { /* A malformed spawn result may not own a live process. */ }
            }
            finish({ kind: "spawn-error" });
            return;
        }

        const onOutput = chunk => {
            if (settled || stopping) return;
            const bytes = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
            stdoutBytes += bytes.length;
            if (stdoutBytes > maxOutputBytes) {
                terminate("output-too-large");
            } else stdout.push(bytes);
        };
        const onErrorOutput = chunk => {
            if (settled || stopping) return;
            stderrBytes += Buffer.isBuffer(chunk) ? chunk.length : Buffer.byteLength(String(chunk));
            if (stderrBytes > MAX_STDERR_BYTES) {
                terminate("output-too-large");
            }
        };
        // Keep the pending result alive even when the child no longer owns an event-loop handle.
        deadline = setTimeout(() => terminate("timeout"), timeoutMs);
        child.stdout.on("data", onOutput);
        child.stderr.on("data", onErrorOutput);
        child.once("error", () => {
            if (!stopping) finish({ kind: "spawn-error" });
        });
        child.once("close", (code, signal) => {
            resolveChildClose({ code, signal });
            if (settled || stopping) return;
            finish({ kind: "exit", code, stdout: Buffer.concat(stdout, stdoutBytes).toString("utf8") });
        });
        signal?.addEventListener("abort", onAbort, { once: true });
        if (signal?.aborted) onAbort();
        if (input !== undefined && !stopping && !settled) {
            if (!child.stdin || typeof child.stdin.end !== "function") terminate("stdin-error");
            else {
                child.stdin.once("error", () => terminate("stdin-error"));
                try { child.stdin.end(input, "utf8"); } catch { terminate("stdin-error"); }
            }
        }
    });
}

function isRecord(value) {
    return value !== null && typeof value === "object" && !Array.isArray(value);
}

function hasExactKeys(value, expected) {
    const actual = Object.keys(value);
    return actual.length === expected.length && expected.every(key => Object.hasOwn(value, key));
}

function validText(value, maximum = 2048, required = false) {
    return typeof value === "string" && value.length <= maximum && (!required || value.trim().length > 0);
}

function decodeDoctorResult(output, exitCode) {
    let result;
    try { result = JSON.parse(output); }
    catch { return null; }
    if (!isRecord(result) || result.schemaVersion !== 1 || result.kind !== "environment-doctor"
        || result.scope !== "static-offline") return null;

    if (result.status === "unavailable") {
        return hasExactKeys(result, ["schemaVersion", "kind", "scope", "status", "error"])
            && isRecord(result.error) && hasExactKeys(result.error, ["code", "message"])
            && exitCode === 2 && result.error.code === "doctor-invocation"
            && validText(result.error.message, 512, true) ? { kind: "invocation-unavailable" } : null;
    }
    if (!new Set(["available", "blocked"]).has(result.status) || ![0, 1].includes(exitCode)
        || !hasExactKeys(result, ["schemaVersion", "kind", "scope", "status", "capabilities", "checks"])
        || !Array.isArray(result.capabilities) || !Array.isArray(result.checks)
        || result.checks.length === 0 || result.checks.length > 64) return null;

    const capabilities = new Map();
    for (const item of result.capabilities) {
        if (!isRecord(item) || !hasExactKeys(item, ["name", "status"])
            || !CAPABILITIES.has(item.name) || !CAPABILITY_STATES.has(item.status)
            || capabilities.has(item.name)) return null;
        capabilities.set(item.name, item.status);
    }
    if (capabilities.size !== CAPABILITIES.size) return null;

    const checks = new Set();
    for (const item of result.checks) {
        if (!isRecord(item) || !hasExactKeys(item, ["code", "capability", "status", "summary", "evidence", "remedy", "expected", "scope", "severity"])
            || !CHECK_CODES.has(item.code) || checks.has(item.code)
            || !CAPABILITIES.has(item.capability) || !CHECK_STATES.has(item.status)
            || !SEVERITIES.has(item.severity) || item.scope !== "static-offline"
            || item.capability !== CHECK_CAPABILITY.get(item.code)
            || !validText(item.summary, 512, true) || !validText(item.expected, 512, true)
            || (item.evidence !== undefined && item.evidence !== null && !validText(item.evidence, MAX_DOCTOR_OUTPUT_BYTES))
            || (item.remedy !== undefined && item.remedy !== null && !validText(item.remedy, 512))) return null;
        const severity = item.status === "fail" ? "error" : item.status === "notChecked" ? "warning" : "info";
        if (item.severity !== severity) return null;
        checks.add(item.code);
    }
    if (checks.size !== CHECK_CODES.size) return null;

    for (const capability of CAPABILITIES) {
        const relevant = result.checks.filter(item => item.capability === capability);
        if (relevant.length === 0) return null;
        const expected = relevant.some(item => item.status === "fail") ? "blocked"
            : relevant.some(item => item.status === "notChecked") ? "notChecked" : "available";
        if (capabilities.get(capability) !== expected) return null;
    }
    const expectedStatus = ["build", "editor"].some(capability => capabilities.get(capability) === "blocked")
        ? "blocked" : "available";
    if (result.status !== expectedStatus || (result.status === "available" && exitCode !== 0)
        || (result.status === "blocked" && exitCode !== 1)) return null;
    return result;
}

function decodeNativeDoctorResult(output, exitCode) {
    let result;
    try { result = JSON.parse(output); } catch { return null; }
    const scope = "installed-windows-x64-toolchain";
    if (!isRecord(result) || result.schemaVersion !== 1 || result.kind !== "native-prerequisites-doctor"
        || result.scope !== scope) return null;
    if (result.status === "unavailable" && Object.hasOwn(result, "error")) {
        return hasExactKeys(result, ["schemaVersion", "kind", "scope", "status", "error"])
            && isRecord(result.error) && hasExactKeys(result.error, ["code", "message"])
            && exitCode === 2 && result.error.code === "doctor-invocation"
            && validText(result.error.message, 512, true) ? { kind: "invocation-unavailable" } : null;
    }
    const exits = { available: 0, blocked: 1, unavailable: 2 };
    if (!Object.hasOwn(exits, result.status) || exits[result.status] !== exitCode
        || !hasExactKeys(result, ["schemaVersion", "kind", "scope", "status", "capabilities", "checks"])
        || !Array.isArray(result.capabilities) || result.capabilities.length !== 1
        || !isRecord(result.capabilities[0]) || !hasExactKeys(result.capabilities[0], ["name", "status"])
        || result.capabilities[0].name !== "native"
        || result.capabilities[0].status !== (result.status === "available" ? "observed" : "notChecked")
        || !Array.isArray(result.checks)) return null;
    const expectedCodes = result.status === "unavailable" ? ["native-prerequisites"]
        : ["native-visual-studio", "native-cpp-x64", "native-windows-sdk", "native-prerequisites", "native-publish"];
    if (result.checks.length !== expectedCodes.length) return null;
    const checks = new Map();
    for (const item of result.checks) {
        if (!isRecord(item) || !hasExactKeys(item, ["code", "capability", "status", "summary", "evidence", "remedy", "expected", "scope", "severity"])
            || !expectedCodes.includes(item.code) || checks.has(item.code) || item.capability !== "native"
            || !CHECK_STATES.has(item.status) || item.scope !== scope
            || item.severity !== (item.status === "fail" ? "error" : item.status === "notChecked" ? "warning" : "info")
            || !validText(item.summary, 512, true) || !validText(item.expected, 512, true)
            || (item.evidence !== null && !validText(item.evidence, MAX_DOCTOR_OUTPUT_BYTES))
            || (item.remedy !== null && !validText(item.remedy, 512))) return null;
        checks.set(item.code, item.status);
    }
    if (result.status === "unavailable") return checks.get("native-prerequisites") === "notChecked" ? result : null;
    if (checks.get("native-publish") !== "notChecked"
        || checks.get("native-prerequisites") !== (result.status === "available" ? "pass" : "fail")) return null;
    const components = ["native-visual-studio", "native-cpp-x64", "native-windows-sdk"];
    if (components.some(code => !["pass", "fail"].includes(checks.get(code)))
        || (result.status === "available" && components.some(code => checks.get(code) !== "pass"))) return null;
    return result;
}

function validFeedIdentity(packageId, version, generation) {
    return typeof packageId === "string" && /^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$/.test(packageId)
        && typeof version === "string" && /^[0-9][0-9A-Za-z.+-]{0,127}$/.test(version)
        && typeof generation === "string" && /^[A-Za-z0-9_-]{1,128}$/.test(generation);
}

function decodeFeedDoctorResult(output, exitCode, { online, generation }) {
    let result;
    try { result = JSON.parse(output); } catch { return null; }
    const scope = online ? "online-observation" : "effective-configuration";
    if (!isRecord(result) || !hasExactKeys(result, ["schemaVersion", "kind", "scope", "generation", "status", "reason", "sources", "authenticationPolicy", "restoreReadiness", "privateAvailability", "configuredAuthentication", "configurationScope"])
        || result.schemaVersion !== 1 || result.kind !== "nuget-feed-doctor" || result.scope !== scope
        || result.generation !== generation || result.authenticationPolicy !== "anonymous-no-credentials"
        || result.restoreReadiness !== "notChecked" || result.privateAvailability !== "notChecked"
        || result.configuredAuthentication !== "notChecked" || result.configurationScope !== "windows-local-fixed"
        || !["observed", "invalid-request", "configuration-unavailable", "stale", "timed-out", "unsupported-host", "unavailable"].includes(result.status)
        || !["none", "invalid-request", "configuration-load", "unsupported-defaults", "unsupported-host", "stale-inputs", "deadline", "cancelled", "invocation-failed"].includes(result.reason)
        || exitCode !== (result.status === "observed" ? 0 : 1)
        || !Array.isArray(result.sources) || result.sources.length > 32
        || (result.status !== "observed" && result.sources.length !== 0)
        || (result.status === "observed" && result.reason !== "none")) return null;
    for (const [index, source] of result.sources.entries()) {
        if (!isRecord(source) || !hasExactKeys(source, ["source", "selection", "kind", "reachability", "authentication", "release", "reason"])
            || source.source !== index + 1 || !["eligible", "disabled", "mapping-excluded"].includes(source.selection)
            || !["http", "local"].includes(source.kind)
            || !["notChecked", "reachable", "unreachable", "inconclusive", "timed-out", "unsupported"].includes(source.reachability)
            || !["notChecked", "notExercised", "unknown", "authRequired", "forbidden"].includes(source.authentication)
            || !["notChecked", "available", "notFoundInAnonymousView", "unknown"].includes(source.release)
            || !["none", "unsupported-source", "unsupported-resource", "unsupported-redirect", "unreachable", "invalid-response", "deadline", "http-status"].includes(source.reason)
            || ((!online || source.selection !== "eligible")
                && (source.reachability !== "notChecked" || source.authentication !== "notChecked"
                    || source.release !== "notChecked" || source.reason !== "none"))) return null;
    }
    return result;
}

async function runFeedDoctor({ verifiedDoctorDllPath, workspacePath, packageId, version, online = false,
    generation, signal, timeoutMs = 65_000, spawn = childProcess.spawn, environment = process.env } = {}) {
    if (signal?.aborted) throw abortError();
    if (typeof verifiedDoctorDllPath !== "string" || !path.isAbsolute(verifiedDoctorDllPath)
        || typeof workspacePath !== "string" || !path.isAbsolute(workspacePath) || workspacePath.length > 2048
        || typeof online !== "boolean" || !validFeedIdentity(packageId, version, generation)) return unavailable("invalid-request");
    const preflight = await preflightDotnet({ workspacePath, signal, timeoutMs, spawn, environment });
    if (preflight.status !== "available") return preflight;
    const input = JSON.stringify({ workspace: path.resolve(workspacePath), packageId, version, online, generation });
    if (input.length > 16_384) return unavailable("invalid-request");
    const invocation = await runProcess(preflight.dotnetPath, [path.resolve(verifiedDoctorDllPath)], {
        spawn, signal, timeoutMs: normalizedTimeout(timeoutMs), maxOutputBytes: 64 * 1024, environment, input
    });
    if (invocation.kind === "cancelled") throw abortError();
    if (invocation.kind === "timeout") return unavailable("doctor-timeout");
    if (invocation.kind === "output-too-large") return unavailable("doctor-output-too-large");
    if (invocation.kind !== "exit") return unavailable("doctor-invocation-failed");
    return decodeFeedDoctorResult(invocation.stdout, invocation.code, { online, generation }) ?? unavailable("doctor-result-invalid");
}

function parseRuntimeList(output) {
    return output.split(/\r?\n/).some(line => {
        const match = /^Microsoft\.NETCore\.App\s+(\d+)\./.exec(line.trim());
        return match && Number(match[1]) === RUNTIME_MAJOR;
    });
}

function hasInstalledSdk(output) {
    return output.split(/\r?\n/).some(line => /^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?\s+\[[^\r\n]+\]$/.test(line.trim()));
}

function normalizedTimeout(value) {
    return Number.isInteger(value) && value > 0
        ? Math.min(value, MAX_TIMEOUT_MS) : DEFAULT_TIMEOUT_MS;
}

async function preflightDotnet({ workspacePath, signal, timeoutMs, requireSdk = false, spawn = childProcess.spawn,
    environment = process.env } = {}) {
    if (signal?.aborted) throw abortError();
    if (typeof workspacePath !== "string" || !path.isAbsolute(workspacePath)) return unavailable("invalid-request");
    const workspace = path.resolve(workspacePath);
    const host = resolveTrustedDotnetHost(workspace, environment);
    if (!host) return unavailable("dotnet-host-missing");
    const preflight = await runProcess(host, ["--list-runtimes"], {
        spawn, signal, timeoutMs: normalizedTimeout(timeoutMs),
        maxOutputBytes: MAX_PREFLIGHT_OUTPUT_BYTES, environment
    });
    if (preflight.kind === "cancelled") throw abortError();
    if (preflight.kind === "timeout") return unavailable("dotnet-host-timeout");
    if (preflight.kind !== "exit" || preflight.code !== 0) return unavailable("dotnet-host-unavailable");
    if (!parseRuntimeList(preflight.stdout)) return unavailable("dotnet-runtime-missing");
    if (requireSdk) {
        const inventory = await runProcess(host, ["--list-sdks"], {
            spawn, signal, timeoutMs: normalizedTimeout(timeoutMs),
            maxOutputBytes: MAX_PREFLIGHT_OUTPUT_BYTES, environment
        });
        if (inventory.kind === "cancelled") throw abortError();
        if (inventory.kind === "timeout") return unavailable("dotnet-host-timeout");
        if (inventory.kind !== "exit" || inventory.code !== 0) return unavailable("dotnet-host-unavailable");
        if (!hasInstalledSdk(inventory.stdout)) return unavailable("dotnet-sdk-missing");
    }
    return { status: "available", dotnetPath: host };
}

// The caller must verify this DLL before invocation; this module does not locate or ship a doctor payload.
async function runDoctor({ verifiedDoctorDllPath, workspacePath, mode = "static", signal, timeoutMs, spawn = childProcess.spawn,
    environment = process.env } = {}) {
    if (signal?.aborted) throw abortError();
    if (typeof verifiedDoctorDllPath !== "string" || !path.isAbsolute(verifiedDoctorDllPath)
        || typeof workspacePath !== "string" || !path.isAbsolute(workspacePath)
        || !["static", "native"].includes(mode)) return unavailable("invalid-request");

    const workspace = path.resolve(workspacePath);
    const timeout = normalizedTimeout(timeoutMs);
    const preflight = await preflightDotnet({ workspacePath: workspace, signal, timeoutMs: timeout,
        spawn, environment });
    if (preflight.status !== "available") return preflight;

    const doctorDll = path.resolve(verifiedDoctorDllPath);
    const arguments_ = mode === "native" ? [doctorDll, "doctor", "--json", "--native-prerequisites"]
        : [doctorDll, "doctor", "--json", "--workspace", workspace];
    const invocation = await runProcess(preflight.dotnetPath, arguments_, {
        spawn, signal, timeoutMs: timeout, maxOutputBytes: MAX_DOCTOR_OUTPUT_BYTES, environment
    });
    if (invocation.kind === "cancelled") throw abortError();
    if (invocation.kind === "timeout") return unavailable("doctor-timeout");
    if (invocation.kind === "output-too-large") return unavailable("doctor-output-too-large");
    if (invocation.kind !== "exit") return unavailable("doctor-invocation-failed");
    if (![0, 1, 2].includes(invocation.code)) return unavailable("doctor-invocation-failed");
    const result = mode === "native" ? decodeNativeDoctorResult(invocation.stdout, invocation.code)
        : decodeDoctorResult(invocation.stdout, invocation.code);
    if (result?.kind === "invocation-unavailable") return unavailable("doctor-invocation-failed");
    if (invocation.code === 2 && mode !== "native") return unavailable("doctor-invocation-failed");
    return result ?? unavailable("doctor-result-invalid");
}

module.exports = { preflightDotnet, runDoctor, runFeedDoctor, decodeDoctorResult, decodeNativeDoctorResult, decodeFeedDoctorResult };
