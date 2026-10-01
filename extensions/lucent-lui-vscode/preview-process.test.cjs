"use strict";

const assert = require("node:assert/strict");
const { EventEmitter } = require("node:events");
const { PassThrough } = require("node:stream");
const { test } = require("node:test");
const { runSupervised, decodeSupervisorResult } = require("./preview-process");

const request = { protocolVersion: 1, kind: "preview-supervisor-request", requestId: "request-1", timeoutMs: 1000, graceMs: 10 };
const result = { protocolVersion: 1, kind: "preview-supervisor-result", requestId: "request-1", status: "completed",
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
    function close(value = result, code = 0) {
        child.stdout.write(typeof value === "string" ? value : JSON.stringify(value));
        child.emit("close", code);
    }
    return { child, writes, close };
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
    controller.abort();
    assert.equal(f.writes[1], "stop\n");
    f.close({ ...result, status: "cancelled", termination: "cooperative" });
    assert.equal((await running).treeReaped, true);
});

test("missing or mismatched supervisor evidence is explicit uncertain termination", async () => {
    for (const value of ["", "noise", { ...result, requestId: "other" }, { ...result, treeReaped: false }, { ...result, termination: "unconfirmed" }]) {
        const f = childFixture();
        const running = runSupervised("C:\\tools\\supervisor.exe", request, { spawn: () => f.child });
        f.close(value);
        await assert.rejects(running, error => error.code === "termination-failed");
    }
});

test("oversized supervisor output kills its owner and leaves cleanup explicitly unconfirmed", async () => {
    const f = childFixture();
    const running = runSupervised("C:\\tools\\supervisor.exe", request, { spawn: () => f.child });
    f.child.stdout.write(Buffer.alloc(65537));
    await assert.rejects(running, error => error.code === "termination-failed");
    assert.equal(f.child.killed, true);
});

test("pre-cancelled work never launches and bounded result status remains explicit", async () => {
    const controller = new AbortController();
    controller.abort();
    await assert.rejects(runSupervised("unused", request, { signal: controller.signal,
        spawn: () => assert.fail("must not spawn") }), error => error.code === "cancelled");
    const timeout = decodeSupervisorResult(JSON.stringify({ ...result, status: "timeout", termination: "forced" }), request.requestId);
    assert.equal(timeout.status, "timeout");
    assert.equal(timeout.termination, "forced");
});
