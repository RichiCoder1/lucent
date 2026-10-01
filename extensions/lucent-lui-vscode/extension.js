"use strict";

const childProcess = require("child_process");
const path = require("path");
const os = require("node:os");
const { isDeepStrictEqual } = require("node:util");
const vscode = require("vscode");
const { verifyBundledServer } = require("./server-bundle");
const { readProjectRequirements, assertCompatibleCompiler, verifyRequirementInputs, trustedProjectReport } = require("./project-requirements");
const { selectApprovedEntry, resolveCachedServer, importApprovedArchive, importDownloadedRelease } = require("./server-cache");
const { acquireApprovedRelease } = require("./server-acquisition");
const { preflightDotnet } = require("./doctor-client");
const { createOnboardingUi } = require("./onboarding-ui");
const { createEnvironmentCommands } = require("./environment-ui");
const { createPreviewCommands } = require("./preview-ui");
const manifest = require("./package.json");
const clientRelease = manifest.lucentRelease;
const releaseCatalog = require("./release-catalog.json");

const semanticTokensLegend = ["keyword", "type", "property", "enumMember"];
const crossLanguageSelector = [{ language: "lui" }, { language: "csharp", scheme: "file" }];

function toVsCodeCompletionKind(kind, kinds) {
    switch (kind) {
    case 2: return kinds.Method;
    case 3: return kinds.Function;
    case 4: return kinds.Constructor;
    case 5: return kinds.Field;
    case 6: return kinds.Variable;
    case 7: return kinds.Class;
    case 8: return kinds.Interface;
    case 9: return kinds.Module;
    case 10: return kinds.Property;
    case 13: return kinds.Enum;
    case 20: return kinds.EnumMember;
    case 22: return kinds.Struct;
    default: return kinds.Text;
    }
}

function toVsCodeSymbolKind(kind, kinds) {
    switch (kind) {
    case 5: return kinds.Class;
    case 6: return kinds.Method;
    case 7: return kinds.Property;
    case 8: return kinds.Field;
    case 12: return kinds.Function;
    case 13: return kinds.Variable;
    default: return kinds.String;
    }
}

function toVsCodeDiagnosticSeverity(severity, severities) {
    switch (severity) {
    case 1: return severities.Error;
    case 2: return severities.Warning;
    case 3: return severities.Information;
    default: return severities.Hint;
    }
}

function plaintext(value) {
    const markdown = new vscode.MarkdownString();
    markdown.appendText(value);
    return markdown;
}

function csharp(value) {
    const markdown = new vscode.MarkdownString();
    markdown.appendCodeblock(value, "csharp");
    return markdown;
}

function toWorkspaceEdit(result) {
    if (!result) return undefined;
    const edit = new vscode.WorkspaceEdit();
    const documents = result.documentChanges ?? Object.entries(result.changes ?? {}).map(([uri, edits]) => ({ textDocument: { uri }, edits }));
    for (const changeSet of documents) {
        const { uri, version } = changeSet.textDocument;
        if (version !== undefined && version !== null) {
            const document = vscode.workspace.textDocuments.find(item => item.uri.toString() === uri);
            if (!document || document.version !== version) return undefined;
        }
        const changes = changeSet.edits;
        for (const change of changes) {
            edit.replace(vscode.Uri.parse(uri), new vscode.Range(
                change.range.start.line,
                change.range.start.character,
                change.range.end.line,
                change.range.end.character
            ), change.newText);
        }
    }
    return edit;
}

function toTextEdits(changes) {
    return (changes ?? []).map(change => new vscode.TextEdit(
        new vscode.Range(
            change.range.start.line,
            change.range.start.character,
            change.range.end.line,
            change.range.end.character
        ),
        change.newText
    ));
}

async function formattingEdits(rpc, document, range, token) {
    if (token?.isCancellationRequested) return [];
    const version = document.version;
    const result = await rpc.request(range ? "textDocument/rangeFormatting" : "textDocument/formatting", {
        textDocument: { uri: document.uri.toString(), version },
        ...(range ? { range } : {}),
        options: {}
    });
    if (token?.isCancellationRequested || document.version !== version) return [];
    return toTextEdits(result);
}

class Rpc {
    constructor(process, log) {
        this.log = log;
        this.process = process;
        this.buffer = Buffer.alloc(0);
        this.nextId = 1;
        this.pending = new Map();
        this.notifications = new Map();
        this.failure = null;
        this.onFailure = null;
        process.stdout.on("data", chunk => {
            try { this.read(chunk); }
            catch (error) { this.fail(error); }
        });
        process.on("error", error => this.fail(new Error(`Lucent language server process failed: ${error.message}`)));
        process.stdin.on("error", error => this.fail(error));
        process.stdout.on("error", error => this.fail(error));
        process.on("exit", (code, signal) => this.fail(new Error(
            `Lucent language server exited (code ${code ?? "none"}, signal ${signal ?? "none"}).`
        )));
    }

    request(method, params) {
        if (this.failure) return Promise.reject(this.failure);
        const id = this.nextId++;
        return new Promise((resolve, reject) => {
            const started = Date.now();
            const timer = setTimeout(() => this.log?.warn(`${method} #${id} still pending after 5 seconds (${this.pending.size} pending requests).`), 5000);
            this.log?.info(`Request ${method} #${id}`);
            this.pending.set(id, { resolve, reject, method, started, timer });
            try { this.write({ jsonrpc: "2.0", id, method, params }); }
            catch (error) { this.fail(error); }
        });
    }

    notify(method, params) {
        if (this.failure) return;
        try { this.write({ jsonrpc: "2.0", method, params }); }
        catch (error) { this.fail(error); }
    }

    onNotification(method, handler) {
        const handlers = this.notifications.get(method) || [];
        handlers.push(handler);
        this.notifications.set(method, handlers);
    }

    write(message) {
        const body = Buffer.from(JSON.stringify(message), "utf8");
        this.process.stdin.write(`Content-Length: ${body.length}\r\n\r\n`);
        this.process.stdin.write(body);
    }

