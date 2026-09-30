"use strict";

const assert = require("node:assert/strict");
const crypto = require("node:crypto");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { test } = require("node:test");
const { acquireApprovedRelease } = require("./server-acquisition");

const sourceCommit = "a".repeat(40);
const sha = bytes => crypto.createHash("sha256").update(bytes).digest("hex");
const complete = Buffer.from("a complete release ZIP for transport tests");
const artifactDigest = `sha256:${sha(complete)}`;
const signedUrl = "https://signed.example.test/archive?signature=secret";

function fixture(t) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "lucent-acquisition-"));
    t.after(() => fs.rmSync(root, { recursive: true, force: true }));
    const compiler = { sha256: "b".repeat(64), informationalVersion: `0.2.0+${sourceCommit}`, sourceCommit };
    const language = { id: "lui", version: "preview", featureLevel: "preview-1" };
    const entry = {
        releaseVersion: "0.3.0-dev.101.1",
        sdk: { id: "Lucent.Lui.Sdk", version: "0.3.0-dev.101.1", packageSha256: "c".repeat(64) },
        server: { artifact: { bytes: 123, sha256: "d".repeat(64) }, filesSha256: "e".repeat(64),
            identity: { sourceCommit, compiler, language, protocol: { id: "lucent-lui", major: 1, minor: 0 } } },
        anchor: { kind: "bundled-catalog", sourceCommit, descriptorSha256: "f".repeat(64),
            githubActions: { repository: "RichiCoder1/lucent", workflow: ".github/workflows/tests.yml",
                runId: 36686517408, runAttempt: 1, headSha: sourceCommit,
                artifact: { id: 11084179291, name: "complete-release-36686517408-1", digest: artifactDigest } } }
    };
    const requirement = { schemaVersion: 1, kind: "project-requirements", state: "package", semanticReady: false,
        compiler, projects: [{ state: "package", sdk: { ...entry.sdk, repositoryCommit: sourceCommit } }] };
    const clientRelease = { language, protocol: { id: "lucent-lui", minimum: { major: 1, minor: 0 }, maximumInclusive: { major: 1, minor: 0 } } };
    const run = { id: 36686517408, run_attempt: 1, run_number: 101, workflow_id: 55, head_sha: sourceCommit,
        repository: { id: 77, full_name: "RichiCoder1/lucent" }, head_repository: { id: 77, full_name: "RichiCoder1/lucent" },
        event: "push", head_branch: "main", status: "completed", conclusion: "success" };
    const artifact = { id: 11084179291, name: "complete-release-36686517408-1", digest: artifactDigest,
        expired: false, size_in_bytes: complete.length, workflow_run: { id: run.id, head_sha: sourceCommit,
            repository_id: 77, head_repository_id: 77 } };
    return { root, entry, run, artifact, workflow: { id: 55, path: ".github/workflows/tests.yml" }, options: { requirement, approvedEntries: [entry], clientRelease,
        token: "private-token", stagingRoot: path.join(root, "staging") } };
}

function reply(body, status = 200, headers = {}) {
    return new Response(body, { status, headers });
}

function transport(fixture, observations, overrides = {}) {
    return async (url, options) => {
        observations.push({ url, options });
        if (url.includes("/runs/")) return reply(JSON.stringify(overrides.run ?? fixture.run));
        if (url.includes("/workflows/")) return reply(JSON.stringify(overrides.workflow ?? fixture.workflow));
        if (url.endsWith("/zip")) return reply(null, 302, { location: overrides.redirect ?? signedUrl });
        if (url.includes("/actions/artifacts/")) return reply(JSON.stringify(overrides.artifact ?? fixture.artifact));
        if (url === signedUrl) return reply(overrides.complete ?? complete, 200, { "content-length": String((overrides.complete ?? complete).length) });
        throw new Error("unexpected request");
    };
}

test("approved release checks fixed metadata before downloading without forwarding auth", async t => {
    const data = fixture(t);
    const calls = [];
    const result = await acquireApprovedRelease({ ...data.options, fetch: transport(data, calls) });
    assert.equal(result.status, "downloaded");
    assert.deepEqual(fs.readFileSync(result.completeArchivePath), complete);
    assert.equal(calls.length, 5);
    assert.deepEqual(calls.slice(0, 4).map(call => call.options.redirect), ["manual", "manual", "manual", "manual"]);
    assert.ok(calls.slice(0, 4).every(call => call.options.headers.Authorization === "Bearer private-token"));
    assert.equal(calls[4].url, signedUrl);
    assert.equal(calls[4].options.headers.Authorization, undefined);
    assert.equal(calls[4].options.redirect, "error");
    result.cleanup();
    assert.equal(fs.readdirSync(data.options.stagingRoot).length, 0);
});

test("no approved compiler returns before token or network access", async t => {
    const data = fixture(t);
    let fetched = 0;
    const result = await acquireApprovedRelease({ ...data.options, approvedEntries: [], token: undefined,
        fetch: () => { fetched++; throw new Error("unexpected fetch"); } });
    assert.equal(result.status, "no-anchor");
    assert.equal(fetched, 0);
});

test("forged run and artifact metadata fail before signed download", async t => {
    const data = fixture(t);
    for (const [overrides, code] of [
        [{ run: { ...data.run, conclusion: "failure" } }, "run_identity"],
        [{ artifact: { ...data.artifact, digest: `sha256:${"0".repeat(64)}` } }, "artifact_identity"],
        [{ artifact: { ...data.artifact, expired: true } }, "artifact_expired"]
    ]) {
        const calls = [];
        await assert.rejects(acquireApprovedRelease({ ...data.options, fetch: transport(data, calls, overrides) }), error => error.code === code);
        assert.ok(calls.length <= 3);
    }
});

