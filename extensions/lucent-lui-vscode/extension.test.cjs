const assert = require("node:assert/strict");
const { EventEmitter } = require("node:events");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");
const crypto = require("node:crypto");
const os = require("node:os");
const { readProjectRequirements, validateRequirements, assertCompatibleCompiler, verifyRequirementInputs, trustedProjectReport } = require("./project-requirements");

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
    assert.deepEqual(manifest.contributes.breakpoints, [{ language: "lui" }]);
    assert.equal(manifest.contributes.configuration.properties["lucentLui.projectPath"].scope, "resource");
    assert.deepEqual(manifest.extensionKind, ["workspace"]);
    assert.equal(manifest.capabilities.untrustedWorkspaces.supported, "limited");
    assert.deepEqual(manifest.capabilities.untrustedWorkspaces.restrictedConfigurations,
        ["lucentLui.serverPath", "lucentLui.projectPath"]);
    assert.deepEqual(JSON.parse(JSON.stringify(loaded.exports.crossLanguageSelector)), [
        { language: "lui" }, { language: "csharp", scheme: "file" }
    ]);
});

test("Check Environment cold activation stays static until a Lucent editor opens", async () => {
    let environmentChecks = 0;
    let requirementReads = 0;
    const server = new MockProcess();
    const harness = failureHarness(server, { openLui: false, dependencies: {
        readRequirements: async (...args) => {
            requirementReads++;
            return { compiler: { sha256: "3".repeat(64), sourceCommit: null }, projectPath: args[1], inputs: [] };
        },
        environmentUi: { createEnvironmentCommands(vscode, context) {
            context.subscriptions.push(vscode.commands.registerCommand("lucentLui.checkEnvironment", async () => {
                environmentChecks++;
            }));
            return { cancel() {} };
        } }
    } });

    await harness.activate();
    await harness.command("lucentLui.checkEnvironment");
    assert.equal(environmentChecks, 1);
    assert.equal(requirementReads, 0);
    assert.equal(harness.identityChecks, 0);
    assert.equal(harness.spawnCount, 0);
    assert.equal(server.lastRequest, undefined);

    harness.open({ uri: { toString: () => "file:///workspace/Main.lui" }, languageId: "lui", version: 1, getText: () => "" });
    await harness.started;
    assert.equal(requirementReads, 1);
    assert.equal(harness.spawnCount, 1);
    assert.equal(server.lastRequest.method, "initialize");
    harness.context.subscriptions.forEach(resource => resource.dispose());
    server.emit("exit", 0, null);
});

function trustedCommandBoundary(controller = new AbortController()) {
    return { createEnvironmentCommands(vscode, context, _manifest, check) {
        let ticket = 0;
        context.subscriptions.push(vscode.commands.registerCommand("lucentLui.checkTrustedProject", () => {
            const captured = ticket;
            return check(controller.signal, () => captured === ticket && !controller.signal.aborted);
        }));
        return { cancel() { ticket++; } };
    } };
}

test("cold explicit project check evaluates using the absolute host without starting or acquiring tools", async () => {
    let reads = 0;
    const server = new MockProcess();
    const host = path.resolve("host/dotnet.exe");
    const harness = failureHarness(server, { openLui: false, dependencies: {
        environmentUi: trustedCommandBoundary(),
        readRequirements: async (_server, projectPath, _signal, _execute, dotnetPath) => {
            reads++;
            assert.equal(dotnetPath, host);
            return { projectPath, compiler: { sha256: "3".repeat(64), sourceCommit: null }, state: "development-source", inputs: [] };
        },
        acquisition: { acquireApprovedRelease: () => assert.fail("doctor must not acquire tools") },
        cache: { importApprovedArchive: () => assert.fail("doctor must not install tools") }
    } });
    await harness.activate();
    const report = await harness.command("lucentLui.checkTrustedProject");
    assert.equal(report.evaluation, "evaluated");
    assert.equal(report.target.status, "notChecked");
    assert.equal(reads, 1);
    assert.equal(harness.identityChecks, 1);
    assert.equal(harness.spawnCount, 0);
    assert.equal(harness.watchers.length, 0);
    assert.equal(server.lastRequest, undefined);
    harness.context.subscriptions.forEach(resource => resource.dispose());
});

test("active project check reuses fresh evidence but rejects changed inputs without restarting services", async () => {
    let reads = 0;
    let changed = false;
    const server = new MockProcess();
    const harness = failureHarness(server, { dependencies: {
        environmentUi: trustedCommandBoundary(),
        readRequirements: async (_server, projectPath) => {
            reads++;
            return { projectPath, compiler: { sha256: "3".repeat(64), sourceCommit: null }, state: "development-source", inputs: [] };
        },
        verifyInputs: async () => { if (changed) throw new Error("SECRET-input-path"); }
    } });
    await harness.activate();
    const report = await harness.command("lucentLui.checkTrustedProject");
    assert.equal(report.evaluation, "reused");
    assert.equal(reads, 1);
    assert.equal(harness.identityChecks, 2);
    changed = true;
    await assert.rejects(harness.command("lucentLui.checkTrustedProject"), { doctorReason: "inputs-changed" });
    assert.equal(reads, 1);
    assert.equal(harness.spawnCount, 1);
    assert.equal(server.notifications.some(item => item.method === "exit"), false);
    assert.equal(harness.resources.some(resource => resource.disposed), false);
    harness.context.subscriptions.forEach(resource => resource.dispose());
    server.emit("exit", 0, null);
});

test("canceling an active project check leaves language services running and rejects its late result", async () => {
    const controller = new AbortController();
    let finish;
    let checking = false;
    const server = new MockProcess();
    const harness = failureHarness(server, { dependencies: {
        environmentUi: trustedCommandBoundary(controller),
        verifyInputs: () => checking ? new Promise(resolve => { finish = resolve; }) : Promise.resolve()
    } });
    await harness.activate();
    checking = true;
    const check = harness.command("lucentLui.checkTrustedProject");
    while (!finish) await new Promise(resolve => setImmediate(resolve));
    controller.abort();
    finish();
    assert.equal(await check, undefined);
    assert.equal(harness.spawnCount, 1);
    assert.equal(server.notifications.some(item => item.method === "exit"), false);
    assert.equal(harness.resources.some(resource => resource.disposed), false);
    harness.context.subscriptions.forEach(resource => resource.dispose());
    server.emit("exit", 0, null);
});

