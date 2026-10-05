"use strict";

const net = require("node:net");
const { createHash } = require("node:crypto");
const { decodeJson, exact, validateCorrelation, validateScenario, validatePresentation,
    MAX_MESSAGE, MAX_FRAME } = require("./preview-protocol");

const identityKeys = ["sessionId", "generation", "requestId", "projectTargetDigest", "inputDigest",
    "artifactDigest", "scenarioId", "presentationId"];
const commonKeys = ["protocolVersion", "kind", "identity"];
const keyNames = new Set(["F10", "ContextMenu", "Tab", "Enter", "Space", "Escape", "Left", "Right", "Up", "Down",
    "Home", "End", "PageUp", "PageDown", "Backspace", "Delete", "A", "C", "F", "N", "S", "V", "X", "Y", "Z", "F4"]);
const cancelled = () => Object.assign(new Error("Preview cancelled."), { code: "cancelled" });
const protocolError = message => Object.assign(new Error(message), { code: "live-protocol" });

function liveIdentity(request) {
    validateCorrelation(request);
    validateScenario(request);
    return Object.freeze(Object.fromEntries(identityKeys.map(key => [key, request[key]])));
}

function validateLiveInputEvent(event) {
    const modifiers = () => {
        if (!Number.isInteger(event.modifiers) || event.modifiers < 0 || event.modifiers > 15)
            throw protocolError("Invalid preview input modifiers.");
    };
    const coordinate = key => {
        if (!Number.isFinite(event[key]) || Math.abs(event[key]) > 65536)
            throw protocolError("Invalid preview input coordinate.");
    };
    if (event?.type === "pointer") {
        exact(event, ["type", "action", "pointerId", "x", "y", "button", "modifiers"]);
        if (!["move", "down", "up", "cancel"].includes(event.action)
            || !Number.isInteger(event.pointerId) || event.pointerId < 0 || event.pointerId > 15
            || !["none", "primary", "secondary", "middle"].includes(event.button)
            || event.action === "down" && event.button === "none"
            || ["move", "cancel"].includes(event.action) && event.button !== "none")
            throw protocolError("Invalid preview pointer input.");
        coordinate("x"); coordinate("y"); modifiers();
    } else if (event?.type === "wheel") {
        exact(event, ["type", "x", "y", "deltaX", "deltaY", "modifiers"]);
        coordinate("x"); coordinate("y"); modifiers();
        if (![event.deltaX, event.deltaY].every(value => Number.isFinite(value) && Math.abs(value) <= 4096))
            throw protocolError("Invalid preview wheel input.");
    } else if (event?.type === "key") {
        exact(event, ["type", "action", "key", "modifiers", "repeat"]);
        if (!["down", "up"].includes(event.action) || !keyNames.has(event.key)
            || typeof event.repeat !== "boolean" || event.action === "up" && event.repeat)
            throw protocolError("Invalid preview key input.");
        modifiers();
    } else if (event?.type === "text") {
        exact(event, ["type", "text"]);
        if (typeof event.text !== "string" || event.text.length < 1 || event.text.length > 4096
            || /[\u0000]/.test(event.text) || !event.text.isWellFormed())
            throw protocolError("Invalid preview committed text.");
    } else throw protocolError("Unsupported preview input event.");
    return Object.freeze({ ...event });
}

function decodeLiveMessage(bytes, expected, dimensions) {
    const value = decodeJson(bytes);
    exact(value.identity, identityKeys);
    if (value.protocolVersion !== 2 || identityKeys.some(key => value.identity[key] !== expected[key]))
        throw protocolError("Preview live identity differs from its launch.");
    if (value.kind === "preview-live-ready") exact(value, commonKeys);
    else if (value.kind === "preview-live-frame") {
        exact(value, [...commonKeys, "frameSequence", "byteLength", "sha256", "width", "height"]);
        if (!Number.isSafeInteger(value.frameSequence) || value.frameSequence < 1
            || !Number.isInteger(value.byteLength) || value.byteLength < 33 || value.byteLength > MAX_FRAME
            || typeof value.sha256 !== "string" || !/^[a-f0-9]{64}$/.test(value.sha256)
            || value.width !== dimensions.width || value.height !== dimensions.height)
            throw protocolError("Invalid preview live frame metadata.");
    } else throw protocolError("Unsupported preview live worker message.");
    return value;
}

