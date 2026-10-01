"use strict";

const assert = require("node:assert/strict");
const path = require("node:path");
const { test } = require("node:test");
const { createPreviewCommands, frameHtml } = require("./preview-ui");

function fixture({ release = async () => {}, inputReport } = {}) {
    const commands = new Map();
    const timers = new Map();
    const watchers = [];
    const errors = [];
    const builds = [];
    const runtimes = [];
    const folder = { name: "app", uri: { scheme: "file", fsPath: path.resolve("preview-ui-fixture") } };
    let closed;
    let saved;
    let panel;
    let nextTimer = 0;
    const settings = { projectPath: "preview/Preview.csproj", scenarioId: "card/empty", targetFramework: "net10.0",
        toolsDirectory: path.resolve("trusted-tools") };
    const vscode = {
        env: {}, ViewColumn: { Beside: 2 },
        RelativePattern: class { constructor(base, pattern) { this.base = base; this.pattern = pattern; } },
        commands: { registerCommand(name, callback) { commands.set(name, callback); return { dispose() {} }; } },
        workspace: {
            isTrusted: true, workspaceFolders: [folder],
            getConfiguration: () => ({ get: () => new Proxy(settings, {}) }),
            createFileSystemWatcher(pattern) {
                const watcher = { pattern, callbacks: [], disposed: false, dispose() { this.disposed = true; } };
                for (const event of ["onDidCreate", "onDidChange", "onDidDelete"])
                    watcher[event] = callback => { watcher.callbacks.push(callback); return { dispose() {} }; };
                watchers.push(watcher);
                return watcher;
            },
            onDidSaveTextDocument(callback) { saved = callback; return { dispose() {} }; }
        },
        window: {
            showErrorMessage: message => errors.push(message),
            createOutputChannel: () => ({ appendLine() {}, dispose() {} }),
            createWebviewPanel(_id, _title, _column, options) {
                assert.equal(options.enableScripts, false);
                assert.deepEqual(options.localResourceRoots, []);
                panel = { webview: { html: "" }, onDidDispose(callback) { closed = callback; }, dispose() { closed(); } };
                return panel;
            }
        }
    };
    const context = { subscriptions: [], globalStorageUri: { fsPath: path.resolve("preview-test-storage") } };
    createPreviewCommands(vscode, context, {
        platform: "win32", schedule(callback) { timers.set(++nextTimer, callback); return nextTimer; },
        cancelScheduled(id) { timers.delete(id); },
        runtimeFactory: options => {
            runtimes.push(options);
            return {
            build: async request => { builds.push(request); if (inputReport) options.onInputs(inputReport, request); return {}; }, verify: async () => true,
            render: async (_artifact, request) => ({ ...request, scenarioId: request.selection.scenarioId,
                png: Buffer.from("image"), logicalWidth: 320 }), release
            };
        }
    });
    return { commands, builds, errors, vscode, watchers, timers, folder, settings, runtimes, get panel() { return panel; },
        save(file) { saved({ uri: { scheme: "file", fsPath: file } }); },
        async runTimers() { for (const callback of timers.values()) callback(); timers.clear(); await new Promise(resolve => setImmediate(resolve)); },
        close() { closed(); } };
}

test("preview registration is inert and trust or remote boundaries prevent execution", async () => {
    const f = fixture();
    assert.deepEqual(f.builds, []);
    assert.equal(f.panel, undefined);
    f.vscode.workspace.isTrusted = false;
    await f.commands.get("lucentLui.startPreview")();
    f.vscode.workspace.isTrusted = true;
    f.vscode.env.remoteName = "ssh";
    await f.commands.get("lucentLui.startPreview")();
    assert.equal(f.errors.length, 2);
    assert.deepEqual(f.builds, []);
});

