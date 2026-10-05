"use strict";

const assert = require("node:assert/strict");
const path = require("node:path");
const fs = require("node:fs/promises");
const os = require("node:os");
const { test } = require("node:test");
const { createPreviewCommands } = require("./preview-ui");

function fixture({ release = async () => {}, inputReport, failure, renderFailure, folderPath } = {}) {
    const commands = new Map();
    const timers = new Map();
    const watchers = [];
    const errors = [];
    const builds = [];
    const runtimes = [];
    const opened = [];
    const folder = { name: "app", uri: { scheme: "file", fsPath: folderPath ?? path.resolve("preview-ui-fixture") } };
    let closed;
    let saved;
    let foldersChanged;
    let panel;
    let view;
    let panelActions;
    let nextTimer = 0;
    const settings = { projectPath: "preview/Preview.csproj", scenarioId: "card/empty", targetFramework: "net10.0",
        toolsDirectory: path.resolve("trusted-tools") };
    const vscode = {
        env: {}, ViewColumn: { Beside: 2 },
        Uri: { file: file => ({ scheme: "file", fsPath: file }) },
        Range: class { constructor(line, column, endLine, endColumn) { Object.assign(this, { line, column, endLine, endColumn }); } },
        RelativePattern: class { constructor(base, pattern) { this.base = base; this.pattern = pattern; } },
        commands: { registerCommand(name, callback) { commands.set(name, callback); return { dispose() {} }; } },
        workspace: {
            isTrusted: true, workspaceFolders: [folder],
            getConfiguration: () => ({ get: () => new Proxy(settings, {}) }),
            async openTextDocument(uri) {
                const lines = (await fs.readFile(uri.fsPath, "utf8")).split(/\r?\n/);
                return { uri, lineCount: lines.length, lineAt: line => ({ text: lines[line] }) };
            },
            createFileSystemWatcher(pattern) {
                const watcher = { pattern, callbacks: [], disposed: false, dispose() { this.disposed = true; } };
                for (const event of ["onDidCreate", "onDidChange", "onDidDelete"])
                    watcher[event] = callback => { watcher.callbacks.push(callback); return { dispose() {} }; };
                watchers.push(watcher);
                return watcher;
            },
            onDidSaveTextDocument(callback) { saved = callback; return { dispose() {} }; },
            onDidChangeWorkspaceFolders(callback) { foldersChanged = callback; return { dispose() {} }; }
        },
        window: {
            async showTextDocument(document, options) { opened.push({ file: document.uri.fsPath, selection: options.selection }); },
            showErrorMessage: message => errors.push(message),
            createOutputChannel: () => ({ appendLine() {}, dispose() {} }),
            createWebviewPanel(_id, _title, _column, options) {
                assert.equal(options.enableScripts, true);
                assert.deepEqual(options.localResourceRoots, []);
                panel = { visible: true, webview: { html: "" }, onDidDispose(callback) { closed = callback; }, dispose() { closed(); } };
                return panel;
            }
        }
    };
    const context = { subscriptions: [], globalStorageUri: { fsPath: path.resolve("preview-test-storage") } };
    createPreviewCommands(vscode, context, {
        platform: "win32", schedule(callback) { timers.set(++nextTimer, callback); return nextTimer; },
        cancelScheduled(id) { timers.delete(id); },
        panelFactory(options) {
            panelActions = options;
            options.panel.onDidDispose(options.onClose);
            return { update(next) { view = next; }, dispose() {} };
        },
        runtimeFactory: options => {
            runtimes.push(options);
            return {
            build: async request => { builds.push(request); if (failure) throw failure; if (inputReport) options.onInputs(inputReport, request); return {}; }, verify: async () => true,
            discover: async (_artifact, request) => ({ sessionId: request.sessionId, generation: request.generation,
                scenarios: ["card/empty", "card/changed", "card/replacement"].map(id => ({ id, title: id, logicalWidth: 320,
                    logicalHeight: 240, scale: 1, colorScheme: "light", contrast: "normal", density: 1,
                    culture: "", uiCulture: "", initialTime: "2024-01-01T00:00:00.0000000+00:00" })) }),
            render: async (_artifact, request) => {
                const failed = renderFailure?.(request.selection.scenarioId);
                if (failed) throw failed;
                return { ...request, scenarioId: request.selection.scenarioId,
                effectivePresentation: { logicalWidth: 320, logicalHeight: 240, scale: 1, colorScheme: "light", contrast: "normal", density: 1,
                    culture: "", uiCulture: "", initialTime: "2024-01-01T00:00:00.0000000+00:00", ...request.selection.presentation },
                png: Buffer.from("image"), logicalWidth: 320 };
            }, release
            };
        }
    });
    return { commands, builds, errors, opened, vscode, watchers, timers, folder, settings, runtimes, get panel() { return panel; }, get view() { return view; },
        action(value) { return panelActions.onAction(value); },
        async visible(value) { panel.visible = value; panelActions.onVisibility(value); await new Promise(resolve => setImmediate(resolve)); },
        async removeFolder() { vscode.workspace.workspaceFolders = []; foldersChanged({ removed: [folder], added: [] }); await new Promise(resolve => setImmediate(resolve)); },
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
    assert.equal(f.view.state.phase, "current");
    f.close();
});

