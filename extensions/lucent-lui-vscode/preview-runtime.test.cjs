"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const os = require("node:os");
const { createHash } = require("node:crypto");
const { test } = require("node:test");
const { createPreviewRuntime } = require("./preview-runtime");
const { createPreviewCoordinator } = require("./preview-coordinator");

const pixel = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1ZkAAAAASUVORK5CYII=", "base64");

async function fixture(t, { uncertain = false, failBuild = false, failWorker = false, logBytes = 0, sdkError,
    retentionPolicy, onWorkerRequest, uncertainWorker, corruptEcho, catalogScenarios } = {}) {
    const storageDirectory = await fs.mkdtemp(path.join(os.tmpdir(), "lucent-preview-runtime-"));
    t.after(async () => {
        assert.equal(path.dirname(storageDirectory), os.tmpdir());
        assert.ok(path.basename(storageDirectory).startsWith("lucent-preview-runtime-"));
        await fs.rm(storageDirectory, { recursive: true, force: true });
    });
    let trusted = true;
    const calls = [];
    const workerRequests = [];
    let buildDirectory;
    async function supervise(_supervisor, request, { signal } = {}) {
        calls.push(request.args[0]);
        const ok = { status: "completed", exitCode: 0, treeReaped: true };
        if (request.args[0] === "build") {
            const input = JSON.parse(await fs.readFile(request.args[2], "utf8"));
            buildDirectory = input.outputDirectory;
            const publish = path.join(buildDirectory, "artifacts", "publish");
            await fs.mkdir(publish, { recursive: true });
            const report = { protocolVersion: 1, kind: "preview-build-report", status: "succeeded", request: input,
                projectTargetDigest: "a".repeat(64), inputDigest: "b".repeat(64), artifactDigest: "c".repeat(64),
                entryPoint: path.join(publish, "Fixture.exe"), projects: [], watchDirectories: [] };
            await fs.writeFile(request.args[4], JSON.stringify(report));
            await fs.writeFile(path.join(request.logDirectory, "stdout.log"), JSON.stringify({ protocolVersion: 1,
                kind: "preview-build", status: "succeeded", reportPath: request.args[4] }));
            if (uncertain) return { ...ok, treeReaped: false };
            if (failBuild) {
                if (sdkError) await fs.writeFile(path.join(buildDirectory, "publish.stdout.log"),
                    typeof sdkError === "function" ? sdkError(input) : sdkError);
                await fs.writeFile(path.join(request.logDirectory, "stderr.log"),
                    `compiler failure for generation ${input.generation}\n` + "x".repeat(logBytes));
                return { ...ok, exitCode: 17 };
            }
        } else if (request.args[0] === "verify") {
            const report = JSON.parse(await fs.readFile(request.args[2], "utf8"));
            await fs.writeFile(path.join(request.logDirectory, "stdout.log"), JSON.stringify({ protocolVersion: 1,
                kind: "preview-build-verification", status: "fresh", sessionId: report.request.sessionId,
                generation: report.request.generation, requestId: report.request.requestId,
                projectTargetDigest: report.projectTargetDigest, inputDigest: report.inputDigest, artifactDigest: report.artifactDigest }));
        } else {
            assert.equal(request.args[0], "--request");
            const input = JSON.parse(await fs.readFile(request.args[1], "utf8"));
            workerRequests.push({ input, program: request.program });
            await onWorkerRequest?.(input, signal);
            if (signal?.aborted) return { ...ok, status: "cancelled" };
            if (uncertainWorker === input.kind) return { ...ok, treeReaped: false };
            const defaults = { logicalWidth: 1, logicalHeight: 1, scale: 1, colorScheme: "light", contrast: "normal",
                density: 1, culture: "", uiCulture: "", initialTime: "1970-01-01T00:00:00.0000000+00:00" };
            if (input.kind === "preview-catalog-request") {
                const { outputDirectory, ...identity } = input;
                await fs.writeFile(path.join(outputDirectory, "catalog.json"), JSON.stringify({ ...identity,
                    kind: "preview-catalog-result", scenarios: catalogScenarios ?? [{ id: "card/empty", title: "Empty",
                        sourceProject: "Fixture.csproj", sourceDocument: "Card.lui", sourceComponent: "Fixture.Card", ...defaults }] }));
                return ok;
            }
            assert.equal(input.kind, "preview-capture-request");
            if (failWorker) {
                await fs.writeFile(path.join(request.logDirectory, "stderr.log"), "scenario cleanup failed\n");
                return { ...ok, exitCode: 19 };
            }
            const { outputDirectory, ...correlation } = input;
            await fs.writeFile(path.join(outputDirectory, "frame.png"), pixel);
            await fs.writeFile(path.join(outputDirectory, "result.json"), JSON.stringify({ ...correlation, kind: "preview-frame-result",
                culture: defaults.culture, uiCulture: defaults.uiCulture, initialTime: defaults.initialTime,
                frameSequence: 1, fileName: "frame.png", byteLength: pixel.length,
                sha256: createHash("sha256").update(pixel).digest("hex"), width: 1, height: 1, ...corruptEcho }));
        }
        return ok;
    }
    const messages = [];
    const makeRuntime = () => createPreviewRuntime({ storageDirectory, supervisorPath: path.join(storageDirectory, "supervisor.exe"),
        buildToolPath: path.join(storageDirectory, "build.exe"), isTrusted: () => trusted, supervise,
        retentionPolicy, log: message => messages.push(message) });
    const request = generation => ({ sessionId: "session", generation: String(generation), requestId: "request-" + generation,
        selection: { projectPath: path.join(storageDirectory, "Fixture.csproj"), targetFramework: "net10.0", scenarioId: "card/empty" } });
    return { runtime: makeRuntime(), makeRuntime, request, storageDirectory, calls, messages, workerRequests,
        trust(value) { trusted = value; }, get buildDirectory() { return buildDirectory; } };
}

