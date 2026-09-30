const assert = require("node:assert/strict");
const { EventEmitter } = require("node:events");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const loaded = { exports: {} };
vm.runInNewContext(fs.readFileSync(path.join(__dirname, "extension.js"), "utf8"), {
    Buffer,
    clearTimeout,
    console,
    exports: loaded.exports,
    module: loaded,
    require: name => name === "vscode" ? {} : require(name),
    setTimeout
}, { filename: "extension.js" });

test("converts LSP completion, symbol, and diagnostic kinds to VS Code kinds", () => {
    const completion = { Method: "method", Function: "function", Field: "field", EnumMember: "enum-member", Struct: "struct", Text: "text" };
    const symbol = { Class: "class", Method: "method", Field: "field", Function: "function", Variable: "variable", String: "string" };
    const severity = { Error: "error", Warning: "warning", Information: "information", Hint: "hint" };
    assert.equal(loaded.exports.toVsCodeCompletionKind(2, completion), "method");
    assert.equal(loaded.exports.toVsCodeCompletionKind(3, completion), "function");
    assert.equal(loaded.exports.toVsCodeCompletionKind(5, completion), "field");
    assert.equal(loaded.exports.toVsCodeCompletionKind(20, completion), "enum-member");
    assert.equal(loaded.exports.toVsCodeCompletionKind(22, completion), "struct");
    assert.equal(loaded.exports.toVsCodeCompletionKind(999, completion), "text");
    assert.equal(loaded.exports.toVsCodeSymbolKind(5, symbol), "class");
    assert.equal(loaded.exports.toVsCodeSymbolKind(6, symbol), "method");
    assert.equal(loaded.exports.toVsCodeSymbolKind(8, symbol), "field");
    assert.equal(loaded.exports.toVsCodeSymbolKind(12, symbol), "function");
    assert.equal(loaded.exports.toVsCodeSymbolKind(13, symbol), "variable");
    assert.equal(loaded.exports.toVsCodeSymbolKind(999, symbol), "string");
    assert.equal(loaded.exports.toVsCodeDiagnosticSeverity(1, severity), "error");
    assert.equal(loaded.exports.toVsCodeDiagnosticSeverity(2, severity), "warning");
    assert.equal(loaded.exports.toVsCodeDiagnosticSeverity(3, severity), "information");
    assert.equal(loaded.exports.toVsCodeDiagnosticSeverity(999, severity), "hint");
});

test("does not treat component tag delimiters as expression brackets", () => {
    const configuration = JSON.parse(
        fs.readFileSync(path.join(__dirname, "language-configuration.json"), "utf8")
    );
    assert.ok(!configuration.brackets.some(pair => pair[0] === "<"));
});

test("formatting drops cancelled and obsolete edits and sends the captured version", async () => {
    const document = { uri: { toString: () => "file:///Example.lui" }, version: 2 };
    let calls = 0;
    const cancelled = { isCancellationRequested: true };
    const rpc = { request: async (_method, params) => {
        calls++;
        assert.equal(params.textDocument.version, 2);
        document.version = 3;
        return [{ newText: "must never be applied" }];
    } };
    assert.equal((await loaded.exports.formattingEdits(rpc, document, undefined, cancelled)).length, 0);
    assert.equal(calls, 0);
    assert.equal((await loaded.exports.formattingEdits(rpc, document)).length, 0);
    assert.equal(calls, 1);
    document.version = 2;
    cancelled.isCancellationRequested = false;
    rpc.request = async () => { cancelled.isCancellationRequested = true; return [{ newText: "obsolete" }]; };
    assert.equal((await loaded.exports.formattingEdits(rpc, document, undefined, cancelled)).length, 0);
});

test("grammar retains parameterized component and style declaration headers", () => {
    const grammar = JSON.parse(
        fs.readFileSync(path.join(__dirname, "syntaxes", "lui.tmLanguage.json"), "utf8")
    );
    const declarations = grammar.repository.declarations.patterns;
    assert.ok(declarations.some(pattern =>
        pattern.begin === "\\b(component|style)\\s+([A-Za-z_][A-Za-z0-9_]*)(\\s*)(\\()"
        && pattern.end === "\\)"
    ));
});

test("grammar marks declarative transition policies as keywords", () => {
    const grammar = JSON.parse(
        fs.readFileSync(path.join(__dirname, "syntaxes", "lui.tmLanguage.json"), "utf8")
    );
    const keywords = grammar.repository.keywords.patterns;
    assert.ok(keywords.some(pattern => pattern.match.includes("transition")));
});

