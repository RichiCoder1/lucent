"use strict";

const path = require("node:path");
const { verifyManagedToolFiles } = require("./managed-tool");
const { runDoctor } = require("./doctor-client");

function createEnvironmentCommands(vscode, context, manifest) {
    let controller;
    let generation = 0;
    let disposed = false;
    let report;
    const cancel = () => { generation++; controller?.abort(); controller = undefined; report = undefined; };
    const current = (ticket, operation) => !disposed && generation === ticket && !operation.signal.aborted;

    async function preview(value, ticket) {
        const document = await vscode.workspace.openTextDocument({ language: "json", content: JSON.stringify(value, null, 2) });
        if (!disposed && ticket === generation) await vscode.window.showTextDocument(document, { preview: true });
    }

    async function check() {
        if (disposed) return;
        cancel();
        const ticket = generation;
        const operation = controller = new AbortController();
        const folders = (vscode.workspace.workspaceFolders ?? []).filter(folder => folder.uri.scheme === "file");
        if (!folders.length) {
            await vscode.window.showInformationMessage("Open a local workspace folder before checking its Lucent environment.");
            return;
        }
        const choice = folders.length === 1 ? { folder: folders[0] } : await vscode.window.showQuickPick(
            folders.map(folder => ({ label: folder.name, description: folder.uri.fsPath, folder })),
            { title: "Check Lucent environment", placeHolder: "Choose a folder to inspect without evaluating project code" }
        );
        if (!current(ticket, operation)) return;
        if (!choice) return;
        try {
            const result = await vscode.window.withProgress({
                location: vscode.ProgressLocation.Notification, title: "Checking Lucent environment (offline, read-only)", cancellable: true
            }, async (_progress, token) => {
                const subscription = token.onCancellationRequested(() => operation.abort());
                try {
                    if (token.isCancellationRequested) operation.abort();
                    const doctor = await verifyManagedToolFiles(context.asAbsolutePath("doctor"), manifest.lucentDoctor,
                        manifest.lucentRelease.sourceCommit, "Lucent.Tools", operation.signal);
                    if (!current(ticket, operation)) return;
                    return await runDoctor({ verifiedDoctorDllPath: doctor, workspacePath: path.resolve(choice.folder.uri.fsPath), signal: operation.signal });
                } finally { subscription.dispose(); }
            });
            if (!result || !current(ticket, operation)) return;
            report = result;
            await preview(result, ticket);
        } catch {
            if (current(ticket, operation)) {
                report = { schemaVersion: 1, kind: "environment-doctor-client", status: "unavailable", capability: "environment-doctor", reason: "doctor-payload-unavailable" };
                await preview(report, ticket);
            }
        } finally { if (controller === operation) controller = undefined; }
    }

    async function copyReport() {
        if (disposed) return;
        if (!report) { await vscode.window.showInformationMessage("Run Lucent: Check Environment before copying a report."); return; }
        const ticket = generation;
        const value = report;
        await preview(value, ticket);
        if (disposed || ticket !== generation) return;
        const choice = await vscode.window.showInformationMessage("Review the environment report before sharing it. Project contents and credential values are excluded.", "Copy Report");
        if (choice === "Copy Report" && !disposed && ticket === generation) {
            await vscode.env.clipboard.writeText(JSON.stringify(value, null, 2));
        }
    }

    context.subscriptions.push(
        vscode.commands.registerCommand("lucentLui.checkEnvironment", check),
        vscode.commands.registerCommand("lucentLui.copyEnvironmentReport", copyReport),
        { dispose() { disposed = true; cancel(); } }
    );
    return { cancel };
}

module.exports = { createEnvironmentCommands };
