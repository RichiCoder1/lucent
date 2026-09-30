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

function loadEnvironmentUi(verify, runDoctor, verifyFeed, runFeedDoctor) {
    const source = fs.readFileSync(path.join(__dirname, "environment-ui.js"), "utf8");
    const module = { exports: {} };
    const evaluate = vm.runInNewContext(`(function(require, module, exports) { ${source}\n})`,
        { AbortController, JSON, Promise, console });
    evaluate(name => name === "node:path" ? path
        : name === "./managed-tool" ? { verifyManagedToolFiles: verify, verifyNuGetDoctorFiles: verifyFeed }
        : name === "./doctor-client" ? { runDoctor, runFeedDoctor }
        : (() => { throw new Error(`Unexpected dependency: ${name}`); })(), module, module.exports);
    return module.exports;
}

function fixture({ trusted = true, folders = 1, verify, runDoctor, verifyFeed, runFeedDoctor, inputAnswers = ["Lucent.Core", "0.3.0-dev.101.1"], onlineChoice = "Check Anonymous Access", openDocument, copyChoice, checkTrustedProject } = {}) {
    const events = [];
    const clipboard = [];
    const commands = new Map();
    const subscriptions = [];
    let cancelProgress;
    const inputs = [];
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
            async showInputBox(options) { events.push("input"); inputs.push(options); return inputAnswers[inputs.length - 1]; },
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
                if (action === "Check Anonymous Access") { events.push("online-prompt"); return onlineChoice; }
                events.push(action ? "copy-prompt" : "information");
                return action && copyChoice ? copyChoice() : action;
            }
        },
        env: { clipboard: { async writeText(text) { events.push("clipboard"); clipboard.push(text); } } }
    };
    const context = { subscriptions, asAbsolutePath: name => path.resolve(name) };
    const manifest = { lucentDoctor: { schemaVersion: 1 }, lucentRelease: { sourceCommit: "a".repeat(40) } };
    const calls = { verify: 0, doctor: 0, verifyFeed: 0, feed: 0 };
    const verifyBoundary = async (...args) => { calls.verify++; return verify ? verify(...args) : path.resolve("verified-doctor.dll"); };
    const doctorBoundary = async (...args) => {
        calls.doctor++;
        return runDoctor ? runDoctor(...args) : { schemaVersion: 1, kind: "environment-doctor", status: "available" };
    };
    const feedVerifyBoundary = async (...args) => { calls.verifyFeed++; return verifyFeed ? verifyFeed(...args) : path.resolve("nuget-doctor/Lucent.Tools.NuGet.dll"); };
    const feedBoundary = async options => { calls.feed++; return runFeedDoctor ? runFeedDoctor(options) : { kind: "nuget-feed-doctor", generation: options.generation, status: "observed" }; };
    const { createEnvironmentCommands } = loadEnvironmentUi(verifyBoundary, doctorBoundary, feedVerifyBoundary, feedBoundary);
    const environment = createEnvironmentCommands(vscode, context, manifest, checkTrustedProject);
    return { events, clipboard, commands, calls, subscriptions, environment, inputs,
        setTrusted: value => { vscode.workspace.isTrusted = value; },
        cancelProgress: () => cancelProgress?.(),
        check: () => commands.get("lucentLui.checkEnvironment")(),
        checkNative: () => commands.get("lucentLui.checkNativePrerequisites")(),
        checkFeed: () => commands.get("lucentLui.checkFeedConfiguration")(),
        checkOnline: () => commands.get("lucentLui.checkAnonymousFeedAccess")(),
        checkProject: () => commands.get("lucentLui.checkTrustedProject")(),
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

test("feed configuration uses explicit inputs and a separately verified payload without trust or evaluation", async () => {
    let options;
    const ui = fixture({ trusted: false, runFeedDoctor: value => { options = value; return { kind: "nuget-feed-doctor" }; } });
    await ui.checkFeed();
    assert.equal(ui.calls.verifyFeed, 1);
    assert.equal(ui.calls.verify, 0);
    assert.equal(ui.calls.doctor, 0);
    assert.equal(options.online, false);
    assert.equal(options.packageId, "Lucent.Core");
    assert.equal(options.version, "0.3.0-dev.101.1");
    assert.equal(ui.inputs[1].value, "");
    assert.ok(ui.inputs[1].placeHolder.includes("Exact"));
    await ui.copy();
    assert.equal(JSON.parse(ui.clipboard[0]).kind, "nuget-feed-doctor");
});

test("online feed access requires trust and the anonymous destination prompt", async () => {
    const restricted = fixture({ trusted: false });
    await restricted.checkOnline();
    assert.equal(restricted.calls.feed, 0);
    assert.equal(restricted.inputs.length, 0);
    const refused = fixture({ onlineChoice: "Cancel" });
    await refused.checkOnline();
    assert.equal(refused.calls.verifyFeed, 0);
    assert.equal(refused.calls.feed, 0);
    const accepted = fixture();
    await accepted.checkOnline();
    assert.ok(accepted.events.includes("online-prompt"));
    assert.equal(accepted.calls.verifyFeed, 1);
    assert.equal(accepted.calls.feed, 1);
});

test("feed payload failure is sanitized and late online observations are discarded after trust loss", async () => {
    const failed = fixture({ verifyFeed: () => { throw new Error("SECRET https://private.invalid"); } });
    await failed.checkFeed();
    assert.equal(failed.calls.feed, 0);
    await failed.copy();
    assert.ok(!failed.clipboard[0].includes("SECRET"));
    const pending = deferred();
    const ui = fixture({ runFeedDoctor: () => pending.promise });
    const running = ui.checkOnline();
    while (!ui.calls.feed) await new Promise(resolve => setImmediate(resolve));
    ui.setTrusted(false);
    pending.resolve({ kind: "nuget-feed-doctor" });
    await running;
    assert.ok(!ui.events.includes("show-preview"));
    await ui.copy();
    assert.deepEqual(ui.clipboard, []);
});

test("native prerequisites are explicit, verified and available without trust or a workspace", async () => {
    let request;
    let projectCalls = 0;
    const ui = fixture({ trusted: false, folders: 0,
        checkTrustedProject: () => { projectCalls++; },
        runDoctor: options => { request = options; return { kind: "native-prerequisites-doctor", status: "available" }; }
    });
    assert.equal(ui.calls.verify, 0);
    await ui.checkNative();
    assert.equal(request.mode, "native");
    assert.equal(ui.calls.verify, 1);
    assert.equal(projectCalls, 0);
    await ui.copy();
    assert.equal(JSON.parse(ui.clipboard[0]).kind, "native-prerequisites-doctor");
    assert.deepEqual(ui.events.slice(-2), ["copy-prompt", "clipboard"]);
});

test("cancelled native checks discard late results and cannot export stale observations", async () => {
    const pending = deferred();
    let signal;
    const ui = fixture({ runDoctor: options => { signal = options.signal; return pending.promise; } });
    const running = ui.checkNative();
    while (!signal) await new Promise(resolve => setImmediate(resolve));
    ui.environment.cancel();
    assert.equal(signal.aborted, true);
    pending.resolve({ kind: "native-prerequisites-doctor" });
    await running;
    await ui.copy();
    assert.deepEqual(ui.clipboard, []);
    assert.ok(!ui.events.includes("show-preview"));
});

test("explicit trusted command is trust gated and does not invoke static doctor", async () => {
    let calls = 0;
    const checkTrustedProject = async () => { calls++; return { kind: "trusted-project-doctor", scope: "trusted-project" }; };
    const restricted = fixture({ trusted: false, checkTrustedProject });
    await restricted.checkProject();
    assert.equal(calls, 0);
    const trusted = fixture({ checkTrustedProject });
    await trusted.checkProject();
    assert.equal(calls, 1);
    assert.equal(trusted.calls.doctor, 0);
    await trusted.copy();
    assert.equal(JSON.parse(trusted.clipboard[0]).scope, "trusted-project");
});

test("trusted command cancellation suppresses late results and failures export only stable reasons", async () => {
    const pending = deferred();
    let signal;
    const ui = fixture({ checkTrustedProject: operationSignal => { signal = operationSignal; return pending.promise; } });
    const check = ui.checkProject();
    ui.cancelProgress();
    assert.equal(signal.aborted, true);
    pending.resolve({ kind: "trusted-project-doctor" });
    await check;
    assert.equal(ui.events.includes("show-preview"), false);
    await ui.copy();
    assert.equal(ui.clipboard.length, 0);
    const failure = fixture({ checkTrustedProject: () => { const error = new Error("SECRET C:\\Users\\private"); error.doctorReason = "SECRET-code"; throw error; } });
    await failure.checkProject();
    await failure.copy();
    assert.equal(failure.clipboard[0].includes("SECRET"), false);
    assert.equal(JSON.parse(failure.clipboard[0]).reason, "requirements-unavailable");
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
