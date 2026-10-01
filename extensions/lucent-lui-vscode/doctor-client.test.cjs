"use strict";

const assert = require("node:assert/strict");
const childProcess = require("node:child_process");
const { EventEmitter } = require("node:events");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { PassThrough } = require("node:stream");
const { test } = require("node:test");
const { preflightDotnet, runDoctor, runFeedDoctor, decodeDoctorResult, decodeNativeDoctorResult, decodeFeedDoctorResult } = require("./doctor-client");

const CHECKS = [
    ["global-json", "build", "pass"], ["dotnet-sdk", "build", "pass"], ["sdk-selection", "build", "notChecked"],
    ["dotnet-runtime", "editor", "pass"], ["native-platform", "native", "pass"],
    ["native-prerequisites", "native", "notChecked"], ["feed-config", "restore", "pass"],
    ["feed-reachability", "restore", "notChecked"], ["project-requirements", "editor", "notChecked"]
];

function report(status = "available") {
    const checks = CHECKS.map(([code, capability, originalStatus]) => {
        const checkStatus = status === "blocked" && code === "dotnet-sdk" ? "fail" : originalStatus;
        return {
            code,
            capability,
            status: checkStatus,
            severity: checkStatus === "fail" ? "error" : checkStatus === "notChecked" ? "warning" : "info",
            scope: "static-offline",
            summary: `${code} check summary.`,
            evidence: null,
            expected: `${code} expected state.`,
            remedy: null
        };
    });
    const capabilities = ["build", "editor", "restore", "native"].map(name => {
        const relevant = checks.filter(check => check.capability === name);
        return {
            name,
            status: relevant.some(check => check.status === "fail") ? "blocked"
                : relevant.some(check => check.status === "notChecked") ? "notChecked" : "available"
        };
    });
    return { schemaVersion: 1, kind: "environment-doctor", scope: "static-offline", status, capabilities, checks };
}

function fixture(t) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "lucent-doctor-client-"));
    t.after(() => fs.rmSync(root, { recursive: true, force: true }));
    const workspacePath = path.join(root, "workspace");
    const hostDirectory = path.join(root, "dotnet");
    fs.mkdirSync(workspacePath);
    fs.mkdirSync(hostDirectory);
    const hostPath = path.join(hostDirectory, process.platform === "win32" ? "dotnet.exe" : "dotnet");
    fs.writeFileSync(hostPath, "test host placeholder");
    const verifiedDoctorDllPath = path.join(root, "verified", "Lucent.Tools.dll");
    return {
        root,
        workspacePath,
        hostDirectory,
        hostPath,
        verifiedDoctorDllPath,
        environment: { DOTNET_ROOT: hostDirectory, PATH: "" }
    };
}

function nativeReport(status = "available") {
    const codes = status === "unavailable" ? ["native-prerequisites"]
        : ["native-visual-studio", "native-cpp-x64", "native-windows-sdk", "native-prerequisites", "native-publish"];
    return { schemaVersion: 1, kind: "native-prerequisites-doctor", scope: "installed-windows-x64-toolchain", status,
        capabilities: [{ name: "native", status: status === "available" ? "observed" : "notChecked" }],
        checks: codes.map(code => {
            const state = status === "unavailable" || code === "native-publish" ? "notChecked"
                : code === "native-prerequisites" && status === "blocked" ? "fail" : "pass";
            return { code, capability: "native", status: state, scope: "installed-windows-x64-toolchain",
                severity: state === "fail" ? "error" : state === "notChecked" ? "warning" : "info",
                summary: "Installed registration observation.", expected: "Registered Windows x64 components.", evidence: null, remedy: null };
        }) };
}

