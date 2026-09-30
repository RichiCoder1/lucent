"use strict";

const states = {
    checking: { label: "Checking tools", icon: "sync~spin", detail: "Checking the selected project's language tools." },
    untrusted: { label: "Workspace not trusted", icon: "shield", detail: "Syntax highlighting is available. Manage Workspace Trust to enable project language services." },
    "no-project": { label: "Select project", icon: "folder", detail: "Select the project that owns this workspace's Lucent language services." },
    "missing-tools": { label: "Setup needed", icon: "warning", detail: "Required .NET tooling is unavailable on the workspace host.", blocked: true },
    mismatch: { label: "Matching tools needed", icon: "warning", detail: "The project needs a matching compiler and language server.", blocked: true },
    offline: { label: "Tools unavailable", icon: "warning", detail: "Matching tools are unavailable. Import an approved archive or retry acquisition.", blocked: true },
    installing: { label: "Installing tools", icon: "sync~spin", detail: "Installation is cancellable. The running language server is unchanged." },
    "tools-ready": { label: "Restart to use tools", icon: "check", detail: "Verified matching tools are available. Restart language services to use them." },
    ready: { label: "Ready", icon: "check", detail: "Language services are active for the selected project." },
    stopped: { label: "Language services stopped", icon: "warning", detail: "Inspect the reported failure, then explicitly restart language services.", blocked: true }
};

const actions = {
    environment: { label: "Check environment", description: "Read-only checks on the workspace host", command: "lucentLui.checkEnvironment" },
    project: { label: "Select project", description: "Choose the project for this workspace", command: "lucentLui.selectProject" },
    install: { label: "Install matching language tools", description: "Use the verified cache or explicitly download an approved release", command: "lucentLui.installMatchingServer" },
    import: { label: "Import server archive", description: "Verify an approved offline server ZIP", command: "lucentLui.importServerArchive" },
    restart: { label: "Restart language services", command: "lucentLui.restartLanguageServices" },
    guide: { label: "Open getting started", command: "lucentLui.openGettingStarted" },
    trust: { label: "Manage Workspace Trust", command: "workbench.trust.manage" }
};

function availableActions(kind, trusted) {
    if (!trusted || kind === "untrusted") return [actions.trust, actions.environment, actions.guide];
    const primary = kind === "no-project" ? [actions.project]
        : kind === "mismatch" || kind === "offline" ? [actions.install, actions.import, actions.restart]
        : kind === "tools-ready" || kind === "stopped" ? [actions.restart]
        : kind === "installing" ? [] : [actions.project];
    return [...primary, actions.environment, actions.guide];
}

function createOnboardingUi(vscode, context) {
    const item = vscode.window.createStatusBarItem("lucentLui.tooling", vscode.StatusBarAlignment.Right, 10);
    item.name = "Lucent language tools";
    item.command = "lucentLui.showToolingStatus";
    const diagnostics = vscode.languages.createDiagnosticCollection("lucent-setup");
    let current = { kind: "checking" };
    let revision = 0;
    let disposed = false;
    let notified;

    function update(next, { notify = false } = {}) {
        if (disposed) return;
        const presentation = states[next.kind];
        if (!presentation) throw new Error("Unsupported Lucent tooling state.");
        current = { ...next };
        revision++;
        const detail = next.message || presentation.detail;
        item.text = `$(${presentation.icon}) Lucent: ${presentation.label}`;
        item.tooltip = detail;
        item.accessibilityInformation = { label: `Lucent: ${presentation.label}. ${detail}` };
        item.show();
        diagnostics.clear();
        if (presentation.blocked && next.projectPath) {
            const diagnostic = new vscode.Diagnostic(new vscode.Range(0, 0, 0, 0), detail, vscode.DiagnosticSeverity.Error);
            diagnostic.source = "Lucent setup";
            diagnostic.code = next.code || `lucent-tooling-${next.kind}`;
            diagnostics.set(vscode.Uri.file(next.projectPath), [diagnostic]);
        }
        if (next.kind === "ready") notified = undefined;
        const blocker = JSON.stringify([next.kind, next.code, next.projectPath, detail]);
        if (notify && presentation.blocked && blocker !== notified) {
            notified = blocker;
            void vscode.window.showErrorMessage(detail);
        }
    }

    async function showActions() {
        if (disposed) return;
        const ticket = revision;
        const choice = await vscode.window.showQuickPick(availableActions(current.kind, vscode.workspace.isTrusted), {
            title: "Lucent language tools", placeHolder: item.tooltip
        });
        if (!choice || disposed || ticket !== revision) return;
        await vscode.commands.executeCommand(choice.command);
    }

    const owned = { dispose() {
        if (disposed) return;
        disposed = true;
        revision++;
        diagnostics.dispose();
        item.dispose();
    } };
    context.subscriptions.push(owned);
    update(current);
    return { update, showActions, dispose: owned.dispose };
}

module.exports = { createOnboardingUi, availableActions };
