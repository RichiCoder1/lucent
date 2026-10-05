"use strict";

const assert = require("node:assert/strict");
const { test } = require("node:test");
const vm = require("node:vm");
const { createPreviewPanel } = require("./preview-panel");

const presentation = { logicalWidth: 160, logicalHeight: 120, scale: 1.1, colorScheme: "light", contrast: "normal", density: 1 };
const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aU8sAAAAASUVORK5CYII=", "base64");
function view(generation = "1", extra = {}) {
    return { state: { phase: "current", generation, stale: false, catalogStale: false,
        catalog: { scenarios: [{ id: "card/empty", title: "Empty card", ...presentation, source: { document: "file:///private/Card.lui" } }] },
        diagnostics: [{ message: "Missing member", uri: "file:///private/Card.lui", line: 4 }], ...extra },
        selection: { scenarioId: "card/empty" }, zoom: 1 };
}
function fixture({ visible = true, post = () => Promise.resolve(true), onAction } = {}) {
    const events = { message: new Set(), visibility: new Set(), close: new Set() };
    const subscribe = kind => callback => { events[kind].add(callback); return { dispose: () => events[kind].delete(callback) }; };
    const messages = [];
    const actions = [];
    const visibility = [];
    const timers = new Map();
    let timerId = 0;
    let closes = 0;
    let panelDisposed = false;
    const panel = { visible, webview: { html: "", onDidReceiveMessage: subscribe("message"),
        postMessage(message) { messages.push(message); return post(message); } },
    onDidChangeViewState: subscribe("visibility"), onDidDispose: subscribe("close"), dispose() { panelDisposed = true; } };
    const transport = createPreviewPanel({ panel, onAction: action => { actions.push(action); return onAction?.(action); },
        onVisibility: value => visibility.push(value), onClose: () => closes++,
        schedule(callback, delay) { assert.equal(delay, 5000); const key = ++timerId; timers.set(key, callback); return key; },
        cancelScheduled: key => timers.delete(key) });
    const emit = (kind, value) => { for (const callback of [...events[kind]]) callback(value); };
    const panelId = () => /data-panel-id="([^"]+)"/.exec(panel.webview.html)[1];
    const receive = message => emit("message", { version: 1, panelId: panelId(), ...message });
    return { panel, transport, messages, actions, visibility, timers, events, panelId, receive,
        ready: () => receive({ kind: "ready" }), ack: message => receive({ kind: "ack", deliveryId: message.deliveryId }),
        action: (message, fields) => receive({ deliveryId: message.deliveryId, ...fields }),
        show(value) { panel.visible = value; emit("visibility", { webviewPanel: panel }); }, close: () => emit("close"),
        get closes() { return closes; }, get panelDisposed() { return panelDisposed; } };
}

test("nonce CSP and bounded metadata isolate authored text and omit locations", () => {
    const a = fixture(); const b = fixture();
    const nonce = /<script nonce="([^"]+)">/.exec(a.panel.webview.html)[1];
    assert.match(nonce, /^[a-f0-9]{48}$/);
    assert.notEqual(nonce, /<script nonce="([^"]+)">/.exec(b.panel.webview.html)[1]);
    assert.match(a.panel.webview.html, /default-src 'none'; img-src data:;/);
    assert.match(a.panel.webview.html, new RegExp(`script-src 'nonce-${nonce}'`));
    assert.doesNotMatch(a.panel.webview.html, /unsafe-inline|unsafe-eval|command:|https?:|innerHTML|setInterval/);
    const hostile = "<script>throw new Error('authored')</script>";
    const input = view("2", { diagnostic: "Failed at file:///secret/Card.lui and C:\\secret\\Card.lui",
        catalog: { scenarios: [{ id: "測試/card", title: hostile, ...presentation, source: { project: "C:\\secret\\app.csproj" } }] } });
    input.selection.scenarioId = "測試/card";
    a.transport.update(input);
    assert.equal(a.messages.length, 0);
    assert.doesNotMatch(a.panel.webview.html, /authored/);
    a.ready();
    const output = a.messages[0].view;
    assert.deepEqual(output.selection.presentation, presentation);
    assert.equal(output.scenarios[0].title, hostile);
    assert.equal(output.diagnostics[0].message, "Missing member");
    assert.equal(output.diagnostics[0].index, 0);
    assert.doesNotMatch(JSON.stringify(output), /file:\/\/|C:\\|private|secret|"uri"|"source"/);
    a.transport.dispose(); b.transport.dispose();
});

test("one flight replaces pending state and old acknowledgments cannot release a newer delivery", () => {
    const f = fixture();
    f.transport.update(view("1")); f.ready();
    const first = f.messages[0];
    f.transport.update(view("2")); f.transport.update(view("3"));
    assert.equal(f.messages.length, 1);
    assert.equal(f.timers.size, 1);
    f.ack({ deliveryId: "obsolete" });
    assert.equal(f.messages.length, 1);
    f.ack(first);
    assert.equal(f.messages.length, 2);
    const second = f.messages[1];
    assert.equal(second.view.generation, "3");
    f.transport.update(view("4"));
    f.ack(first);
    assert.equal(f.messages.length, 2);
    f.ack(second);
    assert.equal(f.messages[2].view.generation, "4");
    f.ack(f.messages[2]);
    assert.equal(f.timers.size, 0);
    f.transport.dispose();
});

test("timeout suspends rather than retries and explicit ready recovers the latest snapshot", () => {
    const f = fixture();
    f.transport.update(view()); f.ready();
    const old = f.messages[0];
    const timeout = [...f.timers.values()][0]; timeout();
    assert.equal(f.timers.size, 0);
    f.transport.update(view("2")); f.ack(old);
    assert.equal(f.messages.length, 1);
    f.ready();
    assert.equal(f.messages.length, 2);
    assert.equal(f.messages[1].view.generation, "2");
    assert.notEqual(f.messages[1].deliveryId, old.deliveryId);
    f.transport.dispose();
});

