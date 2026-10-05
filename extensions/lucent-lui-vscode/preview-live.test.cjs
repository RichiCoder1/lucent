"use strict";

const assert = require("node:assert/strict");
const { EventEmitter } = require("node:events");
const { test } = require("node:test");
const { createLiveTransport, connectLive, validateLiveInputEvent } = require("./preview-live");

const pixel = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1ZkAAAAASUVORK5CYII=", "base64");
// An independent fixture digest, rather than hashing through the implementation.
const pixelHash = "3a551478629e0c406e9e447cc1de9626e03e6900159c5cd6c4990a914c294004";
const identity = { sessionId: "session", generation: "generation-1", requestId: "request-1",
    projectTargetDigest: "a".repeat(64), inputDigest: "b".repeat(64), artifactDigest: "c".repeat(64),
    scenarioId: "card/empty", presentationId: "default" };
const presentation = { logicalWidth: 1, logicalHeight: 1, scale: 1, colorScheme: "light", contrast: "normal",
    density: 1, culture: "", uiCulture: "", initialTime: "1970-01-01T00:00:00.0000000+00:00" };
const request = { protocolVersion: 2, kind: "preview-capture-request", ...identity, ...presentation };
const ready = { protocolVersion: 2, kind: "preview-live-ready", identity };
const frame = sequence => ({ protocolVersion: 2, kind: "preview-live-frame", identity,
    frameSequence: sequence, byteLength: pixel.length, sha256: pixelHash, width: 1, height: 1 });

class Socket extends EventEmitter {
    constructor({ blockWrites = false } = {}) { super(); this.packets = []; this.destroyed = false; this.blockWrites = blockWrites; }
    write(bytes, callback) {
        this.packets.push(Buffer.from(bytes));
        if (!this.blockWrites) queueMicrotask(() => callback());
    }
    destroy() { this.destroyed = true; this.emit("close"); }
}

function packet(value, payload) {
    const json = Buffer.from(typeof value === "string" ? value : JSON.stringify(value));
    const prefix = Buffer.alloc(4);
    prefix.writeUInt32LE(json.length);
    return Buffer.concat([prefix, json, payload ?? Buffer.alloc(0)]);
}

function fixture(t, options) {
    const socket = new Socket(options);
    const failures = [];
    const transport = createLiveTransport(socket, { request, effectivePresentation: presentation,
        scenarioTitle: "Empty", onFailure: error => failures.push(error) });
    t.after(() => transport.close());
    return { socket, failures, transport };
}

function sent(socket) {
    return socket.packets.map(bytes => {
        assert.equal(bytes.readUInt32LE(0), bytes.length - 4);
        return JSON.parse(bytes.subarray(4).toString("utf8"));
    });
}

test("fragmented independent wire fixture admits pixels and exact displayed-frame input", async t => {
    const f = fixture(t);
    const bytes = Buffer.concat([packet(ready), packet(frame(1), pixel)]);
    for (let offset = 0; offset < bytes.length; offset += 7) f.socket.emit("data", bytes.subarray(offset, offset + 7));
    await f.transport.ready;
    const actual = await f.transport.readFrame();
    assert.deepEqual(actual.png, pixel);
    assert.equal(actual.scenarioTitle, "Empty");
    assert.equal(actual.effectivePresentation, presentation);
    assert.equal(actual.frameSequence, 1);
    assert.equal(actual.live, true);
    assert.equal(actual.requestId, "request-1");
    assert.equal(await f.transport.input(actual, { type: "text", text: "before display" }), false);
    assert.equal(await f.transport.acknowledge({ ...actual }), false);
    assert.equal(await f.transport.acknowledge(actual), true);
    assert.equal(await f.transport.acknowledge(actual), false);
    assert.equal(await f.transport.input(actual, { type: "text", text: "hello" }), true);
    assert.equal(await f.transport.focus(false), true);
    assert.deepEqual(sent(f.socket), [
        { protocolVersion: 2, kind: "preview-live-ack", identity, frameSequence: 1 },
        { protocolVersion: 2, kind: "preview-live-input", identity, frameSequence: 1, inputSequence: 1,
            event: { type: "text", text: "hello" } },
        { protocolVersion: 2, kind: "preview-live-focus", identity, inputSequence: 2, focused: false }
    ]);
    const next = f.transport.readFrame();
    f.socket.emit("data", packet(frame(2), pixel));
    const second = await next;
    await f.transport.acknowledge(second);
    assert.equal(await f.transport.input(actual, { type: "text", text: "stale" }), false);
    assert.equal(f.failures.length, 0);
});