    read(chunk) {
        if (this.failure) return;
        this.buffer = Buffer.concat([this.buffer, chunk]);
        for (;;) {
            if (this.failure) return;
            const end = this.buffer.indexOf("\r\n\r\n");
            if (end < 0) return;
            const header = this.buffer.subarray(0, end).toString("ascii");
            const match = /^Content-Length:\s*(\d+)$/im.exec(header);
            if (!match) throw new Error("Lucent language server sent an invalid LSP header.");
            const length = Number(match[1]);
            const start = end + 4;
            if (this.buffer.length < start + length) return;
            const message = JSON.parse(this.buffer.subarray(start, start + length).toString("utf8"));
            this.buffer = this.buffer.subarray(start + length);
            if (message.id === undefined) {
                for (const handler of this.notifications.get(message.method) || []) handler(message.params);
                continue;
            }
            const pending = this.pending.get(message.id);
            if (!pending) continue;
            this.pending.delete(message.id);
            clearTimeout(pending.timer);
            this.log?.info(`${pending.method} #${message.id} completed in ${Date.now() - pending.started} ms`);
            if (message.error) this.log?.error(`${pending.method}: ${message.error.message}`);
            message.error ? pending.reject(new Error(message.error.message)) : pending.resolve(message.result);
        }
    }

    fail(error) {
        if (this.failure) return;
        this.failure = error;
        this.notifications.clear();
        this.buffer = Buffer.alloc(0);
        this.rejectAll(error);
        this.onFailure?.(error);
    }

    rejectAll(error) {
        for (const pending of this.pending.values()) {
            clearTimeout(pending.timer);
            pending.reject(error);
        }
        this.pending.clear();
    }
}

function identifyServer(server, signal, dotnetPath = "dotnet") {
    return new Promise((resolve, reject) => {
        childProcess.execFile(dotnetPath, [server, "--identity"], {
            timeout: 10000,
            maxBuffer: 64 * 1024,
            windowsHide: true,
            signal
        }, (error, output) => {
            if (error) return reject(new Error(`Lucent server identity check failed: ${error.message}`));
            try {
                const identity = JSON.parse(output);
                const protocol = identity.protocol;
                const accepted = clientRelease.protocol;
                const language = identity.language;
                if (identity.schemaVersion !== 1 || !/^[0-9a-f]{40}$/.test(identity.sourceCommit)
                    || !/^[0-9a-f]{64}$/.test(identity.server?.sha256)
                    || !/^[0-9a-f]{64}$/.test(identity.compiler?.sha256)
                    || language?.id !== clientRelease.language.id
                    || language.version !== clientRelease.language.version
                    || language.featureLevel !== clientRelease.language.featureLevel
                    || protocol?.id !== accepted.id || !Number.isInteger(protocol.major)
                    || !Number.isInteger(protocol.minor) || protocol.major !== accepted.minimum.major
                    || protocol.minor < accepted.minimum.minor || protocol.minor > accepted.maximumInclusive.minor) {
                    throw new Error("The server identity or protocol is incompatible with this extension.");
                }
                resolve(identity);
            } catch (failure) { reject(failure); }
        });
    });
}

async function selectProject() {
    const folders = vscode.workspace.workspaceFolders ?? [];
    if (folders.some(folder => folder.uri.scheme && folder.uri.scheme !== "file")) {
        throw new Error("Lucent project evaluation requires a file-based workspace on the extension host.");
    }
    const selected = folders.length <= 1 ? folders[0] : await vscode.window.showQuickPick(
        folders.map(folder => ({ label: folder.name, description: folder.uri.fsPath, folder })),
        { placeHolder: "Choose the workspace folder containing the Lucent project" }
    );
    if (folders.length > 1 && !selected) return undefined;
    const folder = selected?.folder ?? selected;
    const projectSetting = vscode.workspace.getConfiguration("lucentLui", folder?.uri).get("projectPath");
    if (!projectSetting) return { folder, projectPath: undefined };
    if (!folder) throw new Error("Select a workspace folder before setting a Lucent project path.");
    if (path.isAbsolute(projectSetting)) {
        const relative = path.relative(folder.uri.fsPath, projectSetting);
        if (relative === ".." || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
            throw new Error("The selected Lucent project is outside its workspace folder.");
        }
        return { folder, projectPath: projectSetting };
    }
    const projectPath = path.resolve(folder.uri.fsPath, projectSetting);
    const relative = path.relative(folder.uri.fsPath, projectPath);
    if (relative === ".." || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
        throw new Error("The selected Lucent project path escapes its workspace folder.");
    }
    return { folder, projectPath };
}

async function bootstrapServer(context, folder, signal, dotnetPath) {
    const configured = vscode.workspace.getConfiguration("lucentLui", folder?.uri).get("serverPath");
    if (configured && !path.isAbsolute(configured)) {
        throw new Error("lucentLui.serverPath must be an absolute path on the workspace host.");
    }
    let bundled;
    let server = configured;
    if (!server) {
        if (clientRelease.serverDelivery !== "bundled" || !clientRelease.bundledServer) {
            throw new Error("The Lucent extension has no bundled server. Set an absolute lucentLui.serverPath override.");
        }
        bundled = verifyBundledServer(context.asAbsolutePath("server"), clientRelease.bundledServer, clientRelease.sourceCommit);
        server = bundled.serverPath;
    }
    const identity = await identifyServer(server, signal, dotnetPath);
    if (bundled && !isDeepStrictEqual(identity, bundled.identity)) {
        throw new Error("The bundled server reported a different identity.");
    }
    return { server, bundled, identity, configured };
}

async function cacheOptions(context, requirement, signal, verifiedDotnetPath) {
    if (releaseCatalog.schemaVersion !== 1 || !Array.isArray(releaseCatalog.releases)) {
        throw new Error("The installed Lucent release catalog is unsupported.");
    }
    if (!context.globalStorageUri?.fsPath || (context.globalStorageUri.scheme && context.globalStorageUri.scheme !== "file")) {
        throw new Error("Lucent tooling cache requires file storage on the workspace host.");
    }
    const dotnetPath = verifiedDotnetPath ?? await new Promise((resolve, reject) => {
        childProcess.execFile("where.exe", ["dotnet.exe"], { timeout: 10000, maxBuffer: 65536, windowsHide: true, signal }, (error, output) => {
            const host = output?.trim().split(/\r?\n/)[0];
            if (error || !host || !path.isAbsolute(host)) reject(new Error("The .NET host could not be located for Lucent tooling installation."));
            else resolve(host);
        });
    });
    return {
        requirement, approvedEntries: releaseCatalog.releases, clientRelease,
        cacheRoot: path.join(context.globalStorageUri.fsPath, "tooling"),
        helperDirectory: context.asAbsolutePath("tooling-cache"), helperManifest: manifest.lucentCacheHelper,
        sourceCommit: clientRelease.sourceCommit, dotnetPath, signal
    };
}