test("folder additions invalidate a pending explicit project check", async () => {
    let finish;
    const harness = failureHarness(new MockProcess(), { openLui: false, dependencies: {
        environmentUi: trustedCommandBoundary(),
        readRequirements: (_server, projectPath) => new Promise(resolve => { finish = () => resolve({
            projectPath, compiler: { sha256: "3".repeat(64), sourceCommit: null }, state: "development-source", inputs: []
        }); })
    } });
    await harness.activate();
    const check = harness.command("lucentLui.checkTrustedProject");
    while (!finish) await new Promise(resolve => setImmediate(resolve));
    harness.foldersChanged({ added: [{ uri: { scheme: "file", fsPath: path.resolve("another") } }], removed: [] });
    finish();
    assert.equal(await check, undefined);
    assert.equal(harness.spawnCount, 0);
    harness.context.subscriptions.forEach(resource => resource.dispose());
});

test("a rejected explicit check is discarded when another root's selected project changes", async () => {
    const folders = ["active", "checked"].map(name => ({
        name, uri: { scheme: "file", fsPath: path.resolve("workspace", name) }
    }));
    let selected = 0;
    let checkedProject = "host/Host.csproj";
    let rejectProbe;
    const server = new MockProcess();
    const harness = failureHarness(server, { folders, dependencies: {
        environmentUi: trustedCommandBoundary(),
        window: { showQuickPick: async items => items[selected] },
        readRequirements: (_server, projectPath) => selected === 0
            ? Promise.resolve({ projectPath, compiler: { sha256: "3".repeat(64), sourceCommit: null }, inputs: [] })
            : new Promise((_resolve, reject) => { rejectProbe = reject; })
    } });
    harness.workspace.getConfiguration = (_section, uri) => ({ get: key => key === "projectPath"
        ? uri?.fsPath === folders[1].uri.fsPath ? checkedProject : "host/Host.csproj"
        : path.resolve("server.dll") });
    await harness.activate();
    try {
        selected = 1;
        const check = harness.command("lucentLui.checkTrustedProject");
        while (!rejectProbe) await new Promise(resolve => setImmediate(resolve));
        checkedProject = "replacement/Replacement.csproj";
        harness.configurationChanged("lucentLui.projectPath", folders[1]);
        rejectProbe(new Error("A stale project failure must not become a report."));
        assert.equal(await check, undefined);
        assert.equal(harness.spawnCount, 1);
        assert.equal(server.notifications.some(item => item.method === "exit"), false);
        assert.deepEqual(harness.messages, []);
    } finally {
        harness.context.subscriptions.forEach(resource => resource.dispose());
        server.emit("exit", 0, null);
    }
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

function requirementFixture(t) {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), "lucent-requirements-"));
    t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
    const projectPath = path.join(directory, "App.csproj");
    const project = Buffer.from('<Project Sdk="Microsoft.NET.Sdk" />');
    fs.writeFileSync(projectPath, project);
    return {
        schemaVersion: 1, kind: "project-requirements", state: "development-source", semanticReady: false,
        projectPath, compiler: { sha256: "a".repeat(64), informationalVersion: "0.3.0+" + "b".repeat(40), sourceCommit: "b".repeat(40) },
        projects: [{ projectPath, state: "development-source", sdk: null }], packages: [],
        inputs: [{ path: projectPath, sha256: crypto.createHash("sha256").update(project).digest("hex") }]
    };
}

test("requirements reject malformed provenance and never infer semantic readiness", t => {
    const good = requirementFixture(t);
    assert.equal(validateRequirements(good, good.projectPath), good);
    const packaged = structuredClone(good);
    packaged.state = "package";
    packaged.projects[0].state = "package";
    packaged.projects[0].sdk = { id: "Lucent.Lui.Sdk", version: "0.3.0-dev.1.1",
        repositoryCommit: good.compiler.sourceCommit, packageSha256: "d".repeat(64) };
    assert.equal(validateRequirements(packaged, good.projectPath), packaged);
    packaged.projects[0].sdk.repositoryCommit = "e".repeat(40);
    assert.throws(() => validateRequirements(packaged, good.projectPath), /SDK package identity/);
    for (const mutate of [
        value => { value.semanticReady = true; },
        value => { value.state = "package"; },
        value => { value.inputs = []; },
        value => { value.inputs.push({ ...value.inputs[0] }); },
        value => { value.projectPath = path.join(path.dirname(value.projectPath), "Other.csproj"); },
        value => { value.compiler.sourceCommit = "unknown"; }
    ]) {
        const invalid = structuredClone(good);
        mutate(invalid);
        assert.throws(() => validateRequirements(invalid, good.projectPath));
    }
    assertCompatibleCompiler(good, { compiler: good.compiler, sourceCommit: good.compiler.sourceCommit });
    assert.throws(() => assertCompatibleCompiler(good, { compiler: { ...good.compiler, sha256: "c".repeat(64) }, sourceCommit: good.compiler.sourceCommit }), /compiler differs/);
    assert.throws(() => assertCompatibleCompiler(good, { compiler: good.compiler, sourceCommit: "c".repeat(40) }), /compiler differs/);
});

test("target identity accepts old producers and unset RID but rejects present malformed tokens", t => {
    const value = requirementFixture(t);
    assert.equal(validateRequirements(value, value.projectPath), value);
    for (const target of [{ framework: "net10.0-windows", runtimeIdentifier: null }, { framework: "net10.0", runtimeIdentifier: "win-x64" }]) {
        value.projects[0].target = target;
        assert.equal(validateRequirements(value, value.projectPath), value);
    }
    for (const target of [null, {}, { framework: "net10.0" }, { framework: "n".repeat(129), runtimeIdentifier: null },
        { framework: " net10.0", runtimeIdentifier: null }, { framework: "net10.0", runtimeIdentifier: "../secret" },
        { framework: 42, runtimeIdentifier: null }, { framework: "net10.0", runtimeIdentifier: "" }]) {
        value.projects[0].target = target;
        assert.throws(() => validateRequirements(value, value.projectPath), /target identity/);
    }
});