test("runtime removes completed generation payloads while the admitted frame remains usable", async t => {
    const f = await fixture(t);
    const coordinator = createPreviewCoordinator({ ...f.runtime, isTrusted: () => true, isSupported: () => true });
    await coordinator.start({ projectPath: path.join(f.storageDirectory, "Fixture.csproj"), targetFramework: "net10.0", scenarioId: "card/empty" });
    assert.equal(coordinator.state.phase, "current", coordinator.state.diagnostic);
    assert.deepEqual(f.calls, ["build", "verify", "--request", "verify", "--request", "verify"]);
    assert.deepEqual(coordinator.state.frame.png, pixel);
    await assert.rejects(fs.stat(path.join(f.buildDirectory, "artifacts")), { code: "ENOENT" });
    await assert.rejects(fs.stat(path.dirname(f.buildDirectory)), { code: "ENOENT" });
    assert.deepEqual(await fs.readdir(f.storageDirectory), []);
});

test("unconfirmed build cleanup retains its generation and blocks later executable work", async t => {
    const f = await fixture(t, { uncertain: true });
    const coordinator = createPreviewCoordinator({ ...f.runtime, isTrusted: () => true, isSupported: () => true });
    const selection = { projectPath: path.join(f.storageDirectory, "Fixture.csproj"), targetFramework: "net10.0", scenarioId: "card/empty" };
    await coordinator.start(selection);
    await coordinator.start(selection);
    assert.equal(coordinator.state.phase, "blocked");
    assert.deepEqual(f.calls, ["build"]);
    assert.ok((await fs.stat(path.join(f.buildDirectory, "artifacts"))).isDirectory());
    await assert.rejects(f.runtime.release({ root: path.dirname(f.buildDirectory) }), /unowned/);
    assert.ok((await fs.stat(path.join(f.buildDirectory, "artifacts"))).isDirectory());
});

async function history(storageDirectory) {
    const directory = path.join(storageDirectory, "diagnostics");
    const entries = [];
    for (const name of await fs.readdir(directory)) {
        const root = path.join(directory, name);
        let summary;
        try { summary = JSON.parse(await fs.readFile(path.join(root, "diagnostic.json"), "utf8")); }
        catch (error) { if (error.code === "ENOENT") continue; throw error; }
        const marker = JSON.parse(await fs.readFile(path.join(root, "retention.json"), "utf8"));
        let bytes = 0;
        for (const file of await fs.readdir(root)) bytes += (await fs.stat(path.join(root, file))).size;
        entries.push({ root, summary, marker, bytes });
    }
    return entries.sort((left, right) => left.marker.createdAt - right.marker.createdAt);
}