async function prepareToolingInstall(context, current, controller) {
    if (!current()) return;
    const selection = await selectProject();
    if (!selection || !current()) return;
    if (!selection.projectPath) throw new Error("Set lucentLui.projectPath before installing compatible tooling.");
    const bootstrap = await bootstrapServer(context, selection.folder, controller.signal);
    if (!current()) return;
    const requirement = await readProjectRequirements(bootstrap.server, selection.projectPath, controller.signal);
    if (!current()) return;
    const approved = selectApprovedEntry(requirement, releaseCatalog.releases, clientRelease);
    if (approved.status !== "approved") {
        throw new Error("No authenticated release anchor matches this project's compiler. Update the extension's release catalog or use a trusted absolute server override.");
    }
    const options = await cacheOptions(context, requirement, controller.signal);
    await verifyRequirementInputs(requirement, controller.signal);
    return current() ? { requirement, options, approved } : undefined;
}

async function finishToolingInstall(requirement, result, current, controller) {
    if (!current() || result.status === "cancelled") return;
    if (result.status !== "selected") throw new Error(`Lucent tooling was not installed: ${result.reason ?? result.status}. The running server is unchanged.`);
    await verifyRequirementInputs(requirement, controller.signal);
    if (!current()) return;
    const choice = await vscode.window.showInformationMessage("Verified Lucent tooling is ready. Restart language services to use it.", "Restart Language Services");
    if (choice === "Restart Language Services" && current()) await vscode.commands.executeCommand("lucentLui.restartLanguageServices");
    return true;
}

async function importServerArchive(context, current, controller) {
    const prepared = await prepareToolingInstall(context, current, controller);
    if (!prepared) return;
    const { requirement, options } = prepared;
    const selected = await vscode.window.showOpenDialog({
        title: "Import verified Lucent server archive", canSelectMany: false,
        canSelectFiles: true, canSelectFolders: false, filters: { "Lucent server ZIP": ["zip"] }
    });
    if (!selected?.length || !current()) return;
    if (selected[0].scheme !== "file") throw new Error("Choose a server archive on the workspace host.");
    await verifyRequirementInputs(requirement, controller.signal);
    if (!current()) return;
    const result = await vscode.window.withProgress({
        location: vscode.ProgressLocation.Notification, title: "Verifying Lucent tooling", cancellable: true
    }, async (_progress, token) => {
        const subscription = token.onCancellationRequested(() => controller.abort());
        try {
            if (token.isCancellationRequested) controller.abort();
            return await importApprovedArchive({ ...options, archivePath: selected[0].fsPath });
        } finally { subscription.dispose(); }
    });
    return finishToolingInstall(requirement, result, current, controller);
}

async function installMatchingServer(context, current, controller) {
    const prepared = await prepareToolingInstall(context, current, controller);
    if (!prepared) return;
    const { requirement, options, approved } = prepared;
    const cached = await resolveCachedServer(options);
    if (!current() || cached.status === "cancelled") return;
    if (cached.status === "selected") {
        return finishToolingInstall(requirement, cached, current, controller);
    }
    let authentication;
    try { authentication = await vscode.authentication.getSession("github", ["repo"], { createIfNone: true }); }
    catch {
        if (current()) throw new Error("GitHub sign-in was not completed. Retry Lucent: Install Matching Language Tools or import an approved archive.");
        return;
    }
    if (!authentication?.accessToken || !current()) return;
    await verifyRequirementInputs(requirement, controller.signal);
    if (!current()) return;
    const result = await vscode.window.withProgress({
        location: vscode.ProgressLocation.Notification,
        title: `Installing Lucent language tools ${approved.version}`, cancellable: true
    }, async (progress, token) => {
        const subscription = token.onCancellationRequested(() => controller.abort());
        let downloaded;
        let failed = false;
        try {
            if (token.isCancellationRequested) controller.abort();
            if (!current()) return { status: "cancelled" };
            progress.report({ message: "Authenticating and downloading the approved release" });
            downloaded = await acquireApprovedRelease({ ...options, token: authentication.accessToken,
                stagingRoot: path.join(options.cacheRoot, ".downloads") });
            if (!current() || downloaded.status === "cancelled") return { status: "cancelled" };
            if (downloaded.status !== "downloaded") return downloaded;
            await verifyRequirementInputs(requirement, controller.signal);
            if (!current()) return { status: "cancelled" };
            progress.report({ message: "Verifying and installing language tools" });
            return await importDownloadedRelease({ ...options, downloaded });
        } catch (error) {
            failed = true;
            throw error;
        } finally {
            subscription.dispose();
            if (downloaded?.cleanup) {
                try { await downloaded.cleanup(); }
                catch { if (!failed && current()) throw new Error("Language tools were processed, but temporary download cleanup failed. Retry after checking workspace-host storage access."); }
            }
        }
    });
    return finishToolingInstall(requirement, result, current, controller);
}