test("ready is independent of frames and focus loss requires no displayed frame", async t => {
    const f = fixture(t);
    f.socket.emit("data", packet(ready));
    await f.transport.ready;
    await f.transport.focus(false);
    assert.deepEqual(sent(f.socket), [{ protocolVersion: 2, kind: "preview-live-focus", identity, inputSequence: 1, focused: false }]);
    const controller = new AbortController();
    const pending = f.transport.readFrame(controller.signal);
    controller.abort();
    await assert.rejects(pending, { code: "cancelled" });
    assert.equal(f.socket.destroyed, false);
    const next = f.transport.readFrame();
    f.socket.emit("data", packet(frame(1), pixel));
    assert.deepEqual((await next).png, pixel);
});

test("worker packets reject duplicate escaped keys, extras, wrong identity and invalid frame ordering", async t => {
    const cases = [
        { value: '{"protocolVersion":2,"kind":"preview-live-ready","identity":' + JSON.stringify(identity) + ',"k\\u0069nd":"preview-live-ready"}' },
        { value: { ...ready, unexpected: true } },
        { value: { ...ready, identity: { ...identity, artifactDigest: "d".repeat(64) } } },
        { value: frame(1), payload: pixel },
        { first: ready, value: ready },
        { first: ready, value: frame(2), payload: pixel },
        { first: ready, value: { ...frame(1), sha256: "0".repeat(64) }, payload: pixel },
        { first: ready, value: { ...frame(1), width: 2 }, payload: pixel },
        { first: ready, value: { ...frame(1), byteLength: 33554433 } },
        { first: ready, value: frame(1), payload: Buffer.alloc(pixel.length) }
    ];
    for (const entry of cases) {
        const f = fixture(t);
        const reading = f.transport.readFrame();
        if (entry.first) f.socket.emit("data", packet(entry.first));
        f.socket.emit("data", packet(entry.value, entry.payload));
        await assert.rejects(reading);
        assert.equal(f.failures.length, 1);
        assert.equal(f.socket.destroyed, true);
    }
});

test("one-frame backpressure rejects a second unacknowledged frame", async t => {
    const f = fixture(t);
    f.socket.emit("data", Buffer.concat([packet(ready), packet(frame(1), pixel)]));
    await f.transport.readFrame();
    f.socket.emit("data", packet(frame(2), pixel));
    await assert.rejects(f.transport.readFrame(), /ordering/);
    assert.equal(f.socket.destroyed, true);
});

test("length and EOF failures reject waiting consumers without allocating an unbounded packet", async t => {
    for (const length of [0, 1, 65537, 0xffffffff]) {
        const f = fixture(t);
        const pending = f.transport.readFrame();
        const prefix = Buffer.alloc(4); prefix.writeUInt32LE(length);
        f.socket.emit("data", prefix);
        await assert.rejects(pending, /packet size/);
    }
    const f = fixture(t);
    f.socket.emit("data", packet(ready));
    f.socket.emit("data", packet(frame(1), pixel.subarray(0, 9)));
    const pending = f.transport.readFrame();
    f.socket.emit("end");
    await assert.rejects(pending, /disconnected/);
});

