"use strict";

const assert = require("node:assert/strict");
const { test } = require("node:test");
const { createPreviewCoordinator } = require("./preview-coordinator");

const defaults = Object.freeze({ logicalWidth: 160, logicalHeight: 120, scale: 1, colorScheme: "light",
    contrast: "normal", density: 1, culture: "", uiCulture: "", initialTime: "1970-01-01T00:00:00.0000000+00:00" });

function frameFor(request) {
    return { sessionId: request.sessionId, generation: request.generation, requestId: request.requestId,
        presentationId: request.presentationId, scenarioId: request.selection.scenarioId,
        effectivePresentation: { ...defaults, ...request.selection.presentation }, pixels: "copied-png" };
}

function deferred() {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
}

function fixture(overrides = {}) {
    let trusted = true;
    const events = [];
    const states = [];
    const coordinator = createPreviewCoordinator({
        sessionId: "test-session",
        isTrusted: () => trusted,
        isSupported: selection => selection.platform !== "remote",
        build: async request => { events.push(`build:${request.generation}`); return { request }; },
        verify: async artifact => { events.push(`verify:${artifact.request.generation}`); return true; },
        discover: async (_artifact, request) => {
            events.push(`discover:${request.generation}`);
            return { sessionId: request.sessionId, generation: request.generation,
                scenarios: [{ id: request.selection.scenarioId, title: "Authored fixture", ...defaults }] };
        },
        render: async (_artifact, request) => {
            events.push(`render:${request.generation}`);
            return frameFor(request);
        },
        release: async artifact => { events.push(`release:${artifact.request.generation}`); },
        onState: state => states.push(state),
        ...overrides
    });
    return { coordinator, events, states, trust(value) { trusted = value; } };
}

test("catalog and capture use the same artifact with freshness checked before each admission", async () => {
    const f = fixture();
    await f.coordinator.start({ scenarioId: "empty" });
    assert.deepEqual(f.events, ["build:1", "verify:1", "discover:1", "verify:1", "render:1", "verify:1", "release:1"]);
    assert.equal(f.coordinator.state.phase, "current");
    assert.equal(f.coordinator.state.frame.scenarioId, "empty");
    assert.equal(f.coordinator.state.stale, false);
    await f.coordinator.dispose();
    assert.equal(f.coordinator.state.phase, "stopped");
});

test("untrusted and remote selections perform no executable work", async () => {
    const f = fixture();
    f.trust(false);
    await f.coordinator.start({ scenarioId: "empty" });
    assert.equal(f.coordinator.state.phase, "untrusted");
    f.trust(true);
    await f.coordinator.start({ scenarioId: "empty", platform: "remote" });
    assert.equal(f.coordinator.state.phase, "error");
    assert.deepEqual(f.events, []);
});

test("superseding saves cancel immediately and wait for cleanup before the newest build", async () => {
    const started = deferred();
    const cancelled = deferred();
    const cleaned = deferred();
    const events = [];
    const f = fixture({
        build: async (request, signal) => {
            events.push(`build:${request.generation}`);
            if (request.generation === "1") {
                signal.addEventListener("abort", cancelled.resolve, { once: true });
                started.resolve();
                await cleaned.promise;
            }
            return { request };
        },
        release: async artifact => events.push(`release:${artifact.request.generation}`)
    });
    const first = f.coordinator.start({ scenarioId: "old" });
    await started.promise;
    const second = f.coordinator.start({ scenarioId: "intermediate" });
    const third = f.coordinator.start({ scenarioId: "newest" });
    await cancelled.promise;
    assert.deepEqual(events, ["build:1"]);
    cleaned.resolve();
    await Promise.all([first, second, third]);
    assert.deepEqual(events, ["build:1", "release:1", "build:3", "release:3"]);
    assert.equal(f.coordinator.state.frame.scenarioId, "newest");
    assert.equal(f.coordinator.state.frame.generation, "3");
});

test("late frames cannot replace the last good frame while a newer request waits", async () => {
    const rendering = deferred();
    const finish = deferred();
    const f = fixture({ render: async (_artifact, request) => {
        if (request.generation === "2") { rendering.resolve(); await finish.promise; }
        if (request.generation === "3") throw new Error("new scenario failed");
        return frameFor(request);
    } });
    await f.coordinator.start({ scenarioId: "good" });
    const good = f.coordinator.state.frame;
    const second = f.coordinator.start({ scenarioId: "late" });
    await rendering.promise;
    const third = f.coordinator.start({ scenarioId: "failed" });
    assert.equal(f.coordinator.state.frame, good);
    assert.equal(f.coordinator.state.stale, true);
    finish.resolve();
    await Promise.all([second, third]);
    assert.equal(f.coordinator.state.phase, "stale");
    assert.equal(f.coordinator.state.frame, good);
    assert.match(f.coordinator.state.diagnostic, /new scenario failed/);
});

test("trust loss between build and launch prevents rendering and releases the artifact", async () => {
    let f;
    f = fixture({ verify: async () => { f.trust(false); return true; } });
    await f.coordinator.start({ scenarioId: "empty" });
    assert.deepEqual(f.events, ["build:1", "release:1"]);
    assert.equal(f.coordinator.state.phase, "untrusted");
});