test("raw attribute strings stay inside tags and do not promise C# escapes", () => {
    const grammar = JSON.parse(fs.readFileSync(path.join(__dirname, "syntaxes", "lui.tmLanguage.json"), "utf8"));
    assert.ok(!grammar.patterns.some(pattern => pattern.include === "#strings"));
    const tag = grammar.repository.tags.patterns.find(pattern => pattern.begin);
    assert.ok(tag.patterns.some(pattern => pattern.include === "#strings"));
    const quoted = grammar.repository.strings.patterns[0];
    assert.equal(quoted.end, '"');
    assert.deepEqual(quoted.patterns ?? [], []);
});

test("activates only Lucent workspaces and registers C# cross-language selectors", () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, "package.json"), "utf8"));
    assert.deepEqual(manifest.activationEvents, ["onLanguage:lui", "workspaceContains:**/*.lui"]);
    assert.deepEqual(manifest.extensionKind, ["workspace"]);
    assert.equal(manifest.capabilities.untrustedWorkspaces.supported, "limited");
    assert.deepEqual(manifest.capabilities.untrustedWorkspaces.restrictedConfigurations,
        ["lucentLui.serverPath", "lucentLui.projectPath"]);
    assert.deepEqual(JSON.parse(JSON.stringify(loaded.exports.crossLanguageSelector)), [
        { language: "lui" }, { language: "csharp", scheme: "file" }
    ]);
});