test("native mode invokes only the explicit native flag after verified host runtime preflight", async t => {
    const input = fixture(t);
    for (const [status, code] of [["available", 0], ["blocked", 1], ["unavailable", 2]]) {
        const result = nativeReport(status);
        const fake = fakeSpawn([runtimeResponse(), { code, stdout: JSON.stringify(result) }]);
        assert.deepEqual(await runDoctor(runOptions(input, { mode: "native", spawn: fake.spawn })), result);
        assert.deepEqual(fake.calls[1].args, [path.resolve(input.verifiedDoctorDllPath), "doctor", "--json", "--native-prerequisites"]);
        assert.equal(fake.calls[1].options.cwd, os.tmpdir());
        assert.equal(fake.calls.length, 2);
    }
});

test("native result validation is separate from static validation and cannot certify publication", () => {
    const result = nativeReport();
    assert.equal(decodeDoctorResult(JSON.stringify(result), 0), null);
    assert.equal(decodeNativeDoctorResult(JSON.stringify(report()), 0), null);
    assert.equal(decodeNativeDoctorResult(JSON.stringify(result), 1), null);
    for (const mutate of [
        value => { value.capabilities[0].status = "available"; },
        value => { value.checks.at(-1).status = "pass"; value.checks.at(-1).severity = "info"; },
        value => { value.checks[0].scope = "static-offline"; },
        value => { value.checks[0].code = "native-platform"; },
        value => { value.checks.push(value.checks[0]); },
    ]) {
        const value = structuredClone(result);
        mutate(value);
        assert.equal(decodeNativeDoctorResult(JSON.stringify(value), 0), null);
    }
});

function fakeSpawn(responses = []) {
    const calls = [];
    const spawn = (program, args, options) => {
        const child = new EventEmitter();
        child.stdout = new PassThrough();
        child.stderr = new PassThrough();
        child.stdin = new PassThrough();
        child.killCount = 0;
        child.kill = () => {
            child.killCount++;
            setImmediate(() => child.emit("close", null, "SIGKILL"));
            return true;
        };
        calls.push({ program, args, options, child });
        const response = responses.shift();
        if (typeof response === "function") response(child, calls.at(-1));
        else if (response) {
            setImmediate(() => {
                if (response.stdout !== undefined) child.stdout.end(response.stdout);
                else child.stdout.end();
                if (response.stderr !== undefined) child.stderr.end(response.stderr);
                else child.stderr.end();
                setImmediate(() => child.emit("close", response.code));
            });
        }
        return child;
    };
    return { spawn, calls };
}

function runtimeResponse(version = "10.0.0") {
    return { code: 0, stdout: `Microsoft.NETCore.App ${version} [host runtime path]\n` };
}

function runOptions(input, overrides = {}) {
    return {
        verifiedDoctorDllPath: input.verifiedDoctorDllPath,
        workspacePath: input.workspacePath,
        environment: input.environment,
        timeoutMs: 2000,
        ...overrides
    };
}

function feedReport(request, status = "observed") {
    return { schemaVersion: 1, kind: "nuget-feed-doctor", scope: request.online ? "online-observation" : "effective-configuration",
        generation: request.generation, status, reason: status === "observed" ? "none" : "configuration-load", sources: [],
        authenticationPolicy: "anonymous-no-credentials", restoreReadiness: "notChecked", privateAvailability: "notChecked",
        configuredAuthentication: "notChecked", configurationScope: "windows-local-fixed" };
}

test("feed payload invocation uses only the verified DLL and bounded stdin JSON to EOF", async t => {
    const input = fixture(t);
    let received;
    const fake = fakeSpawn([runtimeResponse(), child => {
        let text = "";
        child.stdin.on("data", chunk => { text += chunk; });
        child.stdin.on("end", () => {
            received = JSON.parse(text);
            child.stdout.end(JSON.stringify(feedReport(received)));
            child.stderr.end();
            setImmediate(() => child.emit("close", 0));
        });
    }]);
    const options = runOptions(input, { packageId: "Lucent.Core", version: "0.3.0-dev.101.1", online: false, generation: "feed-current", spawn: fake.spawn });
    const result = await runFeedDoctor(options);
    assert.equal(result.status, "observed");
    assert.deepEqual(received, { workspace: path.resolve(input.workspacePath), packageId: "Lucent.Core", version: "0.3.0-dev.101.1", online: false, generation: "feed-current" });
    assert.deepEqual(fake.calls[1].args, [path.resolve(input.verifiedDoctorDllPath)]);
    assert.deepEqual(fake.calls[1].options.stdio, ["pipe", "pipe", "pipe"]);
    assert.equal(fake.calls[1].child.stdin.writableEnded, true);
    assert.equal(fake.calls[1].options.cwd, os.tmpdir());
});

