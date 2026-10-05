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
    function node() { return { value: "", style: {}, children: [], handlers: new Map(),
        addEventListener(kind, callback) { this.handlers.set(kind, callback); },
        appendChild(child) { this.children.push(child); }, replaceChildren() { this.children = []; }, removeAttribute() {} }; }
    const document = { body: { dataset: { panelId: /data-panel-id="([^"]+)"/.exec(html)[1] } },
        getElementById(id) { if (!nodes.has(id)) nodes.set(id, node()); return nodes.get(id); }, createElement: node,
        addEventListener(kind, callback) { events.set(kind, callback); } };
    const window = { addEventListener(kind, callback) { events.set(kind, callback); } };
    vm.runInNewContext(/<script nonce="[^"]+">([\s\S]*)<\/script>/.exec(html)[1],
        { document, window, acquireVsCodeApi: () => ({ postMessage: message => messages.push(JSON.parse(JSON.stringify(message))) }) });
    return { nodes, messages, deliver: value => events.get("message")({ data: value }) };
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