test("activation preserves current diagnostics and clears closed documents", async () => {
    const diagnostics = { deleted: [], delete(uri) { this.deleted.push(uri.toString()); }, dispose() {} };
    const patterns = [];
    const watchers = [];
    let onDidOpenTextDocument;
    let onDidChangeTextDocument;
    let onDidCloseTextDocument;
    let semanticProvider;
    let semanticLegend;
    let referenceProvider;
    let referenceSelector;
    let renameProvider;
    let renameSelector;
    let lucentRename;
    const competingCsharpProvider = { provideRenameEdits: () => "csharp-edit" };
    const renameProviders = [{ selector: "csharp", provider: competingCsharpProvider }];
    let spawnOptions;
    const process = new MockProcess();
    const openDocument = {
        uri: { toString: () => "file:///missing.lui" },
        version: 1,
        languageId: "lui",
        getText: () => ""
    };
    const csharpDocument = {
        uri: { toString: () => "file:///Helpers.cs" },
        version: 1,
        languageId: "csharp",
        getText: () => "class Helpers {}"
    };
    const editorConfigDocument = {
        uri: { scheme: "file", fsPath: path.resolve("workspace", ".editorconfig"), toString: () => "file:///workspace/.editorconfig" },
        version: 1,
        languageId: "properties",
        getText: () => "root = true"
    };
    const vscode = {
        Diagnostic: class { constructor() {} },
        DiagnosticSeverity: { Error: 1, Warning: 2, Information: 3, Hint: 4 },
        Location: class { constructor(uri, range) { this.uri = uri; this.range = range; } },
        Range: class { constructor() {} },
        RelativePattern: class { constructor(base, pattern) { this.base = base; this.pattern = pattern; patterns.push([base, pattern]); } },
        SemanticTokens: class { constructor(data) { this.data = data; } },
        SemanticTokensLegend: class { constructor(types, modifiers) { this.types = types; this.modifiers = modifiers; } },
        Uri: {
            file: value => ({ toString: () => "file:///" + value.replaceAll("\\", "/") }),
            parse: value => ({ toString: () => value })
        },
        languages: {
            createDiagnosticCollection: () => diagnostics,
            registerCompletionItemProvider: disposable,
            registerDefinitionProvider: disposable,
            registerDocumentFormattingEditProvider: disposable,
            registerCodeActionsProvider: disposable,
            registerDocumentRangeFormattingEditProvider: disposable,
            registerDocumentSymbolProvider: disposable,
            registerDocumentSemanticTokensProvider: (_selector, provider, legend) => {
                semanticProvider = provider;
                semanticLegend = legend;
                return disposable();
            },
            registerHoverProvider: disposable,
            registerReferenceProvider: (selector, provider) => {
                referenceSelector = selector;
                referenceProvider = provider;
                return disposable();
            },
            registerRenameProvider: (selector, provider) => {
                renameSelector = selector;
                renameProvider = provider;
                renameProviders.push({ selector, provider });
                return disposable();
            },
            registerSignatureHelpProvider: disposable
        },
        commands: { registerCommand: (name, command) => { if (name === "lucentLui.rename") lucentRename = command; return disposable(); } },
        window: { createOutputChannel: () => ({ info() {}, warn() {}, error() {}, dispose() {} }), showErrorMessage() {}, showInputBox: async () => "Renamed" },
        workspace: {
            isTrusted: true,
            applyEdit: async () => true,
            createFileSystemWatcher: pattern => {
                const watcher = {
                    pattern,
                    onDidCreate(callback) { this.create = callback; return disposable(); },
                    onDidChange(callback) { this.change = callback; return disposable(); },
                    onDidDelete(callback) { this.delete = callback; return disposable(); },
                    dispose() {}
                };
                watchers.push(watcher);
                return watcher;
            },
            workspaceFolders: [{ uri: { fsPath: path.resolve("workspace") } }],
            getConfiguration: () => ({ get: key => key === "projectPath" ? "host/Host.csproj" : path.resolve("server.dll") }),
            onDidChangeTextDocument: callback => { onDidChangeTextDocument = callback; return disposable(); },
            onDidCloseTextDocument: callback => { onDidCloseTextDocument = callback; return disposable(); },
            onDidOpenTextDocument: callback => { onDidOpenTextDocument = callback; return disposable(); },
            registerTextDocumentContentProvider: disposable,
            textDocuments: [openDocument, csharpDocument, editorConfigDocument]
        }
    };
    const extension = loadExtension(vscode, process, options => spawnOptions = options);
    await extension.activate({ subscriptions: [] });
    assert.equal(spawnOptions.windowsHide, true);
    assert.ok(process.notifications.some(notification =>
        notification.method === "textDocument/didOpen"
        && notification.params.textDocument.uri === "file:///Helpers.cs"
    ));
    assert.ok(process.notifications.some(notification =>
        notification.method === "textDocument/didOpen"
        && notification.params.textDocument.uri === "file:///workspace/.editorconfig"
        && notification.params.textDocument.text === "root = true"
    ));
    const nestedEditorConfig = {
        uri: { scheme: "file", fsPath: path.resolve("workspace", "features", ".editorconfig"), toString: () => "file:///workspace/features/.editorconfig" },
        version: 1,
        languageId: "properties",
        getText: () => "[*.lui]\nlucent_lui_default_content = error"
    };
    onDidOpenTextDocument(nestedEditorConfig);
    nestedEditorConfig.version = 2;
    nestedEditorConfig.getText = () => "[*.lui]\nlucent_lui_default_content = warning";
    onDidChangeTextDocument({ document: nestedEditorConfig });
    onDidCloseTextDocument(nestedEditorConfig);
    assert.deepEqual(process.notifications.filter(notification =>
        notification.params?.textDocument?.uri === "file:///workspace/features/.editorconfig"
    ).map(notification => notification.method), [
        "textDocument/didOpen", "textDocument/didChange", "textDocument/didClose"
    ]);
    assert.equal(process.notifications.find(notification =>
        notification.method === "textDocument/didChange"
        && notification.params.textDocument.uri === "file:///workspace/features/.editorconfig"
    ).params.contentChanges[0].text, "[*.lui]\nlucent_lui_default_content = warning");
    const nestedEditorConfigWatcher = watchers.find(watcher => watcher.pattern.pattern === "*/**/.editorconfig");
    assert.ok(nestedEditorConfigWatcher);
    nestedEditorConfigWatcher.change({ toString: () => "file:///workspace/features/.editorconfig" });
    assert.ok(process.notifications.some(notification =>
        notification.method === "workspace/didChangeWatchedFiles"
        && notification.params.changes[0].uri === "file:///workspace/features/.editorconfig"
        && notification.params.changes[0].type === 2
    ));
    process.send({ jsonrpc: "2.0", method: "textDocument/publishDiagnostics", params: {
        uri: "file:///missing.lui", version: 999, diagnostics: []
    } });
    assert.deepEqual(diagnostics.deleted, []);
    vscode.workspace.textDocuments.length = 0;
    process.send({ jsonrpc: "2.0", method: "textDocument/publishDiagnostics", params: {
        uri: "file:///missing.lui", version: 999, diagnostics: []
    } });
    assert.deepEqual(diagnostics.deleted, ["file:///missing.lui"]);
    assert.ok(patterns.some(([base]) => base === "referenced"));
    assert.ok(referenceProvider);
    assert.equal(referenceSelector, "lui");
    assert.equal(renameSelector, "lui");
    assert.ok(lucentRename);
    assert.equal(
        renameProviders.filter(provider => provider.selector === "csharp").length,
        1
    );
    assert.equal(competingCsharpProvider.provideRenameEdits(), "csharp-edit");
    const references = await referenceProvider.provideReferences(
        { uri: { toString: () => "file:///Widget.lui" } },
        { line: 0, character: 0 },
        { includeDeclaration: true }
    );
    assert.equal(process.lastRequest.params.context.includeDeclaration, true);
    assert.equal(references.length, 1);
    assert.equal(await referenceProvider.provideReferences(
        { uri: { toString: () => "file:///Helpers.cs" } },
        { line: 0, character: 0 },
        { includeDeclaration: false }
    ), undefined);
    assert.equal(process.lastRequest.params.textDocument.uri, "file:///Helpers.cs");
    assert.equal(await renameProvider.provideRenameEdits(
        { uri: { toString: () => "file:///Helpers.cs" } },
        { line: 0, character: 0 },
        "Renamed"
    ), undefined);
    assert.equal(process.lastRequest.params.textDocument.uri, "file:///Helpers.cs");
    await lucentRename(csharpDocument, { line: 0, character: 0 }, "Renamed");
    assert.equal(process.lastRequest.params.textDocument.uri, "file:///Helpers.cs");
    assert.deepEqual(Array.from(semanticLegend.types), ["keyword", "type", "property", "enumMember"]);
    const tokens = await semanticProvider.provideDocumentSemanticTokens({
        uri: { toString: () => "file:///Widget.lui" }
    });
    assert.deepEqual(Array.from(tokens.data), [0, 0, 3, 0, 0]);
});