test("blocked outgoing input has a bounded queue and closes every pending write on overflow", async t => {
    const f = fixture(t, { blockWrites: true });
    f.socket.emit("data", packet(ready));
    const writes = [];
    for (let index = 0; index < 65; index++) writes.push(f.transport.focus(false));
    const results = await Promise.allSettled(writes);
    assert.ok(results.every(result => result.status === "rejected"));
    assert.match(results.at(-1).reason.message, /queue/);
    assert.ok(f.socket.packets.length <= 64);
    assert.equal(f.failures.length, 1);
});

test("normalized input validators preserve supported forms and reject invalid or extra fields", () => {
    for (const event of [
        { type: "pointer", action: "down", pointerId: 0, x: 12, y: 4.5, button: "primary", modifiers: 3 },
        { type: "pointer", action: "cancel", pointerId: 15, x: -65536, y: 65536, button: "none", modifiers: 0 },
        { type: "wheel", x: 0, y: 1, deltaX: -4096, deltaY: 4096, modifiers: 15 },
        { type: "key", action: "down", key: "Backspace", modifiers: 0, repeat: true },
        { type: "text", text: "Hello \u{1f600}" }
    ]) assert.deepEqual(validateLiveInputEvent(event), event);
    for (const event of [
        { type: "text", text: "" }, { type: "text", text: "a".repeat(4097) }, { type: "text", text: "\ud800" },
        { type: "text", text: "a", path: "C:\\external" },
        { type: "key", action: "up", key: "A", modifiers: 0, repeat: true },
        { type: "key", action: "down", key: "Unrecognized", modifiers: 0, repeat: false },
        { type: "wheel", x: NaN, y: 0, deltaX: 0, deltaY: 0, modifiers: 0 },
        { type: "pointer", action: "down", pointerId: 0, x: 0, y: 0, button: "none", modifiers: 0 },
        { type: "pointer", action: "move", pointerId: 0, x: 0, y: 0, button: "primary", modifiers: 0 }
    ]) assert.throws(() => validateLiveInputEvent(event));
});

test("invalid normalized input fails the owned session without sending a partial command", async t => {
    const f = fixture(t);
    f.socket.emit("data", Buffer.concat([packet(ready), packet(frame(1), pixel)]));
    const current = await f.transport.readFrame();
    await f.transport.acknowledge(current);
    await assert.rejects(f.transport.input(current, { type: "text", text: "valid", extra: "unknown" }), /field/);
    assert.equal(sent(f.socket).length, 1);
    assert.equal(f.socket.destroyed, true);
    assert.equal(f.failures.length, 1);
});

test("only exact acknowledged gesture owners can release pointer and key edges across newer frames", async t => {
    const f = fixture(t);
    f.socket.emit("data", Buffer.concat([packet(ready), packet(frame(1), pixel)]));
    const first = await f.transport.readFrame();
    await f.transport.acknowledge(first);
    const pointer = { type: "pointer", action: "down", pointerId: 3, x: 0.5, y: 0.5, button: "primary", modifiers: 0 };
    const key = { type: "key", action: "down", key: "A", modifiers: 1, repeat: false };
    await f.transport.input(first, pointer);
    assert.equal(await f.transport.input(first, pointer), false);
    assert.equal(await f.transport.input(first, { ...pointer, button: "secondary" }), false);
    await f.transport.input(first, key);
    f.socket.emit("data", packet(frame(2), pixel));
    const second = await f.transport.readFrame();
    await f.transport.acknowledge(second);
    // A repeat may arrive on the newer display; the original down still owns release.
    await f.transport.input(second, { ...key, repeat: true });
    for (const event of [pointer, { ...pointer, action: "move", button: "none" },
        { ...pointer, action: "up", pointerId: 4 }, { ...pointer, action: "up", button: "secondary" },
        { type: "text", text: "stale" }, { ...key, action: "up", key: "C" }])
        assert.equal(await f.transport.input(first, event), false);
    assert.equal(await f.transport.input({ ...first }, { ...pointer, action: "up" }), false);
    assert.equal(await f.transport.input(first, { ...pointer, action: "up" }), true);
    assert.equal(await f.transport.input(first, { ...key, action: "up", modifiers: 0 }), true);
    assert.equal(await f.transport.input(first, { ...pointer, action: "up" }), false);
    assert.equal(await f.transport.input(first, { ...key, action: "up", modifiers: 0 }), false);
    const releasePackets = sent(f.socket).filter(value => value.kind === "preview-live-input" && value.event.action === "up");
    assert.deepEqual(releasePackets.map(value => [value.frameSequence, value.event.type]), [[1, "pointer"], [1, "key"]]);
    assert.equal(f.failures.length, 0);
});

