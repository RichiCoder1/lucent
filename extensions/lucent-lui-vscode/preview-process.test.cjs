"use strict";

const assert = require("node:assert/strict");
const { EventEmitter } = require("node:events");
const { PassThrough } = require("node:stream");
const { test } = require("node:test");
const { startSupervised, runSupervised, decodeSupervisorResult } = require("./preview-process");

const request = { protocolVersion: 2, kind: "preview-supervisor-request", mode: "bounded", requestId: "request-1", timeoutMs: 1000, graceMs: 10 };
const started = { protocolVersion: 2, kind: "preview-supervisor-started", requestId: "request-1" };
const result = { protocolVersion: 2, kind: "preview-supervisor-result", requestId: "request-1", status: "completed",
    exitCode: 0, termination: "natural", treeReaped: true, stdoutBytes: 0, stderrBytes: 0 };

function childFixture() {
    const child = new EventEmitter();
    child.pid = 100;
    child.stdin = new PassThrough();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    const writes = [];
    child.stdin.on("data", chunk => writes.push(chunk.toString()));
    child.kill = () => { child.killed = true; };
    const record = value => child.stdout.write(JSON.stringify(value) + "\n");
    const close = (value = result, code = 0) => { record(value); child.emit("close", code); };
    return { child, writes, record, close };
}

function fakeClock() {
    let next = 0;
    const pending = new Map();
    return {
        pending,
        setTimeout(callback, ms) { const id = ++next; pending.set(id, { callback, ms }); return id; },
        clearTimeout(id) { pending.delete(id); },
        expire() { const entries = [...pending.values()]; pending.clear(); for (const entry of entries) entry.callback(); }
    };
}

test("supervisor input remains open, abort sends stop, completion requires reaped acknowledgement", async () => {
    const f = childFixture();
    const controller = new AbortController();
    const running = runSupervised("C:\\tools\\supervisor.exe", request, { signal: controller.signal,
        spawn: (_file, args, options) => {
            assert.deepEqual(args, []);
            assert.equal(options.shell, false);
            assert.equal(options.windowsHide, true);
            return f.child;
        } });
    assert.equal(f.child.stdin.writableEnded, false);
    assert.deepEqual(JSON.parse(f.writes[0]), request);
    f.record(started);
    controller.abort();
    assert.equal(f.writes[1], "stop\n");
    f.close({ ...result, status: "cancelled", termination: "cooperative" });
    assert.equal((await running).treeReaped, true);
});

test("live handle confirms ownership independently and has no execution deadline", async () => {
    const f = childFixture();
    const clock = fakeClock();
    const handle = startSupervised("supervisor", { ...request, mode: "live" }, { spawn: () => f.child, clock });
    assert.equal(clock.pending.size, 1, "Launch must have a bound even in live mode.");
    const bytes = Buffer.from(JSON.stringify(started) + "\r\n");
    f.child.stdout.write(bytes.subarray(0, 12));
    f.child.stdout.write(bytes.subarray(12));
    assert.deepEqual(await handle.started, started);
    assert.equal(clock.pending.size, 0, "Healthy live workers must not inherit capture wallclock limits.");
    let completed = false;
    void handle.completion.then(() => { completed = true; });
    await Promise.resolve();
    assert.equal(completed, false);
    const stopping = handle.stop();
    assert.equal(stopping, handle.completion);
    handle.stop();
    assert.equal(f.writes.filter(write => write === "stop\n").length, 1);
    assert.equal([...clock.pending.values()][0].ms, request.graceMs + 15000);
    f.record({ ...result, status: "cancelled", termination: "cooperative" });
    f.child.stdout.end();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(completed, false, "Final JSON alone is insufficient before owner exit.");
    assert.equal(f.child.killed, undefined, "EOF after valid final evidence is normal.");
    f.child.emit("close", 0);
    assert.equal((await stopping).treeReaped, true);
    assert.equal(clock.pending.size, 0);
});

test("live protocol EOF without a final record rejects immediately and keeps cleanup uncertain", async () => {
    const f = childFixture();
    const clock = fakeClock();
    const handle = startSupervised("supervisor", { ...request, mode: "live" }, { spawn: () => f.child, clock });
    let outcome;
    const observed = handle.completion.then(() => { outcome = "accepted"; }, error => { outcome = error.code; });
    f.record(started);
    await handle.started;
    f.child.stdout.end();
    await new Promise(resolve => setImmediate(resolve));
    try {
        assert.equal(outcome, "termination-failed", "A closed protocol cannot leave a live owner pending forever.");
        assert.equal(f.child.killed, true);
        assert.equal(clock.pending.size, 0);
    } finally {
        f.child.emit("close", 0);
        await observed;
    }
    await assert.rejects(handle.stop(), error => error.code === "termination-failed");
});

test("stop before started keeps its cleanup deadline and cannot be reset by later ownership", async () => {
    const f = childFixture();
    const clock = fakeClock();
    const handle = startSupervised("supervisor", { ...request, mode: "live" }, { spawn: () => f.child, clock });
    handle.stop();
    const timer = [...clock.pending.keys()][0];
    f.record(started);
    await handle.started;
    assert.deepEqual([...clock.pending.keys()], [timer]);
    clock.expire();
    await assert.rejects(handle.completion, error => error.code === "termination-failed");
    f.close();
    await assert.rejects(handle.stop(), error => error.code === "termination-failed");
});