function disposable() { return { dispose() {} }; }

function loadExtension(vscode, process, spawned, identityOutput = {
    schemaVersion: 1,
    sourceCommit: "1".repeat(40),
    server: { sha256: "2".repeat(64) },
    compiler: { sha256: "3".repeat(64) },
    language: { id: "lui", version: "preview", featureLevel: "preview-1" },
    protocol: { id: "lucent-lui", major: 1, minor: 0 }
}, identified) {
    const module = { exports: {} };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, "extension.js"), "utf8"), {
        Buffer,
        clearTimeout,
        console,
        exports: module.exports,
        module,
        require: name => name === "vscode" ? vscode : name === "child_process" ? {
            execFile: (_command, _args, _options, callback) => {
                identified?.();
                return typeof identityOutput === "function"
                    ? identityOutput(callback) : callback(null, JSON.stringify(identityOutput));
            },
            spawn: (_command, _args, options) => { spawned?.(options); return typeof process === "function" ? process() : process; }
        } : require(name),
        setTimeout
    }, { filename: "extension.js" });
    return module.exports;
}

class MockProcess extends EventEmitter {
    constructor() {
        super();
        this.exitCode = null;
        this.stdout = new EventEmitter();
        this.stderr = new EventEmitter();
        this.notifications = [];
        this.pid = 123;
        this.stdin = Object.assign(new EventEmitter(), { write: value => {
            if (!Buffer.isBuffer(value)) return;
            const request = JSON.parse(value.toString());
            if (request.id === undefined) this.notifications.push(request);
            this.lastRequest = request;
            if (this.holdRequests?.has(request.method)) return;
            if (request.method === "initialize") {
                this.send({ jsonrpc: "2.0", method: "lucent/projectGraph", params: { directories: ["host", "referenced"] } });
            }
            if (request.id !== undefined) this.send({
                jsonrpc: "2.0",
                id: request.id,
                result: this.responses?.[request.method] ?? (request.method === "textDocument/semanticTokens/full" ? { data: [0, 0, 3, 0, 0] }
                    : request.method === "textDocument/references"
                        ? request.params.textDocument.uri.endsWith("Helpers.cs") ? null : [{
                        uri: "file:///Widget.lui",
                        range: { start: { line: 0, character: 0 }, end: { line: 0, character: 1 } }
                    }]
                        : request.method === "textDocument/rename" ? null : {})
            });
        } });
    }

    kill() { this.exitCode = 0; this.emit("exit"); }

    send(message) {
        const body = Buffer.from(JSON.stringify(message));
        this.stdout.emit("data", Buffer.concat([Buffer.from(`Content-Length: ${body.length}\r\n\r\n`), body]));
    }
}