test("unsafe redirect and excess bytes reject without retained staging", async t => {
    const data = fixture(t);
    await assert.rejects(acquireApprovedRelease({ ...data.options,
        fetch: transport(data, [], { redirect: "http://signed.example.test/archive" }) }), error => error.code === "redirect");
    await assert.rejects(acquireApprovedRelease({ ...data.options,
        fetch: transport(data, [], { complete: Buffer.concat([complete, Buffer.from("extra")]) }) }), error => error.code === "size");
    assert.equal(fs.existsSync(data.options.stagingRoot) ? fs.readdirSync(data.options.stagingRoot).length : 0, 0);
});

test("cancellation after the final download chunk removes only its stage", async t => {
    const data = fixture(t);
    const controller = new AbortController();
    const fetcher = transport(data, []);
    const result = await acquireApprovedRelease({ ...data.options, signal: controller.signal, fetch: async (url, options) => {
        if (url === signedUrl) {
            let reads = 0;
            return { status: 200, headers: new Headers({ "content-length": String(complete.length) }),
                body: { getReader: () => ({
                    read: async () => {
                        if (reads++ === 0) return { done: false, value: complete };
                        controller.abort();
                        return { done: true };
                    },
                    cancel: async () => {},
                    releaseLock: () => {}
                }) } };
        }
        return fetcher(url, options);
    } });
    assert.equal(result.status, "cancelled");
    assert.equal(fs.readdirSync(data.options.stagingRoot).length, 0);
});

test("network failures do not expose signed download URLs", async t => {
    const data = fixture(t);
    const fetcher = transport(data, []);
    await assert.rejects(acquireApprovedRelease({ ...data.options, fetch: (url, options) => {
        if (url === signedUrl) throw Object.assign(new Error(`Failed ${signedUrl}`), { code: "ENOTFOUND" });
        return fetcher(url, options);
    } }), error => error.code === "network" && !error.message.includes("signature="));
    assert.equal(fs.readdirSync(data.options.stagingRoot).length, 0);
});

test("rejected metadata and downloads cancel unread response bodies", async t => {
    const data = fixture(t);
    const tracked = (bytes, status = 200, headers = {}) => {
        let cancelled = false;
        const body = new ReadableStream({ start(controller) { controller.enqueue(bytes); }, cancel() { cancelled = true; } });
        return { response: new Response(body, { status, headers }), wasCancelled: () => cancelled };
    };
    for (const caseName of ["unauthorized", "metadata-overflow", "length-mismatch", "stream-overflow"]) {
        const body = tracked(caseName === "metadata-overflow" ? Buffer.alloc(128 * 1024 + 1) :
            caseName === "stream-overflow" ? Buffer.concat([complete, Buffer.from("extra")]) : complete,
            caseName === "unauthorized" ? 401 : 200,
            caseName === "length-mismatch" ? { "content-length": String(complete.length + 1) } : {});
        const ordinary = transport(data, []);
        await assert.rejects(acquireApprovedRelease({ ...data.options, fetch: (url, options) => {
            if ((caseName === "unauthorized" || caseName === "metadata-overflow") && url.includes("/runs/")) return body.response;
            if ((caseName === "length-mismatch" || caseName === "stream-overflow") && url === signedUrl) return body.response;
            return ordinary(url, options);
        } }));
        assert.equal(body.wasCancelled(), true, `${caseName} retained an unread response body`);
    }
    assert.equal(fs.existsSync(data.options.stagingRoot) ? fs.readdirSync(data.options.stagingRoot).length : 0, 0);
});

test("cancelling a stalled partial response releases its reader and stage", async t => {
    const data = fixture(t);
    const cancellation = new AbortController();
    const ordinary = transport(data, []);
    let readerCancelled = false;
    let resolveSecondRead;
    const secondReadStarted = new Promise(resolve => { resolveSecondRead = resolve; });
    let reads = 0;
    const operation = acquireApprovedRelease({ ...data.options, signal: cancellation.signal, fetch: (url, options) => {
        if (url !== signedUrl) return ordinary(url, options);
        return { status: 200, headers: new Headers(), body: { getReader: () => ({
            read: () => {
                if (++reads === 1) return Promise.resolve({ done: false, value: complete.subarray(0, 5) });
                const pending = new Promise((_resolve, reject) => options.signal.addEventListener("abort", () => reject(new Error("aborted")), { once: true }));
                resolveSecondRead();
                return pending;
            },
            cancel: async () => { readerCancelled = true; }, releaseLock: () => {}
        }) } };
    } });
    await secondReadStarted;
    assert.equal(reads, 2);
    cancellation.abort();
    assert.equal((await operation).status, "cancelled");
    assert.equal(readerCancelled, true);
    assert.equal(fs.readdirSync(data.options.stagingRoot).length, 0);
});

test("a failed output open cancels the signed response reader", async t => {
    const data = fixture(t);
    const ordinary = transport(data, []);
    let cancelled = false;
    const originalOpen = fs.promises.open;
    fs.promises.open = async () => { throw new Error("disk failure"); };
    try {
        await assert.rejects(acquireApprovedRelease({ ...data.options, fetch: (url, options) => {
            if (url !== signedUrl) return ordinary(url, options);
            return new Response(new ReadableStream({ start(controller) { controller.enqueue(complete); },
                cancel() { cancelled = true; } }), { status: 200 });
        } }), error => error.code === "network");
    } finally { fs.promises.open = originalOpen; }
    assert.equal(cancelled, true);
    assert.equal(fs.readdirSync(data.options.stagingRoot).length, 0);
});