test("successive completed generations leave no disk payloads", async t => {
    const f = await fixture(t);
    for (let generation = 1; generation <= 6; generation++) {
        const request = f.request(generation);
        const artifact = await f.runtime.build(request);
        await f.runtime.discover(artifact, request);
        const frame = await f.runtime.render(artifact, request);
        await f.runtime.release(artifact);
        assert.deepEqual(frame.png, pixel);
        assert.deepEqual(await fs.readdir(f.storageDirectory), []);
    }
});

test("confirmed build failures retain only the newest three actionable diagnostic bundles", async t => {
    const f = await fixture(t, { failBuild: true });
    for (let generation = 1; generation <= 5; generation++) {
        await assert.rejects(f.runtime.build(f.request(generation)), error => {
            assert.match(error.message, /Preview build failed \(completed, exit 17\)/);
            assert.match(error.message, /Retained diagnostics:/);
            assert.doesNotMatch(error.message, /Logs:/);
            return true;
        });
        await assert.rejects(fs.stat(path.dirname(f.buildDirectory)), { code: "ENOENT" });
    }
    const entries = await history(f.storageDirectory);
    assert.deepEqual(entries.map(entry => entry.summary.request.generation), ["3", "4", "5"]);
    assert.deepEqual(await fs.readdir(f.storageDirectory), ["diagnostics"]);
    for (const entry of entries) {
        assert.equal(entry.marker.treeReaped, true);
        assert.equal(entry.marker.kind, "preview-confirmed-diagnostic");
        const stderr = entry.summary.logs.find(item => item.file.endsWith("-stderr.log"));
        assert.ok(stderr);
        assert.match(await fs.readFile(path.join(entry.root, stderr.file), "utf8"),
            new RegExp("compiler failure for generation " + entry.summary.request.generation));
        assert.deepEqual((await fs.readdir(entry.root)).sort(),
            ["diagnostic.json", "retention.json", ...entry.summary.logs.map(item => item.file)].sort());
    }
});

test("diagnostic history enforces actual bytes and records truncated output", async t => {
    const f = await fixture(t, { failBuild: true, logBytes: 16384,
        retentionPolicy: { maxFailures: 3, maxBytes: 8192 } });
    for (let generation = 1; generation <= 5; generation++)
        await assert.rejects(f.runtime.build(f.request(generation)), /exit 17/);
    const entries = await history(f.storageDirectory);
    assert.ok(entries.length >= 1 && entries.length < 3);
    assert.ok(entries.reduce((bytes, entry) => bytes + entry.bytes, 0) <= 8192);
    assert.equal(entries.at(-1).summary.request.generation, "5");
    const stderr = entries.at(-1).summary.logs.find(item => item.file.endsWith("-stderr.log"));
    assert.equal(stderr.truncated, true);
    assert.match(await fs.readFile(path.join(entries.at(-1).root, stderr.file), "utf8"),
        /compiler failure for generation 5/);
});

test("SDK compiler diagnostics survive generation deletion within the shared byte budget", async t => {
    const compilerError = "Broken.lui(12,4): error CS0103: The name 'MissingValue' does not exist in the current context\n";
    const f = await fixture(t, { failBuild: true, logBytes: 16384, sdkError: compilerError,
        retentionPolicy: { maxFailures: 3, maxBytes: 8192 } });
    await assert.rejects(f.runtime.build(f.request(1)), /exit 17/);
    await assert.rejects(fs.stat(path.dirname(f.buildDirectory)), { code: "ENOENT" });
    const [entry] = await history(f.storageDirectory);
    assert.ok(entry.bytes <= 8192);
    const sdkLog = entry.summary.logs.find(item => item.file === "sdk-publish.stdout.log");
    assert.ok(sdkLog);
    assert.equal(sdkLog.truncated, false);
    assert.equal(await fs.readFile(path.join(entry.root, sdkLog.file), "utf8"), compilerError);
});