test("hide clears deliveries and show rotates the handshake while close detaches everything", () => {
    const f = fixture();
    f.transport.update(view()); f.ready();
    const oldId = f.panelId(); const old = f.messages[0];
    f.show(false); f.transport.update(view("hidden")); f.ready(); f.ack(old);
    assert.equal(f.messages.length, 1); assert.equal(f.timers.size, 0);
    f.show(true);
    assert.notEqual(f.panelId(), oldId);
    f.receive({ panelId: oldId, kind: "ready" });
    assert.equal(f.messages.length, 1);
    f.ready();
    assert.equal(f.messages[1].view.generation, "hidden");
    assert.deepEqual(f.visibility, [false, true]);
    f.close(); f.close(); f.transport.update(view("closed"));
    assert.equal(f.closes, 1); assert.equal(f.timers.size, 0);
    assert.ok(Object.values(f.events).every(set => set.size === 0));
    assert.equal(f.panelDisposed, false);
    const other = fixture(); other.transport.dispose(); other.close();
    assert.equal(other.closes, 0); assert.equal(other.panelDisposed, false);
});

test("actions are exact bounded current-view capabilities and callback rejection is contained", async () => {
    const f = fixture({ onAction: async () => { throw new Error("controller handled separately"); } });
    f.transport.update(view()); f.ready();
    const current = f.messages[0];
    for (const fields of [
        { kind: "select", scenarioId: "unknown" }, { kind: "select", scenarioId: "card/empty", uri: "file:///secret" },
        { kind: "presentation", presentation: { ...presentation, logicalWidth: 8192, logicalHeight: 8192 } },
        { kind: "presentation", presentation: { ...presentation, density: 0 } },
        { kind: "presentation", presentation: { ...presentation, path: "C:\\secret" } },
        { kind: "zoom", zoom: Infinity }, { kind: "zoom", zoom: 0.1 }, { kind: "diagnostic", index: -1 },
        { kind: "diagnostic", index: 1 }, { kind: "reset", extra: true }, { kind: "command", command: "vscode.open" }
    ]) f.action(current, fields);
    f.receive({ ...current, kind: "stop", version: 2 });
    assert.equal(f.actions.length, 0);
    for (const fields of [{ kind: "select", scenarioId: "card/empty" }, { kind: "presentation", presentation },
        { kind: "zoom", zoom: 2 }, { kind: "reset" }, { kind: "refresh" }, { kind: "stop" }, { kind: "diagnostic", index: 0 }])
        f.action(current, fields);
    assert.deepEqual(f.actions.map(action => action.kind), ["select", "presentation", "zoom", "reset", "refresh", "stop", "diagnostic"]);
    assert.deepEqual(f.actions[1], { kind: "presentation", presentation });
    f.transport.update(view("2", { diagnostics: [{ message: "Different source" }] }));
    f.action(current, { kind: "diagnostic", index: 0 }); f.action(current, { kind: "stop" });
    assert.equal(f.actions.length, 8, "Stop must remain available while indexed old DOM actions are rejected.");
    assert.deepEqual(f.actions.at(-1), { kind: "stop" });
    f.ack(current); f.action(f.messages[1], { kind: "diagnostic", index: 0 });
    assert.equal(f.actions.length, 9);
    f.ack(f.messages[1]);
    f.transport.update(view("3", { phase: "error", catalogStale: true }));
    f.action(f.messages[2], { kind: "select", scenarioId: "card/empty" });
    f.action(f.messages[2], { kind: "presentation", presentation });
    assert.deepEqual(f.actions.slice(-2).map(action => action.kind), ["select", "presentation"],
        "Retained catalog choices can request a new independently verified build.");
    await Promise.resolve(); await Promise.resolve(); f.transport.dispose();
});

test("malformed or excessive display inputs cannot enqueue encoded image or catalog metadata", () => {
    const f = fixture();
    f.transport.update(view("1", { frame: { png: Buffer.alloc(33554433), logicalWidth: 1, logicalHeight: 1, scale: 1, width: 1, height: 1 },
        catalog: { scenarios: Array.from({ length: 65 }, (_, n) => ({ id: String(n), title: "card", ...presentation })) } }));
    f.ready();
    assert.equal(f.messages[0].view.frame, undefined);
    assert.deepEqual(f.messages[0].view.scenarios, []);
    f.ack(f.messages[0]);
    f.transport.update(view("2", { frame: { png, logicalWidth: 1, logicalHeight: 1, scale: 1, width: 2, height: 1 } }));
    assert.equal(f.messages[1].view.frame, undefined);
    f.transport.dispose();
});

test("post failure suspends and late rejection cannot cancel a new acknowledged flight", async () => {
    let reject;
    const f = fixture({ post: () => new Promise((_resolve, fail) => { reject = fail; }) });
    f.transport.update(view()); f.ready();
    const firstReject = reject;
    f.transport.update(view("2")); f.ack(f.messages[0]);
    firstReject(new Error("old post")); await Promise.resolve();
    assert.equal(f.timers.size, 1);
    reject(new Error("current post")); await Promise.resolve();
    assert.equal(f.timers.size, 0);
    f.transport.update(view("3")); assert.equal(f.messages.length, 2);
    f.ready(); assert.equal(f.messages[2].view.generation, "3");
    f.transport.dispose();
});