test("trusted report allowlist excludes producer paths versions inputs and seeded errors", t => {
    const value = requirementFixture(t);
    value.compiler.informationalVersion = "SECRET-version-token";
    value.packages.push({ path: "SECRET-package-path", version: "SECRET-package-version" });
    value.error = { message: "SECRET-error" };
    const identity = { sourceCommit: "b".repeat(40), server: { sha256: "c".repeat(64) }, compiler: value.compiler,
        protocol: { id: "lucent-lui", major: 1, minor: 0 }, language: { id: "lui", version: "preview", featureLevel: "preview-1" },
        runtimeOptions: "SECRET-options" };
    const evidence = { requirements: value, identity, configured: "SECRET-server-path" };
    const report = trustedProjectReport(evidence, "evaluated");
    assert.equal(report.target.status, "notChecked");
    assert.equal(report.releaseAuthentication, "notChecked");
    assert.equal(report.managedBuildReadiness, "notChecked");
    assert.equal(JSON.stringify(report).includes("SECRET"), false);
    assert.equal(JSON.stringify(report).includes(value.projectPath), false);
    value.projects[0].target = { framework: "net10.0-windows", runtimeIdentifier: null };
    assert.deepEqual(trustedProjectReport(evidence, "reused").target,
        { status: "observed", framework: "net10.0-windows", runtimeIdentifier: null });
});

test("requirements transport executes the preflight's absolute host", async t => {
    const value = requirementFixture(t);
    const host = path.resolve("verified-host/dotnet.exe");
    await readProjectRequirements("server.dll", value.projectPath, undefined,
        (command, _args, _options, callback) => { assert.equal(command, host); callback(null, JSON.stringify(value)); }, host);
});

test("requirement input proof detects same-length edits and newly created control files", async t => {
    const value = requirementFixture(t);
    const optional = path.join(path.dirname(value.projectPath), "packages.lock.json");
    value.inputs.push({ path: optional, sha256: null });
    await verifyRequirementInputs(value);
    const original = fs.readFileSync(value.projectPath);
    fs.writeFileSync(value.projectPath, Buffer.alloc(original.length, "x"));
    await assert.rejects(verifyRequirementInputs(value), /inputs changed/);
    fs.writeFileSync(value.projectPath, original);
    fs.writeFileSync(optional, "{}");
    await assert.rejects(verifyRequirementInputs(value), /inputs changed/);
    fs.unlinkSync(optional);
    fs.unlinkSync(value.projectPath);
    await assert.rejects(verifyRequirementInputs(value), /inputs changed/);
});

test("prerequisite transport is bounded, cancellable and rejects nonzero success payloads", async t => {
    const value = requirementFixture(t);
    const controller = new AbortController();
    const execute = (command, args, options, callback) => {
        assert.equal(command, "dotnet");
        assert.deepEqual(args, ["server.dll", "--project-requirements", "--trusted-project", value.projectPath]);
        assert.equal(options.cwd, path.dirname(value.projectPath));
        assert.equal(options.env.LUCENT_REQUIREMENTS_CANCEL_STDIN, "1");
        assert.equal(options.windowsHide, true);
        assert.equal(options.signal, undefined);
        assert.ok(options.maxBuffer <= 2 * 1024 * 1024);
        callback(null, JSON.stringify(value));
    };
    assert.deepEqual(await readProjectRequirements("server.dll", value.projectPath, controller.signal, execute), value);
    await assert.rejects(readProjectRequirements("server.dll", value.projectPath, undefined,
        (_command, _args, _options, callback) => callback(new Error("exit 3"), JSON.stringify(value))), /did not succeed/);
    await assert.rejects(readProjectRequirements("server.dll", value.projectPath, undefined,
        (_command, _args, _options, callback) => callback(new Error("exit 3"), JSON.stringify({
            schemaVersion: 1, kind: "project-requirements", state: "unavailable",
            error: { code: "stale-restore", message: "Central package settings differ from the restored graph." }
        }))), /stale-restore/);
    controller.abort();
    await assert.rejects(readProjectRequirements("server.dll", value.projectPath, controller.signal, execute), { name: "AbortError" });
    const active = new AbortController();
    let cancellation;
    const pending = readProjectRequirements("server.dll", value.projectPath, active.signal,
        (_command, _args, _options, callback) => ({
            stdin: Object.assign(new EventEmitter(), { end: line => {
                cancellation = line;
                callback(new Error("canceled"), "");
            } }),
            kill: () => assert.fail("cooperatively canceled probe must not be force-killed")
        }));
    active.abort();
    await assert.rejects(pending, { name: "AbortError" });
    assert.equal(cancellation, "cancel\n");
});