function failureHarness(process, { trusted = true, identity, processes = [process], folders, selectedFolder = 0 } = {}) {
    let completionProvider;
    let codeActionsProvider;
    let symbolProvider;
    const logs = [];
    const resources = [];
    const messages = [];
    let generated;
    let onGrantTrust;
    let onOpen;
    let spawnCount = 0;
    let identityChecks = 0;
    let restart;
    const own = () => {
        const resource = { disposed: 0, dispose() { this.disposed++; } };
        resources.push(resource);
        return resource;
    };
    const workspace = {
        isTrusted: trusted,
        onDidGrantWorkspaceTrust: callback => { onGrantTrust = callback; return own(); },
        workspaceFolders: folders ?? [{ uri: { fsPath: path.resolve("workspace") } }],
            getConfiguration: () => ({ get: key => key === "projectPath" ? "host/Host.csproj" : path.resolve("server.dll") }),
        textDocuments: [],
        createFileSystemWatcher: () => Object.assign(own(), {
            onDidCreate: own, onDidChange: own, onDidDelete: own
        }),
        onDidOpenTextDocument: callback => { onOpen = callback; return own(); },
        onDidChangeTextDocument: own,
        onDidCloseTextDocument: own,
        registerTextDocumentContentProvider: (_scheme, provider) => { generated = provider; return own(); }
    };
    const vscode = {
        workspace,
        window: { createOutputChannel: () => {
            const channel = own();
            const write = text => {
                if (channel.disposed) throw new Error("A disposed output channel was used.");
                logs.push(text);
            };
            return Object.assign(channel, { info: write, warn: write, error: write });
        }, showErrorMessage: message => messages.push(message), showQuickPick: async items => items[selectedFolder] },
        Uri: { file: value => ({ toString: () => value }), parse: value => ({ scheme: value.startsWith("file:") ? "file" : undefined, toString: () => value }) },
        Range: class { constructor(...values) { this.values = values; } },
        WorkspaceEdit: class { constructor() { this.changes = []; } replace(uri, range, text) { this.changes.push({ uri, range, text }); } },
        CodeAction: class { constructor(title, kind) { this.title = title; this.kind = kind; } },
        CodeActionKind: { QuickFix: "quickfix" },
        RelativePattern: class {},
        SemanticTokensLegend: class {},
        commands: { registerCommand: (name, command) => { if (name === "lucentLui.restartLanguageServices") restart = command; return { dispose() {} }; } },
        CompletionItem: class { constructor(label, kind) { this.label = label; this.kind = kind; } },
        CompletionItemKind: { Field: "field" },
        MarkdownString: class { appendText(text) { this.value = text; } },
        languages: new Proxy({}, { get: (_target, key) => key === "createDiagnosticCollection"
            ? () => Object.assign(own(), { delete() {} }) : key === "registerCompletionItemProvider"
                ? (_selector, provider) => { completionProvider = provider; return own(); } : key === "registerCodeActionsProvider"
                    ? (_selector, provider) => { codeActionsProvider = provider; return own(); } : key === "registerDocumentSymbolProvider"
                        ? (_selector, provider) => { symbolProvider = provider; return own(); } : own })
    };
    const context = { subscriptions: [] };
    let started;
    const startedPromise = new Promise(resolve => { started = resolve; });
    let nextProcess = 0;
    const extension = loadExtension(vscode, () => processes[Math.min(nextProcess++, processes.length - 1)], () => { spawnCount++; started(); }, identity, () => { identityChecks++; });
    return { context, messages, resources, logs, workspace, started: startedPromise, get spawnCount() { return spawnCount; }, get identityChecks() { return identityChecks; }, grantTrust: () => { workspace.isTrusted = true; onGrantTrust(); }, restart: () => restart(), open: document => onOpen(document), codeActions: () => codeActionsProvider, completion: () => completionProvider, symbols: () => symbolProvider, activate: () => extension.activate(context),
        request: () => generated.provideTextDocumentContent({ toString: () => "lucent-lui:test" }) };
}

test("Restricted Mode leaves project evaluation and server process untouched until trust is granted", async () => {
    const server = new MockProcess();
    const harness = failureHarness(server, { trusted: false });
    await harness.activate();
    assert.equal(harness.spawnCount, 0);
    assert.equal(server.lastRequest, undefined);
    assert.equal(harness.resources.length, 1);
    await harness.restart();
    assert.equal(harness.spawnCount, 0);
    assert.match(harness.messages[0], /Trust this workspace/);
    harness.grantTrust();
    await harness.started;
    assert.equal(harness.spawnCount, 1);
    assert.equal(server.lastRequest.method, "initialize");
    harness.context.subscriptions.forEach(resource => resource.dispose());
});