test("focus loss clears stale gesture ownership and cancel releases only its actual owner", async t => {
    const f = fixture(t);
    f.socket.emit("data", Buffer.concat([packet(ready), packet(frame(1), pixel)]));
    const first = await f.transport.readFrame();
    await f.transport.acknowledge(first);
    const pointer = { type: "pointer", action: "down", pointerId: 0, x: 0, y: 0, button: "primary", modifiers: 0 };
    await f.transport.input(first, pointer);
    await f.transport.input(first, { ...pointer, pointerId: 1 });
    await f.transport.input(first, { type: "key", action: "down", key: "Enter", modifiers: 0, repeat: false });
    f.socket.emit("data", packet(frame(2), pixel));
    await f.transport.acknowledge(await f.transport.readFrame());
    assert.equal(await f.transport.input(first, { ...pointer, action: "cancel", button: "none" }), true);
    assert.equal(await f.transport.input(first, { ...pointer, action: "cancel", button: "none" }), false);
    await f.transport.focus(false);
    assert.equal(await f.transport.input(first, { ...pointer, action: "up", pointerId: 1 }), false);
    assert.equal(await f.transport.input(first, { type: "key", action: "up", key: "Enter", modifiers: 0, repeat: false }), false);
});

test("pending paint permits only exact last display Down and wheel until the next display ACK", async t => {
    const f = fixture(t);
    f.socket.emit("data", Buffer.concat([packet(ready), packet(frame(1), pixel)]));
    const first = await f.transport.readFrame();
    await f.transport.acknowledge(first);
    f.socket.emit("data", packet(frame(2), pixel));
    const second = await f.transport.readFrame();
    const down = { type: "pointer", action: "down", pointerId: 2, x: 0.5, y: 0.5, button: "primary", modifiers: 0 };
    const wheel = { type: "wheel", x: 0.5, y: 0.5, deltaX: 0, deltaY: 1, modifiers: 0 };
    for (const invalidFrame of [undefined, null, { ...first }]) {
        assert.equal(await f.transport.input(invalidFrame, down), false);
        assert.equal(await f.transport.input(invalidFrame, { ...down, action: "up", button: "none" }), false);
        assert.equal(await f.transport.input(invalidFrame, { type: "key", action: "up", key: "Enter", modifiers: 0, repeat: false }), false);
    }
    assert.equal(await f.transport.input({ ...first }, down), false);
    assert.equal(await f.transport.input(second, down), false);
    assert.equal(await f.transport.input(first, down), true);
    assert.equal(await f.transport.input(first, wheel), true);
    for (const event of [
        { ...down, action: "move", button: "none" }, { type: "text", text: "old" },
        { type: "key", action: "down", key: "Enter", modifiers: 0, repeat: false },
        { type: "key", action: "up", key: "Enter", modifiers: 0, repeat: false },
        { ...down, action: "up", pointerId: 3 }
    ]) assert.equal(await f.transport.input(first, event), false);
    await f.transport.acknowledge(second);
    assert.equal(await f.transport.input(first, down), false);
    assert.equal(await f.transport.input(first, wheel), false);
    assert.equal(await f.transport.input(first, { ...down, action: "up" }), true);
    f.socket.emit("data", packet(frame(3), pixel));
    const third = await f.transport.readFrame();
    assert.equal(await f.transport.input(first, down), false);
    assert.equal(await f.transport.input(first, wheel), false);
    assert.equal(await f.transport.input(second, { ...down, pointerId: 3 }), true);
    assert.equal(await f.transport.input(second, wheel), true);
    await f.transport.acknowledge(third);
    assert.equal(await f.transport.input(second, down), false);
    assert.equal(await f.transport.input(second, wheel), false);
    assert.equal(await f.transport.input(second, { ...down, action: "up", pointerId: 3 }), true);
    const edges = sent(f.socket).filter(value => value.kind === "preview-live-input");
    assert.deepEqual(edges.map(value => [value.frameSequence, value.event.type, value.event.action ?? "wheel"]), [
        [1, "pointer", "down"], [1, "wheel", "wheel"], [1, "pointer", "up"],
        [2, "pointer", "down"], [2, "wheel", "wheel"], [2, "pointer", "up"]
    ]);
    assert.equal(f.failures.length, 0);
});