async function selectVerifiedProjectTools(context, selection, signal, current) {
    const { folder, projectPath } = selection;
    let stage = "host-unavailable";
    try {
        const preflight = await preflightDotnet({ workspacePath: folder?.uri.fsPath ?? os.tmpdir(), requireSdk: !!projectPath, signal });
        if (!current()) return;
        if (preflight.status !== "available") throw new Error("Lucent requires an available .NET host, .NET 10 runtime and project SDK. Run Lucent: Check Environment for setup details.");
        const dotnetPath = preflight.dotnetPath;
        stage = "server-unavailable";
        let { bundled, server, identity, configured } = await bootstrapServer(context, folder, signal, dotnetPath);
        if (!current()) return;
        let requirements, cached;
        if (projectPath) {
            stage = "requirements-unavailable";
            requirements = await readProjectRequirements(server, projectPath, signal, undefined, dotnetPath);
            if (!current()) return;
            if (!configured && selectApprovedEntry(requirements, releaseCatalog.releases, clientRelease).status === "approved") {
                const options = await cacheOptions(context, requirements, signal, dotnetPath);
                if (!current()) return;
                cached = await resolveCachedServer(options);
                if (!current()) return;
                if (cached.status === "selected") {
                    server = cached.serverPath;
                    bundled = undefined;
                    identity = await identifyServer(server, signal, dotnetPath);
                    if (!current()) return;
                    if (!isDeepStrictEqual(identity, cached.identity)) throw new Error("The cached server reported a different identity.");
                }
            }
            assertCompatibleCompiler(requirements, identity);
            stage = "inputs-changed";
            await verifyRequirementInputs(requirements, signal);
            if (!current()) return;
        }
        stage = "server-changed";
        if (bundled) verifyBundledServer(context.asAbsolutePath("server"), clientRelease.bundledServer, clientRelease.sourceCommit);
        if (!current()) return;
        return { bundled, server, identity, configured, cached, dotnetPath, requirements, selection };
    } catch (error) {
        error.doctorReason = stage;
        throw error;
    }
}

async function reverifyProjectTools(context, evidence, signal, current) {
    try { await verifyRequirementInputs(evidence.requirements, signal); }
    catch (error) { error.doctorReason = "inputs-changed"; throw error; }
    if (!current()) return;
    try {
        if (evidence.bundled) verifyBundledServer(context.asAbsolutePath("server"), clientRelease.bundledServer, clientRelease.sourceCommit);
        if (evidence.cached?.status === "selected") {
            const options = await cacheOptions(context, evidence.requirements, signal, evidence.dotnetPath);
            if (!current()) return;
            const cached = await resolveCachedServer(options);
            if (!current()) return;
            if (cached.status !== "selected" || cached.serverPath !== evidence.server || !isDeepStrictEqual(cached.identity, evidence.identity)) {
                throw new Error("The selected server changed.");
            }
        }
        const identity = await identifyServer(evidence.server, signal, evidence.dotnetPath);
        if (!current()) return;
        if (!isDeepStrictEqual(identity, evidence.identity)) throw new Error("The selected server changed.");
        assertCompatibleCompiler(evidence.requirements, identity);
        return evidence;
    } catch (error) { error.doctorReason = "server-changed"; throw error; }
}