function loadExtension(vscode, process, spawned, identityOutput = {
    schemaVersion: 1,
    sourceCommit: "1".repeat(40),
    server: { sha256: "2".repeat(64) },
    compiler: { sha256: "3".repeat(64) },
    language: { id: "lui", version: "preview", featureLevel: "preview-1" },
    protocol: { id: "lucent-lui", major: 1, minor: 0 }
}, identified, dependencies = {}) {
    vscode.StatusBarAlignment ??= { Right: 2 };
    vscode.DiagnosticSeverity ??= { Error: 0 };
    vscode.Diagnostic ??= class { constructor(range, message, severity) { Object.assign(this, { range, message, severity }); } };
    vscode.window.createStatusBarItem ??= () => ({ show() {}, dispose() {} });
    if (vscode.languages) {
        const createDiagnostics = vscode.languages.createDiagnosticCollection;
        vscode.languages.createDiagnosticCollection = (...args) => Object.assign({ clear() {}, set() {} }, createDiagnostics(...args));
    }
    const module = { exports: {} };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, "extension.js"), "utf8"), {
        AbortController,
        Buffer,
        clearTimeout,
        console,
        JSON,
        exports: module.exports,
        module,
        require: name => name === "./package.json" && dependencies.packageRelease
            ? { lucentRelease: dependencies.packageRelease, lucentCacheHelper: dependencies.helperManifest }
            : name === "./release-catalog.json" && dependencies.releaseCatalog ? dependencies.releaseCatalog
            : name === "./server-cache" && dependencies.cache ? { ...require("./server-cache"), ...dependencies.cache }
            : name === "./server-acquisition" && dependencies.acquisition ? { ...require("./server-acquisition"), ...dependencies.acquisition }
            : name === "./doctor-client" ? { preflightDotnet: dependencies.preflight ?? (async () => ({ status: "available", dotnetPath: path.resolve("host/dotnet.exe") })) }
            : name === "./environment-ui" && dependencies.environmentUi ? dependencies.environmentUi
            : name === "./project-requirements" ? {
                ...require("./project-requirements"),
                readProjectRequirements: dependencies.readRequirements ?? (async (_server, projectPath) => ({
                    compiler: { sha256: "3".repeat(64), sourceCommit: null },
                    projectPath, state: "development-source", inputs: []
                })),
                verifyRequirementInputs: dependencies.verifyInputs ?? (async () => {})
            }
            : name === "./server-bundle" && dependencies.bundleVerifier
                ? { verifyBundledServer: dependencies.bundleVerifier }
                : name === "vscode" ? vscode : name === "child_process" ? {
            execFile: (_command, _args, _options, callback) => {
                if (_command === "where.exe") { callback(null, path.resolve("dotnet.exe")); return; }
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

function failureHarness(process, { trusted = true, identity, processes = [process], folders, selectedFolder = 0,
    openLui = true, serverPath = path.resolve("server.dll"), dependencies = {} } = {}) {
    let completionProvider;
    let codeActionsProvider;
    let symbolProvider;
    const logs = [];
    const resources = [];
    const setupResources = [];
    const messages = [];
    const watchers = [];
    const statusItem = { show() {}, dispose() {} };
    let generated;
    let onGrantTrust;
    let onFoldersChanged;
    let onConfigurationChanged;
    let spawnCount = 0;
    let identityChecks = 0;
    const commands = new Map();
    const workspaceFolders = folders ?? [{ uri: { scheme: "file", fsPath: path.resolve("workspace") } }];
    const initialFolder = workspaceFolders[selectedFolder] ?? workspaceFolders[0];
    const openListeners = [];
    const textDocuments = openLui && initialFolder ? [{
        uri: { scheme: "file", fsPath: path.join(initialFolder.uri.fsPath, "Initial.lui"),
            toString() { return `file:///${this.fsPath.replaceAll("\\", "/")}`; } },
        version: 1,
        languageId: "lui",
        getText: () => ""
    }] : [];
    const own = scope => {
        const resource = { disposed: 0, dispose() { this.disposed++; } };
        (scope === "setup" ? setupResources : resources).push(resource);
        return resource;
    };
    const workspace = {
        isTrusted: trusted,
        onDidGrantWorkspaceTrust: callback => { onGrantTrust = callback; return own(); },
        onDidChangeWorkspaceFolders: callback => { onFoldersChanged = callback; return { dispose() {} }; },
        onDidChangeConfiguration: callback => { onConfigurationChanged = callback; return { dispose() {} }; },
        workspaceFolders,
            getConfiguration: () => ({ get: key => key === "projectPath" ? "host/Host.csproj" : serverPath }),
        textDocuments,
        createFileSystemWatcher: pattern => {
            const watcher = Object.assign(own(), { pattern,
                onDidCreate(callback) { this.create = callback; return own(); },
                onDidChange(callback) { this.change = callback; return own(); },
                onDidDelete(callback) { this.delete = callback; return own(); }
            });
            watchers.push(watcher);
            return watcher;
        },
        onDidOpenTextDocument: callback => {
            openListeners.push(callback);
            const resource = own(openListeners.length === 1 ? "setup" : undefined);
            return { dispose() {
                resource.dispose();
                const index = openListeners.indexOf(callback);
                if (index >= 0) openListeners.splice(index, 1);
            } };
        },
        onDidChangeTextDocument: own,
        onDidCloseTextDocument: own,
        registerTextDocumentContentProvider: (_scheme, provider) => { generated = provider; return own(); }
    };
    const vscode = {
        workspace,
        authentication: dependencies.authentication,
        window: { createStatusBarItem: () => statusItem, createOutputChannel: () => {
            const channel = own();
            const write = text => {
                if (channel.disposed) throw new Error("A disposed output channel was used.");
                logs.push(text);
            };
            return Object.assign(channel, { info: write, warn: write, error: write });
        }, showErrorMessage: message => messages.push(message), showQuickPick: async items => items[selectedFolder], ...dependencies.window },
        ProgressLocation: { Notification: 15 },
        ConfigurationTarget: { WorkspaceFolder: 3 },
        Uri: { file: value => ({ toString: () => value }), parse: value => ({ scheme: value.startsWith("file:") ? "file" : undefined, toString: () => value }) },
        Range: class { constructor(...values) { this.values = values; } },
        WorkspaceEdit: class { constructor() { this.changes = []; } replace(uri, range, text) { this.changes.push({ uri, range, text }); } },
        CodeAction: class { constructor(title, kind) { this.title = title; this.kind = kind; } },
        CodeActionKind: { QuickFix: "quickfix" },
        RelativePattern: class { constructor(base, pattern) { this.base = base; this.pattern = pattern; } },
        SemanticTokensLegend: class {},
        commands: { registerCommand: (name, command) => { commands.set(name, command); return { dispose() {} }; }, executeCommand: name => commands.get(name)() },
        CompletionItem: class { constructor(label, kind) { this.label = label; this.kind = kind; } },
        CompletionItemKind: { Field: "field" },
        MarkdownString: class { appendText(text) { this.value = text; } },
        languages: new Proxy({}, { get: (_target, key) => key === "createDiagnosticCollection"
            ? name => Object.assign(own(name === "lucent-setup" ? "setup" : undefined), { delete() {}, clear() {}, set() {} }) : key === "registerCompletionItemProvider"
                ? (_selector, provider) => { completionProvider = provider; return own(); } : key === "registerCodeActionsProvider"
                    ? (_selector, provider) => { codeActionsProvider = provider; return own(); } : key === "registerDocumentSymbolProvider"
                        ? (_selector, provider) => { symbolProvider = provider; return own(); } : own })
    };
    const context = { subscriptions: [], asAbsolutePath: relative => path.resolve("extension-host", relative), globalStorageUri: { scheme: "file", fsPath: path.resolve("extension-storage") } };
    let started;
    const startedPromise = new Promise(resolve => { started = resolve; });
    let nextProcess = 0;
    const extension = loadExtension(vscode, () => processes[Math.min(nextProcess++, processes.length - 1)], () => { spawnCount++; started(); }, identity, () => { identityChecks++; }, dependencies);
    return { context, messages, resources, logs, watchers, workspace, started: startedPromise, get spawnCount() { return spawnCount; }, get identityChecks() { return identityChecks; }, grantTrust: () => { workspace.isTrusted = true; onGrantTrust(); }, restart: () => commands.get("lucentLui.restartLanguageServices")(), importArchive: () => commands.get("lucentLui.importServerArchive")(), open: document => {
        if (!workspace.textDocuments.some(openDocument => openDocument.uri.toString() === document.uri.toString()))
            workspace.textDocuments.push(document);
        for (const listener of openListeners) listener(document);
    }, codeActions: () => codeActionsProvider, completion: () => completionProvider, symbols: () => symbolProvider, activate: () => extension.activate(context),
        installMatching: () => commands.get("lucentLui.installMatchingServer")(),
        command: name => commands.get(name)(),
        foldersChanged: event => onFoldersChanged(event),
        configurationChanged: (name, folder) => onConfigurationChanged({ affectsConfiguration: (setting, uri) =>
            setting === name && (!folder || uri?.fsPath === folder.uri.fsPath) }),
        status: () => statusItem,
        request: () => generated.provideTextDocumentContent({ toString: () => "lucent-lui:test" }) };
}

test("Restricted Mode leaves project evaluation and server process untouched until trust is granted", async () => {
    const server = new MockProcess();
    const harness = failureHarness(server, { trusted: false });
    await harness.activate();
    assert.equal(harness.spawnCount, 0);
    assert.equal(server.lastRequest, undefined);
    assert.equal(harness.resources.length, 1); // Trust listener only; setup UI has its own lifetime.
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

test("project compiler mismatch and stale restore never spawn semantic services", async () => {
    for (const readRequirements of [
        async () => ({ compiler: { sha256: "9".repeat(64), sourceCommit: null }, inputs: [] }),
        async () => { throw new Error("stale-restore: restore the imported package requirements"); }
    ]) {
        const harness = failureHarness(new MockProcess(), { dependencies: { readRequirements } });
        await harness.activate();
        assert.equal(harness.spawnCount, 0);
        assert.match(harness.messages[0], /compiler differs|stale-restore/);
        harness.context.subscriptions.forEach(resource => resource.dispose());
    }
});

test("restart cancels pending project evaluation and ignores its late result", async () => {
    let complete;
    let oldSignal;
    let evaluations = 0;
    const harness = failureHarness(new MockProcess(), { dependencies: {
        readRequirements: (_server, _project, signal) => {
            if (evaluations++ === 0) {
                oldSignal = signal;
                return new Promise(resolve => { complete = resolve; });
            }
            return Promise.resolve({ compiler: { sha256: "3".repeat(64), sourceCommit: null }, inputs: [] });
        }
    } });
    const first = harness.activate();
    while (!complete) await new Promise(resolve => setImmediate(resolve));
    await harness.restart();
    assert.equal(oldSignal.aborted, true);
    assert.equal(harness.spawnCount, 1);
    complete({ compiler: { sha256: "3".repeat(64), sourceCommit: null }, inputs: [] });
    await first;
    assert.equal(harness.spawnCount, 1);
    assert.deepEqual(harness.messages, []);
    harness.context.subscriptions.forEach(resource => resource.dispose());
});

test("changed project input between evaluation and startup prevents initialization", async () => {
    const harness = failureHarness(new MockProcess(), { dependencies: {
        verifyInputs: async () => { throw new Error("project inputs changed during tooling selection"); }
    } });
    await harness.activate();
    assert.equal(harness.spawnCount, 0);
    assert.match(harness.messages[0], /inputs changed/);
    harness.context.subscriptions.forEach(resource => resource.dispose());
});

test("evaluated tooling input changes stop services while unrelated sibling files do not", async () => {
    const input = path.resolve("workspace", "Directory.Packages.props");
    const server = new MockProcess();
    const harness = failureHarness(server, { dependencies: {
        readRequirements: async () => ({ compiler: { sha256: "3".repeat(64), sourceCommit: null },
            inputs: [{ path: input, sha256: "1".repeat(64) }] })
    } });
    await harness.activate();
    const watcher = harness.watchers.find(item => item.pattern.base === path.dirname(input) && item.pattern.pattern === "*");
    assert.ok(watcher);
    watcher.change({ fsPath: path.join(path.dirname(input), "Unrelated.md") });
    assert.deepEqual(harness.messages, []);
    assert.equal(watcher.disposed, 0);
    watcher.change({ fsPath: input });
    assert.equal(watcher.disposed, 1);
    assert.match(harness.messages[0], /tooling inputs changed/);
    assert.equal(server.lastRequest.method, "shutdown");
    watcher.change({ fsPath: input });
    assert.equal(harness.messages.length, 1);
    harness.open({ uri: { toString: () => "file:///workspace/AfterStop.lui" }, languageId: "lui", version: 1, getText: () => "" });
    assert.equal(harness.spawnCount, 1);
    assert.equal(server.lastRequest.method, "shutdown");
    server.emit("exit", 0, null);
    harness.context.subscriptions.forEach(resource => resource.dispose());
});

test("bundled server verifies workspace-host bytes and exact identity before initialization", async () => {
    const identity = {
        schemaVersion: 1, sourceCommit: "1".repeat(40),
        server: { sha256: "2".repeat(64) }, compiler: { sha256: "3".repeat(64) },
        language: { id: "lui", version: "preview", featureLevel: "preview-1" },
        protocol: { id: "lucent-lui", major: 1, minor: 0 }
    };
    const roots = [];
    const packageRelease = {
        ...require("./package.json").lucentRelease,
        serverDelivery: "bundled", bundledServer: { identity }, sourceCommit: identity.sourceCommit
    };
    const server = new MockProcess();
    const harness = failureHarness(server, {
        identity, serverPath: null,
        dependencies: {
            packageRelease,
            bundleVerifier: root => { roots.push(root); return { serverPath: path.join(root, "Lucent.Lui.LanguageServer.dll"), identity }; }
        }
    });
    await harness.activate();
    assert.equal(harness.spawnCount, 1, JSON.stringify({ messages: harness.messages, roots }));
    assert.deepEqual(roots, Array(3).fill(path.resolve("extension-host/server")));
    assert.ok(server.notifications.some(notification => notification.method === "initialized"));
    harness.context.subscriptions.forEach(resource => resource.dispose());

    let evaluatedMismatch = false;
    const mismatch = failureHarness(new MockProcess(), {
        identity: { ...identity, sourceCommit: "4".repeat(40) }, serverPath: null,
        dependencies: {
            packageRelease,
            readRequirements: async () => { evaluatedMismatch = true; throw new Error("Must not evaluate with a false bundled identity."); },
            bundleVerifier: root => ({ serverPath: path.join(root, "Lucent.Lui.LanguageServer.dll"), identity })
        }
    });
    await mismatch.activate();
    assert.equal(mismatch.spawnCount, 0);
    assert.equal(evaluatedMismatch, false);
    assert.match(mismatch.messages[0], /different identity/);

    let inputScans = 0;
    let bundleChanged = false;
    const changedBundle = failureHarness(new MockProcess(), {
        identity, serverPath: null,
        dependencies: {
            packageRelease,
            verifyInputs: async () => { if (++inputScans === 2) bundleChanged = true; },
            bundleVerifier: root => {
                if (bundleChanged) throw new Error("Bundled server bytes changed during input verification.");
                return { serverPath: path.join(root, "Lucent.Lui.LanguageServer.dll"), identity };
            }
        }
    });
    try {
        await changedBundle.activate();
        assert.equal(inputScans, 2);
        assert.equal(changedBundle.spawnCount, 0);
        assert.match(changedBundle.messages[0], /Bundled server bytes changed/);
        assert.ok(changedBundle.watchers.every(watcher => watcher.disposed === 1));
    } finally {
        changedBundle.context.subscriptions.forEach(resource => resource.dispose());
    }
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

function cacheLifecycleFixture() {
    const identity = {
        schemaVersion: 1, sourceCommit: "1".repeat(40), server: { sha256: "2".repeat(64) },
        compiler: { sha256: "3".repeat(64), informationalVersion: "0.3.0+" + "1".repeat(40) },
        language: { id: "lui", version: "preview", featureLevel: "preview-1" },
        protocol: { id: "lucent-lui", major: 1, minor: 0 }
    };
    const projectPath = path.resolve("workspace/host/Host.csproj");
    const requirement = {
        schemaVersion: 1, kind: "project-requirements", state: "package", semanticReady: false, projectPath,
        compiler: { ...identity.compiler, sourceCommit: identity.sourceCommit }, inputs: [],
        projects: [{ projectPath, state: "package", sdk: { id: "Lucent.Lui.Sdk", version: "0.3.0-dev.101.1", repositoryCommit: identity.sourceCommit, packageSha256: "7".repeat(64) } }]
    };
    const entry = {
        sdk: { id: "Lucent.Lui.Sdk", version: "0.3.0-dev.101.1", packageSha256: "7".repeat(64) },
        releaseVersion: "0.3.0-dev.101.1", server: { identity, artifact: { bytes: 123, sha256: "4".repeat(64) }, filesSha256: "5".repeat(64) },
        anchor: { kind: "bundled-catalog", sourceCommit: identity.sourceCommit, descriptorSha256: "6".repeat(64),
            githubActions: { repository: "RichiCoder1/lucent", workflow: ".github/workflows/tests.yml",
                runId: 123, runAttempt: 1, headSha: identity.sourceCommit,
                artifact: { id: 456, name: "complete-release-123-1", digest: "sha256:" + "8".repeat(64) } } }
    };
    return { identity, requirement, dependencies: {
        packageRelease: { ...require("./package.json").lucentRelease, sourceCommit: identity.sourceCommit, bundledServer: { identity } },
        releaseCatalog: { schemaVersion: 1, releases: [entry] },
        bundleVerifier: root => ({ serverPath: path.join(root, "Lucent.Lui.LanguageServer.dll"), identity }),
        readRequirements: async () => requirement
    } };
}

test("cache selection uses the exact project release and cancelled verification cannot spawn later", async () => {
    const fixture = cacheLifecycleFixture();
    let finish;
    let signal;
    const harness = failureHarness(new MockProcess(), { identity: fixture.identity, serverPath: null, dependencies: {
        ...fixture.dependencies, cache: { resolveCachedServer: options => {
            assert.equal(options.requirement, fixture.requirement);
            assert.equal(options.cacheRoot, path.resolve("extension-storage/tooling"));
            signal = options.signal;
            return new Promise(resolve => { finish = resolve; });
        } }
    } });
    const activating = harness.activate();
    for (let attempt = 0; !finish && attempt < 100; attempt++) await new Promise(resolve => setImmediate(resolve));
    assert.ok(finish, JSON.stringify(harness.messages));
    harness.context.subscriptions.forEach(item => item.dispose());
    assert.equal(signal.aborted, true);
    finish({ status: "selected", serverPath: path.resolve("cache/server.dll"), identity: fixture.identity });
    await activating;
    assert.equal(harness.spawnCount, 0);
    assert.equal(harness.identityChecks, 1);

    const ready = failureHarness(new MockProcess(), { identity: fixture.identity, serverPath: null, dependencies: {
        ...fixture.dependencies, cache: { resolveCachedServer: async () => ({ status: "selected", serverPath: path.resolve("cache/server.dll"), identity: fixture.identity }) }
    } });
    await ready.activate();
    assert.equal(ready.spawnCount, 1, JSON.stringify(ready.messages));
    assert.equal(ready.identityChecks, 2);
    assert.ok(ready.logs.some(line => line.includes("Starting verified cache language server")));
    ready.context.subscriptions.forEach(item => item.dispose());
});

test("explicit offline import preserves the active process and reports failed or cancelled installs", async () => {
    for (const outcome of ["selected", "cache-missing", "cancelled"]) {
        const fixture = cacheLifecycleFixture();
        const server = new MockProcess();
        const notices = [];
        let imported = 0;
        const harness = failureHarness(server, { identity: fixture.identity, dependencies: {
            ...fixture.dependencies,
            window: {
                showOpenDialog: async () => [{ scheme: "file", fsPath: path.resolve("server.zip") }],
                withProgress: async (_options, action) => action({}, { isCancellationRequested: false, onCancellationRequested: () => disposable() }),
                showInformationMessage: async message => { notices.push(message); }
            },
            cache: { importApprovedArchive: async options => {
                imported++;
                assert.equal(options.archivePath, path.resolve("server.zip"));
                return { status: outcome, reason: "fixture rejection" };
            } }
        } });
        await harness.activate();
        assert.equal(harness.spawnCount, 1, JSON.stringify(harness.messages));
        await harness.importArchive();
        assert.equal(imported, 1);
        assert.equal(server.exitCode, null);
        assert.equal(harness.spawnCount, 1);
        assert.equal(notices.length, outcome === "selected" ? 1 : 0);
        assert.equal(harness.messages.length, outcome === "cache-missing" ? 1 : 0);
        harness.context.subscriptions.forEach(item => item.dispose());
    }
});

test("offline import in Restricted Mode does not inspect projects, ask for files or start tools", async () => {
    let inspected = 0;
    const harness = failureHarness(new MockProcess(), { trusted: false, dependencies: {
        readRequirements: async () => { inspected++; },
        window: { showOpenDialog: async () => { throw new Error("must not request files"); } }
    } });
    await harness.activate();
    await harness.importArchive();
    assert.equal(inspected, 0);
    assert.equal(harness.identityChecks, 0);
    assert.equal(harness.spawnCount, 0);
    assert.match(harness.messages[0], /Trust this workspace before importing/);
    harness.context.subscriptions.forEach(item => item.dispose());
});

test("online install requires trust and an approved project before requesting authentication", async () => {
    for (const trusted of [false, true]) {
        const fixture = cacheLifecycleFixture();
        const harness = failureHarness(new MockProcess(), { trusted, dependencies: {
            ...fixture.dependencies, releaseCatalog: { schemaVersion: 1, releases: [] },
            authentication: { getSession: async () => assert.fail("must not request authentication") },
            acquisition: { acquireApprovedRelease: async () => assert.fail("must not download") }
        } });
        await harness.activate();
        await harness.installMatching();
        assert.ok(harness.messages.some(message => trusted ? /No authenticated release anchor/.test(message) : /Trust this workspace/.test(message)));
        harness.context.subscriptions.forEach(item => item.dispose());
    }
});

test("explicit online install cleans its download and preserves the active process", async () => {
    for (const outcome of ["selected", "cache-missing", "cancelled", "throws"]) {
        const fixture = cacheLifecycleFixture();
        const server = new MockProcess();
        const notices = [];
        let cleaned = 0;
        let imported = 0;
        const harness = failureHarness(server, { identity: fixture.identity, dependencies: {
            ...fixture.dependencies,
            authentication: { getSession: async (provider, _scopes, options) => {
                assert.equal(provider, "github");
                assert.equal(options.createIfNone, true);
                return { accessToken: "private-test-token" };
            } },
            window: {
                withProgress: async (_options, action) => action({ report() {} }, { isCancellationRequested: false, onCancellationRequested: () => disposable() }),
                showInformationMessage: async message => { notices.push(message); }
            },
            acquisition: { acquireApprovedRelease: async options => {
                assert.equal(options.token, "private-test-token");
                assert.equal(options.requirement, fixture.requirement);
                return { status: "downloaded", cleanup: async () => { cleaned++; } };
            } },
            cache: { importDownloadedRelease: async options => {
                imported++;
                assert.equal(options.token, undefined);
                if (outcome === "throws") throw new Error("fixture installation failed");
                return { status: outcome, reason: "fixture rejection" };
            } }
        } });
        await harness.activate();
        await harness.installMatching();
        assert.equal(imported, 1);
        assert.equal(cleaned, 1);
        assert.equal(server.exitCode, null);
        assert.equal(harness.spawnCount, 1);
        assert.equal(notices.length, outcome === "selected" ? 1 : 0);
        assert.equal(harness.messages.length, ["cache-missing", "throws"].includes(outcome) ? 1 : 0);
        assert.ok(![...harness.messages, ...harness.logs, ...notices].some(message => message.includes("private-test-token")));
        harness.context.subscriptions.forEach(item => item.dispose());
    }
});

test("explicit online install reuses a verified cache without authentication or download", async () => {
    const fixture = cacheLifecycleFixture();
    const notices = [];
    const server = new MockProcess();
    const harness = failureHarness(server, { identity: fixture.identity, dependencies: {
        ...fixture.dependencies,
        authentication: { getSession: async () => assert.fail("verified cache must not request authentication") },
        acquisition: { acquireApprovedRelease: async () => assert.fail("verified cache must not download") },
        cache: { resolveCachedServer: async () => ({ status: "selected", serverPath: path.resolve("verified-cache/server.dll"), identity: fixture.identity }) },
        window: { showInformationMessage: async message => { notices.push(message); } }
    } });
    await harness.activate();
    await harness.installMatching();
    assert.equal(notices.length, 1);
    assert.equal(server.exitCode, null);
    assert.equal(harness.spawnCount, 1);
    harness.context.subscriptions.forEach(item => item.dispose());
});

test("disposing while authentication is pending ignores its late response", async () => {
    const fixture = cacheLifecycleFixture();
    let finishAuthentication;
    const harness = failureHarness(new MockProcess(), { identity: fixture.identity, dependencies: {
        ...fixture.dependencies,
        authentication: { getSession: () => new Promise(resolve => { finishAuthentication = resolve; }) },
        acquisition: { acquireApprovedRelease: async () => assert.fail("disposed command must not download") }
    } });
    await harness.activate();
    const installing = harness.installMatching();
    for (let attempt = 0; !finishAuthentication && attempt < 100; attempt++) await new Promise(resolve => setImmediate(resolve));
    assert.ok(finishAuthentication);
    harness.context.subscriptions.forEach(item => item.dispose());
    finishAuthentication({ accessToken: "late-test-token" });
    await installing;
    assert.equal(harness.spawnCount, 1);
    assert.deepEqual(harness.messages, []);
});

test("changed project inputs after download prevent installation and clean the download", async () => {
    const fixture = cacheLifecycleFixture();
    let changed = false;
    let cleaned = 0;
    const harness = failureHarness(new MockProcess(), { identity: fixture.identity, dependencies: {
        ...fixture.dependencies,
        authentication: { getSession: async () => ({ accessToken: "test-token" }) },
        window: { withProgress: async (_options, action) => action({ report() {} }, { isCancellationRequested: false, onCancellationRequested: () => disposable() }) },
        verifyInputs: async () => { if (changed) throw new Error("project inputs changed"); },
        acquisition: { acquireApprovedRelease: async () => {
            changed = true;
            return { status: "downloaded", cleanup: async () => { cleaned++; } };
        } },
        cache: { importDownloadedRelease: async () => assert.fail("changed project must not install") }
    } });
    await harness.activate();
    await harness.installMatching();
    assert.equal(cleaned, 1);
    assert.match(harness.messages[0], /project inputs changed/);
    harness.context.subscriptions.forEach(item => item.dispose());
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
    assert.match(harness.messages[0], /dotnet.*selected server.*ENOENT/);
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
    assert.match(harness.messages[0], /selected server.*exited/);
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

test("missing runtime blocks before identity or evaluation and repeated checks notify once", async () => {
    const harness = failureHarness(new MockProcess(), { dependencies: {
        preflight: async () => ({ status: "unavailable", reason: "dotnet-runtime-missing" }),
        readRequirements: () => assert.fail("must not evaluate without the runtime")
    } });
    await harness.activate();
    await harness.restart();
    assert.equal(harness.identityChecks, 0);
    assert.equal(harness.spawnCount, 0);
    assert.equal(harness.messages.length, 1);
    assert.match(harness.messages[0], /\.NET 10 runtime/);
    assert.match(harness.status().text, /Setup needed/);
    harness.context.subscriptions.forEach(resource => resource.dispose());
});

test("removing the owner stops services without selecting a different root", async () => {
    const first = { name: "First", uri: { scheme: "file", fsPath: path.resolve("first") } };
    const second = { name: "Second", uri: { scheme: "file", fsPath: path.resolve("second") } };
    const server = new MockProcess();
    const harness = failureHarness(server, { folders: [first, second] });
    await harness.activate();
    harness.foldersChanged({ added: [], removed: [second] });
    assert.ok(server.notifications.some(notification => notification.method === "initialized"));
    harness.workspace.workspaceFolders = [second];
    harness.foldersChanged({ added: [], removed: [first] });
    assert.equal(server.lastRequest.method, "shutdown");
    assert.equal(harness.spawnCount, 1);
    assert.match(harness.status().text, /Select project/);
    server.emit("exit", 0, null);
    harness.context.subscriptions.forEach(resource => resource.dispose());
});

test("workspace removal cancels preflight and a late result cannot start tools", async () => {
    let complete, signal;
    const harness = failureHarness(new MockProcess(), { dependencies: {
        preflight: options => { signal = options.signal; return new Promise(resolve => { complete = resolve; }); }
    } });
    const activating = harness.activate();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(typeof complete, "function");
    const removed = harness.workspace.workspaceFolders;
    harness.workspace.workspaceFolders = [];
    harness.foldersChanged({ added: [], removed });
    assert.equal(signal.aborted, true);
    complete({ status: "available", dotnetPath: path.resolve("host/dotnet.exe") });
    await activating;
    assert.equal(harness.identityChecks, 0);
    assert.equal(harness.spawnCount, 0);
    assert.match(harness.status().text, /Select project/);
    harness.context.subscriptions.forEach(resource => resource.dispose());
});

test("project selection writes only a relative project inside its owning folder", async () => {
    for (const inside of [true, false]) {
        const updates = [];
        const harness = failureHarness(new MockProcess(), { dependencies: { window: {
            showOpenDialog: async () => [{ scheme: "file", fsPath: path.resolve(inside ? "workspace/app/App.csproj" : "outside/App.csproj") }]
        } } });
        harness.workspace.getConfiguration = (_section, uri) => ({ get: key => key === "projectPath" ? "host/Host.csproj" : path.resolve("server.dll"),
            update: async (...args) => updates.push({ uri, args }) });
        await harness.activate();
        await harness.command("lucentLui.selectProject");
        assert.equal(updates.length, inside ? 1 : 0);
        if (inside) {
            assert.equal(updates[0].uri, harness.workspace.workspaceFolders[0].uri);
            assert.deepEqual(updates[0].args, ["projectPath", "app/App.csproj", 3]);
        } else assert.match(harness.messages[0], /inside the chosen workspace folder/);
        harness.context.subscriptions.forEach(resource => resource.dispose());
    }
});

test("changing tooling settings cancels old evaluation and only starts the current selection", async () => {
    let finishFirst, firstSignal, calls = 0;
    const harness = failureHarness(new MockProcess(), { dependencies: {
        preflight: options => {
            if (++calls === 1) { firstSignal = options.signal; return new Promise(resolve => { finishFirst = resolve; }); }
            return Promise.resolve({ status: "available", dotnetPath: path.resolve("host/dotnet.exe") });
        }
    } });
    const activating = harness.activate();
    await new Promise(resolve => setImmediate(resolve));
    harness.configurationChanged("editor.fontSize");
    assert.equal(calls, 1);
    harness.configurationChanged("lucentLui.projectPath");
    await harness.started;
    assert.equal(firstSignal.aborted, true);
    finishFirst({ status: "available", dotnetPath: path.resolve("host/dotnet.exe") });
    await activating;
    assert.equal(harness.spawnCount, 1);
    assert.equal(harness.identityChecks, 1);
    harness.context.subscriptions.forEach(resource => resource.dispose());
});