test("feed decoder rejects stale generations, raw fields and impossible offline success claims", () => {
    const request = { online: false, generation: "feed-current" };
    const report = feedReport(request);
    assert.deepEqual(decodeFeedDoctorResult(JSON.stringify(report), 0, request), report);
    assert.equal(decodeDoctorResult(JSON.stringify(report), 0), null);
    for (const mutate of [
        value => { value.generation = "stale"; },
        value => { value.sourceName = "SECRET https://private.invalid"; },
        value => { value.reason = "SECRET C:\\private"; },
        value => { value.privateAvailability = "available"; },
        value => { value.sources = [{ source: 1, selection: "eligible", kind: "http", reachability: "reachable", authentication: "notExercised", release: "available", reason: "none" }]; },
    ]) {
        const value = structuredClone(report);
        mutate(value);
        assert.equal(decodeFeedDoctorResult(JSON.stringify(value), 0, request), null);
    }
    assert.equal(decodeFeedDoctorResult(JSON.stringify(report), 1, request), null);
});

test("feed cancellation waits for owned process close and discards late stdout", async t => {
    const input = fixture(t);
    let child;
    let started;
    const ready = new Promise(resolve => { started = resolve; });
    const fake = fakeSpawn([runtimeResponse(), value => { child = value; started(); }]);
    const controller = new AbortController();
    const running = runFeedDoctor(runOptions(input, { packageId: "Lucent.Core", version: "1.0.0", generation: "current", spawn: fake.spawn, signal: controller.signal }));
    await ready;
    controller.abort();
    await assert.rejects(running, { name: "AbortError" });
    assert.equal(child.killCount, 1);
    child.stdout.end(JSON.stringify(feedReport({ generation: "current", online: false })));
    child.stderr.end();
});

function trackedSignal(controller) {
    const listeners = new Set();
    const signal = {
        get aborted() { return controller.signal.aborted; },
        addEventListener(type, listener, options) {
            assert.equal(type, "abort");
            listeners.add(listener);
            controller.signal.addEventListener(type, listener, options);
        },
        removeEventListener(type, listener) {
            assert.equal(type, "abort");
            listeners.delete(listener);
            controller.signal.removeEventListener(type, listener);
        }
    };
    return { signal, listeners };
}

function isProcessRunning(pid) {
    try { process.kill(pid, 0); return true; }
    catch (error) { return error?.code !== "ESRCH"; }
}

async function readPidRecord(file, timeoutMs = 10_000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        try {
            const value = JSON.parse(fs.readFileSync(file, "utf8"));
            if ([value.doctor, value.child, value.grandchild].every(pid => Number.isInteger(pid) && pid > 0))
                return value;
        } catch { /* The child writes the record after both descendants start. */ }
        await new Promise(resolve => setTimeout(resolve, 20));
    }
    assert.fail("The owned process tree did not publish its PID record before the deadline.");
}

async function waitForProcessesToExit(pids, timeoutMs = 5_000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (Object.values(pids).every(pid => !isProcessRunning(pid))) return;
        await new Promise(resolve => setTimeout(resolve, 20));
    }
    assert.fail(`Owned process tree still has live PIDs: ${Object.entries(pids)
        .filter(([, pid]) => isProcessRunning(pid)).map(([name, pid]) => `${name}=${pid}`).join(", ")}`);
}