async function activateTrusted(context, isActive, onStarted, onState, onSelected, onEvidence) {
    if (!vscode.workspace.isTrusted || !isActive()) return;
    let selection;
    try { selection = await selectProject(); }
    catch (error) {
        if (isActive()) onState({ kind: "no-project", message: error.message });
        vscode.window.showErrorMessage(error.message);
        return;
    }
    if (!isActive()) return;
    if (!selection) { onState({ kind: "no-project" }); return; }
    const { folder, projectPath } = selection;
    onSelected(folder);
    const controller = new AbortController();
    const subscriptions = [];
    let activeStop = { dispose: () => {
        onEvidence(undefined);
        controller.abort();
        for (const subscription of subscriptions.splice(0).reverse()) subscription.dispose();
    } };
    onStarted(activeStop);
    const current = () => isActive() && !controller.signal.aborted && vscode.workspace.isTrusted;
    const report = (kind, message) => { if (isActive()) onState({ kind, projectPath, message }, { notify: true }); };
    let evidence;
    try {
        evidence = await selectVerifiedProjectTools(context, selection, controller.signal, current);
    }
    catch (error) {
        const shouldReport = current();
        activeStop.dispose();
        if (shouldReport) report(error.doctorReason === "host-unavailable" ? "missing-tools" : "mismatch", error.message);
        return;
    }
    if (!evidence || !current()) return;
    const { bundled, server, identity, cached, dotnetPath, requirements } = evidence;
    try {
        if (projectPath) {
            const watchedInputs = new Map();
            for (const input of requirements.inputs) {
                const directory = path.dirname(input.path);
                const inputs = watchedInputs.get(directory) ?? new Set();
                inputs.add(path.resolve(input.path).toLowerCase());
                watchedInputs.set(directory, inputs);
            }
            const changed = inputs => uri => {
                if (!current() || !uri.fsPath || !inputs.has(path.resolve(uri.fsPath).toLowerCase())) return;
                activeStop.dispose();
                report("stopped", "Lucent project tooling inputs changed. Restore if needed, then run Lucent: Restart Language Services. Syntax highlighting remains available.");
            };
            for (const [directory, inputs] of watchedInputs) {
                const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(directory, "*"));
                const invalidate = changed(inputs);
                subscriptions.push(watcher, watcher.onDidCreate(invalidate), watcher.onDidChange(invalidate), watcher.onDidDelete(invalidate));
            }
            await verifyRequirementInputs(requirements, controller.signal);
            if (!current()) return;
        }
        if (bundled) verifyBundledServer(context.asAbsolutePath("server"), clientRelease.bundledServer, clientRelease.sourceCommit);
    } catch (error) {
        const shouldReport = current();
        activeStop.dispose();
        if (shouldReport) report("mismatch", error.message);
        return;
    }
    if (!current()) { activeStop.dispose(); return; }
    const log = vscode.window.createOutputChannel("Lucent LUI", { log: true });
    const delivery = cached?.status === "selected" ? "verified cache" : bundled ? "bundled" : "override";
    log.info(`Starting ${delivery} language server: ${server}; project: ${projectPath ?? "formatting only (set lucentLui.projectPath for semantic tooling)"}`);
    log.info(`${delivery} server source ${identity.sourceCommit}, protocol ${identity.protocol.major}.${identity.protocol.minor}.`);
    if (requirements) log.info(`Evaluated ${requirements.state} project requirements match compiler ${requirements.compiler.sha256}.`);
    const process = childProcess.spawn(dotnetPath, [server], {
        stdio: ["pipe", "pipe", "pipe"],
        windowsHide: true
    });
    const logStderr = chunk => log.error(chunk.toString("utf8").trimEnd());
    process.stderr.on("data", logStderr);
    const rpc = new Rpc(process, log);
    const acceptsUri = value => {
        if (!folder || !vscode.workspace.getWorkspaceFolder) return true;
        const uri = vscode.Uri.parse(value);
        if (uri.scheme !== "file") return true;
        const owner = vscode.workspace.getWorkspaceFolder(uri);
        return owner?.uri.toString() === folder.uri.toString();
    };
    const request = rpc.request.bind(rpc);
    rpc.request = (method, params) => params?.textDocument?.uri && !acceptsUri(params.textDocument.uri)
        ? Promise.resolve(null) : request(method, params);
    const notify = rpc.notify.bind(rpc);
    rpc.notify = (method, params) => {
        if (params?.textDocument?.uri && !acceptsUri(params.textDocument.uri)) return;
        notify(method, params);
    };
    const lintActions = new WeakMap();
    let stopped = false;
    let initialized = false;
    let logDisposed = false;
    const closeLog = () => {
        if (logDisposed) return;
        logDisposed = true;
        log.dispose();
    };
    const diagnostics = vscode.languages.createDiagnosticCollection("lucent-lui");
    rpc.onNotification("textDocument/publishDiagnostics", message => {
        const uri = vscode.Uri.parse(message.uri);
        const document = vscode.workspace.textDocuments.find(item => item.uri.toString() === uri.toString());
        if (document && document.version !== message.version) return;
        if (!message.diagnostics.length) {
            diagnostics.delete(uri);
            return;
        }
        if (!document) return;
        diagnostics.set(uri, message.diagnostics.map(diagnostic => {
            const item = new vscode.Diagnostic(
                new vscode.Range(
                    diagnostic.range.start.line,
                    diagnostic.range.start.character,
                    diagnostic.range.end.line,
                    diagnostic.range.end.character
                ),
                diagnostic.message,
                toVsCodeDiagnosticSeverity(diagnostic.severity, vscode.DiagnosticSeverity)
            );
            item.code = diagnostic.code;
            item.source = diagnostic.source;
            return item;
        }));
    });
    const stop = { dispose: () => {
        if (stopped) return;
        stopped = true;
        onEvidence(undefined);
        controller.abort();
        for (const subscription of subscriptions.splice(0).reverse()) subscription.dispose();
        rpc.notifications.clear();
        process.stderr.off("data", logStderr);
        if (rpc.failure) {
            if (process.exitCode === null && process.pid !== undefined) process.kill();
            closeLog();
            return;
        }
        if (!initialized) {
            rpc.fail(new Error("Lucent language server startup was canceled."));
            if (process.exitCode === null && process.pid !== undefined) process.kill();
            closeLog();
            return;
        }
        if (process.exitCode !== null) { closeLog(); return; }
        const timeout = setTimeout(() => {
            if (process.exitCode === null) process.kill();
            closeLog();
        }, 1000);
        process.once("exit", () => { clearTimeout(timeout); closeLog(); });
        rpc.request("shutdown", {}).then(() => rpc.notify("exit", {})).catch(() => {});
    } };
    activeStop = stop;
    onStarted(stop);
    const reportFailure = error => {
        log.error(error.message);
        return report("stopped",
            `Lucent language server stopped. Check that dotnet and the selected server are available. ${error.message} See Output > Lucent LUI.`
        );
    };
    rpc.onFailure = error => {
        if (stopped) return;
        reportFailure(error);
        stop.dispose();
    };
    subscriptions.push(diagnostics);
    const notifyWatchedFile = (uri, type) => rpc.notify("workspace/didChangeWatchedFiles", {
        changes: [{ uri: uri.toString(), type }]
    });
    const watchedDirectories = new Set();
    const watchProjectDirectories = directories => {
        if (stopped || !isActive()) return;
        for (const projectDirectory of directories) {
            if (watchedDirectories.has(projectDirectory)) continue;
            watchedDirectories.add(projectDirectory);
            const projectAncestors = [];
            for (let directory = projectDirectory; ; directory = path.dirname(directory)) {
                projectAncestors.push(directory);
                if (directory === path.dirname(directory)) break;
            }
            const watchers = [
                vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(projectDirectory, "**/*.{lui,cs,csproj,props,targets,dll,winmd}")),
                vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(projectDirectory, "{packages.lock.json,obj/project.assets.json}")),
                vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(projectDirectory, "*/**/.editorconfig")),
                ...projectAncestors.map(directory => vscode.workspace.createFileSystemWatcher(
                    new vscode.RelativePattern(directory, "{global.json,.editorconfig,Directory.Build.props,Directory.Build.targets,Directory.Packages.props}")
                ))
            ];
            for (const watcher of watchers) {
                subscriptions.push(
                    watcher,
                    watcher.onDidCreate(uri => notifyWatchedFile(uri, 1)),
                    watcher.onDidChange(uri => notifyWatchedFile(uri, 2)),
                    watcher.onDidDelete(uri => notifyWatchedFile(uri, 3))
                );
            }
        }
    };
    rpc.onNotification("lucent/projectGraph", message => watchProjectDirectories(message.directories));
    if (projectPath) watchProjectDirectories([path.dirname(projectPath)]);
    const projectUri = projectPath ? vscode.Uri.file(projectPath).toString() : undefined;
    try {
        if (!vscode.workspace.isTrusted) throw new Error("Workspace Trust is required for Lucent project evaluation.");
        await rpc.request("initialize", { initializationOptions: { projectUri } });
        initialized = true;
    }
    catch (error) {
        if (!stopped) { reportFailure(error); stop.dispose(); }
        throw error;
    }
    if (stopped || !isActive()) { stop.dispose(); return; }
    if (requirements) onEvidence({ ...evidence, owner: stop });
    rpc.notify("initialized", {});
    onState({ kind: projectPath ? "ready" : "no-project", projectPath,
        message: projectPath ? `Language services are active. ${delivery} server source ${identity.sourceCommit.slice(0, 8)}; protocol ${identity.protocol.major}.${identity.protocol.minor}.` : undefined });
    const completionData = new WeakMap();
    const isLucentDocument = document => document.languageId === "lui" || document.languageId === "csharp";
    const isSynchronizedDocument = document => acceptsUri(document.uri.toString()) && (isLucentDocument(document)
        || document.uri.scheme === "file" && path.basename(document.uri.fsPath).toLowerCase() === ".editorconfig");
    const rename = async (document, position, newName) => toWorkspaceEdit(
        await rpc.request("textDocument/rename", {
            textDocument: { uri: document.uri.toString() }, position, newName
        })
    );
    const update = document => isSynchronizedDocument(document) && rpc.notify("textDocument/didChange", {
        textDocument: { uri: document.uri.toString(), version: document.version }, contentChanges: [{ text: document.getText() }]
    });
    for (const document of vscode.workspace.textDocuments.filter(isSynchronizedDocument)) rpc.notify("textDocument/didOpen", {
        textDocument: { uri: document.uri.toString(), version: document.version, text: document.getText() }
    });
    if (stopped) return;
    subscriptions.push(
        vscode.workspace.onDidOpenTextDocument(document => isSynchronizedDocument(document) && rpc.notify("textDocument/didOpen", {
            textDocument: { uri: document.uri.toString(), version: document.version, text: document.getText() }
        })),
        vscode.workspace.onDidChangeTextDocument(event => update(event.document)),
        vscode.workspace.onDidCloseTextDocument(document => isSynchronizedDocument(document) && rpc.notify("textDocument/didClose", { textDocument: { uri: document.uri.toString() } })),
        vscode.workspace.registerTextDocumentContentProvider("lucent-lui", {
            provideTextDocumentContent: uri => rpc.request("lucent/generatedText", { uri: uri.toString() })
        }),
        vscode.languages.registerDefinitionProvider([...crossLanguageSelector, { scheme: "lucent-lui" }], {
            provideDefinition: async (document, position) => {
                const location = await rpc.request("textDocument/definition", {
                    textDocument: { uri: document.uri.toString() }, position
                });
                return location && new vscode.Location(
                    vscode.Uri.parse(location.uri),
                    new vscode.Range(location.range.start.line, location.range.start.character, location.range.end.line, location.range.end.character)
                );
            }
        }),
        vscode.languages.registerRenameProvider("lui", {
            prepareRename: async (document, position) => {
                const result = await rpc.request("textDocument/prepareRename", {
                    textDocument: { uri: document.uri.toString() }, position
                });
                return result && new vscode.Range(
                    result.range.start.line,
                    result.range.start.character,
                    result.range.end.line,
                    result.range.end.character
                );
            },
            provideRenameEdits: rename
        }),
        vscode.languages.registerReferenceProvider("lui", {
            provideReferences: async (document, position, context) => {
                const result = await rpc.request("textDocument/references", {
                    textDocument: { uri: document.uri.toString() }, position,
                    context: { includeDeclaration: context.includeDeclaration }
                });
                return result?.map(location => new vscode.Location(
                    vscode.Uri.parse(location.uri),
                    new vscode.Range(
                        location.range.start.line,
                        location.range.start.character,
                        location.range.end.line,
                        location.range.end.character
                    )
                ));
            }
        }),
        vscode.commands.registerCommand("lucentLui.rename", async (document, position, newName) => {
            const editor = vscode.window.activeTextEditor;
            document = document || editor?.document;
            position = position || editor?.selection.active;
            if (!document || !position || !isLucentDocument(document)) return;
            newName = newName || await vscode.window.showInputBox({ prompt: "New Lucent symbol name" });
            if (!newName) return;
            const edit = await rename(document, position, newName);
            return edit && vscode.workspace.applyEdit(edit);
        }),
        vscode.languages.registerDocumentFormattingEditProvider("lui", {
            provideDocumentFormattingEdits: (document, _options, token) => formattingEdits(rpc, document, undefined, token)
        }),
        vscode.languages.registerDocumentRangeFormattingEditProvider("lui", {
            provideDocumentRangeFormattingEdits: (document, range, _options, token) => formattingEdits(rpc, document, range, token)
        }),
        vscode.languages.registerCodeActionsProvider("lui", {
            provideCodeActions: async (document, range, _context, token) => {
                if (token?.isCancellationRequested) return [];
                const version = document.version;
                const result = await rpc.request("textDocument/codeAction", {
                    textDocument: { uri: document.uri.toString(), version }, range, context: { diagnostics: [] }
                });
                if (token?.isCancellationRequested || document.version !== version) return [];
                return (result ?? []).map(value => {
                    const action = new vscode.CodeAction(value.title, vscode.CodeActionKind.QuickFix);
                    lintActions.set(action, { value, document, version });
                    return action;
                });
            },
            resolveCodeAction: async (action, token) => {
                // A previously resolved action may be requested again after an edit.
                delete action.edit;
                delete action.disabled;
                const pending = lintActions.get(action);
                if (!pending || token?.isCancellationRequested) return action;
                if (pending.document.version !== pending.version) {
                    action.disabled = { reason: "The document changed; request a fresh code action." };
                    return action;
                }
                const resolved = await rpc.request("codeAction/resolve", pending.value);
                if (token?.isCancellationRequested || pending.document.version !== pending.version) {
                    action.disabled = { reason: "The document changed during analysis." };
                    return action;
                }
                if (resolved?.disabled) action.disabled = resolved.disabled;
                else {
                    action.edit = toWorkspaceEdit(resolved?.edit);
                    if (!action.edit) action.disabled = { reason: "A current source snapshot is required." };
                }
                return action;
            }
        }),
        vscode.languages.registerCompletionItemProvider("lui", {
            provideCompletionItems: async (document, position, token) => {
                if (token?.isCancellationRequested) return [];
                const result = await rpc.request("textDocument/completion", {
                    textDocument: { uri: document.uri.toString() }, position
                });
                if (token?.isCancellationRequested) return [];
                return (result?.items ?? []).map(item => {
                    const completion = new vscode.CompletionItem(
                        item.label,
                        toVsCodeCompletionKind(item.kind, vscode.CompletionItemKind)
                    );
                    completion.detail = item.detail;
                    completion.documentation = item.documentation && plaintext(item.documentation.value);
                    if (item.data && !item.documentation) completionData.set(completion, item);
                    return completion;
                });
            },
            resolveCompletionItem: async (completion, token) => {
                const original = completionData.get(completion);
                if (!original || token?.isCancellationRequested) return completion;
                const result = await rpc.request("completionItem/resolve", original);
                if (token?.isCancellationRequested || !result) return completion;
                completion.detail = result.detail;
                completion.documentation = result.documentation && plaintext(result.documentation.value);
                return completion;
            }
        }, "<", " ", ".", ":", "{"),
        vscode.languages.registerHoverProvider([{ language: "lui" }, { scheme: "lucent-lui" }], {
            provideHover: async (document, position) => {
                const result = await rpc.request("textDocument/hover", {
                    textDocument: { uri: document.uri.toString() }, position
                });
                return result && new vscode.Hover(result.contents.map(content =>
                    content.language ? csharp(content.value) : plaintext(content.value)
                ));
            }
        }),
        vscode.languages.registerSignatureHelpProvider("lui", {
            provideSignatureHelp: async (document, position) => {
                const result = await rpc.request("textDocument/signatureHelp", {
                    textDocument: { uri: document.uri.toString() }, position
                });
                if (!result) return undefined;
                const help = new vscode.SignatureHelp();
                help.activeSignature = result.activeSignature;
                help.activeParameter = result.activeParameter;
                help.signatures = result.signatures.map(signature => {
                    const information = new vscode.SignatureInformation(signature.label, signature.documentation && plaintext(signature.documentation.value));
                    information.parameters = signature.parameters.map(parameter => new vscode.ParameterInformation(parameter.label));
                    return information;
                });
                return help;
            }
        }, "(", ",", " "),
        vscode.languages.registerDocumentSymbolProvider("lui", {
            provideDocumentSymbols: async document => {
                const result = await rpc.request("textDocument/documentSymbol", {
                    textDocument: { uri: document.uri.toString() }
                });
                const convert = symbol => {
                    const item = new vscode.DocumentSymbol(
                        symbol.name,
                        "",
                        toVsCodeSymbolKind(symbol.kind, vscode.SymbolKind),
                        new vscode.Range(symbol.range.start.line, symbol.range.start.character, symbol.range.end.line, symbol.range.end.character),
                        new vscode.Range(symbol.selectionRange.start.line, symbol.selectionRange.start.character, symbol.selectionRange.end.line, symbol.selectionRange.end.character)
                    );
                    item.children = symbol.children.map(convert);
                    return item;
                };
                return (result ?? []).map(convert);
            }
        }),
        vscode.languages.registerDocumentSemanticTokensProvider("lui", {
            provideDocumentSemanticTokens: async document => {
                const result = await rpc.request("textDocument/semanticTokens/full", {
                    textDocument: { uri: document.uri.toString() }
                });
                return result && new vscode.SemanticTokens(Uint32Array.from(result.data));
            }
        }, new vscode.SemanticTokensLegend(semanticTokensLegend, []))
    );
    return stop;
}