test("history limits survive runtime restart and preserve unknown and external evidence", async t => {
    const f = await fixture(t, { failBuild: true });
    const unknown = path.join(f.storageDirectory, "generation-unknown");
    const historyUnknown = path.join(f.storageDirectory, "diagnostics", "failure-unknown");
    const external = await fs.mkdtemp(path.join(os.tmpdir(), "lucent-preview-first-failure-"));
    t.after(async () => {
        assert.equal(path.dirname(external), os.tmpdir());
        assert.ok(path.basename(external).startsWith("lucent-preview-first-failure-"));
        await fs.rm(external, { recursive: true, force: true });
    });
    for (const directory of [unknown, historyUnknown, external]) {
        await fs.mkdir(directory, { recursive: true });
        await fs.writeFile(path.join(directory, "evidence.log"), "original failed evidence");
    }
    for (let generation = 1; generation <= 3; generation++)
        await assert.rejects(f.runtime.build(f.request(generation)), /exit 17/);
    const restarted = f.makeRuntime();
    await assert.rejects(restarted.build(f.request(4)), /exit 17/);
    assert.deepEqual((await history(f.storageDirectory)).map(entry => entry.summary.request.generation), ["2", "3", "4"]);
    for (const directory of [unknown, historyUnknown, external])
        assert.equal(await fs.readFile(path.join(directory, "evidence.log"), "utf8"), "original failed evidence");
    await assert.rejects(restarted.release({ root: unknown }), /unowned/);
});

test("a failed worker returns a live retained location after its generation is deleted", async t => {
    const f = await fixture(t, { failWorker: true });
    const coordinator = createPreviewCoordinator({ ...f.runtime, isTrusted: () => true, isSupported: () => true });
    await coordinator.start(f.request(1).selection);
    assert.equal(coordinator.state.phase, "error");
    assert.match(coordinator.state.diagnostic, /Preview worker failed \(completed, exit 19\)/);
    assert.doesNotMatch(coordinator.state.diagnostic, /Logs:/);
    const [entry] = await history(f.storageDirectory);
    assert.ok(coordinator.state.diagnostic.includes(entry.root));
    await assert.rejects(fs.stat(path.dirname(f.buildDirectory)), { code: "ENOENT" });
    const stderr = entry.summary.logs.find(item => item.file.startsWith("worker-") && item.file.endsWith("-stderr.log"));
    assert.equal(await fs.readFile(path.join(entry.root, stderr.file), "utf8"), "scenario cleanup failed\n");
});

test("the executable adapter independently rejects trust loss", async t => {
    const f = await fixture(t);
    f.trust(false);
    await assert.rejects(f.runtime.build({ selection: {} }, new AbortController().signal), /Trust/);
    assert.deepEqual(f.calls, []);
    assert.deepEqual(await fs.readdir(f.storageDirectory), []);
});

test("catalog and capture share verified artifact identity and resolve only session presentation overrides", async t => {
    const f = await fixture(t);
    const request = f.request(1);
    request.presentationId = "effective-1";
    request.selection.presentation = { density: 0.3, colorScheme: "dark", contrast: "high" };
    const artifact = await f.runtime.build(request);
    await assert.rejects(f.runtime.render(artifact, request), /not registered/);
    const catalog = await f.runtime.discover(artifact, request);
    const frame = await f.runtime.render(artifact, request);
    const [listed, captured] = f.workerRequests;
    assert.equal(listed.program, captured.program);
    assert.equal(listed.program, artifact.report.entryPoint);
    assert.notEqual(listed.input.requestId, captured.input.requestId);
    for (const field of ["sessionId", "generation", "projectTargetDigest", "inputDigest", "artifactDigest"])
        assert.equal(listed.input[field], captured.input[field]);
    assert.equal(captured.input.protocolVersion, 2);
    assert.equal(captured.input.kind, "preview-capture-request");
    assert.equal(captured.input.density, 0.30000001192092896);
    assert.equal(captured.input.colorScheme, "dark");
    assert.equal(captured.input.contrast, "high");
    assert.equal(frame.effectivePresentation.density, 0.30000001192092896);
    assert.equal(frame.initialTime, "1970-01-01T00:00:00.0000000+00:00");
    assert.equal(catalog.scenarios[0].density, 1);
    assert.equal(catalog.scenarios[0].colorScheme, "light");
    assert.ok(Object.isFrozen(frame.effectivePresentation));
    await f.runtime.release(artifact);
});

