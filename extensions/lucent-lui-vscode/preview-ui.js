"use strict";

const path = require("node:path");
const { createPreviewCoordinator } = require("./preview-coordinator");
const { createPreviewRuntime } = require("./preview-runtime");

function escapeHtml(text) {
    return String(text).replace(/[&<>"']/g, value => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[value]));
}

function frameHtml(state) {
    const labels = { idle: "Ready", building: "Building preview", rendering: "Rendering preview", current: "Up to date",
        stale: "Previous preview · out of date", stopped: "Preview stopped", blocked: "Preview cleanup needs attention",
        untrusted: "Workspace trust required", error: "Preview unavailable" };
    const label = (labels[state.phase] ?? "Preview") + (state.stale && state.phase !== "stale" ? " · previous image is out of date" : "");
    const logicalWidth = Number.isFinite(state.frame?.logicalWidth) ? `width:${state.frame.logicalWidth}px;` : "";
    const image = state.frame?.png ? `<img alt="Compiled component preview" style="${logicalWidth}opacity:${state.stale ? "0.6" : "1"}" src="data:image/png;base64,${state.frame.png.toString("base64")}">` : "";
    return `<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline';"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
body{font:var(--vscode-font-size) var(--vscode-font-family);color:var(--vscode-foreground);background:var(--vscode-editor-background);padding:16px}header{margin-bottom:16px}p{white-space:pre-wrap}img{display:block;max-width:100%;height:auto;border:1px solid var(--vscode-panel-border)}</style></head><body><header role="status">${escapeHtml(label)}</header>${state.diagnostic ? `<p>${escapeHtml(state.diagnostic)}</p>` : ""}${image}</body></html>`;
}

function createPreviewCommands(vscode, context, { runtimeFactory = createPreviewRuntime, platform = process.platform,
    schedule = setTimeout, cancelScheduled = clearTimeout } = {}) {
    let coordinator;
    let retiring;
    let panel;
    let output;
    let selectedFolder;
    let settingsIdentity;
    let disposed = false;
    let commandGeneration = 0;
    let watchGeneration;
    let saveEvent;
    let refreshTimer;
    const watchers = [];
    const ownedStorage = path.join(context.globalStorageUri?.fsPath ?? "", "preview");
    const log = message => { output ??= vscode.window.createOutputChannel("Lucent Preview"); output.appendLine(message); };
    const supported = folder => platform === "win32" && !vscode.env.remoteName && folder?.uri?.scheme === "file";

    function clearWatchers() {
        cancelScheduled(refreshTimer);
        saveEvent = undefined;
        while (watchers.length) watchers.pop().dispose();
    }

    function installWatchers(projects, inputs, generation, globWatchRoots = []) {
        clearWatchers();
        watchGeneration = generation;
        const exact = new Set(inputs.map(file => path.resolve(file).toLowerCase()));
        const projectRoots = [...new Set([...projects.map(file => path.dirname(file)), ...globWatchRoots]
            .map(root => path.resolve(root)))];
        if (projectRoots.length > 512 || projectRoots.some(root => root === path.parse(root).root))
            throw new Error("Preview recursive watch roots exceed the supported scope.");
        const sourceEvent = uri => {
            if (disposed || !coordinator || watchGeneration !== generation || uri.scheme !== "file") return;
            const file = path.resolve(uri.fsPath);
            const relative = path.relative(ownedStorage, file);
            if (!relative || relative !== ".." && !relative.startsWith(".." + path.sep) && !path.isAbsolute(relative)) return;
            if (!exact.has(file.toLowerCase()) && /(?:^|[\\/])(?:bin|obj|\.git)(?:[\\/]|$)/i.test(file)) return;
            coordinator.invalidate();
            cancelScheduled(refreshTimer);
            refreshTimer = schedule(() => {
                void coordinator.refresh().catch(error => log(error.message));
            }, 100);
        };
        saveEvent = uri => {
            if (exact.has(path.resolve(uri.fsPath).toLowerCase()) || projectRoots.some(root => {
                const relative = path.relative(root, uri.fsPath);
                return relative !== ".." && !relative.startsWith(".." + path.sep) && !path.isAbsolute(relative);
            })) sourceEvent(uri);
        };
        for (const root of projectRoots) {
            const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(root, "**/*"));
            watchers.push(watcher, watcher.onDidCreate(sourceEvent), watcher.onDidChange(sourceEvent), watcher.onDidDelete(sourceEvent));
        }
        const external = new Map();
        for (const input of inputs) {
            if (projectRoots.some(root => {
                const relative = path.relative(root, input);
                return relative !== ".." && !relative.startsWith(".." + path.sep) && !path.isAbsolute(relative);
            })) continue;
            const directory = path.dirname(input);
            if (!external.has(directory)) external.set(directory, new Set());
            external.get(directory).add(path.basename(input).toLowerCase());
        }
        if (external.size > 512) throw new Error("Preview input watch directories exceed the supported bound.");
        for (const [directory, names] of external) {
            const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(directory, "*"));
            const changed = uri => { if (names.has(path.basename(uri.fsPath).toLowerCase())) sourceEvent(uri); };
            watchers.push(watcher, watcher.onDidCreate(changed), watcher.onDidChange(changed), watcher.onDidDelete(changed));
        }
    }

    function showState(state) {
        if (panel) panel.webview.html = frameHtml(state);
        if (state.diagnostic) log(state.diagnostic);
    }

    async function retireCoordinator() {
        if (!retiring && coordinator) {
            clearWatchers();
            const previous = coordinator;
            coordinator = undefined;
            retiring = { previous, completion: previous.dispose() };
        }
        const pending = retiring;
        if (!pending) return;
        await pending.completion;
        if (retiring === pending) {
            // Retirement belongs to the old owner, even when the command that
            // requested it was superseded or stopped. Never reinstall a disposed
            // usable coordinator, but keep uncertain termination sticky.
            if (pending.previous.state.phase === "blocked") coordinator = pending.previous;
            retiring = undefined;
        }
    }

    async function stop() {
        ++commandGeneration;
        clearWatchers();
        if (retiring) await retireCoordinator();
        else await coordinator?.stop();
    }

    async function start() {
        const ticket = ++commandGeneration;
        const current = () => !disposed && ticket === commandGeneration && vscode.workspace.isTrusted;
        if (disposed) return;
        if (!vscode.workspace.isTrusted) { vscode.window.showErrorMessage("Trust this workspace before running a Lucent preview."); return; }
        if (coordinator?.state.phase === "blocked") {
            showState(coordinator.state);
            vscode.window.showErrorMessage(coordinator.state.diagnostic);
            return;
        }
        const folders = vscode.workspace.workspaceFolders ?? [];
        const documentUri = vscode.window.activeTextEditor?.document.uri;
        let folder = documentUri ? vscode.workspace.getWorkspaceFolder?.(documentUri) : undefined;
        folder ??= folders.length === 1 ? folders[0] : undefined;
        if (!folder) folder = (await vscode.window.showQuickPick(folders.map(value => ({ label: value.name, folder: value })),
            { placeHolder: "Choose the folder that owns the preview project" }))?.folder;
        if (!current() || !folder) return;
        if (!supported(folder)) { vscode.window.showErrorMessage("Lucent preview currently requires a local Windows desktop workspace."); return; }
        // VS Code configuration values can be proxies; JSON snapshots preserve
        // their supported values without retaining the live configuration object.
        const settings = JSON.parse(JSON.stringify(vscode.workspace.getConfiguration("lucentLui", folder.uri).get("preview") ?? {}));
        if (!settings.projectPath || !settings.scenarioId || !settings.targetFramework || !path.isAbsolute(settings.toolsDirectory ?? "")) {
            vscode.window.showErrorMessage("Configure lucentLui.preview with a development project, scenarioId, targetFramework and absolute toolsDirectory. See the native preview setup guide.");
            return;
        }
        const projectPath = path.resolve(folder.uri.fsPath, settings.projectPath);
        const relativeProject = path.relative(folder.uri.fsPath, projectPath);
        if (relativeProject === ".." || relativeProject.startsWith(".." + path.sep) || path.isAbsolute(relativeProject)
            || path.extname(projectPath).toLowerCase() !== ".csproj") {
            vscode.window.showErrorMessage("Select a preview .csproj inside its trusted workspace folder."); return;
        }
        const identity = JSON.stringify({ folder: folder.uri.fsPath, settings });
        if (retiring || coordinator && identity !== settingsIdentity) {
            await retireCoordinator();
            if (!current()) return;
            if (coordinator?.state.phase === "blocked") return;
        }
        if (!current()) return;
        selectedFolder = folder;
        if (!coordinator) {
            const runtime = runtimeFactory({
                supervisorPath: path.join(settings.toolsDirectory, "supervisor", "Lucent.Preview.Supervisor.exe"),
                buildToolPath: path.join(settings.toolsDirectory, "build", "Lucent.Preview.Build.exe"),
                storageDirectory: ownedStorage, isTrusted: () => !disposed && vscode.workspace.isTrusted,
                onInputs: (report, request) => installWatchers(report.projects.map(project => project.projectPath),
                    report.projects.flatMap(project => project.inputs.map(input => input.path)), request.generation, report.globWatchRoots), log
            });
            coordinator = createPreviewCoordinator({ ...runtime, isTrusted: () => !disposed && vscode.workspace.isTrusted,
                isSupported: () => supported(selectedFolder), onState: showState, log });
            settingsIdentity = identity;
        }
        if (!panel) {
            panel = vscode.window.createWebviewPanel("lucent.preview", "Lucent Preview", vscode.ViewColumn.Beside,
                { enableScripts: false, localResourceRoots: [] });
            panel.onDidDispose(() => { panel = undefined; void stop().catch(error => log(error.message)); });
        }
        installWatchers([projectPath], [], "initial");
        await coordinator.start({ projectPath, scenarioId: settings.scenarioId, targetFramework: settings.targetFramework,
            configuration: settings.configuration ?? "Debug", extraInputs: (settings.extraInputs ?? []).map(file => path.resolve(folder.uri.fsPath, file)) });
    }

    context.subscriptions.push(
        vscode.commands.registerCommand("lucentLui.startPreview", () => start().catch(error => vscode.window.showErrorMessage(error.message))),
        vscode.commands.registerCommand("lucentLui.refreshPreview", () => coordinator?.refresh()),
        vscode.commands.registerCommand("lucentLui.stopPreview", stop),
        { dispose() { disposed = true; clearWatchers(); panel?.dispose(); void coordinator?.dispose().catch(error => log(error.message)); output?.dispose(); } }
    );
    if (vscode.workspace.onDidChangeWorkspaceFolders) context.subscriptions.push(vscode.workspace.onDidChangeWorkspaceFolders(event => {
        if (event.removed?.some(folder => folder.uri.fsPath === selectedFolder?.uri.fsPath)) void stop().catch(error => log(error.message));
    }));
    if (vscode.workspace.onDidSaveTextDocument) context.subscriptions.push(vscode.workspace.onDidSaveTextDocument(document => saveEvent?.(document.uri)));
    if (vscode.workspace.onDidChangeConfiguration) context.subscriptions.push(vscode.workspace.onDidChangeConfiguration(event => {
        if (event.affectsConfiguration("lucentLui.preview", selectedFolder?.uri)) void stop().catch(error => log(error.message));
    }));
    return { stop };
}

module.exports = { createPreviewCommands, frameHtml };
