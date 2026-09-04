"use strict";

const childProcess = require("child_process");
const path = require("path");
const vscode = require("vscode");

const semanticTokensLegend = ["keyword", "type", "property", "enumMember"];

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

class Rpc {
    constructor(process) {
        this.process = process;
        this.buffer = Buffer.alloc(0);
        this.nextId = 1;
        this.pending = new Map();
        this.notifications = new Map();
        process.stdout.on("data", chunk => this.read(chunk));
        process.on("exit", () => this.rejectAll(new Error("Lucent language server exited.")));
    }

    request(method, params) {
        const id = this.nextId++;
        return new Promise((resolve, reject) => {
            this.pending.set(id, { resolve, reject });
            this.write({ jsonrpc: "2.0", id, method, params });
        });
    }

    notify(method, params) { this.write({ jsonrpc: "2.0", method, params }); }

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
        this.buffer = Buffer.concat([this.buffer, chunk]);
        for (;;) {
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
            message.error ? pending.reject(new Error(message.error.message)) : pending.resolve(message.result);
        }
    }

    rejectAll(error) {
        for (const pending of this.pending.values()) pending.reject(error);
        this.pending.clear();
    }
}

async function activate(context) {
    const projectPath = vscode.workspace.getConfiguration("lucentLui").get("projectPath");
    if (!projectPath) { vscode.window.showErrorMessage("Set lucentLui.projectPath to the evaluated .csproj."); return; }
    const configured = vscode.workspace.getConfiguration("lucentLui").get("serverPath");
    if (!configured) {
        vscode.window.showErrorMessage("Set lucentLui.serverPath to Lucent.Lui.LanguageServer.dll.");
        return;
    }
    const server = path.resolve(configured);
    const process = childProcess.spawn("dotnet", [server], {
        stdio: ["pipe", "pipe", "inherit"],
        windowsHide: true
    });
    const rpc = new Rpc(process);
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
        const timeout = setTimeout(() => process.exitCode === null && process.kill(), 1000);
        process.once("exit", () => clearTimeout(timeout));
        rpc.request("shutdown", {}).then(() => rpc.notify("exit", {})).catch(() => {});
    } };
    context.subscriptions.push(stop, diagnostics);
    const notifyWatchedFile = (uri, type) => rpc.notify("workspace/didChangeWatchedFiles", {
        changes: [{ uri: uri.toString(), type }]
    });
    const watchedDirectories = new Set();
    const watchProjectDirectories = directories => {
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
                ...projectAncestors.map(directory => vscode.workspace.createFileSystemWatcher(
                    new vscode.RelativePattern(directory, "{global.json,.editorconfig,Directory.Build.props,Directory.Build.targets,Directory.Packages.props}")
                ))
            ];
            for (const watcher of watchers) {
                context.subscriptions.push(
                    watcher,
                    watcher.onDidCreate(uri => notifyWatchedFile(uri, 1)),
                    watcher.onDidChange(uri => notifyWatchedFile(uri, 2)),
                    watcher.onDidDelete(uri => notifyWatchedFile(uri, 3))
                );
            }
        }
    };
    rpc.onNotification("lucent/projectGraph", message => watchProjectDirectories(message.directories));
    watchProjectDirectories([path.dirname(path.resolve(projectPath))]);
    const projectUri = vscode.Uri.file(path.resolve(projectPath)).toString();
    try { await rpc.request("initialize", { initializationOptions: { projectUri } }); }
    catch (error) { stop.dispose(); throw error; }
    rpc.notify("initialized", {});
    const isLui = document => document.languageId === "lui";
    const update = document => isLui(document) && rpc.notify("textDocument/didChange", {
        textDocument: { uri: document.uri.toString(), version: document.version }, contentChanges: [{ text: document.getText() }]
    });
    for (const document of vscode.workspace.textDocuments.filter(isLui)) rpc.notify("textDocument/didOpen", {
        textDocument: { uri: document.uri.toString(), version: document.version, text: document.getText() }
    });
    context.subscriptions.push(
        vscode.workspace.onDidOpenTextDocument(document => isLui(document) && rpc.notify("textDocument/didOpen", {
            textDocument: { uri: document.uri.toString(), version: document.version, text: document.getText() }
        })),
        vscode.workspace.onDidChangeTextDocument(event => update(event.document)),
        vscode.workspace.onDidCloseTextDocument(document => isLui(document) && rpc.notify("textDocument/didClose", { textDocument: { uri: document.uri.toString() } })),
        vscode.workspace.registerTextDocumentContentProvider("lucent-lui", {
            provideTextDocumentContent: uri => rpc.request("lucent/generatedText", { uri: uri.toString() })
        }),
        vscode.languages.registerDefinitionProvider([{ language: "lui" }, { scheme: "lucent-lui" }], {
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
        vscode.languages.registerCompletionItemProvider("lui", {
            provideCompletionItems: async (document, position) => {
                const result = await rpc.request("textDocument/completion", {
                    textDocument: { uri: document.uri.toString() }, position
                });
                return result.items.map(item => {
                    const completion = new vscode.CompletionItem(
                        item.label,
                        toVsCodeCompletionKind(item.kind, vscode.CompletionItemKind)
                    );
                    completion.detail = item.detail;
                    completion.documentation = item.documentation && plaintext(item.documentation.value);
                    return completion;
                });
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
                return result.map(convert);
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
}

exports.activate = activate;
exports.toVsCodeCompletionKind = toVsCodeCompletionKind;
exports.toVsCodeDiagnosticSeverity = toVsCodeDiagnosticSeverity;
exports.toVsCodeSymbolKind = toVsCodeSymbolKind;
exports.semanticTokensLegend = semanticTokensLegend;
