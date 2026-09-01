"use strict";

const childProcess = require("child_process");
const path = require("path");
const vscode = require("vscode");

class Rpc {
    constructor(process) {
        this.process = process;
        this.buffer = Buffer.alloc(0);
        this.nextId = 1;
        this.pending = new Map();
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
    const process = childProcess.spawn("dotnet", [server], { stdio: ["pipe", "pipe", "inherit"] });
    const rpc = new Rpc(process);
    const stop = { dispose: () => {
        const timeout = setTimeout(() => process.exitCode === null && process.kill(), 1000);
        process.once("exit", () => clearTimeout(timeout));
        rpc.request("shutdown", {}).then(() => rpc.notify("exit", {})).catch(() => {});
    } };
    context.subscriptions.push(stop);
    const projectUri = vscode.Uri.file(path.resolve(projectPath)).toString();
    try { await rpc.request("initialize", { initializationOptions: { projectUri } }); }
    catch (error) { stop.dispose(); throw error; }
    rpc.notify("initialized", {});
    const isLui = document => document.languageId === "lui";
    const update = document => isLui(document) && rpc.notify("textDocument/didChange", {
        textDocument: { uri: document.uri.toString() }, contentChanges: [{ text: document.getText() }]
    });
    for (const document of vscode.workspace.textDocuments.filter(isLui)) rpc.notify("textDocument/didOpen", {
        textDocument: { uri: document.uri.toString(), text: document.getText() }
    });
    context.subscriptions.push(
        vscode.workspace.onDidOpenTextDocument(document => isLui(document) && rpc.notify("textDocument/didOpen", {
            textDocument: { uri: document.uri.toString(), text: document.getText() }
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
        })
    );
}

exports.activate = activate;