test("changed inputs at final admission reject otherwise valid pixels", async () => {
    let checks = 0;
    const f = fixture({ verify: async () => ++checks < 3 });
    await f.coordinator.start({ scenarioId: "empty" });
    assert.equal(f.coordinator.state.phase, "error");
    assert.equal(f.coordinator.state.frame, undefined);
    assert.match(f.coordinator.state.diagnostic, /before frame admission/);
    assert.equal(f.coordinator.state.catalog.scenarios[0].id, "empty");
    assert.equal(f.coordinator.state.catalogStale, true);
    assert.ok(f.events.includes("release:1"));
});

test("wrong generation correlation never publishes a frame", async () => {
    const f = fixture({ render: async (_artifact, request) => ({ ...request, generation: "0", scenarioId: "empty" }) });
    await f.coordinator.start({ scenarioId: "empty" });
    assert.equal(f.coordinator.state.phase, "error");
    assert.equal(f.coordinator.state.frame, undefined);
});

test("uncertain termination blocks queued replacement and preserves its directory", async () => {
    const entered = deferred();
    const finish = deferred();
    const f = fixture({ render: async () => {
        entered.resolve();
        await finish.promise;
        throw Object.assign(new Error("Owned process tree did not stop"), { code: "termination-failed" });
    } });
    const first = f.coordinator.start({ scenarioId: "hung" });
    await entered.promise;
    const next = f.coordinator.start({ scenarioId: "replacement" });
    finish.resolve();
    await Promise.all([first, next]);
    await f.coordinator.stop();
    await f.coordinator.start({ scenarioId: "still-blocked" });
    assert.equal(f.coordinator.state.phase, "blocked");
    assert.deepEqual(f.events, ["build:1", "verify:1", "discover:1", "verify:1"]);
});

test("stop rejects an in-flight frame and permits a fresh later start", async () => {
    const entered = deferred();
    const finish = deferred();
    const f = fixture({ render: async (_artifact, request) => {
        if (request.generation === "1") { entered.resolve(); await finish.promise; }
        return frameFor(request);
    } });
    const first = f.coordinator.start({ scenarioId: "old" });
    await entered.promise;
    const stopped = f.coordinator.stop();
    finish.resolve();
    await Promise.all([first, stopped]);
    assert.equal(f.coordinator.state.phase, "stopped");
    assert.equal(f.coordinator.state.frame, undefined);
    await f.coordinator.start({ scenarioId: "new" });
    assert.equal(f.coordinator.state.frame.scenarioId, "new");
    await f.coordinator.dispose();
    assert.throws(() => f.coordinator.start({ scenarioId: "disposed" }), /disposed/);
});

test("queued selections deeply snapshot presentation and extra inputs before caller mutation", async () => {
    const entered = deferred();
    const finish = deferred();
    let captured;
    const f = fixture({ build: async request => {
        captured = request.selection;
        entered.resolve();
        await finish.promise;
        return { request };
    } });
    const selection = { scenarioId: "empty", presentation: { colorScheme: "dark", density: 2 }, extraInputs: ["first.lui"] };
    const running = f.coordinator.start(selection);
    selection.presentation.density = 4;
    selection.extraInputs[0] = "mutated.lui";
    await entered.promise;
    assert.deepEqual(captured.presentation, { colorScheme: "dark", density: 2 });
    assert.deepEqual(captured.extraInputs, ["first.lui"]);
    assert.ok(Object.isFrozen(captured) && Object.isFrozen(captured.presentation) && Object.isFrozen(captured.extraInputs));
    finish.resolve();
    await running;
    assert.equal(f.coordinator.state.effectivePresentation.density, 2);
});

test("invalid registered selection keeps the fresh catalog available without scenario execution", async () => {
    const f = fixture({ discover: async (_artifact, request) => ({ sessionId: request.sessionId,
        generation: request.generation, scenarios: [{ id: "registered", title: "Registered", ...defaults }] }) });
    await f.coordinator.start({ scenarioId: "missing" });
    assert.equal(f.coordinator.state.phase, "error");
    assert.match(f.coordinator.state.diagnostic, /not registered/);
    assert.equal(f.coordinator.state.catalog.scenarios[0].id, "registered");
    assert.equal(f.coordinator.state.catalogStale, false);
    assert.equal(f.coordinator.state.selectedScenarioId, "missing");
    assert.ok(Object.isFrozen(f.coordinator.state.catalog.scenarios[0]));
    assert.ok(!f.events.some(event => event.startsWith("render:")));
    await f.coordinator.start({ scenarioId: "registered" });
    assert.equal(f.coordinator.state.phase, "current");
});