test("saves invalidate immediately, coalesce rebuilds, and panel close cancels watchers and pending work", async () => {
    const f = fixture();
    await f.commands.get("lucentLui.startPreview")();
    assert.equal(f.builds.length, 1);
    assert.equal(f.view.state.phase, "current");
    const component = path.join(f.folder.uri.fsPath, "preview", "Card.lui");
    f.save(component);
    f.save(component);
    assert.equal(f.builds.length, 1);
    assert.equal(f.timers.size, 1);
    assert.equal(f.view.state.stale, true);
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
        assert.equal(f.view.state.phase, "current");
        f.close();
    });
}

test("new external glob members invalidate a current frame and schedule a rebuild", async () => {
    const shared = path.resolve("shared-preview-source");
    const f = fixture({ inputReport: { projects: [{ projectPath: path.resolve("preview-ui-fixture/preview/Preview.csproj"),
        inputs: [{ path: path.join(shared, "Existing.cs") }] }], globWatchRoots: [shared] } });
    await f.commands.get("lucentLui.startPreview")();
    const watcher = f.watchers.find(item => !item.disposed && item.pattern.base === shared && item.pattern.pattern === "**/*");
    assert.ok(watcher, "external glob root must watch previously unknown files");
    watcher.callbacks[0]({ scheme: "file", fsPath: path.join(shared, "New.cs") });
    assert.equal(f.view.state.stale, true);
    assert.equal(f.timers.size, 1);
    await f.runTimers();
    assert.equal(f.builds.length, 2);
    f.save(path.join(shared, "Another.cs"));
    assert.equal(f.timers.size, 1);
    f.close();
});

test("hidden preview stops work, ignores old watchers, and resumes only if the user left it running", async () => {
    const f = fixture();
    await f.commands.get("lucentLui.startPreview")();
    const oldWatcher = f.watchers.find(item => !item.disposed);
    await f.visible(false);
    assert.equal(f.view.state.phase, "suspended");
    assert.equal(f.view.state.stale, true);
    assert.ok(f.watchers.every(item => item.disposed));
    f.save(path.join(f.folder.uri.fsPath, "preview", "Card.lui"));
    oldWatcher.callbacks[1]({ scheme: "file", fsPath: path.join(f.folder.uri.fsPath, "preview", "Card.lui") });
    await f.runTimers();
    assert.equal(f.builds.length, 1);
    await f.visible(true);
    assert.equal(f.builds.length, 2);
    assert.equal(f.view.state.phase, "current");
    // A queued callback from the old watcher cannot address a new coordinator generation.
    oldWatcher.callbacks[1]({ scheme: "file", fsPath: path.join(f.folder.uri.fsPath, "preview", "Card.lui") });
    assert.equal(f.timers.size, 0);
    await f.action({ kind: "stop" });
    await f.action({ kind: "select", scenarioId: "card/changed" });
    const pendingPresentation = { logicalWidth: 480, logicalHeight: 320, scale: 1.5,
        colorScheme: "dark", contrast: "normal", density: 0.75 };
    await f.action({ kind: "presentation", presentation: pendingPresentation });
    assert.equal(f.builds.length, 2, "changing settings must not undo an explicit Stop");
    assert.equal(f.view.selection.scenarioId, "card/changed");
    assert.deepEqual(f.view.selection.presentation, pendingPresentation);
    await f.visible(false);
    await f.visible(true);
    assert.equal(f.builds.length, 2);
    assert.equal(f.view.state.phase, "stopped");
    await f.commands.get("lucentLui.refreshPreview")();
    assert.equal(f.builds.length, 3);
    assert.equal(f.builds.at(-1).selection.scenarioId, "card/changed");
    assert.deepEqual(f.builds.at(-1).selection.presentation, pendingPresentation);
    f.close();
});