test("overlapping starts replace an active coordinator only after its cleanup and only once", async () => {
    let releaseCleanup;
    let cleanupStarted;
    const cleaning = new Promise(resolve => { cleanupStarted = resolve; });
    const held = new Promise(resolve => { releaseCleanup = resolve; });
    let releases = 0;
    const f = fixture({ release: async () => {
        if (++releases === 1) { cleanupStarted(); await held; }
    } });
    const start = f.commands.get("lucentLui.startPreview");
    const first = start();
    await cleaning;
    f.save(path.join(f.folder.uri.fsPath, "preview", "Card.lui"));
    assert.equal(f.timers.size, 1);
    f.settings.scenarioId = "card/changed";
    const obsolete = start();
    const latest = start();
    assert.equal(f.runtimes.length, 1);
    assert.equal(f.timers.size, 0, "retirement must cancel the old owner's pending refresh");
    assert.ok(f.watchers.every(watcher => watcher.disposed));
    await f.runTimers();
    releaseCleanup();
    await Promise.all([first, obsolete, latest]);
    assert.equal(f.runtimes.length, 2);
    assert.deepEqual(f.builds.map(request => request.selection.scenarioId), ["card/empty", "card/changed"]);
    assert.match(f.panel.webview.html, /Up to date/);
    f.close();
});

test("saves invalidate immediately, coalesce rebuilds, and panel close cancels watchers and pending work", async () => {
    const f = fixture();
    await f.commands.get("lucentLui.startPreview")();
    assert.equal(f.builds.length, 1);
    assert.match(f.panel.webview.html, /Up to date/);
    const component = path.join(f.folder.uri.fsPath, "preview", "Card.lui");
    f.save(component);
    f.save(component);
    assert.equal(f.builds.length, 1);
    assert.equal(f.timers.size, 1);
    assert.match(f.panel.webview.html, /out of date/);
    await f.runTimers();
    assert.equal(f.builds.length, 2);
    f.save(component);
    f.close();
    await f.runTimers();
    assert.equal(f.builds.length, 2);
    assert.ok(f.watchers.every(watcher => watcher.disposed));
});

for (const stopBetween of [false, true]) {
    test(`retirement completes when replacement is superseded by ${stopBetween ? "Stop and restart" : "the original configuration"}`, async () => {
        let releaseCleanup;
        let cleanupStarted;
        const cleaning = new Promise(resolve => { cleanupStarted = resolve; });
        const held = new Promise(resolve => { releaseCleanup = resolve; });
        let releases = 0;
        const f = fixture({ release: async () => {
            if (++releases === 1) { cleanupStarted(); await held; }
        } });
        const start = f.commands.get("lucentLui.startPreview");
        const first = start();
        await cleaning;
        f.settings.scenarioId = "card/replacement";
        const replaced = start();
        const stopped = stopBetween ? f.commands.get("lucentLui.stopPreview")() : Promise.resolve();
        f.settings.scenarioId = "card/empty";
        const resumed = start();
        releaseCleanup();
        await Promise.all([first, replaced, stopped, resumed]);
        assert.deepEqual(f.errors, []);
        assert.equal(f.runtimes.length, 2);
        assert.deepEqual(f.builds.map(request => request.selection.scenarioId), ["card/empty", "card/empty"]);
        assert.match(f.panel.webview.html, /Up to date/);
        f.close();
    });
}

test("stale panel escapes diagnostics and disables script and external resource execution", () => {
    const html = frameHtml({ phase: "stale", stale: true, diagnostic: '<script>alert("bad")</script>',
        frame: { png: Buffer.from("image"), logicalWidth: 320 } });
    assert.match(html, /out of date/);
    assert.match(html, /&lt;script&gt;/);
    assert.doesNotMatch(html, /<script>/);
    assert.match(html, /default-src 'none'/);
    assert.match(html, /width:320px/);
});

test("new external glob members invalidate a current frame and schedule a rebuild", async () => {
    const shared = path.resolve("shared-preview-source");
    const f = fixture({ inputReport: { projects: [{ projectPath: path.resolve("preview-ui-fixture/preview/Preview.csproj"),
        inputs: [{ path: path.join(shared, "Existing.cs") }] }], globWatchRoots: [shared] } });
    await f.commands.get("lucentLui.startPreview")();
    const watcher = f.watchers.find(item => !item.disposed && item.pattern.base === shared && item.pattern.pattern === "**/*");
    assert.ok(watcher, "external glob root must watch previously unknown files");
    watcher.callbacks[0]({ scheme: "file", fsPath: path.join(shared, "New.cs") });
    assert.match(f.panel.webview.html, /out of date/);
    assert.equal(f.timers.size, 1);
    await f.runTimers();
    assert.equal(f.builds.length, 2);
    f.save(path.join(shared, "Another.cs"));
    assert.equal(f.timers.size, 1);
    f.close();
});