async function activate(context) {
    createPreviewCommands(vscode, context);
    const ui = createOnboardingUi(vscode, context);
    const environment = createEnvironmentCommands(vscode, context, manifest, checkTrustedProject);
    let currentStop;
    let projectEvidence;
    let importController;
    let selectedFolder;
    let state = { kind: "checking" };
    let disposed = false;
    let generation = 0;
    let semanticRequested = false;
    async function checkTrustedProject(signal, operationCurrent) {
        const ticket = generation;
        const folders = () => JSON.stringify((vscode.workspace.workspaceFolders ?? []).map(folder => [folder.uri.scheme, folder.uri.fsPath]));
        const initialFolders = folders();
        const current = () => operationCurrent() && !disposed && !signal.aborted && vscode.workspace.isTrusted
            && ticket === generation && folders() === initialFolders;
        if (!current()) return;
        const selection = await selectProject();
        if (!current()) return;
        if (!selection?.projectPath) {
            const error = new Error("Select a Lucent project first.");
            error.doctorReason = "project-not-selected";
            throw error;
        }
        const configuration = vscode.workspace.getConfiguration("lucentLui", selection.folder?.uri);
        const projectSetting = configuration.get("projectPath");
        const serverSetting = configuration.get("serverPath");
        const selectedCurrent = () => current()
            && vscode.workspace.getConfiguration("lucentLui", selection.folder?.uri).get("projectPath") === projectSetting
            && vscode.workspace.getConfiguration("lucentLui", selection.folder?.uri).get("serverPath") === serverSetting;
        const saved = projectEvidence;
        const reusable = saved && saved.owner === currentStop && saved.selection.projectPath === selection.projectPath
            && saved.selection.folder?.uri.fsPath === selection.folder?.uri.fsPath;
        const evidenceCurrent = () => selectedCurrent()
            && (!reusable || projectEvidence === saved && saved.owner === currentStop);
        try {
            const evidence = reusable
                ? await reverifyProjectTools(context, saved, signal, evidenceCurrent)
                : await selectVerifiedProjectTools(context, selection, signal, evidenceCurrent);
            if (!evidence || !evidenceCurrent()) return;
            return trustedProjectReport(evidence, reusable ? "reused" : "evaluated");
        } catch (error) {
            if (!evidenceCurrent()) return;
            throw error;
        }
    }
    const update = (next, options) => { state = next; ui.update(next, options); };
    const restart = () => {
        semanticRequested = true;
        const ticket = ++generation;
        projectEvidence = undefined;
        environment.cancel();
        importController?.abort();
        currentStop?.dispose();
        currentStop = undefined;
        selectedFolder = undefined;
        if (disposed) return Promise.resolve();
        if (!vscode.workspace.isTrusted) {
            update({ kind: "untrusted" });
            vscode.window.showErrorMessage("Trust this workspace to start Lucent language services.");
            return Promise.resolve();
        }
        update({ kind: "checking" });
        const isCurrent = () => !disposed && ticket === generation;
        return activateTrusted(context, isCurrent, stop => {
            if (isCurrent()) currentStop = stop;
            else stop.dispose();
        }, (next, options) => { if (isCurrent()) update(next, options); }, folder => { if (isCurrent()) selectedFolder = folder; }, evidence => {
            if (isCurrent()) { projectEvidence = evidence; if (!evidence) environment.cancel(); }
        }).then(stop => {
            if (isCurrent()) currentStop = stop;
            else stop?.dispose();
        }, error => {
            if (isCurrent()) throw error;
        });
    };
    context.subscriptions.push({ dispose: () => { disposed = true; projectEvidence = undefined; generation++; environment.cancel(); importController?.abort(); currentStop?.dispose(); } });
    context.subscriptions.push(vscode.commands.registerCommand("lucentLui.restartLanguageServices", restart));
    if (vscode.workspace.onDidOpenTextDocument) context.subscriptions.push(vscode.workspace.onDidOpenTextDocument(document => {
        if (document?.languageId !== "lui" || semanticRequested || disposed) return;
        semanticRequested = true;
        if (!vscode.workspace.isTrusted) {
            update({ kind: "untrusted" });
            return;
        }
        void restart().catch(error => vscode.window.showErrorMessage(error.message));
    }));
    const install = (action, verb) => async () => {
        if (disposed) return;
        if (!vscode.workspace.isTrusted) { vscode.window.showErrorMessage(`Trust this workspace before ${verb} Lucent tooling.`); return; }
        importController?.abort();
        const controller = importController = new AbortController();
        const ticket = generation;
        const current = () => !disposed && ticket === generation && !controller.signal.aborted && vscode.workspace.isTrusted;
        ui.update({ kind: "installing" });
        try {
            const installed = await action(context, current, controller);
            if (current() && installed) update({ kind: "tools-ready", projectPath: state.projectPath });
        } catch (error) {
            if (current()) update({ kind: "offline", projectPath: state.projectPath, message: error.message }, { notify: true });
        } finally {
            if (importController === controller) {
                importController = undefined;
                if (!disposed && ticket === generation) ui.update(state);
            }
        }
    };
    context.subscriptions.push(
        vscode.commands.registerCommand("lucentLui.importServerArchive", install(importServerArchive, "importing")),
        vscode.commands.registerCommand("lucentLui.installMatchingServer", install(installMatchingServer, "installing")),
        vscode.commands.registerCommand("lucentLui.showToolingStatus", ui.showActions),
        vscode.commands.registerCommand("lucentLui.openGettingStarted", () => vscode.commands.executeCommand("workbench.action.openWalkthrough", "lucent.lucent-lui#gettingStarted", false)),
        vscode.commands.registerCommand("lucentLui.selectProject", async () => {
            if (disposed || !vscode.workspace.isTrusted) return;
            const ticket = generation;
            const folders = (vscode.workspace.workspaceFolders ?? []).filter(folder => folder.uri.scheme === "file");
            const choice = folders.length === 1 ? { folder: folders[0] } : await vscode.window.showQuickPick(
                folders.map(folder => ({ label: folder.name, description: folder.uri.fsPath, folder })),
                { placeHolder: "Choose the workspace folder that will own language services" });
            if (!choice || disposed || ticket !== generation || !vscode.workspace.isTrusted) return;
            const selected = await vscode.window.showOpenDialog({ title: "Select Lucent project", defaultUri: choice.folder.uri,
                canSelectMany: false, canSelectFiles: true, canSelectFolders: false, filters: { "C# project": ["csproj"] } });
            if (!selected?.length || disposed || ticket !== generation || !vscode.workspace.isTrusted) return;
            const relative = path.relative(choice.folder.uri.fsPath, selected[0].fsPath);
            if (selected[0].scheme !== "file" || path.extname(relative).toLowerCase() !== ".csproj" || relative === ".."
                || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
                await vscode.window.showErrorMessage("Select a .csproj inside the chosen workspace folder.");
                return;
            }
            await vscode.workspace.getConfiguration("lucentLui", choice.folder.uri).update("projectPath", relative.replaceAll("\\", "/"), vscode.ConfigurationTarget.WorkspaceFolder);
            if (!disposed && ticket === generation) await restart();
        })
    );
    if (vscode.workspace.onDidChangeWorkspaceFolders) context.subscriptions.push(vscode.workspace.onDidChangeWorkspaceFolders(event => {
        projectEvidence = undefined;
        environment.cancel();
        if (!event.removed?.length || disposed) return;
        importController?.abort();
        if (selectedFolder && !event.removed.some(folder => folder.uri.fsPath === selectedFolder.uri.fsPath)) return;
        generation++;
        currentStop?.dispose();
        currentStop = undefined;
        selectedFolder = undefined;
        update({ kind: vscode.workspace.isTrusted ? "no-project" : "untrusted" });
    }));
    if (vscode.workspace.onDidChangeConfiguration) context.subscriptions.push(vscode.workspace.onDidChangeConfiguration(event => {
        if (disposed || !event.affectsConfiguration("lucentLui.projectPath", selectedFolder?.uri)
            && !event.affectsConfiguration("lucentLui.serverPath", selectedFolder?.uri)) return;
        environment.cancel();
        projectEvidence = undefined;
        if (semanticRequested && vscode.workspace.isTrusted) void restart().catch(() => {}); // Startup reports its own failure once.
    }));
    if (!vscode.workspace.isTrusted) {
        semanticRequested = (vscode.workspace.textDocuments ?? []).some(document => document.languageId === "lui");
        update({ kind: "untrusted" });
        context.subscriptions.push(vscode.workspace.onDidGrantWorkspaceTrust(() => {
            if (semanticRequested) void restart().catch(error => vscode.window.showErrorMessage(error.message));
        }));
        return;
    }
    if ((vscode.workspace.textDocuments ?? []).some(document => document.languageId === "lui")) return restart();
    update({ kind: "no-project" });
}

exports.activate = activate;
exports.identifyServer = identifyServer;
exports.selectProject = selectProject;
exports.toVsCodeCompletionKind = toVsCodeCompletionKind;
exports.toVsCodeDiagnosticSeverity = toVsCodeDiagnosticSeverity;
exports.toVsCodeSymbolKind = toVsCodeSymbolKind;
exports.semanticTokensLegend = semanticTokensLegend;
exports.crossLanguageSelector = crossLanguageSelector;
exports.toTextEdits = toTextEdits;
exports.formattingEdits = formattingEdits;
exports.toWorkspaceEdit = toWorkspaceEdit;
