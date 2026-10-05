"use strict";

const path = require("node:path");
const { createPreviewCoordinator } = require("./preview-coordinator");
const { createPreviewRuntime } = require("./preview-runtime");
const { createPreviewPanel } = require("./preview-panel");
const { verifyDiagnosticLocation } = require("./preview-diagnostics");

function createPreviewCommands(vscode, context, { runtimeFactory = createPreviewRuntime, panelFactory = createPreviewPanel, platform = process.platform,
    schedule = setTimeout, cancelScheduled = clearTimeout } = {}) {
    let coordinator;
    let retiring;
    let panel;
    let view;
    let selection;
    let lastState = { phase: "idle", generation: "0", stale: false };
    let running = false;
    let zoom = 1;
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
    const log = message => { if (disposed) return; output ??= vscode.window.createOutputChannel("Lucent Preview"); output.appendLine(message); };
    const supported = folder => platform === "win32" && !vscode.env.remoteName && folder?.uri?.scheme === "file"
        && vscode.workspace.workspaceFolders?.some(current => current.uri.scheme === "file"
            && path.resolve(current.uri.fsPath).toLowerCase() === path.resolve(folder.uri.fsPath).toLowerCase());

    function clearWatchers() {
        cancelScheduled(refreshTimer);
        refreshTimer = undefined;
        watchGeneration = undefined;
        saveEvent = undefined;
        while (watchers.length) watchers.pop().dispose();
    }

    function installWatchers(projects, inputs, generation, globWatchRoots = []) {
        clearWatchers();
        if (!running || panel?.visible === false || disposed) return;
        const owner = Symbol(generation);
        watchGeneration = owner;
        const exact = new Set(inputs.map(file => path.resolve(file).toLowerCase()));
        const projectRoots = [...new Set([...projects.map(file => path.dirname(file)), ...globWatchRoots]
            .map(root => path.resolve(root)))];
        if (projectRoots.length > 512 || projectRoots.some(root => root === path.parse(root).root))
            throw new Error("Preview recursive watch roots exceed the supported scope.");
        const sourceEvent = uri => {
            if (disposed || !running || panel?.visible === false || !coordinator || watchGeneration !== owner || uri.scheme !== "file") return;
            const file = path.resolve(uri.fsPath);
            const relative = path.relative(ownedStorage, file);
            if (!relative || relative !== ".." && !relative.startsWith(".." + path.sep) && !path.isAbsolute(relative)) return;
            if (!exact.has(file.toLowerCase()) && /(?:^|[\\/])(?:bin|obj|\.git)(?:[\\/]|$)/i.test(file)) return;
            coordinator.invalidate();
            cancelScheduled(refreshTimer);
            refreshTimer = schedule(() => {
                if (watchGeneration === owner && running && coordinator)
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
        if (disposed) return;
        lastState = state;
        updateView();
        if (state.diagnostic) log(state.diagnostic);
    }

    function updateView() {
        const state = running && panel?.visible === false && lastState.phase !== "blocked"
            ? { ...lastState, phase: "suspended", stale: !!lastState.frame } : lastState;
        view?.update({ state, selection, zoom });
    }

    async function launch(chosen = selection) {
        if (disposed || !panel || !chosen || !coordinator || retiring) return;
        if (coordinator.state.phase === "blocked") { updateView(); return; }
        if (!vscode.workspace.isTrusted || !supported(selectedFolder)) { await stop(); return; }
        selection = { ...chosen, presentation: chosen.presentation ? { ...chosen.presentation } : undefined };
        running = true;
        updateView();
        if (panel?.visible === false) return;
        installWatchers([selection.projectPath], [], "initial");
        await coordinator.start(selection);
    }

    async function configure(chosen) {
        selection = { ...chosen, presentation: chosen.presentation ? { ...chosen.presentation } : undefined };
        if (running) await launch();
        else updateView();
    }

    async function openDiagnostic(index) {
        const state = lastState;
        const diagnostic = state.diagnostics?.[index];
        if (!diagnostic || !selectedFolder || !vscode.workspace.isTrusted) return;
        const location = await verifyDiagnosticLocation(diagnostic, [selectedFolder.uri.fsPath]);
        if (lastState !== state || disposed || !vscode.workspace.isTrusted) return;
        const document = await vscode.workspace.openTextDocument(vscode.Uri.file(location.file));
        if (lastState !== state || disposed || !vscode.workspace.isTrusted) return;
        const line = Math.min(document.lineCount - 1, location.line - 1);
        const column = Math.min(document.lineAt(line).text.length, location.column - 1);
        await vscode.window.showTextDocument(document, { selection: new vscode.Range(line, column, line, column), preview: true });
    }

    async function action(message) {
        if (disposed || !panel || panel.visible === false) return;
        if (message.kind === "zoom") { zoom = message.zoom; updateView(); return; }
        if (message.kind === "stop") { await stop(); return; }
        if (message.kind === "diagnostic") { await openDiagnostic(message.index); return; }
        if (!selection || !coordinator || retiring || !vscode.workspace.isTrusted) return;
        if (message.kind === "select") {
            if (!lastState.catalog?.scenarios.some(item => item.id === message.scenarioId)) return;
            await configure({ ...selection, scenarioId: message.scenarioId, presentation: undefined });
        } else if (message.kind === "presentation") {
            if (!lastState.catalog?.scenarios.some(item => item.id === selection.scenarioId)) return;
            await configure({ ...selection, presentation: message.presentation });
        } else if (message.kind === "reset" || message.kind === "refresh") await launch();
    }

    function visibility(visible) {
        if (disposed || !running) return;
        if (visible) void launch().catch(error => log(error.message));
        else {
            clearWatchers();
            void coordinator?.stop().catch(error => log(error.message));
            updateView();
        }
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
        running = false;
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
                onInputs: (report, request) => {
                    if (coordinator?.state.generation !== request.generation) return;
                    installWatchers(report.projects.map(project => project.projectPath),
                        report.projects.flatMap(project => project.inputs.map(input => input.path)), request.generation, report.globWatchRoots);
                }, log
            });
            coordinator = createPreviewCoordinator({ ...runtime, isTrusted: () => !disposed && vscode.workspace.isTrusted,
                isSupported: () => supported(selectedFolder), onState: showState, log });
            settingsIdentity = identity;
        }
        if (!panel) {
            panel = vscode.window.createWebviewPanel("lucent.preview", "Lucent Preview", vscode.ViewColumn.Beside,
                { enableScripts: true, localResourceRoots: [] });
            view = panelFactory({ panel, onAction: message => action(message).catch(error => {
                log(error.message);
                vscode.window.showErrorMessage(error.message);
            }), onVisibility: visibility, onClose: () => {
                view?.dispose(); view = undefined; panel = undefined;
                selection = undefined;
                void stop().catch(error => log(error.message));
            } });
        } else if (panel.visible === false) {
            // Suppress automatic visibility resume; this explicit Start owns the next selection.
            running = false;
            panel.reveal?.(vscode.ViewColumn.Beside);
        }
        await launch({ projectPath, scenarioId: settings.scenarioId, targetFramework: settings.targetFramework,
            configuration: settings.configuration ?? "Debug", extraInputs: (settings.extraInputs ?? []).map(file => path.resolve(folder.uri.fsPath, file)) });
    }

    context.subscriptions.push(
        vscode.commands.registerCommand("lucentLui.startPreview", () => start().catch(error => vscode.window.showErrorMessage(error.message))),
        vscode.commands.registerCommand("lucentLui.refreshPreview", () => launch()),
        vscode.commands.registerCommand("lucentLui.stopPreview", stop),
        { dispose() { disposed = true; running = false; clearWatchers(); view?.dispose(); panel?.dispose(); void coordinator?.dispose().catch(error => log(error.message)); output?.dispose(); } }
    );
    if (vscode.workspace.onDidChangeWorkspaceFolders) context.subscriptions.push(vscode.workspace.onDidChangeWorkspaceFolders(event => {
        if (event.removed?.some(folder => folder.uri.fsPath === selectedFolder?.uri.fsPath)) {
            selection = undefined;
            selectedFolder = undefined;
            void stop().catch(error => log(error.message));
        }
    }));
    if (vscode.workspace.onDidSaveTextDocument) context.subscriptions.push(vscode.workspace.onDidSaveTextDocument(document => saveEvent?.(document.uri)));
    if (vscode.workspace.onDidChangeConfiguration) context.subscriptions.push(vscode.workspace.onDidChangeConfiguration(event => {
        if (event.affectsConfiguration("lucentLui.preview", selectedFolder?.uri)) void stop().catch(error => log(error.message));
    }));
    return { stop };
}

module.exports = { createPreviewCommands };