test("last display press admission also holds while a newer PNG payload is still arriving", async t => {
    const f = fixture(t);
    f.socket.emit("data", Buffer.concat([packet(ready), packet(frame(1), pixel)]));
    const first = await f.transport.readFrame();
    await f.transport.acknowledge(first);
    f.socket.emit("data", packet(frame(2), pixel.subarray(0, 9)));
    const down = { type: "pointer", action: "down", pointerId: 0, x: 0, y: 0, button: "primary", modifiers: 0 };
    assert.equal(await f.transport.input(first, down), true);
    assert.equal(await f.transport.input(first, { type: "text", text: "pending" }), false);
    f.socket.emit("data", pixel.subarray(9));
    const second = await f.transport.readFrame();
    await f.transport.acknowledge(second);
    assert.equal(await f.transport.input(first, { ...down, action: "cancel", button: "none" }), true);
    assert.equal(await f.transport.input(first, down), false);
});

test("connection readiness has bounded deadlines and cancellation destroys the connected pipe", async () => {
    let socket;
    const options = { pipeName: "lucent-preview-" + "f".repeat(32), request, effectivePresentation: presentation,
        connect: () => { socket = new Socket(); queueMicrotask(() => socket.emit("connect")); return socket; } };
    await assert.rejects(connectLive({ ...options, readyTimeoutMs: 5 }), /ready deadline/);
    assert.equal(socket.destroyed, true);
    const controller = new AbortController();
    const pending = connectLive({ ...options, signal: controller.signal });
    await new Promise(resolve => setImmediate(resolve));
    controller.abort();
    await assert.rejects(pending, { code: "cancelled" });
    assert.equal(socket.destroyed, true);
    await assert.rejects(connectLive({ ...options, startupTimeoutMs: 5, connect: () => { socket = new Socket(); return socket; } }), /connect deadline/);
    assert.equal(socket.destroyed, true);
    socket.emit("connect");
    assert.equal(socket.listenerCount("data"), 0);
});

test("bounded connect retries only retry an absent pipe and attach the decoder before ready data", async t => {
    let attempts = 0;
    const sockets = [];
    const transport = await connectLive({ pipeName: "lucent-preview-" + "f".repeat(32), request,
        effectivePresentation: presentation, connect: pipe => {
            assert.equal(pipe, "\\\\.\\pipe\\lucent-preview-" + "f".repeat(32));
            const socket = new Socket(); sockets.push(socket);
            queueMicrotask(() => {
                if (++attempts === 1) socket.emit("error", Object.assign(new Error("not created"), { code: "ENOENT" }));
                else { socket.emit("connect"); socket.emit("data", packet(ready)); }
            });
            return socket;
        } });
    t.after(() => transport.close());
    assert.equal(attempts, 2);
    assert.equal(sockets[0].destroyed, true);
    await transport.focus(false);
    assert.equal(sent(sockets[1])[0].kind, "preview-live-focus");
});