// One decoded frame and at most one frame reader are retained. The worker may
// send another frame only after the exact displayed object has been acknowledged.
function createLiveTransport(socket, { request, effectivePresentation, scenarioTitle, onFailure = () => {} }) {
    const identity = liveIdentity(request);
    const dimensions = validatePresentation({ ...request });
    let resolveReady, rejectReady;
    const ready = new Promise((resolve, reject) => { resolveReady = resolve; rejectReady = reject; });
    void ready.catch(() => {});
    let workerReady = false, closed = false, failure, reader, queuedFrame, inFlight, displayed;
    let frameSequence = 0, inputSequence = 0;
    let prefix = Buffer.alloc(4), prefixBytes = 0, body, bodyBytes = 0, metadata;
    const writes = new Set();
    // Only active down edges retain old frame capabilities. ACK history itself
    // is never retained; supported pointer/key domains bound the owner maps.
    const pointerOwners = new Map();
    const keyOwners = new Map();
    let queuedBytes = 0;

    function finishReader(error, frame) {
        if (!reader) return;
        const current = reader;
        reader = undefined;
        current.signal?.removeEventListener("abort", current.abort);
        if (error) current.reject(error); else current.resolve(frame);
    }
    function close(error = cancelled(), report = false) {
        if (closed) return;
        closed = true;
        failure = error;
        body = metadata = queuedFrame = undefined;
        pointerOwners.clear();
        keyOwners.clear();
        rejectReady(error);
        finishReader(error);
        for (const write of writes) write.reject(error);
        writes.clear();
        queuedBytes = 0;
        socket.destroy();
        if (report) onFailure(error);
    }
    function fail(error) { close(error, true); }
    socket.on("error", error => fail(protocolError(`Preview live pipe failed: ${error.message}`)));
    socket.on("end", () => fail(protocolError("Preview live worker disconnected.")));
    socket.on("close", () => fail(protocolError("Preview live worker disconnected.")));

    function acceptFrame(png) {
        if (createHash("sha256").update(png).digest("hex") !== metadata.sha256
            || !png.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]))
            || png.readUInt32BE(8) !== 13 || png.toString("ascii", 12, 16) !== "IHDR"
            || png.readUInt32BE(16) !== metadata.width || png.readUInt32BE(20) !== metadata.height)
            throw protocolError("Preview live PNG bytes differ from their metadata.");
        frameSequence = metadata.frameSequence;
        inFlight = Object.freeze({ ...effectivePresentation, ...metadata, ...identity, identity,
            scenarioTitle, live: true, png, effectivePresentation });
        if (reader) finishReader(undefined, inFlight); else queuedFrame = inFlight;
        metadata = undefined;
    }
    function acceptMessage(bytes) {
        const value = decodeLiveMessage(bytes, identity, dimensions);
        if (value.kind === "preview-live-ready") {
            if (workerReady) throw protocolError("Duplicate preview live ready record.");
            workerReady = true;
            resolveReady();
        } else {
            if (!workerReady || inFlight || value.frameSequence !== frameSequence + 1)
                throw protocolError("Invalid preview live frame ordering.");
            metadata = value;
            body = Buffer.alloc(value.byteLength);
            bodyBytes = 0;
        }
    }
    socket.on("data", chunk => {
        if (closed) return;
        try {
            let offset = 0;
            let messages = 0;
            while (offset < chunk.length) {
                if (++messages > 128) throw protocolError("Preview live receive burst exceeds its bound.");
                if (!body) {
                    const count = Math.min(4 - prefixBytes, chunk.length - offset);
                    chunk.copy(prefix, prefixBytes, offset, offset + count);
                    prefixBytes += count; offset += count;
                    if (prefixBytes < 4) continue;
                    const length = prefix.readUInt32LE(0);
                    prefixBytes = 0;
                    if (length < 2 || length > MAX_MESSAGE) throw protocolError("Invalid preview live packet size.");
                    body = Buffer.alloc(length);
                    bodyBytes = 0;
                }
                const count = Math.min(body.length - bodyBytes, chunk.length - offset);
                chunk.copy(body, bodyBytes, offset, offset + count);
                bodyBytes += count; offset += count;
                if (bodyBytes === body.length) {
                    const complete = body;
                    body = undefined; bodyBytes = 0;
                    if (metadata) acceptFrame(complete); else acceptMessage(complete);
                }
            }
        } catch (error) { fail(error); }
    });

    function write(kind, fields) {
        if (closed) return Promise.reject(failure);
        const bytes = Buffer.from(JSON.stringify({ protocolVersion: 2, kind, identity, ...fields }));
        if (bytes.length > MAX_MESSAGE || writes.size >= 64 || queuedBytes + bytes.length + 4 > MAX_MESSAGE) {
            const error = protocolError("Preview live outbound queue exceeds its bound.");
            fail(error);
            return Promise.reject(error);
        }
        const packet = Buffer.alloc(4 + bytes.length);
        packet.writeUInt32LE(bytes.length);
        bytes.copy(packet, 4);
        return new Promise((resolve, reject) => {
            const pending = { reject };
            writes.add(pending); queuedBytes += packet.length;
            try {
                socket.write(packet, error => {
                    if (!writes.delete(pending)) return;
                    queuedBytes -= packet.length;
                    if (error) { fail(protocolError("Preview live input delivery failed.")); reject(failure); }
                    else resolve(true);
                });
            } catch (error) { fail(error); }
        });
    }
    function nextInput() {
        if (inputSequence >= Number.MAX_SAFE_INTEGER) throw protocolError("Preview live input sequence exhausted.");
        return ++inputSequence;
    }
    return {
        ready,
        readFrame(signal) {
            if (signal?.aborted) return Promise.reject(cancelled());
            if (closed) return Promise.reject(failure);
            if (reader) return Promise.reject(protocolError("Only one preview live frame reader is allowed."));
            if (queuedFrame) { const frame = queuedFrame; queuedFrame = undefined; return Promise.resolve(frame); }
            return new Promise((resolve, reject) => {
                reader = { resolve, reject, signal, abort: () => finishReader(cancelled()) };
                signal?.addEventListener("abort", reader.abort, { once: true });
                if (signal?.aborted) reader.abort();
            });
        },
        acknowledge(frame) {
            if (!workerReady || frame !== inFlight || queuedFrame) return Promise.resolve(false);
            inFlight = undefined;
            displayed = frame;
            return write("preview-live-ack", { frameSequence: frame.frameSequence });
        },
        async input(frame, event) {
            if (!workerReady || !displayed || closed) return false;
            const pointerOwner = event?.type === "pointer" ? pointerOwners.get(event.pointerId) : undefined;
            const keyOwner = event?.type === "key" ? keyOwners.get(event.key) : undefined;
            const ownedPointerRelease = !!pointerOwner && pointerOwner.frame === frame
                && (event.action === "cancel" || event.action === "up"
                    && (event.button === "none" || event.button === pointerOwner.button));
            const ownedKeyRelease = !!keyOwner && keyOwner === frame && event?.action === "up";
            const currentDisplay = frame === displayed
                && frame.frameSequence === (metadata?.frameSequence ?? frameSequence);
            // A hover paint may already be waiting for display. Preserve the
            // exact last display's press/wheel token; the retained native host
            // must independently prove its input geometry still matches.
            const lastDisplayPress = frame === displayed
                && (event?.type === "wheel" || event?.type === "pointer" && event.action === "down");
            if (!currentDisplay && !lastDisplayPress && !ownedPointerRelease && !ownedKeyRelease) return false;
            try {
                const admittedEvent = validateLiveInputEvent(event);
                if (admittedEvent.type === "pointer" && admittedEvent.action === "down" && pointerOwner)
                    return false;
                if (admittedEvent.type === "pointer") {
                    if (admittedEvent.action === "down" && !pointerOwner)
                        pointerOwners.set(admittedEvent.pointerId, { frame, button: admittedEvent.button });
                    else if (["up", "cancel"].includes(admittedEvent.action)) pointerOwners.delete(admittedEvent.pointerId);
                } else if (admittedEvent.type === "key") {
                    if (admittedEvent.action === "down" && !keyOwner) keyOwners.set(admittedEvent.key, frame);
                    else if (admittedEvent.action === "up") keyOwners.delete(admittedEvent.key);
                }
                return write("preview-live-input", { frameSequence: frame.frameSequence, inputSequence: nextInput(), event: admittedEvent });
            } catch (error) { fail(error); throw error; }
        },
        focus(focused) {
            if (!workerReady || typeof focused !== "boolean") return Promise.resolve(false);
            if (!focused) { pointerOwners.clear(); keyOwners.clear(); }
            return write("preview-live-focus", { inputSequence: nextInput(), focused });
        },
        close
    };
}