function page(html) {
    const nodes = new Map(); const messages = []; const events = new Map();
    function node() { return { value: "", style: {}, children: [], handlers: new Map(), captured: new Set(),
        addEventListener(kind, callback) { this.handlers.set(kind, callback); },
        focus() {
            if (document.activeElement === this) return;
            const previous = document.activeElement; document.activeElement = this;
            previous?.handlers.get("blur")?.(); this.handlers.get("focus")?.();
        },
        blur() { if (document.activeElement === this) { document.activeElement = undefined; this.handlers.get("blur")?.(); } },
        setPointerCapture(id) { this.captured.add(id); }, releasePointerCapture(id) { this.captured.delete(id); },
        appendChild(child) { this.children.push(child); }, replaceChildren() { this.children = []; }, removeAttribute() {} }; }
    const document = { body: { dataset: { panelId: /data-panel-id="([^"]+)"/.exec(html)[1] } },
        getElementById(id) { if (!nodes.has(id)) nodes.set(id, node()); return nodes.get(id); }, createElement: node,
        addEventListener(kind, callback) { events.set(kind, callback); } };
    const window = { addEventListener(kind, callback) { events.set(kind, callback); } };
    vm.runInNewContext(/<script nonce="[^"]+">([\s\S]*)<\/script>/.exec(html)[1],
        { document, window, acquireVsCodeApi: () => ({ postMessage: message => messages.push(JSON.parse(JSON.stringify(message))) }) });
    return { nodes, messages, document,
        emitWindow: kind => events.get(kind)?.(),
        emitDocument: (kind, event) => events.get(kind)?.(event), deliver: value => {
        // Image decoding is controlled by onload; these are the independent decoded dimensions.
        if (value.view?.frame) { const image = document.getElementById("image"); image.naturalWidth = value.view.frame.width; image.naturalHeight = value.view.frame.height; }
        events.get("message")({ data: value });
    } };
}

test("fixed webview script renders text, applies paired dimensions, keeps zoom display-only and captures diagnostic revision", () => {
    const f = fixture(); const browser = page(f.panel.webview.html);
    f.receive(browser.messages.shift());
    const input = view("1", { diagnostic: "<img src=x onerror=alert(1)>", frame: { png, logicalWidth: 1, logicalHeight: 1, scale: 1, width: 1, height: 1 } });
    input.zoom = 2;
    f.transport.update(input);
    browser.deliver(f.messages[0]);
    assert.equal(browser.nodes.get("diagnostic").textContent, input.state.diagnostic);
    assert.equal(browser.nodes.get("image").style.width, "2px");
    assert.equal(browser.nodes.get("image").src, "data:image/png;base64," + png.toString("base64"));
    assert.equal(browser.messages.length, 0, "Receipt alone must not acknowledge an image that has not loaded.");
    browser.nodes.get("image").onload();
    f.receive(browser.messages.shift());
    const oldButton = browser.nodes.get("diagnostics").children[0];
    browser.nodes.get("logicalWidth").value = "320"; browser.nodes.get("logicalHeight").value = "240";
    browser.nodes.get("presentation").handlers.get("submit")({ preventDefault() {} });
    f.receive(browser.messages.shift());
    assert.deepEqual(f.actions[0], { kind: "presentation", presentation: { ...presentation, logicalWidth: 320, logicalHeight: 240 } });
    browser.nodes.get("zoom").value = "3"; browser.nodes.get("zoom").handlers.get("change")();
    f.receive(browser.messages.shift()); assert.deepEqual(f.actions[1], { kind: "zoom", zoom: 3 });
    browser.nodes.get("image").complete = true; browser.nodes.get("image").naturalWidth = 1;
    f.transport.update({ ...input, zoom: 3, state: { ...input.state, generation: "2" } }); browser.deliver(f.messages[1]);
    assert.equal(browser.nodes.get("image").style.width, "3px");
    f.receive(browser.messages.shift());
    assert.equal(f.timers.size, 0, "An already decoded identical image must acknowledge without another load event.");
    f.transport.update(view("3", { diagnostics: [{ message: "New diagnostic" }] })); browser.deliver(f.messages[2]);
    f.receive(browser.messages.shift());
    oldButton.handlers.get("click")(); f.receive(browser.messages.shift());
    assert.equal(f.actions.length, 2, "A retained old diagnostic button must carry its own delivery identity.");
    f.transport.dispose();
});

function liveFrame(extra = {}) {
    return { png, logicalWidth: 160, logicalHeight: 120, scale: 1.1, width: 176, height: 132,
        live: true, generation: "1", scenarioId: "card/empty", sessionId: "private-session", requestId: "private-request",
        projectTargetDigest: "1".repeat(64), inputDigest: "2".repeat(64), artifactDigest: "3".repeat(64), presentationId: "private-presentation",
        projectPath: "C:\\private\\app.csproj", ...extra };
}
function liveView(frame, extra = {}) {
    return { ...view("1", { frame, interactive: true, ...extra }), running: true };
}
function browserLive(options = {}) {
    const f = fixture(options); const browser = page(f.panel.webview.html);
    f.receive(browser.messages.shift());
    const drain = () => { while (browser.messages.length) f.receive(browser.messages.shift()); };
    const render = input => { f.transport.update(input); browser.deliver(f.messages.at(-1)); };
    const load = () => { browser.nodes.get("image").onload?.(); drain(); };
    const emit = (id, kind, fields = {}) => {
        let prevented = false;
        browser.nodes.get(id).handlers.get(kind)({ preventDefault() { prevented = true; }, ...fields }); drain();
        return prevented;
    };
    return { f, browser, drain, render, load, emit, inputs: () => f.actions.filter(action => action.kind === "input").map(action => action.input) };
}

test("live frame ACK waits for image display and status deliveries cannot acknowledge a newer frame", () => {
    const b = browserLive(); const first = liveFrame();
    b.render(liveView(first));
    assert.equal(b.f.actions.length, 0);
    assert.doesNotMatch(JSON.stringify(b.f.messages[0]), /private-session|projectPath|C:\\\\private/);
    const oldLoad = b.browser.nodes.get("image").onload;
    b.browser.nodes.get("image").naturalWidth = 175;
    b.load();
    assert.equal(b.f.actions.length, 0, "A decoded image with mismatched dimensions cannot acknowledge native frame delivery.");
    b.browser.nodes.get("image").naturalWidth = 176;
    b.load();
    assert.deepEqual(b.f.actions, [{ kind: "frameDisplayed", frame: first }]);
    b.browser.nodes.get("image").complete = true; b.browser.nodes.get("image").naturalWidth = 176;
    b.render({ ...liveView(first), zoom: 2 }); b.drain();
    assert.equal(b.f.actions.length, 1, "Zoom re-delivery must not ACK a frame twice.");
    const second = liveFrame({ png: Buffer.concat([png, Buffer.from([1])]) });
    b.browser.nodes.get("image").complete = false;
    b.render(liveView(second)); oldLoad(); b.drain();
    assert.equal(b.f.actions.length, 1, "The old load callback must not ACK the newly delivered image.");
    b.load();
    assert.deepEqual(b.f.actions.at(-1), { kind: "frameDisplayed", frame: second });
    b.f.transport.dispose();
});