async function terminateOwnedTestPid(pid) {
    if (!Number.isInteger(pid) || pid <= 0 || pid === process.pid || !isProcessRunning(pid)) return;
    if (process.platform === "win32") {
        const systemRoot = process.env.SystemRoot;
        assert.ok(systemRoot && path.isAbsolute(systemRoot), "Windows SystemRoot is required for owned-tree cleanup.");
        const taskkill = path.join(systemRoot, "System32", "taskkill.exe");
        await new Promise(resolve => {
            let killer;
            try {
                killer = childProcess.spawn(taskkill, ["/PID", String(pid), "/T", "/F"], {
                    cwd: os.tmpdir(), shell: false, windowsHide: true, stdio: "ignore"
                });
            } catch { resolve(); return; }
            killer.once("close", resolve);
            killer.once("error", resolve);
            setTimeout(() => { try { killer.kill("SIGKILL"); } catch { /* Test cleanup is bounded. */ } resolve(); }, 2_000).unref?.();
        });
        return;
    }
    try { process.kill(pid, "SIGKILL"); } catch { /* The owned process may have exited already. */ }
}

async function cleanupOwnedTestTree(pids) {
    if (!pids) return;
    if (process.platform !== "win32") {
        try { process.kill(-pids.doctor, "SIGKILL"); } catch { /* The owned process group may have exited already. */ }
    }
    for (const pid of [pids.doctor, pids.child, pids.grandchild]) await terminateOwnedTestPid(pid);
}

test("standalone preflight returns the absolute host without invoking the doctor", async t => {
    const input = fixture(t);
    const fake = fakeSpawn([runtimeResponse()]);
    const controller = new AbortController();
    const tracked = trackedSignal(controller);
    const result = await preflightDotnet({ workspacePath: input.workspacePath, spawn: fake.spawn,
        environment: input.environment, signal: tracked.signal, timeoutMs: 2000 });
    assert.deepEqual(result, { status: "available", dotnetPath: fs.realpathSync(input.hostPath) });
    assert.equal(fake.calls.length, 1);
    assert.deepEqual(fake.calls[0].args, ["--list-runtimes"]);
    assert.equal(fake.calls[0].options.cwd, os.tmpdir());
    assert.equal(fake.calls[0].options.env, input.environment);
    assert.equal(tracked.listeners.size, 0);
});

test("cancelling a pending preflight kills its host probe and removes the abort listener", async t => {
    const input = fixture(t);
    let started;
    const ready = new Promise(resolve => { started = resolve; });
    const fake = fakeSpawn([child => started(child)]);
    const controller = new AbortController();
    const tracked = trackedSignal(controller);
    const pending = preflightDotnet({ workspacePath: input.workspacePath, spawn: fake.spawn,
        environment: input.environment, signal: tracked.signal, timeoutMs: 2000 });
    const child = await ready;
    controller.abort();
    await assert.rejects(pending, { name: "AbortError" });
    assert.equal(child.killCount, 1);
    assert.equal(tracked.listeners.size, 0);
    assert.equal(fake.calls.length, 1);
});

test("semantic preflight requires an SDK inventory while static doctor can still run", async t => {
    const input = fixture(t);
    const semantic = fakeSpawn([runtimeResponse(), { code: 0, stdout: "" }]);
    const blocked = await preflightDotnet({ workspacePath: input.workspacePath, requireSdk: true,
        spawn: semantic.spawn, environment: input.environment });
    assert.equal(blocked.reason, "dotnet-sdk-missing");
    assert.deepEqual(semantic.calls.map(call => call.args), [["--list-runtimes"], ["--list-sdks"]]);
    assert.equal(semantic.calls.every(call => call.program === fs.realpathSync(input.hostPath)
        && call.options.cwd === os.tmpdir()), true);

    const staticDoctor = fakeSpawn([runtimeResponse(), { code: 0, stdout: JSON.stringify(report()) }]);
    const available = await runDoctor(runOptions(input, { spawn: staticDoctor.spawn }));
    assert.equal(available.status, "available");
    assert.equal(staticDoctor.calls.length, 2);
    assert.deepEqual(staticDoctor.calls[1].args.slice(1, 3), ["doctor", "--json"]);
});