test("incompatible server identity prevents project initialization", async () => {
    const server = new MockProcess();
    const harness = failureHarness(server, { identity: {
        schemaVersion: 1, sourceCommit: "1".repeat(40),
        server: { sha256: "2".repeat(64) }, compiler: { sha256: "3".repeat(64) },
        language: { id: "lui", version: "preview", featureLevel: "preview-1" },
        protocol: { id: "lucent-lui", major: 2, minor: 0 }
    } });
    await harness.activate();
    assert.equal(harness.spawnCount, 0);
    assert.equal(server.lastRequest, undefined);
    assert.match(harness.messages[0], /incompatible/);
});

test("missing protocol minor and incompatible language fail the project-free identity check", async () => {
    for (const change of [
        identity => { delete identity.protocol.minor; },
        identity => { identity.language.featureLevel = "future"; }
    ]) {
        const identity = {
            schemaVersion: 1, sourceCommit: "1".repeat(40),
            server: { sha256: "2".repeat(64) }, compiler: { sha256: "3".repeat(64) },
            language: { id: "lui", version: "preview", featureLevel: "preview-1" },
            protocol: { id: "lucent-lui", major: 1, minor: 0 }
        };
        change(identity);
        const harness = failureHarness(new MockProcess(), { identity });
        await harness.activate();
        assert.equal(harness.spawnCount, 0);
        assert.match(harness.messages[0], /incompatible/);
    }
});

test("explicit restart disposes the old server's registrations before starting another", async () => {
    const first = new MockProcess();
    const second = new MockProcess();
    const harness = failureHarness(first, { processes: [first, second] });
    await harness.activate();
    const firstResources = [...harness.resources];
    assert.equal(harness.spawnCount, 1);
    await harness.restart();
    assert.equal(harness.spawnCount, 2);
    assert.ok(second.notifications.some(notification => notification.method === "initialized"));
    const resourcesAfterRestart = harness.resources.length;
    first.send({ jsonrpc: "2.0", method: "lucent/projectGraph", params: { directories: ["late-project"] } });
    assert.equal(harness.resources.length, resourcesAfterRestart);
    assert.ok(firstResources.some(resource => resource.disposed === 0));
    first.emit("exit", 0, null);
    assert.ok(firstResources.every(resource => resource.disposed === 1));
    assert.ok(harness.resources.slice(firstResources.length).some(resource => resource.disposed === 0));
    harness.context.subscriptions.forEach(resource => resource.dispose());
    second.emit("exit", 0, null);
    assert.ok(harness.resources.every(resource => resource.disposed === 1));
});

test("restart cancels a pending initialize without waiting for its reply", async () => {
    const first = new MockProcess();
    first.holdRequests = new Set(["initialize"]);
    const second = new MockProcess();
    const harness = failureHarness(first, { processes: [first, second] });
    const initialActivation = harness.activate();
    await harness.started;
    assert.equal(first.lastRequest.method, "initialize");
    await Promise.race([
        harness.restart(),
        new Promise((_, reject) => setTimeout(() => reject(new Error("Restart waited for the old initialize.")), 250))
    ]);
    await initialActivation;
    assert.equal(harness.spawnCount, 2);
    assert.equal(first.exitCode, 0);
    assert.ok(second.notifications.some(notification => notification.method === "initialized"));
    harness.context.subscriptions.forEach(resource => resource.dispose());
    second.emit("exit", 0, null);
});

test("canceling multi-root selection aborts before identity check or process spawn", async () => {
    const folders = ["first", "second"].map(name => ({ name, uri: {
        scheme: "file", fsPath: path.resolve(name), toString: () => `file:///${name}`
    } }));
    const harness = failureHarness(new MockProcess(), { folders, selectedFolder: -1 });
    await harness.activate();
    assert.equal(harness.identityChecks, 0);
    assert.equal(harness.spawnCount, 0);
    assert.deepEqual(harness.messages, []);
    harness.context.subscriptions.forEach(resource => resource.dispose());
});

test("deactivation during the identity check cannot spawn a late server", async () => {
    let completeIdentity;
    const harness = failureHarness(new MockProcess(), { identity: callback => { completeIdentity = callback; } });
    const activation = harness.activate();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(typeof completeIdentity, "function");
    harness.context.subscriptions.forEach(resource => resource.dispose());
    completeIdentity(null, JSON.stringify({
        schemaVersion: 1, sourceCommit: "1".repeat(40),
        server: { sha256: "2".repeat(64) }, compiler: { sha256: "3".repeat(64) },
        language: { id: "lui", version: "preview", featureLevel: "preview-1" },
        protocol: { id: "lucent-lui", major: 1, minor: 0 }
    }));
    await activation;
    assert.equal(harness.spawnCount, 0);
});

