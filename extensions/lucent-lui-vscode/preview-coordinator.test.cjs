"use strict";

const assert = require("node:assert/strict");
const { test } = require("node:test");
const { createPreviewCoordinator } = require("./preview-coordinator");

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
        render: async (_artifact, request) => {
            events.push(`render:${request.generation}`);
            return { sessionId: request.sessionId, generation: request.generation,
                requestId: request.requestId, scenarioId: request.selection.scenarioId, pixels: "copied-png" };
        },
        release: async artifact => { events.push(`release:${artifact.request.generation}`); },
        onState: state => states.push(state),
        ...overrides
    });
    return { coordinator, events, states, trust(value) { trusted = value; } };
}

test("admission checks freshness twice and retains pixels only after release", async () => {
    const f = fixture();
    await f.coordinator.start({ scenarioId: "empty" });
    assert.deepEqual(f.events, ["build:1", "verify:1", "render:1", "verify:1", "release:1"]);
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
        return { ...request, scenarioId: request.selection.scenarioId };
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
    const f = fixture({ verify: async () => ++checks === 1 });
    await f.coordinator.start({ scenarioId: "empty" });
    assert.equal(f.coordinator.state.phase, "error");
    assert.equal(f.coordinator.state.frame, undefined);
    assert.match(f.coordinator.state.diagnostic, /before frame admission/);
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
    assert.deepEqual(f.events, ["build:1", "verify:1"]);
});

test("stop rejects an in-flight frame and permits a fresh later start", async () => {
    const entered = deferred();
    const finish = deferred();
    const f = fixture({ render: async (_artifact, request) => {
        if (request.generation === "1") { entered.resolve(); await finish.promise; }
        return { ...request, scenarioId: request.selection.scenarioId };
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
