"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { test } = require("node:test");

function deferred() {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
}

function loadEnvironmentUi(verify, runDoctor) {
    const source = fs.readFileSync(path.join(__dirname, "environment-ui.js"), "utf8");
    const module = { exports: {} };
    const evaluate = vm.runInNewContext(`(function(require, module, exports) { ${source}\n})`,
        { AbortController, JSON, Promise, console });
    evaluate(name => name === "node:path" ? path
        : name === "./managed-tool" ? { verifyManagedToolFiles: verify }
        : name === "./doctor-client" ? { runDoctor }
        : (() => { throw new Error(`Unexpected dependency: ${name}`); })(), module, module.exports);
    return module.exports;
}

function fixture({ trusted = true, folders = 1, verify, runDoctor, openDocument, copyChoice } = {}) {
    const events = [];
    const clipboard = [];
    const commands = new Map();
    const subscriptions = [];
    let cancelProgress;
    const folderItems = Array.from({ length: folders }, (_, index) => ({
        name: `folder-${index}`, uri: { scheme: "file", fsPath: path.resolve(`workspace-${index}`) }
    }));
    const vscode = {
        ProgressLocation: { Notification: 1 },
        commands: { registerCommand(name, action) { commands.set(name, action); return { dispose() {} }; } },
        workspace: {
            isTrusted: trusted, workspaceFolders: folderItems,
            async openTextDocument(options) {
                events.push("open-preview");
                return openDocument ? openDocument(options) : { content: options.content };
            }
        },
        window: {
            async showQuickPick() { events.push("quick-pick"); return undefined; },
            async withProgress(_options, callback) {
                const token = {
                    isCancellationRequested: false,
                    onCancellationRequested(listener) {
                        cancelProgress = () => { token.isCancellationRequested = true; listener(); };
                        return { dispose() { cancelProgress = undefined; } };
                    }
                };
                return callback({}, token);
            },
            async showTextDocument() { events.push("show-preview"); },
            async showInformationMessage(_message, action) {
                events.push(action ? "copy-prompt" : "information");
                return action && copyChoice ? copyChoice() : action;
            }
        },
        env: { clipboard: { async writeText(text) { events.push("clipboard"); clipboard.push(text); } } }
    };
    const context = { subscriptions, asAbsolutePath: name => path.resolve(name) };
    const manifest = { lucentDoctor: { schemaVersion: 1 }, lucentRelease: { sourceCommit: "a".repeat(40) } };
    const calls = { verify: 0, doctor: 0 };
    const verifyBoundary = async (...args) => { calls.verify++; return verify ? verify(...args) : path.resolve("verified-doctor.dll"); };
    const doctorBoundary = async (...args) => {
        calls.doctor++;
        return runDoctor ? runDoctor(...args) : { schemaVersion: 1, kind: "environment-doctor", status: "available" };
    };
    const { createEnvironmentCommands } = loadEnvironmentUi(verifyBoundary, doctorBoundary);
    const environment = createEnvironmentCommands(vscode, context, manifest);
    return { events, clipboard, commands, calls, subscriptions, environment,
        cancelProgress: () => cancelProgress?.(),
        check: () => commands.get("lucentLui.checkEnvironment")(),
        copy: () => commands.get("lucentLui.copyEnvironmentReport")(),
        dispose: () => subscriptions.at(-1).dispose() };
}

test("trusted and Restricted Mode commands use only the verified static doctor", async () => {
    for (const trusted of [false, true]) {
        const ui = fixture({ trusted });
        await ui.check();
        assert.equal(ui.calls.verify, 1);
        assert.equal(ui.calls.doctor, 1);
        assert.deepEqual(ui.events, ["open-preview", "show-preview"]);
    }
});

test("unverified payload never executes and its error cannot export raw details", async () => {
    const ui = fixture({ verify: () => { throw new Error("token=PRIVATE C:\\Users\\private"); } });
    await ui.check();
    assert.equal(ui.calls.doctor, 0);
    assert.deepEqual(ui.events, ["open-preview", "show-preview"]);
    await ui.copy();
    assert.deepEqual(ui.events.slice(-3), ["show-preview", "copy-prompt", "clipboard"]);
    assert.equal(ui.clipboard.length, 1);
    assert.equal(ui.clipboard[0].includes("PRIVATE"), false);
    assert.equal(ui.clipboard[0].includes("C:\\Users"), false);
});

test("lifecycle cancellation during document opening suppresses preview and clears the report", async () => {
    const opening = deferred();
    const entered = deferred();
    const ui = fixture({ openDocument: () => { entered.resolve(); return opening.promise; } });
    const check = ui.check();
    await entered.promise;
    ui.environment.cancel();
    opening.resolve({});
    await check;
    await ui.copy();
    assert.equal(ui.events.includes("show-preview"), false);
    assert.equal(ui.events.includes("clipboard"), false);
    assert.equal(ui.events.at(-1), "information");
});

test("newer invocation discards a late earlier doctor result", async () => {
    const old = deferred();
    let calls = 0;
    const ui = fixture({ runDoctor: () => ++calls === 1 ? old.promise
        : { schemaVersion: 1, kind: "environment-doctor", status: "available" } });
    const pending = ui.check();
    while (ui.calls.doctor === 0) await new Promise(resolve => setImmediate(resolve));
    await ui.check();
    old.resolve({ schemaVersion: 1, kind: "environment-doctor", status: "available" });
    await pending;
    assert.equal(ui.events.filter(item => item === "show-preview").length, 1);
    assert.equal(ui.calls.doctor, 2);
});

test("disposal while copy confirmation is pending prevents clipboard export", async () => {
    const choice = deferred();
    const copyUi = fixture({ copyChoice: () => choice.promise });
    await copyUi.check();
    const pending = copyUi.copy();
    while (!copyUi.events.includes("copy-prompt")) await new Promise(resolve => setImmediate(resolve));
    copyUi.dispose();
    choice.resolve("Copy Report");
    await pending;
    assert.equal(copyUi.events.includes("clipboard"), false);
});

test("copy requires a fresh preview and an explicit choice; no-folder check avoids a picker", async () => {
    const ui = fixture();
    await ui.check();
    await ui.copy();
    assert.deepEqual(ui.events, ["open-preview", "show-preview", "open-preview", "show-preview", "copy-prompt", "clipboard"]);

    const empty = fixture({ folders: 0 });
    await empty.check();
    assert.equal(empty.calls.verify, 0);
    assert.equal(empty.events.includes("quick-pick"), false);
    assert.deepEqual(empty.events, ["information"]);
});