test("launch deadline without started kills owner but leaves tree termination uncertain", async () => {
    const f = childFixture();
    const clock = fakeClock();
    const handle = startSupervised("supervisor", { ...request, mode: "live" }, { spawn: () => f.child, clock });
    clock.expire();
    await assert.rejects(handle.started, error => error.code === "termination-failed");
    await assert.rejects(handle.completion, error => error.code === "termination-failed");
    assert.equal(f.child.killed, true);
});

test("missing, mismatched, duplicate, reordered or malformed records never prove cleanup", async () => {
    const invalid = [
        "", "noise\n", JSON.stringify(result) + "\n",
        JSON.stringify({ ...started, requestId: "other" }) + "\n",
        JSON.stringify({ ...started, protocolVersion: 1 }) + "\n",
        JSON.stringify({ ...started, extra: true }) + "\n",
        JSON.stringify(started) + "\n" + JSON.stringify(started) + "\n",
        JSON.stringify(started) + "\n" + JSON.stringify({ ...result, requestId: "other" }) + "\n",
        JSON.stringify(started) + "\n" + JSON.stringify({ ...result, treeReaped: false }) + "\n",
        JSON.stringify(started) + "\n" + JSON.stringify({ ...result, termination: "unconfirmed" }) + "\n",
        JSON.stringify(started) + "\n" + JSON.stringify(result),
        JSON.stringify(started) + "\n" + JSON.stringify(result) + "\n" + JSON.stringify(started) + "\n",
        JSON.stringify(started) + "\n" + JSON.stringify(result) + "\n" + JSON.stringify(result) + "\n",
        JSON.stringify(started) + "\n" + JSON.stringify(result) + "\ntrailing",
        Buffer.from([0xff, 0x0a])
    ];
    for (const wire of invalid) {
        const f = childFixture();
        const running = runSupervised("supervisor", request, { spawn: () => f.child });
        f.child.stdout.write(wire);
        f.child.emit("close", 0);
        await assert.rejects(running, error => error.code === "termination-failed");
    }
});

test("bounded adapter accepts final launch failure without falsely resolving started", async () => {
    for (const status of ["launch-failed", "cancelled"]) {
        const f = childFixture();
        const handle = startSupervised("supervisor", request, { spawn: () => f.child });
        f.close({ ...result, status, exitCode: null });
        assert.equal((await handle.completion).status, status);
        await assert.rejects(handle.started, error => error.code === status);
    }
});

test("control EPIPE racing natural exit still requires valid final record and owner close", async () => {
    const f = childFixture();
    const clock = fakeClock();
    const handle = startSupervised("supervisor", { ...request, mode: "live" }, { spawn: () => f.child, clock });
    f.record(started);
    await handle.started;
    f.child.stdin.emit("error", Object.assign(new Error("EPIPE"), { code: "EPIPE" }));
    assert.equal(clock.pending.size, 1);
    f.close();
    assert.equal((await handle.completion).status, "completed");
    assert.equal(f.child.killed, undefined);
});

test("control failure without final evidence remains sticky through later close", async () => {
    const f = childFixture();
    const clock = fakeClock();
    const handle = startSupervised("supervisor", { ...request, mode: "live" }, { spawn: () => f.child, clock });
    f.record(started);
    await handle.started;
    f.child.stdin.emit("error", new Error("EPIPE"));
    clock.expire();
    await assert.rejects(handle.completion, error => error.code === "termination-failed");
    f.close();
    await assert.rejects(handle.completion, error => error.code === "termination-failed");
});

test("protocol and diagnostic overflow are bounded uncertain termination", async () => {
    for (const stream of ["stdout", "stderr"]) {
        const f = childFixture();
        const running = runSupervised("supervisor", request, { spawn: () => f.child });
        f.child[stream].write(Buffer.alloc(65537));
        await assert.rejects(running, error => error.code === "termination-failed");
        assert.equal(f.child.killed, true);
    }
});

test("channel errors and nonzero supervisor exit cannot establish tree cleanup", async () => {
    for (const channel of ["stdout", "stderr", "owner", "exit"]) {
        const f = childFixture();
        const running = runSupervised("supervisor", request, { spawn: () => f.child });
        f.record(started);
        if (channel === "exit") f.close(result, 3);
        else if (channel === "owner") f.child.emit("error", new Error("owner failed"));
        else f.child[channel].emit("error", new Error("channel failed"));
        await assert.rejects(running, error => error.code === "termination-failed");
    }
});

test("pre-cancelled and unsupported work never launches; bounded statuses stay explicit", async () => {
    const controller = new AbortController();
    controller.abort();
    await assert.rejects(runSupervised("unused", request, { signal: controller.signal,
        spawn: () => assert.fail("must not spawn") }), error => error.code === "cancelled");
    await assert.rejects(runSupervised("unused", { ...request, protocolVersion: 1 }, {
        spawn: () => assert.fail("must not spawn") }), error => error.code === "launch-failed");
    await assert.rejects(runSupervised("unused", { ...request, mode: "live" }), error => error.code === "launch-failed");
    const timeout = decodeSupervisorResult(JSON.stringify({ ...result, status: "timeout", termination: "forced" }), request.requestId);
    assert.equal(timeout.status, "timeout");
    assert.equal(timeout.termination, "forced");
});
