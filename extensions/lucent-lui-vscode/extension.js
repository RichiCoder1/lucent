"use strict";

const childProcess = require("child_process");
const path = require("path");
const vscode = require("vscode");

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
    for (const [uri, changes] of Object.entries(result.changes)) {
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
    return changes.map(change => new vscode.TextEdit(
        new vscode.Range(
            change.range.start.line,
            change.range.start.character,
            change.range.end.line,
            change.range.end.character
        ),
        change.newText
    ));
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

async function activate(context) {
    const projectSetting = vscode.workspace.getConfiguration("lucentLui").get("projectPath");
    if (!projectSetting) { vscode.window.showErrorMessage("Set lucentLui.projectPath to the evaluated .csproj."); return; }
    const workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    const projectPath = workspaceRoot ? path.resolve(workspaceRoot, projectSetting) : path.resolve(projectSetting);
    const configured = vscode.workspace.getConfiguration("lucentLui").get("serverPath");
    if (!configured) {
        vscode.window.showErrorMessage("Set lucentLui.serverPath to Lucent.Lui.LanguageServer.dll.");
        return;
    }
    const log = vscode.window.createOutputChannel("Lucent LUI", { log: true });
    context.subscriptions.push(log);
    log.info(`Starting language server: ${configured}; project: ${projectPath}`);
    const server = path.resolve(configured);
    const process = childProcess.spawn("dotnet", [server], {
        stdio: ["pipe", "pipe", "pipe"],
        windowsHide: true
    });
    process.stderr.on("data", chunk => log.error(chunk.toString("utf8").trimEnd()));
    const rpc = new Rpc(process, log);
    const subscriptions = [];
    let stopped = false;
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
        for (const subscription of subscriptions.splice(0).reverse()) subscription.dispose();
        if (rpc.failure) {
            if (process.exitCode === null && process.pid !== undefined) process.kill();
            return;
        }
        const timeout = setTimeout(() => process.exitCode === null && process.kill(), 1000);
        process.once("exit", () => clearTimeout(timeout));
        rpc.request("shutdown", {}).then(() => rpc.notify("exit", {})).catch(() => {});
    } };
    const reportFailure = error => {
        log.error(error.message);
        return vscode.window.showErrorMessage(
            `Lucent language server stopped. Check that dotnet is available and lucentLui.serverPath points to the server DLL. ${error.message} See Output > Lucent LUI.`
        );
    };
    rpc.onFailure = error => {
        if (stopped) return;
        stop.dispose();
        reportFailure(error);
    };
    context.subscriptions.push(stop);
    subscriptions.push(diagnostics);
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
    watchProjectDirectories([path.dirname(path.resolve(projectPath))]);
    const projectUri = vscode.Uri.file(path.resolve(projectPath)).toString();
    try { await rpc.request("initialize", { initializationOptions: { projectUri } }); }
    catch (error) {
        if (!stopped) { stop.dispose(); reportFailure(error); }
        throw error;
    }
    if (stopped) return;
    rpc.notify("initialized", {});
    const completionData = new WeakMap();
    const isLucentDocument = document => document.languageId === "lui" || document.languageId === "csharp";
    const rename = async (document, position, newName) => toWorkspaceEdit(
        await rpc.request("textDocument/rename", {
            textDocument: { uri: document.uri.toString() }, position, newName
        })
    );
    const update = document => isLucentDocument(document) && rpc.notify("textDocument/didChange", {
        textDocument: { uri: document.uri.toString(), version: document.version }, contentChanges: [{ text: document.getText() }]
    });
    for (const document of vscode.workspace.textDocuments.filter(isLucentDocument)) rpc.notify("textDocument/didOpen", {
        textDocument: { uri: document.uri.toString(), version: document.version, text: document.getText() }
    });
    if (stopped) return;
    subscriptions.push(
        vscode.workspace.onDidOpenTextDocument(document => isLucentDocument(document) && rpc.notify("textDocument/didOpen", {
            textDocument: { uri: document.uri.toString(), version: document.version, text: document.getText() }
        })),
        vscode.workspace.onDidChangeTextDocument(event => update(event.document)),
        vscode.workspace.onDidCloseTextDocument(document => isLucentDocument(document) && rpc.notify("textDocument/didClose", { textDocument: { uri: document.uri.toString() } })),
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
            provideDocumentFormattingEdits: async document => toTextEdits(
                await rpc.request("textDocument/formatting", {
                    textDocument: { uri: document.uri.toString() }, options: {}
                })
            )
        }),
        vscode.languages.registerDocumentRangeFormattingEditProvider("lui", {
            provideDocumentRangeFormattingEdits: async (document, range) => toTextEdits(
                await rpc.request("textDocument/rangeFormatting", {
                    textDocument: { uri: document.uri.toString() }, range, options: {}
                })
            )
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
exports.crossLanguageSelector = crossLanguageSelector;
exports.toTextEdits = toTextEdits;
exports.toWorkspaceEdit = toWorkspaceEdit;
