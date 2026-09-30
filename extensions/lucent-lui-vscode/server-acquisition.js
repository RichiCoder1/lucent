"use strict";

const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");
const { selectApprovedEntry } = require("./server-cache");

const API = "https://api.github.com/repos/RichiCoder1/lucent";
const MAX_COMPLETE_BYTES = 512 * 1024 * 1024;
const MAX_METADATA_BYTES = 128 * 1024;
const DEADLINE_MS = 120_000;

function fail(code, message) {
    return Object.assign(new Error(message), { code, acquisitionFailure: true });
}

function checkAbort(signal) {
    if (signal?.aborted) throw fail("cancelled", "Lucent tooling acquisition was cancelled.");
}

async function boundedBody(response, limit, signal) {
    const reader = response.body?.getReader();
    if (!reader) throw fail("response", "GitHub returned an empty response.");
    const chunks = [];
    let size = 0;
    let complete = false;
    try {
        for (;;) {
            checkAbort(signal);
            const { done, value } = await reader.read();
            if (done) break;
            size += value.byteLength;
            if (size > limit) throw fail("size", "GitHub response exceeds its size limit.");
            chunks.push(value);
        }
        complete = true;
    } finally {
        if (!complete) await reader.cancel().catch(() => {});
        reader.releaseLock();
    }
    return Buffer.concat(chunks, size);
}