test("runtime catalog cancellation releases confirmed generation before capture can execute", async t => {
    const f = await fixture(t, { onWorkerRequest: (input, signal) => {
        if (input.kind === "preview-catalog-request") controller.abort();
        assert.equal(signal.aborted, true);
    } });
    const controller = new AbortController();
    const request = f.request(1);
    const artifact = await f.runtime.build(request, controller.signal);
    await assert.rejects(f.runtime.discover(artifact, request, controller.signal), { code: "cancelled" });
    await f.runtime.release(artifact);
    assert.deepEqual(f.workerRequests.map(entry => entry.input.kind), ["preview-catalog-request"]);
    assert.deepEqual(await fs.readdir(f.storageDirectory), []);
});

test("unconfirmed catalog ownership quarantines its entire generation", async t => {
    const f = await fixture(t, { uncertainWorker: "preview-catalog-request" });
    const coordinator = createPreviewCoordinator({ ...f.runtime, isTrusted: () => true, isSupported: () => true });
    await coordinator.start(f.request(1).selection);
    assert.equal(coordinator.state.phase, "blocked");
    assert.equal(coordinator.state.catalog, undefined);
    assert.deepEqual(f.workerRequests.map(entry => entry.input.kind), ["preview-catalog-request"]);
    assert.ok((await fs.stat(path.join(f.buildDirectory, "artifacts"))).isDirectory());
    await assert.rejects(f.runtime.release({ root: path.dirname(f.buildDirectory) }), /unowned/);
});

test("capture with a differing effective echo cannot publish pixels", async t => {
    const f = await fixture(t, { corruptEcho: { density: 2 } });
    const coordinator = createPreviewCoordinator({ ...f.runtime, isTrusted: () => true, isSupported: () => true });
    await coordinator.start(f.request(1).selection);
    assert.equal(coordinator.state.phase, "error");
    assert.match(coordinator.state.diagnostic, /identity/);
    assert.equal(coordinator.state.frame, undefined);
    assert.equal(coordinator.state.catalog.scenarios[0].id, "card/empty");
    await assert.rejects(fs.stat(path.dirname(f.buildDirectory)), { code: "ENOENT" });
});

test("compiler mapped locations survive retained build failure after payload deletion", async t => {
    const f = await fixture(t, { failBuild: true, sdkError: request =>
        `${path.join(path.dirname(request.projectPath), "Card.lui")}(12,4): error CS0103: MissingValue is not declared [${request.projectPath}]\n` });
    const coordinator = createPreviewCoordinator({ ...f.runtime, isTrusted: () => true, isSupported: () => true });
    await coordinator.start(f.request(1).selection);
    assert.equal(coordinator.state.phase, "error");
    const [diagnostic] = coordinator.state.diagnostics;
    assert.equal(diagnostic.file.toLowerCase(), path.join(f.storageDirectory, "Card.lui").toLowerCase());
    assert.equal(diagnostic.line, 12);
    assert.equal(diagnostic.column, 4);
    assert.equal(diagnostic.code, "CS0103");
    assert.equal(diagnostic.message, "MissingValue is not declared");
    assert.match(diagnostic.id, /^[a-f0-9]{32}$/);
    assert.match(coordinator.state.diagnostic, /Retained diagnostics:/);
    await assert.rejects(fs.stat(path.dirname(f.buildDirectory)), { code: "ENOENT" });
    const [retained] = await history(f.storageDirectory);
    const sdkLog = retained.summary.logs.find(entry => entry.file === "sdk-publish.stdout.log");
    assert.match(await fs.readFile(path.join(retained.root, sdkLog.file), "utf8"), /CS0103: MissingValue/);
});