test("zoom is display-only, presentation updates rebuild, and selecting a scenario restores its defaults", async () => {
    const f = fixture();
    await f.commands.get("lucentLui.startPreview")();
    await f.action({ kind: "zoom", zoom: 1.5 });
    assert.equal(f.view.zoom, 1.5);
    assert.equal(f.builds.length, 1);
    const presentation = { logicalWidth: 640, logicalHeight: 480, scale: 2, colorScheme: "dark", contrast: "normal", density: 0.75 };
    await f.action({ kind: "presentation", presentation });
    assert.deepEqual(f.builds.at(-1).selection.presentation, presentation);
    await f.action({ kind: "reset" });
    assert.equal(f.builds.length, 3);
    assert.deepEqual(f.builds.at(-1).selection.presentation, presentation);
    await f.action({ kind: "select", scenarioId: "card/changed" });
    assert.equal(f.builds.at(-1).selection.scenarioId, "card/changed");
    assert.equal(f.builds.at(-1).selection.presentation, undefined);
    await f.action({ kind: "select", scenarioId: "unregistered" });
    assert.equal(f.builds.length, 4);
    f.close();
});

test("diagnostic navigation uses retained source data, bounds locations, and rejects files outside the workspace", async t => {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), "lucent-preview-ui-"));
    t.after(() => fs.rm(root, { recursive: true, force: true }));
    const workspace = path.join(root, "app");
    await fs.mkdir(workspace);
    const inside = path.join(workspace, "Card.lui");
    const outside = path.join(root, "Outside.cs");
    await fs.writeFile(inside, "first\nlast");
    await fs.writeFile(outside, "outside");
    const failure = Object.assign(new Error("Compilation failed."), { diagnostics: [
        { id: "a".repeat(32), file: inside, line: 2, column: 3, code: "LUI2000", message: "Unknown value." },
        { id: "b".repeat(32), file: outside, line: 1, column: 1, code: "CS0103", message: "Unknown name." }
    ] });
    const f = fixture({ failure, folderPath: workspace });
    await f.commands.get("lucentLui.startPreview")();
    await f.action({ kind: "diagnostic", index: 0 });
    assert.equal(f.opened.length, 1);
    assert.equal(f.opened[0].file.toLowerCase(), (await fs.realpath(inside)).toLowerCase());
    assert.deepEqual({ ...f.opened[0].selection }, { line: 1, column: 2, endLine: 1, endColumn: 2 });
    await f.action({ kind: "diagnostic", index: 1 });
    assert.match(f.errors.at(-1), /outside authored source roots/);
    f.vscode.workspace.isTrusted = false;
    await f.action({ kind: "diagnostic", index: 0 });
    assert.equal(f.opened.length, 1);
    f.close();
});

test("a failed scenario leaves catalog choices available for a fresh successful build", async () => {
    const f = fixture({ renderFailure: scenario => scenario === "card/empty" ? new Error("Fixture failed.") : undefined });
    await f.commands.get("lucentLui.startPreview")();
    assert.equal(f.view.state.phase, "error");
    assert.equal(f.view.state.catalogStale, true);
    await f.action({ kind: "select", scenarioId: "card/changed" });
    assert.equal(f.builds.length, 2);
    assert.equal(f.view.state.phase, "current");
    assert.equal(f.view.state.catalogStale, false);
    f.close();
});

test("Refresh cannot restart invisible or removed-workspace work", async () => {
    const closed = fixture();
    await closed.commands.get("lucentLui.startPreview")();
    closed.close();
    await closed.commands.get("lucentLui.refreshPreview")();
    assert.equal(closed.builds.length, 1, "Closing a panel invalidates its launch selection.");
    assert.ok(closed.watchers.every(watcher => watcher.disposed));
    const removed = fixture();
    await removed.commands.get("lucentLui.startPreview")();
    await removed.removeFolder();
    await removed.commands.get("lucentLui.refreshPreview")();
    await removed.action({ kind: "reset" });
    assert.equal(removed.builds.length, 1, "Global trust cannot authorize a folder that has left the workspace.");
    assert.ok(removed.watchers.every(watcher => watcher.disposed));
    removed.close();
});
