"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const { createOnboardingUi, availableActions } = require("./onboarding-ui");

function harness() {
    const commands = [], notices = [], problems = new Map();
    const item = { show() {}, disposed: false, dispose() { this.disposed = true; } };
    let pick;
    const context = { subscriptions: [] };
    const vscode = {
        workspace: { isTrusted: true },
        StatusBarAlignment: { Right: 2 },
        DiagnosticSeverity: { Error: 0 },
        Diagnostic: class { constructor(range, message, severity) { Object.assign(this, { range, message, severity }); } },
        Range: class {},
        Uri: { file: path => path },
        commands: { executeCommand: async command => commands.push(command) },
        window: {
            createStatusBarItem: () => item,
            showErrorMessage: async message => notices.push(message),
            showQuickPick: async choices => new Promise(resolve => { pick = () => resolve(choices[0]); })
        },
        languages: { createDiagnosticCollection: () => ({ clear: () => problems.clear(), set: (uri, values) => problems.set(uri, values), dispose: () => problems.clear() }) }
    };
    return { ui: createOnboardingUi(vscode, context), item, commands, notices, problems, choose: () => pick() };
}

test("untrusted and installing states offer no project execution or competing install action", () => {
    assert.deepEqual(availableActions("mismatch", false).map(action => action.command), [
        "workbench.trust.manage", "lucentLui.checkEnvironment", "lucentLui.openGettingStarted"
    ]);
    assert.deepEqual(availableActions("installing", true).map(action => action.command), [
        "lucentLui.checkEnvironment", "lucentLui.openGettingStarted"
    ]);
});

test("blocking setup states deduplicate notifications and clear stale project diagnostics", () => {
    const value = harness();
    const failure = { kind: "mismatch", message: "Compiler differs.", projectPath: "first.csproj", code: "compiler-mismatch" };
    value.ui.update(failure, { notify: true });
    value.ui.update({ kind: "checking" });
    value.ui.update(failure, { notify: true });
    assert.deepEqual(value.notices, ["Compiler differs."]);
    assert.equal(value.problems.get("first.csproj")[0].code, "compiler-mismatch");
    assert.match(value.item.accessibilityInformation.label, /Matching tools needed/);
    value.ui.update({ kind: "no-project" });
    assert.equal(value.problems.size, 0);
    value.ui.update({ kind: "ready" });
    value.ui.update(failure, { notify: true });
    assert.equal(value.notices.length, 2);
    value.ui.dispose();
});

test("a stale or disposed action picker cannot invoke a command", async () => {
    for (const disposition of ["changed", "disposed", "current"]) {
        const value = harness();
        value.ui.update({ kind: "no-project" });
        const pending = value.ui.showActions();
        if (disposition === "changed") value.ui.update({ kind: "ready" });
        if (disposition === "disposed") value.ui.dispose();
        value.choose();
        await pending;
        assert.deepEqual(value.commands, disposition === "current" ? ["lucentLui.selectProject"] : []);
        value.ui.dispose();
        assert.equal(value.item.disposed, true);
    }
});