test("extension admits exact current displayed input only and rejects unknown fields, keys and stale frames", () => {
    const f = fixture(); const frame = liveFrame();
    f.transport.update(liveView(frame)); f.ready();
    const delivery = f.messages[0];
    const valid = { type: "key", action: "down", key: "Enter", modifiers: 0, repeat: false };
    const submit = (input, extra = {}) => f.action(delivery, { kind: "input", frameCapability: delivery.view.frame.capability, input, ...extra });
    submit(valid); assert.equal(f.actions.length, 0, "Receipt without display ACK must remain read-only.");
    f.ack(delivery);
    for (const invalid of [{ ...valid, key: "F12" }, { ...valid, extra: true }, { ...valid, modifiers: 16 },
        { ...valid, action: "up", repeat: true }, { type: "text", text: "x".repeat(4097) },
        { type: "pointer", action: "down", pointerId: 16, x: 0, y: 0, button: "primary", modifiers: 0 }]) submit(invalid);
    submit(valid, { source: "file:///private" }); submit(valid, { frameCapability: "forged" });
    assert.equal(f.actions.length, 1);
    submit(valid);
    assert.deepEqual(f.actions.at(-1), { kind: "input", frame, input: valid });
    f.transport.update(liveView(liveFrame())); submit(valid);
    assert.equal(f.actions.length, 2, "An older displayed frame cannot drive a pending replacement frame.");
    f.ack(f.messages.at(-1));
    const current = f.messages.at(-1);
    f.transport.update(liveView(liveFrame(), { stale: true, phase: "stale" }));
    f.action(current, { kind: "input", frameCapability: current.view.frame.capability, input: valid });
    assert.equal(f.actions.filter(action => action.kind === "input").length, 1);
    f.transport.dispose();
});

test("pointer coordinates use actual displayed bounds across zoom and letterbox while captured releases may leave the image", () => {
    const b = browserLive(); b.render(liveView(liveFrame())); b.load();
    const image = b.browser.nodes.get("image"); const surface = b.browser.nodes.get("canvas");
    image.getBoundingClientRect = () => ({ left: 100, top: 50, width: 320, height: 240 });
    assert.equal(b.emit("canvas", "pointerdown", { pointerId: 42, button: 0, clientX: 80, clientY: 60 }), false);
    assert.equal(b.inputs().length, 0, "Letterbox pixels do not belong to the preview.");
    assert.equal(b.emit("canvas", "pointerdown", { pointerId: 42, button: 0, clientX: 260, clientY: 170, shiftKey: true }), true);
    assert.deepEqual(b.inputs().at(-1), { type: "pointer", action: "down", pointerId: 0, x: 80, y: 60, button: "primary", modifiers: 1 });
    assert.equal(surface.captured.has(42), true);
    assert.equal(b.emit("canvas", "pointerdown", { pointerId: 42, button: 0, clientX: 260, clientY: 170 }), false);
    assert.equal(b.emit("canvas", "pointerdown", { pointerId: 42, button: 2, clientX: 260, clientY: 170 }), false);
    assert.equal(b.inputs().length, 1, "A held pointer cannot forward another press of either the same or a different button.");
    assert.equal(surface.captured.has(42), true);
    b.emit("canvas", "pointermove", { pointerId: 42, clientX: 460, clientY: 290 });
    assert.deepEqual(b.inputs().at(-1), { type: "pointer", action: "move", pointerId: 0, x: 180, y: 120, button: "none", modifiers: 0 });
    b.emit("canvas", "pointerup", { pointerId: 42, button: 0, clientX: 460, clientY: 290 });
    assert.equal(b.inputs().at(-1).action, "up"); assert.equal(surface.captured.size, 0);
    image.getBoundingClientRect = () => ({ left: 10, top: 20, width: 80, height: 60 });
    b.emit("canvas", "pointermove", { pointerId: 42, clientX: 50, clientY: 50 });
    assert.equal(b.inputs().at(-1).x, 80); assert.equal(b.inputs().at(-1).y, 60);
    assert.equal(b.emit("canvas", "wheel", { clientX: 50, clientY: 50, deltaX: 4, deltaY: 8, deltaMode: 0 }), true);
    assert.deepEqual(b.inputs().at(-1), { type: "wheel", x: 80, y: 60, deltaX: 8, deltaY: 16, modifiers: 0 });
    b.f.transport.dispose();
});

test("committed text does not duplicate printable keys and preview handlers preserve Escape and unsupported browser keys", () => {
    const b = browserLive(); b.render(liveView(liveFrame())); b.load();
    b.browser.nodes.get("canvas").focus(); b.emit("canvas", "keydown", { key: "Enter" });
    assert.equal(b.emit("textInput", "keydown", { key: "a" }), false);
    assert.equal(b.emit("textInput", "beforeinput", { inputType: "insertText", data: "a" }), true);
    assert.deepEqual(b.inputs(), [{ type: "text", text: "a" }]);
    assert.equal(b.emit("textInput", "keydown", { key: "ArrowLeft", ctrlKey: true, repeat: true }), true);
    assert.deepEqual(b.inputs().at(-1), { type: "key", action: "down", key: "Left", modifiers: 2, repeat: true });
    b.emit("textInput", "keyup", { key: "ArrowLeft", ctrlKey: true });
    assert.equal(b.inputs().at(-1).repeat, false);
    assert.equal(b.emit("textInput", "keydown", { key: "Escape" }), false);
    assert.deepEqual(b.inputs().at(-1), { type: "key", action: "down", key: "Escape", modifiers: 0, repeat: false });
    const count = b.inputs().length;
    assert.equal(b.emit("textInput", "keydown", { key: "v", ctrlKey: true }), false);
    assert.equal(b.emit("textInput", "paste", { clipboardData: { getData: () => "Pasted text" } }), true);
    assert.deepEqual(b.inputs().at(-1), { type: "text", text: "Pasted text" });
    assert.equal(b.emit("textInput", "keydown", { key: "F12" }), false);
    assert.equal(b.emit("textInput", "beforeinput", { inputType: "insertCompositionText", data: "仮", isComposing: true }), false);
    assert.equal(b.inputs().length, count + 1);
    b.f.transport.dispose();
});