test("missing host is capability-scoped and never starts a process", async t => {
    const input = fixture(t);
    const fake = fakeSpawn();
    const result = await runDoctor(runOptions(input, { environment: { PATH: "" }, spawn: fake.spawn }));
    assert.deepEqual(result, {
        schemaVersion: 1,
        kind: "environment-doctor-client",
        capability: "environment-doctor",
        status: "unavailable",
        reason: "dotnet-host-missing"
    });
    assert.equal(fake.calls.length, 0);
});

test("workspace-local dotnet is never selected; missing runtime prevents DLL invocation", async t => {
    const input = fixture(t);
    fs.rmSync(input.hostDirectory, { recursive: true, force: true });
    const localDirectory = path.join(input.workspacePath, ".dotnet");
    fs.mkdirSync(localDirectory);
    fs.writeFileSync(path.join(localDirectory, process.platform === "win32" ? "dotnet.exe" : "dotnet"), "workspace host");
    const fake = fakeSpawn([runtimeResponse("9.0.0")]);
    const environment = { DOTNET_ROOT: localDirectory, PATH: localDirectory };
    const result = await runDoctor(runOptions(input, { environment, spawn: fake.spawn }));
    assert.equal(result.status, "unavailable");
    assert.equal(result.capability, "environment-doctor");
    assert.equal(result.reason, "dotnet-host-missing");
    assert.equal(fake.calls.length, 0);
});

test("missing .NET 10 runtime is detected before the verified DLL is invoked", async t => {
    const input = fixture(t);
    const fake = fakeSpawn([runtimeResponse("9.0.0")]);
    const result = await runDoctor(runOptions(input, { spawn: fake.spawn }));
    assert.equal(result.reason, "dotnet-runtime-missing");
    assert.equal(fake.calls.length, 1);
    assert.deepEqual(fake.calls[0].args, ["--list-runtimes"]);
    assert.equal(path.isAbsolute(fake.calls[0].program), true);
    assert.equal(fake.calls[0].options.cwd, os.tmpdir());
});

test("doctor launch uses the absolute verified DLL and an explicit workspace from a neutral cwd", async t => {
    const input = fixture(t);
    const fake = fakeSpawn([runtimeResponse(), { code: 0, stdout: `${JSON.stringify(report())}\n` }]);
    const result = await runDoctor(runOptions(input, { spawn: fake.spawn }));
    assert.equal(result.status, "available");
    assert.equal(fake.calls.length, 2);
    assert.deepEqual(fake.calls[1].args, [path.resolve(input.verifiedDoctorDllPath), "doctor", "--json", "--workspace", path.resolve(input.workspacePath)]);
    assert.equal(fake.calls[1].options.cwd, os.tmpdir());
    assert.equal(fake.calls[0].options.env, input.environment);
    assert.equal(fake.calls[1].options.env, input.environment);
});

test("malformed results and oversized output produce stable failures without diagnostics", async t => {
    const malformedInput = fixture(t);
    const malformed = fakeSpawn([runtimeResponse(), {
        code: 0, stdout: "token=private D:\\workspace\\secret", stderr: "password=not-for-display"
    }]);
    const malformedResult = await runDoctor(runOptions(malformedInput, { spawn: malformed.spawn }));
    assert.equal(malformedResult.reason, "doctor-result-invalid");
    assert.equal(JSON.stringify(malformedResult).includes("private"), false);
    assert.equal(JSON.stringify(malformedResult).includes("not-for-display"), false);

    const oversizedInput = fixture(t);
    const oversized = fakeSpawn([runtimeResponse(), { code: 0, stdout: Buffer.alloc(256 * 1024 + 1, 0x41) }]);
    const oversizedResult = await runDoctor(runOptions(oversizedInput, { spawn: oversized.spawn }));
    assert.equal(oversizedResult.reason, "doctor-output-too-large");
    assert.equal(oversized.calls[1].child.killCount, 1);
});