test("cancelled catalog discovery never publishes metadata or starts capture", async () => {
    const entered = deferred();
    const finish = deferred();
    const f = fixture({ discover: async (_artifact, request) => {
        entered.resolve();
        await finish.promise;
        return { sessionId: request.sessionId, generation: request.generation, scenarios: [{ id: "empty", ...defaults }] };
    } });
    const running = f.coordinator.start({ scenarioId: "empty" });
    await entered.promise;
    const stopped = f.coordinator.stop();
    finish.resolve();
    await Promise.all([running, stopped]);
    assert.equal(f.coordinator.state.phase, "stopped");
    assert.equal(f.coordinator.state.catalog, undefined);
    assert.ok(!f.events.some(event => event.startsWith("render:")));
    assert.equal(f.events.at(-1), "release:1");
});

test("stale or foreign catalog observations cannot be admitted", async () => {
    const stale = fixture({ verify: async () => false });
    await stale.coordinator.start({ scenarioId: "empty" });
    assert.equal(stale.coordinator.state.catalog, undefined);
    let checks = 0;
    const changed = fixture({ verify: async () => ++checks !== 2 });
    await changed.coordinator.start({ scenarioId: "empty" });
    assert.match(changed.coordinator.state.diagnostic, /catalog discovery/);
    assert.equal(changed.coordinator.state.catalog, undefined);
    const foreign = fixture({ discover: async () => ({ sessionId: "other", generation: "1", scenarios: [] }) });
    await foreign.coordinator.start({ scenarioId: "empty" });
    assert.match(foreign.coordinator.state.diagnostic, /catalog does not belong/);
    assert.equal(foreign.coordinator.state.catalog, undefined);
});

test("mismatched effective capture preserves the last good frame and marks catalog freshness", async () => {
    const entered = deferred();
    const finish = deferred();
    const f = fixture({ render: async (_artifact, request) => {
        if (request.generation === "1") return frameFor(request);
        entered.resolve();
        await finish.promise;
        return { ...frameFor(request), effectivePresentation: { ...defaults, density: 4 } };
    } });
    await f.coordinator.start({ scenarioId: "empty" });
    const good = f.coordinator.state.frame;
    const running = f.coordinator.start({ scenarioId: "empty", presentation: { density: 2 } });
    await entered.promise;
    assert.equal(f.coordinator.state.catalogStale, true);
    assert.equal(f.coordinator.state.frame, good);
    finish.resolve();
    await running;
    assert.equal(f.coordinator.state.phase, "stale");
    assert.equal(f.coordinator.state.frame, good);
    assert.equal(f.coordinator.state.catalogStale, false);
    assert.match(f.coordinator.state.diagnostic, /effective presentation/);
});

test("mapped diagnostics stay immutable and are cleared synchronously when their generation is obsolete", async () => {
    const diagnostics = [{ id: "a".repeat(32), file: "C:\\project\\Card.lui", line: 12, column: 4,
        code: "CS0103", message: "MissingValue is not declared" }];
    let currentDiagnostics = diagnostics;
    let pendingBuild;
    const f = fixture({ build: async request => {
        if (pendingBuild) await pendingBuild.promise;
        return { request };
    }, render: async () => {
        throw Object.assign(new Error("Mapped compilation failed"), { diagnostics: currentDiagnostics });
    },
        release: async () => ({ diagnosticDirectory: "C:\\retained", diagnostic: "Mapped compilation failed; retained here" }) });
    await f.coordinator.start({ scenarioId: "empty" });
    assert.equal(f.coordinator.state.diagnostic, "Mapped compilation failed; retained here");
    assert.deepEqual(f.coordinator.state.diagnostics, diagnostics);
    assert.ok(Object.isFrozen(f.coordinator.state.diagnostics) && Object.isFrozen(f.coordinator.state.diagnostics[0]));
    diagnostics[0].message = "mutated";
    assert.equal(f.coordinator.state.diagnostics[0].message, "MissingValue is not declared");
    const previousCatalog = f.coordinator.state.catalog;
    currentDiagnostics = [{ ...diagnostics[0], id: "b".repeat(32), line: 13, message: "Fresh mapped location" }];
    pendingBuild = deferred();
    const rebuilding = f.coordinator.start({ scenarioId: "empty" });
    assert.equal(f.coordinator.state.generation, "2");
    assert.deepEqual(f.coordinator.state.diagnostics, []);
    assert.equal(f.coordinator.state.catalog, previousCatalog);
    assert.equal(f.coordinator.state.catalogStale, true);
    pendingBuild.resolve();
    await rebuilding;
    assert.deepEqual(f.coordinator.state.diagnostics, currentDiagnostics);
    assert.equal(f.coordinator.state.diagnostics[0].line, 13);
    f.coordinator.invalidate();
    assert.equal(f.coordinator.state.generation, "3");
    assert.deepEqual(f.coordinator.state.diagnostics, []);
    currentDiagnostics = [{ ...currentDiagnostics[0], id: "c".repeat(32), line: 14 }];
    pendingBuild = undefined;
    await f.coordinator.refresh();
    assert.equal(f.coordinator.state.generation, "4");
    assert.deepEqual(f.coordinator.state.diagnostics, currentDiagnostics);
    const stopped = f.coordinator.stop();
    assert.deepEqual(f.coordinator.state.diagnostics, []);
    await stopped;
});