function startPointer(b) {
    b.browser.nodes.get("image").getBoundingClientRect = () => ({ left: 0, top: 0, width: 160, height: 120 });
    b.emit("canvas", "pointerdown", { pointerId: 42, button: 0, clientX: 80, clientY: 60 });
}

test("same-run image replacement preserves browser focus and releases retain their initial displayed frame owner", () => {
    const b = browserLive(); const first = liveFrame(); b.render(liveView(first)); b.load(); startPointer(b);
    b.emit("textInput", "keydown", { key: "ArrowLeft" });
    const second = liveFrame({ png: Buffer.concat([png, Buffer.from([1])]) });
    b.render(liveView(second)); b.load();
    assert.equal(b.browser.document.activeElement, b.browser.nodes.get("textInput"));
    assert.equal(b.browser.nodes.get("canvas").captured.has(42), true);
    b.emit("textInput", "keyup", { key: "ArrowLeft" });
    assert.deepEqual(b.f.actions.at(-1), { kind: "input", frame: first,
        input: { type: "key", action: "up", key: "Left", modifiers: 0, repeat: false } });
    b.emit("canvas", "pointerup", { pointerId: 42, button: 0, clientX: 200, clientY: 60 });
    assert.equal(b.f.actions.at(-1).frame, first); assert.equal(b.inputs().at(-1).action, "up");
    assert.equal(b.browser.nodes.get("canvas").captured.size, 0);
    b.f.transport.dispose();
});

test("pending-image releases reach their admitted gesture owner while blur cancels capture and focus", () => {
    for (const edge of ["pointerup", "keyup", "blur"]) {
        const b = browserLive(); const first = liveFrame(); b.render(liveView(first)); b.load(); startPointer(b);
        if (edge === "keyup") b.emit("textInput", "keydown", { key: "Enter" });
        const before = b.inputs().length;
        b.render(liveView(liveFrame({ png: Buffer.concat([png, Buffer.from([1])]) })));
        if (edge === "pointerup") b.emit("canvas", edge, { pointerId: 42, button: 0, clientX: 200, clientY: 60 });
        else if (edge === "keyup") b.emit("textInput", edge, { key: "Enter" });
        else { b.browser.emitWindow("blur"); b.drain(); }
        assert.equal(b.inputs().length, before + 1);
        if (edge === "blur") {
            assert.equal(b.inputs().at(-1).action, "cancel");
            assert.deepEqual(b.f.actions.at(-1), { kind: "focus", frame: first, focused: false });
            assert.equal(b.browser.nodes.get("canvas").captured.size, 0);
            assert.equal(b.browser.document.activeElement, undefined);
        } else {
            assert.equal(b.f.actions.at(-1).frame, first);
            assert.equal(b.inputs().at(-1).action, "up");
            assert.equal(b.f.actions.filter(action => action.kind === "focus" && !action.focused).length, 0);
            assert.equal(b.browser.document.activeElement, b.browser.nodes.get("textInput"));
        }
        b.f.transport.dispose();
    }
});

test("stale authority and moving browser focus to the toolbar release capture without swallowing toolbar keys", () => {
    for (const boundary of ["stale", "toolbar", "visibility", "stop"]) {
        const b = browserLive(); const frame = liveFrame(); b.render(liveView(frame)); b.load(); startPointer(b);
        if (boundary === "stale") b.render(liveView(frame, { stale: true, phase: "stale" }));
        else if (boundary === "toolbar") b.browser.nodes.get("settings").focus();
        else if (boundary === "visibility") { b.browser.document.hidden = true; b.browser.emitDocument("visibilitychange"); }
        else b.emit("stop", "click");
        b.drain();
        assert.equal(b.browser.nodes.get("canvas").captured.size, 0);
        const losses = b.f.actions.filter(action => action.kind === "focus" && !action.focused);
        assert.deepEqual(losses, [{ kind: "focus", frame, focused: false }], `${boundary} releases its owner once.`);
        if (boundary === "toolbar") {
            let prevented = false;
            b.browser.emitDocument("keydown", { key: "ArrowLeft", preventDefault() { prevented = true; } });
            assert.equal(prevented, false); assert.equal(b.browser.document.activeElement, b.browser.nodes.get("settings"));
        }
        b.f.transport.dispose();
    }
});

test("preview surface enters capture deliberately and Shift+Escape leaves without reaching Core or trapping toolbar navigation", () => {
    const b = browserLive(); const frame = liveFrame(); b.render(liveView(frame)); b.load();
    const surface = b.browser.nodes.get("canvas"); surface.focus();
    assert.equal(b.f.actions.filter(action => action.kind === "focus").length, 0);
    assert.equal(b.emit("canvas", "keydown", { key: "Tab" }), false);
    assert.equal(b.emit("canvas", "keydown", { key: "Enter" }), true);
    assert.deepEqual(b.f.actions.at(-1), { kind: "focus", frame, focused: true });
    assert.equal(b.browser.document.activeElement, b.browser.nodes.get("textInput"));
    assert.equal(b.browser.nodes.get("leave").disabled, false);
    assert.match(b.browser.nodes.get("caption").textContent, /Shift\+Escape or Leave preview/);
    assert.equal(b.emit("textInput", "keydown", { key: "Tab", shiftKey: true }), true);
    assert.deepEqual(b.inputs().at(-1), { type: "key", action: "down", key: "Tab", modifiers: 1, repeat: false });
    const count = b.inputs().length;
    for (const fields of [{ key: "F6" }, { key: "F6", shiftKey: true }, { key: "Tab", ctrlKey: true },
        { key: "ArrowLeft", altKey: true }, { key: "p", ctrlKey: true, shiftKey: true }])
        assert.equal(b.emit("textInput", "keydown", fields), false);
    assert.equal(b.inputs().length, count);
    assert.equal(b.emit("textInput", "keydown", { key: "Escape", shiftKey: true }), true);
    assert.equal(b.inputs().length, count, "Release binding must not become an application Escape.");
    assert.deepEqual(b.f.actions.at(-1), { kind: "focus", frame, focused: false });
    assert.equal(b.browser.document.activeElement, surface); assert.equal(b.browser.nodes.get("leave").disabled, true);
    assert.equal(b.emit("canvas", "keydown", { key: "Tab", shiftKey: true }), false);
    b.f.transport.dispose();
});

