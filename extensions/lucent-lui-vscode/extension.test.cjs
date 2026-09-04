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

test("activates only Lucent workspaces and registers C# cross-language selectors", () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, "package.json"), "utf8"));
    assert.deepEqual(manifest.activationEvents, ["onLanguage:lui", "workspaceContains:**/*.lui"]);
    assert.deepEqual(JSON.parse(JSON.stringify(loaded.exports.crossLanguageSelector)), [
        { language: "lui" }, { language: "csharp", scheme: "file" }
    ]);
});

test("activation preserves current diagnostics and clears closed documents", async () => {
    const diagnostics = { deleted: [], delete(uri) { this.deleted.push(uri.toString()); }, dispose() {} };
    const patterns = [];
    let semanticProvider;
    let semanticLegend;
    let referenceProvider;
    let referenceSelector;
    let renameProvider;
    let renameSelector;
    let spawnOptions;
    const process = new MockProcess();
    const openDocument = {
        uri: { toString: () => "file:///missing.lui" },
        version: 1
    };
    const vscode = {
        Diagnostic: class { constructor() {} },
        DiagnosticSeverity: { Error: 1, Warning: 2, Information: 3, Hint: 4 },
        Location: class { constructor(uri, range) { this.uri = uri; this.range = range; } },
        Range: class { constructor() {} },
        RelativePattern: class { constructor(base, pattern) { patterns.push([base, pattern]); } },
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
                return disposable();
            },
            registerSignatureHelpProvider: disposable
        },
        window: { showErrorMessage() {} },
        workspace: {
            createFileSystemWatcher: () => ({ onDidCreate: disposable, onDidChange: disposable, onDidDelete: disposable, dispose() {} }),
            getConfiguration: () => ({ get: key => key === "projectPath" ? "host/Host.csproj" : "server.dll" }),
            onDidChangeTextDocument: disposable,
            onDidCloseTextDocument: disposable,
            onDidOpenTextDocument: disposable,
            registerTextDocumentContentProvider: disposable,
            textDocuments: [openDocument]
        }
    };
    const extension = loadExtension(vscode, process, options => spawnOptions = options);
    await extension.activate({ subscriptions: [] });
    assert.equal(spawnOptions.windowsHide, true);
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
    assert.deepEqual(JSON.parse(JSON.stringify(referenceSelector)), [
        { language: "lui" }, { language: "csharp", scheme: "file" }
    ]);
    assert.deepEqual(JSON.parse(JSON.stringify(renameSelector)), [
        { language: "lui" }, { language: "csharp", scheme: "file" }
    ]);
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
    assert.deepEqual(Array.from(semanticLegend.types), ["keyword", "type", "property", "enumMember"]);
    const tokens = await semanticProvider.provideDocumentSemanticTokens({
        uri: { toString: () => "file:///Widget.lui" }
    });
    assert.deepEqual(Array.from(tokens.data), [0, 0, 3, 0, 0]);
});

function disposable() { return { dispose() {} }; }

function loadExtension(vscode, process, spawned) {
    const module = { exports: {} };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, "extension.js"), "utf8"), {
        Buffer,
        clearTimeout,
        console,
        exports: module.exports,
        module,
        require: name => name === "vscode" ? vscode : name === "child_process" ? {
            spawn: (_command, _args, options) => { spawned?.(options); return process; }
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
        this.stdin = { write: value => {
            if (!Buffer.isBuffer(value)) return;
            const request = JSON.parse(value.toString());
            this.lastRequest = request;
            if (request.method === "initialize") {
                this.send({ jsonrpc: "2.0", method: "lucent/projectGraph", params: { directories: ["host", "referenced"] } });
            }
            if (request.id !== undefined) this.send({
                jsonrpc: "2.0",
                id: request.id,
                result: request.method === "textDocument/semanticTokens/full" ? { data: [0, 0, 3, 0, 0] }
                    : request.method === "textDocument/references"
                        ? request.params.textDocument.uri.endsWith("Helpers.cs") ? null : [{
                        uri: "file:///Widget.lui",
                        range: { start: { line: 0, character: 0 }, end: { line: 0, character: 1 } }
                    }]
                        : request.method === "textDocument/rename" ? null : {}
            });
        } };
    }

    kill() { this.exitCode = 0; this.emit("exit"); }

    send(message) {
        const body = Buffer.from(JSON.stringify(message));
        this.stdout.emit("data", Buffer.concat([Buffer.from(`Content-Length: ${body.length}\r\n\r\n`), body]));
    }
}