test("decoder interprets exit status only when it agrees with the strict doctor result", () => {
    const available = report("available");
    const blocked = report("blocked");
    assert.deepEqual(decodeDoctorResult(JSON.stringify(available), 0), available);
    assert.deepEqual(decodeDoctorResult(JSON.stringify(blocked), 1), blocked);
    assert.equal(decodeDoctorResult(JSON.stringify(available), 1), null);
    assert.equal(decodeDoctorResult("{malformed", 0), null);
    assert.deepEqual(decodeDoctorResult(JSON.stringify({
        schemaVersion: 1, kind: "environment-doctor", scope: "static-offline", status: "unavailable",
        error: { code: "doctor-invocation", message: "safe generic message" }
    }), 2), { kind: "invocation-unavailable" });
    assert.equal(decodeDoctorResult(JSON.stringify(blocked), 0), null);
    const unknownField = structuredClone(available);
    unknownField.checks[0].unexpected = "rejected";
    assert.equal(decodeDoctorResult(JSON.stringify(unknownField), 0), null);
});

test("failed doctor exit is mapped to a stable capability state", async t => {
    const input = fixture(t);
    const fake = fakeSpawn([runtimeResponse(), { code: 2, stdout: "private path or runtime error", stderr: "token=private" }]);
    const result = await runDoctor(runOptions(input, { spawn: fake.spawn }));
    assert.equal(result.status, "unavailable");
    assert.equal(result.capability, "environment-doctor");
    assert.equal(result.reason, "doctor-invocation-failed");
    assert.equal(JSON.stringify(result).includes("private"), false);
});

test("timeout terminates a nonresponding doctor and returns a stable state", async t => {
    const input = fixture(t);
    const fake = fakeSpawn([runtimeResponse(), () => {}]);
    const result = await runDoctor(runOptions(input, { spawn: fake.spawn, timeoutMs: 25 }));
    assert.equal(result.reason, "doctor-timeout");
    assert.equal(fake.calls[1].child.killCount, 1);

    // The test runner can keep an unreferenced deadline alive. A standalone caller cannot.
    const script = [
        'const {EventEmitter}=require("node:events");',
        'const {PassThrough}=require("node:stream");',
        `const {runDoctor}=require(${JSON.stringify(path.join(__dirname, "doctor-client.js"))});`,
        fakeSpawn.toString(),
        `const fake=fakeSpawn([${JSON.stringify(runtimeResponse())},()=>{}]);`,
        `runDoctor({...${JSON.stringify(runOptions(input, { timeoutMs: 25 }))},spawn:fake.spawn})`,
        '.then(result=>console.log(JSON.stringify({reason:result.reason,killCount:fake.calls[1].child.killCount})),',
        'error=>{console.error(error);process.exitCode=1;});'
    ].join("\n");
    // This proves event-loop ownership, not a two-second Node startup budget.
    // Allow startup plus the client's bounded five-second cleanup, and assert
    // the actual result. An unreferenced deadline exits early with no result.
    const standalone = childProcess.spawn(process.execPath, ["-e", script], {
        cwd: os.tmpdir(), windowsHide: true, stdio: ["ignore", "pipe", "pipe"]
    });
    let stdout = "";
    let stderr = "";
    standalone.stdout.on("data", chunk => { stdout += chunk; });
    standalone.stderr.on("data", chunk => { stderr += chunk; });
    const watchdog = setTimeout(() => standalone.kill(), 15_000);
    t.after(() => { clearTimeout(watchdog); if (standalone.exitCode === null) standalone.kill(); });
    const status = await new Promise((resolve, reject) => {
        standalone.once("error", reject);
        standalone.once("close", (code, signal) => resolve({ code, signal }));
    });
    clearTimeout(watchdog);
    assert.deepEqual(status, { code: 0, signal: null }, stderr);
    assert.equal(stdout.trim(), JSON.stringify({ reason: "doctor-timeout", killCount: 1 }));
});