test("release racing extension admission completes its admitted click without requiring a new image ACK", () => {
    const b = browserLive(); const first = liveFrame(); b.render(liveView(first)); b.load(); startPointer(b);
    b.f.transport.update(liveView(liveFrame({ png: Buffer.concat([png, Buffer.from([1])]) })));
    // The next view is in flight, but the page has not received it yet.
    const count = b.inputs().length;
    b.emit("canvas", "pointerup", { pointerId: 42, button: 0, clientX: 200, clientY: 60 });
    assert.equal(b.inputs().length, count + 1);
    assert.equal(b.f.actions.at(-1).frame, first); assert.equal(b.inputs().at(-1).action, "up");
    assert.equal(b.f.messages.at(-1).kind, "view", "An ordinary held release must not force interaction exit.");
    assert.equal(b.browser.nodes.get("canvas").captured.size, 0); assert.equal(b.browser.document.activeElement, b.browser.nodes.get("textInput"));
    b.f.transport.dispose();
});

test("first click acts at unchanged painted coordinates while entering and leaving reserve toolbar and caption space", () => {
    const b = browserLive(); const frame = liveFrame(); b.render(liveView(frame)); b.load();
    assert.match(b.f.panel.webview.html, /<button id="leave" type="button" disabled>Leave preview<\/button>/);
    assert.doesNotMatch(b.f.panel.webview.html, /<button id="leave"[^>]*hidden/);
    const leave = b.browser.nodes.get("leave");
    const caption = b.browser.nodes.get("caption").textContent;
    assert.equal(leave.disabled, true);
    // Model the observed 18px toolbar wrap if Leave starts taking layout space on entry.
    let toolbarMoved = false;
    Object.defineProperty(leave, "hidden", { get: () => false, set() { toolbarMoved = true; } });
    b.browser.nodes.get("image").getBoundingClientRect = () => ({ left: 100, top: toolbarMoved ? 118 : 100, width: 80, height: 60 });
    assert.equal(b.emit("canvas", "pointermove", { pointerId: 42, clientX: 140, clientY: 120 }), false);
    assert.deepEqual(b.inputs(), [], "Moving over the image before entering interaction must not replace its frame ahead of the first click.");
    b.emit("canvas", "pointerdown", { pointerId: 42, button: 0, clientX: 140, clientY: 120 });
    assert.equal(leave.disabled, false); assert.equal(toolbarMoved, false);
    assert.equal(b.browser.nodes.get("caption").textContent, caption, "Entry help cannot resize the canvas between pointer edges.");
    b.emit("canvas", "pointerup", { pointerId: 42, button: 0, clientX: 140, clientY: 120 });
    assert.deepEqual(b.inputs(), [
        { type: "pointer", action: "down", pointerId: 0, x: 80, y: 40, button: "primary", modifiers: 0 },
        { type: "pointer", action: "up", pointerId: 0, x: 80, y: 40, button: "primary", modifiers: 0 }
    ]);
    b.emit("leave", "click");
    assert.equal(leave.disabled, true); assert.equal(toolbarMoved, false);
    assert.equal(b.browser.nodes.get("caption").textContent, caption);
    b.f.transport.dispose();
});

test("browser Ctrl+A down followed by Control up and A up preserves the physical key and original frame owner", () => {
    const b = browserLive(); const first = liveFrame(); b.render(liveView(first)); b.load();
    b.browser.nodes.get("canvas").focus(); b.emit("canvas", "keydown", { key: "Enter", code: "Enter" });
    assert.equal(b.emit("textInput", "keydown", { key: "Control", code: "ControlLeft", ctrlKey: true }), false);
    b.emit("textInput", "keydown", { key: "a", code: "KeyA", ctrlKey: true });
    const second = liveFrame({ png: Buffer.concat([png, Buffer.from([1])]) }); b.render(liveView(second));
    assert.equal(b.emit("textInput", "keyup", { key: "Control", code: "ControlLeft", ctrlKey: false }), false);
    assert.equal(b.emit("textInput", "keyup", { key: "a", code: "KeyA", ctrlKey: false }), true);
    assert.deepEqual(b.inputs(), [
        { type: "key", action: "down", key: "A", modifiers: 2, repeat: false },
        { type: "key", action: "up", key: "A", modifiers: 0, repeat: false }
    ]);
    assert.equal(b.f.actions.at(-1).frame, first);
    assert.equal(b.f.actions.filter(action => action.kind === "focus" && !action.focused).length, 0);
    b.load();
    b.emit("textInput", "keydown", { key: "A", code: "KeyA", ctrlKey: true, shiftKey: true });
    b.emit("textInput", "keyup", { key: "a", code: "KeyA" });
    assert.deepEqual(b.inputs().at(-1), { type: "key", action: "up", key: "A", modifiers: 0, repeat: false });
    assert.equal(b.f.actions.at(-1).frame, second);
    b.f.transport.dispose();
});