async function apiJson(fetcher, url, token, signal) {
    const response = await fetcher(url, {
        redirect: "manual", signal,
        headers: { Accept: "application/vnd.github+json", Authorization: `Bearer ${token}`, "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "lucent-lui-vscode" }
    });
    if (response.status !== 200) {
        await response.body?.cancel().catch(() => {});
        throw fail(response.status === 401 || response.status === 403 ? "authentication" : "metadata", `GitHub metadata request failed (${response.status}).`);
    }
    try { return JSON.parse((await boundedBody(response, MAX_METADATA_BYTES, signal)).toString("utf8")); }
    catch (error) { if (error.acquisitionFailure) throw error; throw fail("metadata", "GitHub returned invalid metadata."); }
}

function validateRun(run, workflow, anchor, releaseVersion) {
    const actions = anchor.githubActions;
    if (run?.id !== actions.runId || run.run_attempt !== actions.runAttempt
        || run.head_sha !== actions.headSha
        || run.repository?.full_name !== actions.repository
        || run.head_repository?.full_name !== actions.repository
        || !Number.isSafeInteger(run.repository?.id) || run.repository.id <= 0
        || run.head_repository?.id !== run.repository.id
        || workflow?.path !== actions.workflow || workflow.id !== run.workflow_id
        || !Number.isSafeInteger(run.run_number) || releaseVersion !== `0.3.0-dev.${run.run_number}.1`
        || !["push", "workflow_dispatch"].includes(run.event) || run.head_branch !== "main"
        || run.status !== "completed" || run.conclusion !== "success") {
        throw fail("run_identity", "The GitHub run attempt does not match the approved release.");
    }
}

function validateArtifact(artifact, run, anchor) {
    const actions = anchor.githubActions;
    if (artifact?.id !== actions.artifact.id || artifact.name !== actions.artifact.name
        || artifact.digest !== actions.artifact.digest || artifact.expired !== false
        || artifact.workflow_run?.id !== actions.runId
        || artifact.workflow_run?.head_sha !== actions.headSha
        || artifact.workflow_run?.repository_id !== run.repository.id
        || artifact.workflow_run?.head_repository_id !== run.repository.id
        || !Number.isSafeInteger(artifact.size_in_bytes) || artifact.size_in_bytes <= 0
        || artifact.size_in_bytes > MAX_COMPLETE_BYTES) {
        throw fail(artifact?.expired ? "artifact_expired" : "artifact_identity", "The GitHub artifact is expired or differs from the approved release.");
    }
}

async function redirectUrl(fetcher, artifactId, token, signal) {
    const response = await fetcher(`${API}/actions/artifacts/${artifactId}/zip`, {
        redirect: "manual", signal,
        headers: { Accept: "application/vnd.github+json", Authorization: `Bearer ${token}`, "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "lucent-lui-vscode" }
    });
    await response.body?.cancel().catch(() => {});
    if (response.status === 410) throw fail("artifact_expired", "The approved GitHub artifact has expired.");
    if (response.status !== 302) throw fail("download", `GitHub did not return an artifact redirect (${response.status}).`);
    const location = response.headers.get("location");
    let url;
    try { url = new URL(location); } catch { throw fail("redirect", "GitHub returned an invalid artifact redirect."); }
    if (url.protocol !== "https:" || url.username || url.password) throw fail("redirect", "GitHub returned an unsafe artifact redirect.");
    return url.toString();
}

async function download(fetcher, url, artifact, stage, signal) {
    // The signed URL is short lived. Do not pass the GitHub token to it.
    const response = await fetcher(url, { redirect: "error", signal, headers: { "User-Agent": "lucent-lui-vscode" } });
    if (response.status !== 200) {
        await response.body?.cancel().catch(() => {});
        throw fail("download", `The signed artifact download failed (${response.status}).`);
    }
    const declared = response.headers.get("content-length");
    if (declared !== null && (!/^\d+$/.test(declared) || Number(declared) !== artifact.size_in_bytes)) {
        await response.body?.cancel().catch(() => {});
        throw fail("size", "The signed artifact length differs from GitHub metadata.");
    }
    const reader = response.body?.getReader();
    if (!reader) throw fail("download", "The signed artifact response has no body.");
    const target = path.join(stage, "complete-release.zip");
    let output;
    const hash = crypto.createHash("sha256");
    let bytes = 0;
    let complete = false;
    let primaryError;
    try {
        output = await fs.promises.open(target, "wx", 0o600);
        for (;;) {
            checkAbort(signal);
            const { done, value } = await reader.read();
            if (done) break;
            bytes += value.byteLength;
            if (bytes > artifact.size_in_bytes || bytes > MAX_COMPLETE_BYTES) throw fail("size", "The signed artifact exceeds its approved size.");
            hash.update(value);
            for (let offset = 0; offset < value.byteLength;) {
                const { bytesWritten } = await output.write(value, offset, value.byteLength - offset);
                offset += bytesWritten;
            }
        }
        checkAbort(signal);
        if (bytes !== artifact.size_in_bytes || `sha256:${hash.digest("hex")}` !== artifact.digest) {
            throw fail("digest", "The downloaded artifact differs from GitHub's approved digest.");
        }
        await output.sync();
        checkAbort(signal);
        complete = true;
        return { completeArchivePath: target, artifactBytes: bytes };
    } catch (error) {
        primaryError = error;
        throw error;
    } finally {
        if (!complete) await reader.cancel().catch(() => {});
        reader.releaseLock();
        if (output) {
            try { await output.close(); } catch (error) { if (!primaryError) throw error; }
        }
    }
}

async function acquireApprovedRelease(options) {
    const selected = selectApprovedEntry(options.requirement, options.approvedEntries, options.clientRelease);
    if (selected.status !== "approved") return selected;
    if (typeof options.token !== "string" || !options.token
        || typeof options.stagingRoot !== "string" || !path.isAbsolute(options.stagingRoot)) {
        throw fail("request", "Authenticated acquisition requires a token and absolute workspace-host staging root.");
    }
    const fetcher = options.fetch ?? globalThis.fetch;
    const actions = selected.entry.anchor.githubActions;
    const controller = new AbortController();
    const cancel = () => controller.abort();
    options.signal?.addEventListener("abort", cancel, { once: true });
    let timedOut = false;
    const deadline = setTimeout(() => { timedOut = true; cancel(); }, DEADLINE_MS);
    let stage;
    try {
        checkAbort(options.signal);
        const run = await apiJson(fetcher, `${API}/actions/runs/${actions.runId}/attempts/${actions.runAttempt}`, options.token, controller.signal);
        const workflow = await apiJson(fetcher, `${API}/actions/workflows/tests.yml`, options.token, controller.signal);
        validateRun(run, workflow, selected.entry.anchor, selected.entry.releaseVersion);
        const artifact = await apiJson(fetcher, `${API}/actions/artifacts/${actions.artifact.id}`, options.token, controller.signal);
        validateArtifact(artifact, run, selected.entry.anchor);
        const url = await redirectUrl(fetcher, actions.artifact.id, options.token, controller.signal);
        fs.mkdirSync(options.stagingRoot, { recursive: true });
        if (fs.lstatSync(options.stagingRoot).isSymbolicLink()) throw fail("staging", "Acquisition staging root is a link.");
        stage = fs.mkdtempSync(path.join(options.stagingRoot, "acquire-"));
        const result = await download(fetcher, url, artifact, stage, controller.signal);
        checkAbort(controller.signal);
        const owned = stage;
        stage = undefined;
        return { status: "downloaded", entry: selected.entry, ...result, cleanup: () => fs.rmSync(owned, { recursive: true, force: true }) };
    } catch (error) {
        if (options.signal?.aborted) return { status: "cancelled" };
        if (timedOut) throw fail("timeout", "Lucent tooling acquisition timed out.");
        // Do not expose fetch exception text: signed URLs can contain credentials.
        if (error.acquisitionFailure) throw error;
        throw fail("network", "GitHub artifact acquisition failed.");
    } finally {
        clearTimeout(deadline);
        controller.abort();
        options.signal?.removeEventListener("abort", cancel);
        if (stage) {
            try { fs.rmSync(stage, { recursive: true, force: true }); }
            catch { /* Preserve the acquisition failure or cancellation status. */ }
        }
    }
}

module.exports = { acquireApprovedRelease };
