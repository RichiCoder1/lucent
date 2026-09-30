"use strict";

const childProcess = require("node:child_process");
const crypto = require("node:crypto");
const fs = require("node:fs/promises");
const path = require("node:path");

const HASH = /^[0-9a-f]{64}$/;
const COMMIT = /^[0-9a-f]{40}$/;
const MAX_INPUT_BYTES = 256 * 1024 * 1024;
const MAX_TOTAL_BYTES = 512 * 1024 * 1024;

function validateRequirements(value, projectPath) {
    if (value?.schemaVersion !== 1 || value.kind !== "project-requirements"
        || !["package", "development-source"].includes(value.state) || value.semanticReady !== false
        || typeof value.projectPath !== "string" || !path.isAbsolute(value.projectPath)
        || path.resolve(value.projectPath) !== path.resolve(projectPath)
        || !HASH.test(value.compiler?.sha256) || typeof value.compiler?.informationalVersion !== "string"
        || !value.compiler.informationalVersion
        || (value.compiler.sourceCommit !== null && !COMMIT.test(value.compiler.sourceCommit))
        || !Array.isArray(value.packages) || !Array.isArray(value.projects)
        || value.projects.length === 0 || value.projects.length > 128 || !Array.isArray(value.inputs)
        || value.inputs.length === 0 || value.inputs.length > 8192) {
        throw new Error("The Lucent bootstrap server returned unsupported project requirements. Update the extension or the advanced server override.");
    }
    const names = new Set();
    let selectedProject = false;
    for (const input of value.inputs) {
        if (typeof input?.path !== "string" || !path.isAbsolute(input.path)
            || (input.sha256 !== null && !HASH.test(input.sha256))) {
            throw new Error("The Lucent project requirement inputs are incomplete.");
        }
        const normalized = path.resolve(input.path);
        const key = process.platform === "win32" ? normalized.toLowerCase() : normalized;
        if (names.has(key)) throw new Error("The Lucent project requirement inputs contain duplicate paths.");
        names.add(key);
        if (normalized === path.resolve(projectPath) && input.sha256 !== null) selectedProject = true;
    }
    if (!selectedProject) throw new Error("The Lucent project requirements omit the selected project.");
    for (const project of value.projects) {
        if (project.state !== value.state || typeof project.projectPath !== "string" || !path.isAbsolute(project.projectPath)) {
            throw new Error("The Lucent project requirement graph is incomplete or mixed.");
        }
        if (project.state === "package" && (project.sdk?.id !== "Lucent.Lui.Sdk"
            || typeof project.sdk.version !== "string" || !project.sdk.version
            || !COMMIT.test(project.sdk.repositoryCommit) || !HASH.test(project.sdk.packageSha256)
            || project.sdk.repositoryCommit !== value.compiler.sourceCommit)) {
            throw new Error("The selected project's Lucent SDK package identity is unavailable. Restore the project's pinned packages first.");
        }
    }
    return value;
}

function readProjectRequirements(serverPath, projectPath, signal, execute = childProcess.execFile) {
    return new Promise((resolve, reject) => {
        let child;
        let timeout;
        let killTimer;
        let settled = false;
        let timedOut = false;
        const finish = (error, value) => {
            if (settled) return;
            settled = true;
            clearTimeout(timeout);
            clearTimeout(killTimer);
            signal?.removeEventListener("abort", cancel);
            error ? reject(error) : resolve(value);
        };
        const cancellationError = () => {
            const error = new Error(timedOut ? "Lucent project evaluation timed out. Check the selected project's build prerequisites."
                : "Lucent project evaluation was canceled.");
            error.name = timedOut ? "Error" : "AbortError";
            return error;
        };
        const cancel = () => {
            if (settled || killTimer) return;
            // The probe must stop its own MSBuild descendants before exiting.
            killTimer = setTimeout(() => { child?.kill(); finish(cancellationError()); }, 2000);
            try { child?.stdin?.end("cancel\n"); } catch { /* The exit callback owns closed-pipe failure. */ }
        };
        if (signal?.aborted) { finish(cancellationError()); return; }
        child = execute("dotnet", [serverPath, "--project-requirements", "--trusted-project", projectPath], {
            cwd: path.dirname(projectPath),
            env: { ...process.env, LUCENT_REQUIREMENTS_CANCEL_STDIN: "1" },
            maxBuffer: 2 * 1024 * 1024,
            windowsHide: true
        }, (error, stdout) => {
            if (signal?.aborted || timedOut) { finish(cancellationError()); return; }
            let value;
            try { value = JSON.parse(stdout); }
            catch {
                finish(new Error(`Lucent project requirement check failed. Restore the selected project or update its tooling. ${error?.code ?? "The bootstrap server returned no supported result."}`));
                return;
            }
            if (error) {
                const detail = value?.schemaVersion === 1 && value.kind === "project-requirements"
                    && value.state === "unavailable" && typeof value.error?.code === "string"
                    && typeof value.error?.message === "string"
                    ? `${value.error.code}: ${value.error.message.slice(0, 2048)}` : "The project prerequisite check did not succeed.";
                finish(new Error(`Lucent language services remain unavailable. ${detail}`));
                return;
            }
            try { finish(null, validateRequirements(value, projectPath)); }
            catch (failure) { finish(failure); }
        });
        if (!settled) {
            child?.stdin?.on("error", () => {});
            signal?.addEventListener("abort", cancel, { once: true });
            timeout = setTimeout(() => { timedOut = true; cancel(); }, 60000);
            if (signal?.aborted) cancel();
        }
    });
}

function assertCompatibleCompiler(requirements, identity) {
    const required = requirements.compiler;
    if (required.sha256 !== identity.compiler?.sha256
        || required.informationalVersion !== identity.compiler?.informationalVersion
        || (required.sourceCommit !== null && required.sourceCommit !== identity.sourceCommit)) {
        throw new Error("The selected project's Lucent compiler differs from this language server. Install matching tools or select a compatible absolute server override; project package pins have not changed.");
    }
}

async function verifyRequirementInputs(requirements, signal) {
    let total = 0;
    for (const input of requirements.inputs) {
        signal?.throwIfAborted();
        try {
            const file = await fs.open(input.path, "r");
            try {
                const stat = await file.stat();
                if (input.sha256 === null || !stat.isFile() || stat.size > MAX_INPUT_BYTES
                    || total + stat.size > MAX_TOTAL_BYTES) {
                    throw new Error("changed");
                }
                const hash = crypto.createHash("sha256");
                let bytes = 0;
                for await (const chunk of file.createReadStream({ autoClose: false })) {
                    signal?.throwIfAborted();
                    bytes += chunk.length;
                    total += chunk.length;
                    if (bytes > MAX_INPUT_BYTES || total > MAX_TOTAL_BYTES) throw new Error("changed");
                    hash.update(chunk);
                }
                if (bytes !== stat.size || hash.digest("hex") !== input.sha256) throw new Error("changed");
            } finally { await file.close(); }
        } catch (error) {
            if (signal?.aborted) throw error;
            if (input.sha256 === null && error.code === "ENOENT") continue;
            throw new Error(`Lucent project inputs changed during tooling selection: ${input.path}. Restore if needed, then restart language services.`);
        }
    }
}

module.exports = { readProjectRequirements, validateRequirements, assertCompatibleCompiler, verifyRequirementInputs };