test("older capabilities permit only one matching admitted release within the same complete live run identity", () => {
    const f = fixture(); const first = liveFrame(); f.transport.update(liveView(first)); f.ready();
    const shown = f.messages[0]; f.ack(shown);
    const send = input => f.action(shown, { kind: "input", frameCapability: shown.view.frame.capability, input });
    f.action(shown, { kind: "focus", frameCapability: shown.view.frame.capability, focused: true });
    const pointer = { type: "pointer", action: "down", pointerId: 0, x: 80, y: 60, button: "primary", modifiers: 0 };
    send(pointer); send({ type: "key", action: "down", key: "A", modifiers: 2, repeat: false });
    f.transport.update(liveView(liveFrame({ png: Buffer.concat([png, Buffer.from([1])]) })));
    f.ack(f.messages.at(-1));
    const before = f.actions.length;
    for (const input of [{ ...pointer, action: "move", button: "none" }, pointer, { type: "text", text: "stale" },
        { ...pointer, action: "up", button: "secondary" }, { ...pointer, action: "up", pointerId: 1 },
        { type: "key", action: "up", key: "C", modifiers: 0, repeat: false }]) send(input);
    assert.equal(f.actions.length, before, "No stale hit testing, movement, text, unrelated release or wrong button is admitted.");
    send({ ...pointer, action: "up" });
    assert.deepEqual(f.actions.at(-1), { kind: "input", frame: first, input: { ...pointer, action: "up" } });
    send({ ...pointer, action: "up" }); assert.equal(f.actions.length, before + 1);
    f.transport.update(liveView(liveFrame({ requestId: "different-owned-request" })));
    const lost = f.actions.length;
    send({ type: "key", action: "up", key: "A", modifiers: 0, repeat: false });
    assert.equal(f.actions.length, lost, "A matching generation string cannot authorize release into a different run.");
    assert.deepEqual(f.actions.at(-1), { kind: "focus", frame: first, focused: false });
    f.transport.dispose();
});

test("pending frame accepts exact last displayed press and wheel with displayed metadata until the newer image ACK", () => {
    const b = browserLive(); const first = liveFrame(); b.render({ ...liveView(first), zoom: 0.5 }); b.load();
    const shown = b.f.messages[0];
    const image = b.browser.nodes.get("image"); image.getBoundingClientRect = () => ({ left: 100, top: 50, width: 80, height: 60 });
    // Distinct pending image metadata catches accidental coordinate conversion against the queued image.
    // The native final geometry check remains responsible for accepting/rejecting that queued scene.
    const second = liveFrame({ png: Buffer.concat([png, Buffer.from([1])]), logicalWidth: 320, logicalHeight: 240, width: 352, height: 264 });
    b.render({ ...liveView(second), zoom: 0.5 });
    assert.equal(image.style.width, "80px", "The old painted image retains its logical display size until decoding completes.");
    assert.equal(b.emit("canvas", "pointerdown", { pointerId: 42, button: 0, clientX: 140, clientY: 80 }), true);
    assert.deepEqual(b.inputs().at(-1), { type: "pointer", action: "down", pointerId: 0, x: 80, y: 60, button: "primary", modifiers: 0 });
    assert.equal(b.f.actions.at(-1).frame, first);
    assert.equal(b.emit("canvas", "wheel", { clientX: 140, clientY: 80, deltaX: 3, deltaY: 4, deltaMode: 0 }), true);
    assert.deepEqual(b.inputs().at(-1), { type: "wheel", x: 80, y: 60, deltaX: 6, deltaY: 8, modifiers: 0 });
    const count = b.inputs().length;
    assert.equal(b.emit("canvas", "pointermove", { pointerId: 42, clientX: 150, clientY: 90 }), false);
    assert.equal(b.emit("textInput", "keydown", { key: "Enter" }), false);
    assert.equal(b.emit("textInput", "beforeinput", { inputType: "insertText", data: "pending" }), false);
    assert.equal(b.inputs().length, count);
    const focus = b.f.actions.find(action => action.kind === "focus");
    assert.deepEqual(focus, { kind: "focus", frame: first, focused: true }, "Entry focus uses the exact last actual display too.");
    b.load(); assert.equal(image.style.width, "160px");
    const after = b.f.actions.length;
    for (const input of [{ type: "wheel", x: 80, y: 60, deltaX: 0, deltaY: 8, modifiers: 0 },
        { type: "pointer", action: "down", pointerId: 1, x: 80, y: 60, button: "primary", modifiers: 0 }])
        b.f.action(shown, { kind: "input", frameCapability: shown.view.frame.capability, input });
    assert.equal(b.f.actions.length, after, "After ACK the old image is history and cannot start a press/wheel.");
    b.emit("canvas", "pointerup", { pointerId: 42, button: 0, clientX: 140, clientY: 80 });
    assert.equal(b.f.actions.at(-1).frame, first, "The existing owned release remains tied to its original down.");
    b.f.transport.dispose();
});

test("host release revokes input before a pending image ACK or worker callback and returns an uncaptured surface focus stop", () => {
    const b = browserLive({ onAction: action => action.kind === "focus" && !action.focused ? new Promise(() => {}) : undefined });
    const first = liveFrame(); b.render(liveView(first)); b.load(); startPointer(b);
    b.emit("textInput", "keydown", { key: "Enter" });
    const second = liveFrame({ png: Buffer.concat([png, Buffer.from([1])]) }); b.render(liveView(second));
    assert.equal(b.f.transport.releaseInput(), true);
    assert.deepEqual(b.f.actions.at(-1), { kind: "focus", frame: first, focused: false });
    assert.equal(b.f.timers.size, 1, "Host release must not ACK the pending image.");
    const release = b.f.messages.at(-1); assert.equal(release.kind, "release");
    b.browser.deliver(release); b.drain();
    assert.equal(b.browser.nodes.get("canvas").captured.size, 0);
    assert.equal(b.browser.nodes.get("leave").disabled, true);
    assert.equal(b.browser.document.activeElement, b.browser.nodes.get("canvas"));
    assert.equal(b.emit("canvas", "keydown", { key: "Tab" }), false);
    b.load();
    const focuses = b.f.actions.filter(action => action.kind === "focus");
    assert.deepEqual(focuses.map(action => action.focused), [true, false], "Image admission must not recapture focus.");
    const count = b.inputs().length;
    b.emit("textInput", "beforeinput", { inputType: "insertText", data: "stale after release" });
    assert.equal(b.inputs().length, count, "The host command revokes extension input admission until deliberate re-entry.");
    b.f.transport.dispose();
    assert.equal(b.f.transport.releaseInput(), false);
});

