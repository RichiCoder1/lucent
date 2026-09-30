"use strict";

const path = require("node:path");
const { verifyManagedToolFiles, verifyNuGetDoctorFiles } = require("./managed-tool");
const { runDoctor, runFeedDoctor } = require("./doctor-client");

function createEnvironmentCommands(vscode, context, manifest, checkTrustedProject) {
    let controller;
    let generation = 0;
    let disposed = false;
    let report;
    const cancel = () => { generation++; controller?.abort(); controller = undefined; report = undefined; };
    const current = (ticket, operation) => !disposed && generation === ticket && !operation.signal.aborted;

    async function preview(value, ticket, operation) {
        const document = await vscode.workspace.openTextDocument({ language: "json", content: JSON.stringify(value, null, 2) });
        if (operation?.signal.aborted) { if (report === value) report = undefined; return; }
        if (!disposed && ticket === generation) await vscode.window.showTextDocument(document, { preview: true });
    }

    async function check(native = false) {
        if (disposed) return;
        cancel();
        const ticket = generation;
        const operation = controller = new AbortController();
        const folders = (vscode.workspace.workspaceFolders ?? []).filter(folder => folder.uri.scheme === "file");
        if (!folders.length && !native) {
            await vscode.window.showInformationMessage("Open a local workspace folder before checking its Lucent environment.");
            return;
        }
        const choice = folders.length === 0 ? { folder: { uri: { fsPath: context.asAbsolutePath(".") } } }
            : folders.length === 1 ? { folder: folders[0] } : await vscode.window.showQuickPick(
            folders.map(folder => ({ label: folder.name, description: folder.uri.fsPath, folder })),
            { title: native ? "Check Windows native prerequisites" : "Check Lucent environment", placeHolder: "Choose a workspace host without evaluating project code" }
        );
        if (!current(ticket, operation)) return;
        if (!choice) return;
        try {
            const result = await vscode.window.withProgress({
                location: vscode.ProgressLocation.Notification, title: native ? "Checking installed Windows x64 native prerequisites (offline, read-only)" : "Checking Lucent environment (offline, read-only)", cancellable: true
            }, async (_progress, token) => {
                const subscription = token.onCancellationRequested(() => operation.abort());
                try {
                    if (token.isCancellationRequested) operation.abort();
                    const doctor = await verifyManagedToolFiles(context.asAbsolutePath("doctor"), manifest.lucentDoctor,
                        manifest.lucentRelease.sourceCommit, "Lucent.Tools", operation.signal);
                    if (!current(ticket, operation)) return;
                    return await runDoctor({ verifiedDoctorDllPath: doctor, workspacePath: path.resolve(choice.folder.uri.fsPath),
                        mode: native ? "native" : "static", signal: operation.signal });
                } finally { subscription.dispose(); }
            });
            if (!result || !current(ticket, operation)) return;
            report = result;
            await preview(result, ticket, operation);
        } catch {
            if (current(ticket, operation)) {
                report = { schemaVersion: 1, kind: "environment-doctor-client", status: "unavailable", capability: "environment-doctor", reason: "doctor-payload-unavailable" };
                await preview(report, ticket, operation);
            }
        } finally { if (controller === operation) controller = undefined; }
    }

    async function copyReport() {
        if (disposed) return;
        if (!report) { await vscode.window.showInformationMessage("Run a Lucent environment check before copying a report."); return; }
        const ticket = generation;
        const value = report;
        await preview(value, ticket);
        if (disposed || ticket !== generation) return;
        const choice = await vscode.window.showInformationMessage("Review the environment report before sharing it. Project contents and credential values are excluded.", "Copy Report");
        if (choice === "Copy Report" && !disposed && ticket === generation) {
            await vscode.env.clipboard.writeText(JSON.stringify(value, null, 2));
        }
    }

    async function checkFeed(online) {
        if (disposed) return;
        cancel();
        const ticket = generation;
        const operation = controller = new AbortController();
        const eligible = () => current(ticket, operation) && (!online || vscode.workspace.isTrusted);
        try {
            if (online && !vscode.workspace.isTrusted) {
                await vscode.window.showInformationMessage("Trust this workspace before contacting its configured feeds. Anonymous requests do not use credentials or restore packages.");
                return;
            }
            const folders = (vscode.workspace.workspaceFolders ?? []).filter(folder => folder.uri.scheme === "file");
            if (!folders.length) {
                await vscode.window.showInformationMessage("Open a local workspace folder before inspecting its NuGet configuration.");
                return;
            }
            const choice = folders.length === 1 ? { folder: folders[0] } : await vscode.window.showQuickPick(
                folders.map(folder => ({ label: folder.name, description: folder.uri.fsPath, folder })),
                { title: online ? "Check anonymous feed access" : "Check NuGet feed configuration", placeHolder: "Choose the workspace configuration to inspect" });
            if (!choice || !eligible()) return;
            const packageId = await vscode.window.showInputBox({ title: "NuGet package to inspect", value: "Lucent.Core",
                prompt: "Package ID for source mapping. This does not evaluate the selected project.",
                validateInput: value => /^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$/.test(value) ? undefined : "Enter a package ID using letters, digits, dots, underscores or hyphens (maximum 100 characters)." });
            if (!packageId || !eligible()) return;
            const version = await vscode.window.showInputBox({ title: "Exact package version", value: manifest.lucentRelease.version || "",
                placeHolder: "Exact package version (no ranges or wildcards)",
                prompt: "Enter the exact version to observe. A suggested release version is not an evaluated project pin.",
                validateInput: value => /^[0-9][0-9A-Za-z.+-]{0,127}$/.test(value) ? undefined : "Enter an exact version without ranges or wildcards (maximum 128 characters)." });
            if (!version || !eligible()) return;
            if (online) {
                const approval = await vscode.window.showInformationMessage("Contact this workspace's eligible configured feed destinations with anonymous requests only? No credentials, credential plugins, package restore or downloads of package contents are used. Private availability remains unverified.", "Check Anonymous Access");
                if (approval !== "Check Anonymous Access" || !eligible()) return;
            }
            const result = await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification,
                title: online ? "Checking anonymous feed access (no credentials or restore)" : "Checking NuGet configuration (offline, read-only)", cancellable: true
            }, async (_progress, token) => {
                const subscription = token.onCancellationRequested(() => operation.abort());
                try {
                    if (token.isCancellationRequested) operation.abort();
                    if (!eligible()) return;
                    const doctor = await verifyNuGetDoctorFiles(context.asAbsolutePath("nuget-doctor"), manifest.lucentNuGetDoctor,
                        manifest.lucentRelease.sourceCommit, operation.signal);
                    if (!eligible()) return;
                    return await runFeedDoctor({ verifiedDoctorDllPath: doctor, workspacePath: path.resolve(choice.folder.uri.fsPath),
                        packageId, version, online, generation: `feed-${ticket}`, signal: operation.signal });
                } finally { subscription.dispose(); }
            });
            if (!result || !eligible()) return;
            report = result;
            await preview(result, ticket, operation);
        } catch {
            if (eligible()) {
                report = { schemaVersion: 1, kind: "environment-doctor-client", status: "unavailable", capability: "environment-doctor", reason: "doctor-payload-unavailable" };
                await preview(report, ticket, operation);
            }
        } finally { if (controller === operation) controller = undefined; }
    }

    async function checkProject() {
        if (disposed) return;
        cancel();
        const ticket = generation;
        const operation = controller = new AbortController();
        if (!vscode.workspace.isTrusted) {
            await vscode.window.showInformationMessage("Trust this workspace before checking its Lucent project. MSBuild evaluation may execute project-supplied tooling.");
            return;
        }
        try {
            const result = await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification,
                title: "Checking trusted Lucent project (MSBuild may execute project tooling; no restore or install)", cancellable: true
            }, async (_progress, token) => {
                const subscription = token.onCancellationRequested(() => operation.abort());
                try {
                    if (token.isCancellationRequested) operation.abort();
                    if (!current(ticket, operation)) return;
                    return await checkTrustedProject?.(operation.signal, () => current(ticket, operation));
                } finally { subscription.dispose(); }
            });
            if (!result || !current(ticket, operation) || !vscode.workspace.isTrusted) return;
            report = result;
            await preview(result, ticket, operation);
        } catch (error) {
            if (current(ticket, operation) && vscode.workspace.isTrusted) {
                const reasons = ["project-not-selected", "host-unavailable", "server-unavailable", "requirements-unavailable", "inputs-changed", "server-changed"];
                report = { schemaVersion: 1, kind: "trusted-project-doctor", scope: "trusted-project", status: "unavailable",
                    reason: reasons.includes(error?.doctorReason) ? error.doctorReason : "requirements-unavailable",
                    semanticReadiness: "notChecked", managedBuildReadiness: "notChecked" };
                await preview(report, ticket, operation);
            }
        } finally { if (controller === operation) controller = undefined; }
    }

    context.subscriptions.push(
        vscode.commands.registerCommand("lucentLui.checkEnvironment", () => check()),
        vscode.commands.registerCommand("lucentLui.checkNativePrerequisites", () => check(true)),
        vscode.commands.registerCommand("lucentLui.checkFeedConfiguration", () => checkFeed(false)),
        vscode.commands.registerCommand("lucentLui.checkAnonymousFeedAccess", () => checkFeed(true)),
        vscode.commands.registerCommand("lucentLui.checkTrustedProject", checkProject),
        vscode.commands.registerCommand("lucentLui.copyEnvironmentReport", copyReport),
        { dispose() { disposed = true; cancel(); } }
    );
    return { cancel };
}

module.exports = { createEnvironmentCommands };