test("cancellation kills the doctor and ignores late output", async t => {
    const input = fixture(t);
    let doctorSpawned;
    const spawned = new Promise(resolve => { doctorSpawned = resolve; });
    const fake = fakeSpawn([runtimeResponse(), child => doctorSpawned(child)]);
    const controller = new AbortController();
    const tracked = trackedSignal(controller);
    const invocation = runDoctor(runOptions(input, { spawn: fake.spawn, signal: tracked.signal }));
    const doctorProcess = await spawned;
    controller.abort();
    await assert.rejects(invocation, { name: "AbortError" });
    assert.equal(doctorProcess.killCount, 1);
    assert.equal(tracked.listeners.size, 0);
    doctorProcess.stdout.end(JSON.stringify(report()));
    doctorProcess.stderr.end();
    doctorProcess.emit("close", 0);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(fake.calls.length, 2);
});

test("cancelling the doctor terminates its real child and grandchild processes", async t => {
    const input = fixture(t);
    const pidFile = path.join(input.root, "owned-process-tree.json");
    const inventoryScript = [
        'const fs=require("node:fs");',
        'const {spawn}=require("node:child_process");',
        'const grandchild=spawn(process.execPath,["-e","setInterval(()=>{},1000)"],{stdio:"ignore"});',
        'fs.writeFileSync(process.env.LUCENT_TEST_TREE_FILE,JSON.stringify({doctor:process.ppid,child:process.pid,grandchild:grandchild.pid}));',
        'setInterval(()=>{},1000);'
    ].join("");
    const doctorScript = [
        'const {spawn}=require("node:child_process");',
        `spawn(process.execPath,["-e",${JSON.stringify(inventoryScript)}],{stdio:"ignore"});`,
        'setInterval(()=>{},1000);'
    ].join("");
    const fakePreflight = fakeSpawn([runtimeResponse()]);
    const controller = new AbortController();
    let doctorProcess;
    let taskkillExitCode = null;
    let taskkillOutput = "";
    let invocation;
    let pids;
    const spawn = (program, args, options) => {
        if (args[0] === "--list-runtimes") return fakePreflight.spawn(program, args, options);
        if (path.basename(program).toLowerCase() === "taskkill.exe") {
            const killer = childProcess.spawn(program, args, { ...options, stdio: ["ignore", "pipe", "pipe"] });
            killer.stdout.on("data", chunk => { taskkillOutput += chunk.toString("utf8"); });
            killer.stderr.on("data", chunk => { taskkillOutput += chunk.toString("utf8"); });
            killer.once("close", code => { taskkillExitCode = code; });
            return killer;
        }
        assert.equal(args[1], "doctor");
        doctorProcess = childProcess.spawn(process.execPath, ["-e", doctorScript], {
            ...options,
            env: { ...process.env, LUCENT_TEST_TREE_FILE: pidFile }
        });
        return doctorProcess;
    };

    try {
        invocation = runDoctor(runOptions(input, { spawn, signal: controller.signal, timeoutMs: 15_000 }));
        pids = await readPidRecord(pidFile);
        assert.equal(pids.doctor, doctorProcess.pid);
        assert.ok(pids.child !== pids.doctor && pids.grandchild !== pids.child);

        controller.abort();
        const outcome = await invocation.then(
            result => ({ result }),
            error => ({ error: { name: error.name, message: error.message } })
        );
        assert.equal(outcome.error?.name, "AbortError", `Cancellation did not settle as AbortError: ${JSON.stringify({
            ...outcome,
            taskkillExitCode,
            taskkillOutput,
            doctorExitCode: doctorProcess.exitCode,
            doctorSignalCode: doctorProcess.signalCode
        })}`);
        if (process.platform === "win32") assert.equal(taskkillExitCode, 0);
        await waitForProcessesToExit(pids);
    } finally {
        if (!controller.signal.aborted) controller.abort();
        if (invocation) {
            let waitTimer;
            await Promise.race([
                invocation.catch(() => {}),
                new Promise(resolve => { waitTimer = setTimeout(resolve, 6_000); })
            ]);
            clearTimeout(waitTimer);
        }
        if (pids && Object.values(pids).some(isProcessRunning)) {
            await cleanupOwnedTestTree(pids);
            await waitForProcessesToExit(pids, 2_000);
        }
    }
});