test("visible Leave preview action exits immediately even when native focus cleanup remains unresolved", () => {
    const b = browserLive({ onAction: action => action.kind === "focus" && !action.focused ? new Promise(() => {}) : undefined });
    const frame = liveFrame(); b.render(liveView(frame)); b.load(); startPointer(b);
    assert.match(b.f.panel.webview.html, /VS Code’s Tab Moves Focus toggle is not synchronized/);
    b.emit("leave", "click");
    assert.deepEqual(b.f.actions.at(-1), { kind: "focus", frame, focused: false });
    assert.equal(b.browser.nodes.get("canvas").captured.size, 0);
    assert.equal(b.browser.document.activeElement, b.browser.nodes.get("canvas"));
    assert.equal(b.emit("canvas", "keydown", { key: "Tab", shiftKey: true }), false);
    b.f.transport.dispose();
});

test("status and zoom updates retain presentation drafts while actual scenario or presentation changes reset them", () => {
    const f = fixture(); const browser = page(f.panel.webview.html);
    f.receive(browser.messages.shift());
    function render(input) {
        f.transport.update(input);
        browser.deliver(f.messages.at(-1));
        f.receive(browser.messages.shift());
    }
    render(view("1"));
    browser.nodes.get("logicalWidth").value = "32";
    browser.nodes.get("logicalHeight").value = "";
    const building = view("2", { phase: "building" }); building.zoom = 2;
    render(building);
    assert.equal(browser.nodes.get("logicalWidth").value, "32");
    assert.equal(browser.nodes.get("logicalHeight").value, "");
    assert.equal(browser.nodes.get("status").textContent, "Building preview");
    const selected = view("3", { phase: "discovering", catalog: { scenarios: [{ id: "card/other", title: "Other", ...presentation, logicalWidth: 640 }] } });
    selected.selection.scenarioId = "card/other";
    render(selected);
    assert.equal(browser.nodes.get("logicalWidth").value, 640);
    assert.equal(browser.nodes.get("logicalHeight").value, 120);
    assert.equal(browser.nodes.get("status").textContent, "Loading scenarios");
    browser.nodes.get("logicalWidth").value = "99";
    selected.selection.presentation = { ...presentation, logicalWidth: 800 };
    selected.state.generation = "4";
    render(selected);
    assert.equal(browser.nodes.get("logicalWidth").value, 800);
    f.transport.dispose();
});


test("settings sheet cancels drafts, defaults require Apply, and invalid physical bounds remain local", () => {
    const f = fixture(); const browser = page(f.panel.webview.html);
    f.receive(browser.messages.shift());
    f.transport.update(view()); browser.deliver(f.messages[0]); f.receive(browser.messages.shift());
    const get = id => browser.nodes.get(id);
    const click = id => get(id).handlers.get("click")();
    click("settings");
    get("logicalWidth").value = "900"; get("logicalWidth").handlers.get("input")();
    const replacement = view("2", { phase: "building", catalog: { scenarios: [{ id: "card/empty", title: "New label", ...presentation, logicalWidth: 700 }] } });
    f.transport.update(replacement); browser.deliver(f.messages[1]); f.receive(browser.messages.shift());
    assert.equal(get("logicalWidth").value, "900", "New catalog defaults must not overwrite a dirty open draft.");
    click("cancel"); assert.equal(get("logicalWidth").value, 700);
    assert.equal(get("presentation").hidden, true); assert.equal(f.actions.length, 0);
    click("settings"); get("logicalWidth").value = "99"; click("defaults");
    assert.equal(get("logicalWidth").value, 700); assert.equal(f.actions.length, 0);
    get("logicalWidth").value = "8192"; get("logicalHeight").value = "8192";
    get("presentation").handlers.get("submit")({ preventDefault() {} });
    assert.equal(browser.messages.length, 0); assert.match(get("draftError").textContent, /16,777,216/);
    click("defaults"); get("presentation").handlers.get("submit")({ preventDefault() {} });
    f.receive(browser.messages.shift()); assert.equal(f.actions.length, 1);
    assert.equal(f.actions[0].presentation.logicalWidth, 700); assert.equal(get("presentation").hidden, true);
    f.transport.dispose();
});

test("Fit shrinks accepted pixels below numeric zoom bounds without enlargement or host rebuild actions", () => {
    const f = fixture(); const browser = page(f.panel.webview.html);
    f.receive(browser.messages.shift());
    const input = view("1", { stale: true, frame: { png, logicalWidth: 160, logicalHeight: 120, scale: 1.1, width: 176, height: 132,
        scenarioId: "card/old", scenarioTitle: "Old accepted scenario", generation: "accepted1", effectivePresentation: { ...presentation, colorScheme: "dark" } } });
    input.zoom = "fit";
    browser.nodes.set("canvas", { clientWidth: 16, clientHeight: 12 });
    f.transport.update(input); browser.deliver(f.messages[0]);
    assert.equal(browser.nodes.get("image").style.width, "16px");
    assert.match(browser.nodes.get("caption").textContent, /Old accepted scenario.*dark.*accepted1.*display 10%/);
    browser.nodes.get("image").onload(); f.receive(browser.messages.shift());
    browser.nodes.get("canvas").clientWidth = 800; browser.nodes.get("canvas").clientHeight = 600;
    f.transport.update({ ...input, state: { ...input.state, generation: "2" } }); browser.deliver(f.messages[1]);
    assert.equal(browser.nodes.get("image").style.width, "160px", "Fit must never enlarge.");
    assert.equal(f.actions.length, 0);
    browser.nodes.get("zoom").value = "2"; browser.nodes.get("zoom").handlers.get("change")();
    f.receive(browser.messages.at(-1)); assert.deepEqual(f.actions.at(-1), { kind: "zoom", zoom: 2 });
    f.transport.dispose();
});