test("multi-root project selection uses the selected workspace host folder", async () => {
    const first = path.resolve("workspace-one");
    const second = path.resolve("workspace-two");
    const folders = [first, second].map((fsPath, index) => ({ name: `Folder ${index + 1}`, uri: { scheme: "file", fsPath } }));
    const vscode = {
        workspace: {
            workspaceFolders: folders,
            getConfiguration: (_section, resource) => ({ get: () => resource?.fsPath === second
                ? "app/App.csproj" : "wrong/Wrong.csproj" })
        },
        window: { showQuickPick: async items => items[1] }
    };
    const extension = loadExtension(vscode, new MockProcess());
    assert.equal((await extension.selectProject()).projectPath, path.join(second, "app", "App.csproj"));
});

test("selected workspace project excludes document content from another root", async () => {
    const first = { name: "First", uri: { scheme: "file", fsPath: path.resolve("first"), toString: () => "file:///first" } };
    const second = { name: "Second", uri: { scheme: "file", fsPath: path.resolve("second"), toString: () => "file:///second" } };
    const server = new MockProcess();
    const harness = failureHarness(server, { folders: [first, second], selectedFolder: 1 });
    harness.workspace.getConfiguration = (_section, resource) => ({ get: key => key === "projectPath"
        ? resource === second.uri ? "host/Host.csproj" : "wrong/Wrong.csproj"
        : path.resolve("server.dll") });
    harness.workspace.getWorkspaceFolder = uri => uri.toString().startsWith("file:///second/") ? second : first;
    await harness.activate();
    const document = (uri, text) => ({ uri: { toString: () => uri }, languageId: "lui", version: 1, getText: () => text });
    harness.open(document("file:///first/Private.lui", "private content"));
    harness.open(document("file:///second/Public.lui", "selected content"));
    assert.equal((await harness.symbols().provideDocumentSymbols(document("file:///first/Private.lui", "private content"))).length, 0);
    const opens = server.notifications.filter(notification => notification.method === "textDocument/didOpen");
    assert.deepEqual(opens.map(message => message.params.textDocument.text), ["selected content"]);
    harness.context.subscriptions.forEach(resource => resource.dispose());
    server.emit("exit", 0, null);
});

test("lint actions resolve current edits and discard previously resolved or cancelled edits", async () => {
    const process = new MockProcess();
    const uri = "file:///Example.lui";
    const range = { start: { line: 0, character: 0 }, end: { line: 0, character: 10 } };
    const action = { title: "Use default content (LUI5003)", kind: "quickfix", data: { uri, version: 2 } };
    process.responses = {
        "textDocument/codeAction": [action],
        "codeAction/resolve": { ...action, edit: { documentChanges: [{ textDocument: { uri, version: 2 }, edits: [{ range, newText: "<Text>Hello</Text>" }] }] } }
    };
    const harness = failureHarness(process);
    await harness.activate();
    const document = { uri: { toString: () => uri }, version: 2 };
    harness.workspace.textDocuments.push(document);
    const provider = harness.codeActions();
    const [offered] = await provider.provideCodeActions(document, range, {});
    assert.equal(offered.edit, undefined);
    assert.equal(process.lastRequest.params.textDocument.version, 2);
    await provider.resolveCodeAction(offered);
    assert.equal(offered.edit.changes[0].text, "<Text>Hello</Text>");
    document.version = 3;
    await provider.resolveCodeAction(offered);
    assert.equal(offered.edit, undefined);
    assert.match(offered.disabled.reason, /document changed/);

    document.version = 2;
    process.holdRequests = new Set(["codeAction/resolve"]);
    const cancelled = { isCancellationRequested: false };
    const pending = provider.resolveCodeAction(offered, cancelled);
    cancelled.isCancellationRequested = true;
    process.send({ jsonrpc: "2.0", id: process.lastRequest.id, result: process.responses["codeAction/resolve"] });
    await pending;
    assert.equal(offered.edit, undefined);
    assert.ok(offered.disabled);
    harness.context.subscriptions.forEach(resource => resource.dispose());
});

test("missing dotnet rejects activation, reports setup guidance and disposes watchers", async () => {
    const process = new MockProcess();
    process.pid = undefined;
    process.holdRequests = new Set(["initialize"]);
    const harness = failureHarness(process);
    const activation = harness.activate();
    await harness.started;
    process.emit("error", new Error("spawn dotnet ENOENT"));
    await assert.rejects(activation, /ENOENT/);
    assert.equal(harness.messages.length, 1);
    assert.match(harness.messages[0], /dotnet.*serverPath.*ENOENT/);
    assert.ok(harness.resources.length > 1);
    assert.ok(harness.resources.every(resource => resource.disposed === 1));
    harness.context.subscriptions.forEach(resource => resource.dispose());
    assert.ok(harness.resources.every(resource => resource.disposed === 1));
    assert.equal(process.lastRequest.method, "initialize");
});