async function connectLive({ pipeName, request, effectivePresentation, scenarioTitle, signal, onFailure,
    connect = net.createConnection, startupTimeoutMs = 15000, readyTimeoutMs = 15000 }) {
    if (!/^lucent-preview-[a-f0-9]{32}$/.test(pipeName)) throw protocolError("Invalid preview live pipe name.");
    liveIdentity(request);
    validatePresentation({ ...request });
    if (![startupTimeoutMs, readyTimeoutMs].every(value => Number.isInteger(value) && value >= 1 && value <= 60000))
        throw protocolError("Invalid preview live startup deadline.");
    if (signal?.aborted) throw cancelled();
    const pipePath = "\\\\.\\pipe\\" + pipeName;
    let socket, transport;
    await new Promise((resolve, reject) => {
        let settled = false, retry;
        const timer = setTimeout(() => finish(protocolError("Preview live pipe exceeded its connect deadline.")), startupTimeoutMs);
        const abort = () => finish(cancelled());
        function finish(error) {
            if (settled) return;
            settled = true;
            clearTimeout(timer); clearTimeout(retry);
            signal?.removeEventListener("abort", abort);
            if (error) { socket?.destroy(); reject(error); } else resolve();
        }
        function attempt() {
            if (settled) return;
            try { socket = connect(pipePath); }
            catch (error) { finish(error); return; }
            const current = socket;
            const connected = () => {
                if (settled || signal?.aborted) { current.destroy(); return; }
                transport = createLiveTransport(current, { request, effectivePresentation, scenarioTitle, onFailure });
                current.removeListener("error", failed);
                finish();
            };
            const failed = error => {
                if (settled) return;
                current.removeListener("connect", connected);
                current.destroy();
                if (["ENOENT", "ECONNREFUSED"].includes(error.code)) retry = setTimeout(attempt, 25);
                else finish(protocolError(`Preview live pipe connection failed: ${error.message}`));
            };
            current.once("connect", connected);
            current.once("error", failed);
        }
        signal?.addEventListener("abort", abort, { once: true });
        attempt();
        if (signal?.aborted) abort();
    });
    await new Promise((resolve, reject) => {
        const timer = setTimeout(() => finish(protocolError("Preview live worker exceeded its ready deadline.")), readyTimeoutMs);
        const abort = () => finish(cancelled());
        let settled = false;
        function finish(error) {
            if (settled) return;
            settled = true;
            clearTimeout(timer);
            signal?.removeEventListener("abort", abort);
            if (error) { transport.close(error); reject(error); } else resolve();
        }
        signal?.addEventListener("abort", abort, { once: true });
        transport.ready.then(() => finish(), finish);
        if (signal?.aborted) abort();
    });
    return transport;
}

module.exports = { connectLive, createLiveTransport, decodeLiveMessage, validateLiveInputEvent, liveIdentity };