test("server startup exit is reported separately from process launch failure", async () => {
    const process = new MockProcess();
    process.holdRequests = new Set(["initialize"]);
    const harness = failureHarness(process);
    const activation = harness.activate();
    await harness.started;
    process.exitCode = 1;
    process.emit("exit", 1, null);
    await assert.rejects(activation, /exited.*code 1/);
    assert.equal(harness.messages.length, 1);
    assert.match(harness.messages[0], /serverPath.*exited/);
    assert.ok(harness.resources.every(resource => resource.disposed === 1));
});

test("runtime pipe failure rejects pending and later RPCs and releases registrations", async () => {
    const process = new MockProcess();
    const harness = failureHarness(process);
    await harness.activate();
    process.holdRequests = new Set(["lucent/generatedText"]);
    const first = harness.request();
    const second = harness.request();
    process.stdin.emit("error", new Error("broken pipe"));
    const settled = await Promise.allSettled([first, second]);
    assert.ok(settled.every(result => result.status === "rejected" && /broken pipe/.test(result.reason.message)));
    await assert.rejects(harness.request(), /broken pipe/);
    assert.equal(harness.messages.length, 1);
    assert.ok(harness.resources.every(resource => resource.disposed === 1));
    process.emit("error", new Error("later process failure"));
    assert.equal(harness.messages.length, 1);
});

test("failure immediately after initialize cannot register disposed providers", async () => {
    const process = new MockProcess();
    const harness = failureHarness(process);
    const activation = harness.activate();
    await harness.started;
    process.emit("error", new Error("early runtime failure"));
    await activation;
    assert.equal(harness.messages.length, 1);
    assert.ok(harness.resources.every(resource => resource.disposed === 1));
});

test("normal disposal releases registrations once without reporting a server failure", async () => {
    const process = new MockProcess();
    const harness = failureHarness(process);
    await harness.activate();
    harness.context.subscriptions.forEach(resource => resource.dispose());
    harness.context.subscriptions.forEach(resource => resource.dispose());
    process.kill();
    await Promise.resolve();
    assert.equal(harness.messages.length, 0);
    assert.ok(harness.resources.every(resource => resource.disposed === 1));
});


test("output channel records request timing and server stderr without document contents", async () => {
    const process = new MockProcess();
    const harness = failureHarness(process);
    await harness.activate();
    process.stderr.emit("data", Buffer.from("example server diagnostic"));
    await harness.request();
    assert.ok(harness.logs.some(text => /initialize #1 completed in \d+ ms/.test(text)));
    assert.ok(harness.logs.includes("example server diagnostic"));
    assert.ok(harness.logs.some(text => text.includes(path.resolve("workspace", "host/Host.csproj"))));
    assert.ok(harness.logs.some(text => text.includes("lucent/generatedText")));
    assert.ok(!harness.logs.some(text => text.includes("lucent-lui:test")));
    harness.context.subscriptions.forEach(resource => resource.dispose());
});


test("completion defers documentation and preserves insertion fields on resolve", async () => {
    const process = new MockProcess();
    const item = { label: "DividerRight", kind: 5, data: { uri: "file:///Widget.lui", epoch: 1, offset: 10 } };
    process.responses = {
        "textDocument/completion": { items: [item] },
        "completionItem/resolve": { ...item, detail: "Token<Border>", documentation: { value: "Right border" } }
    };
    const harness = failureHarness(process);
    await harness.activate();
    const [completion] = await harness.completion().provideCompletionItems({ uri: { toString: () => "file:///Widget.lui" } }, { line: 0, character: 10 });
    assert.equal(completion.documentation, undefined);
    assert.equal(process.lastRequest.method, "textDocument/completion");
    completion.insertText = "DividerRight";
    const resolved = await harness.completion().resolveCompletionItem(completion);
    assert.equal(resolved, completion);
    assert.equal(resolved.documentation.value, "Right border");
    assert.equal(resolved.insertText, "DividerRight");
    assert.equal(process.lastRequest.params.kind, 5);
    assert.deepEqual(process.lastRequest.params.data, item.data);
    harness.context.subscriptions.forEach(resource => resource.dispose());
});
