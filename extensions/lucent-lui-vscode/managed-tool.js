"use strict";

const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");
const { isDeepStrictEqual } = require("node:util");

const COMMIT = /^[0-9a-f]{40}$/;
const HASH = /^[0-9a-f]{64}$/;
const MAX_TOOL_BYTES = 128 * 1024 * 1024;
const TOOL_NAMES = new Set(["Lucent.Tooling.Cache", "Lucent.Tools"]);

function hashFile(file, maxBytes, signal) {
    return new Promise((resolve, reject) => {
        const hash = crypto.createHash("sha256");
        let total = 0;
        const stream = fs.createReadStream(file);
        const abort = () => stream.destroy(Object.assign(new Error("Canceled"), { name: "AbortError" }));
        signal?.addEventListener("abort", abort, { once: true });
        if (signal?.aborted) abort();
        stream.on("data", chunk => {
            total += chunk.length;
            if (total > maxBytes) stream.destroy(new Error("File exceeds the approved size limit."));
            else hash.update(chunk);
        });
        stream.once("error", error => { signal?.removeEventListener("abort", abort); reject(error); });
        stream.once("end", () => { signal?.removeEventListener("abort", abort); resolve({ bytes: total, sha256: hash.digest("hex") }); });
    });
}

async function verifyManagedToolFiles(directory, manifest, sourceCommit, toolName, signal) {
    const label = toolName === "Lucent.Tooling.Cache" ? "Cache helper" : "Environment doctor";
    if (!TOOL_NAMES.has(toolName) || !path.isAbsolute(directory) || manifest?.schemaVersion !== 1
        || manifest.entryPoint !== `${toolName}.dll` || manifest.sourceCommit !== sourceCommit
        || !COMMIT.test(sourceCommit) || !Array.isArray(manifest.files) || manifest.files.length !== 3) {
        throw new Error(`${label} manifest is unsupported.`);
    }
    const required = [`${toolName}.deps.json`, `${toolName}.dll`, `${toolName}.runtimeconfig.json`];
    const listed = manifest.files.map(file => file?.fileName);
    if (!isDeepStrictEqual([...listed].sort(), required)) throw new Error(`${label} manifest omits required files.`);
    const root = fs.lstatSync(directory);
    if (root.isSymbolicLink() || !root.isDirectory()) throw new Error(`${label} directory is not regular.`);
    // These BCL-only tools have an exact, flat three-file runtime inventory.
    const entries = fs.readdirSync(directory, { withFileTypes: true });
    if (entries.some(entry => !entry.isFile() || entry.isSymbolicLink())
        || !isDeepStrictEqual(entries.map(entry => entry.name).sort(), required)) {
        throw new Error(`${label} has missing or undeclared files.`);
    }
    let total = 0;
    for (const entry of manifest.files) {
        if (!Number.isSafeInteger(entry.bytes) || entry.bytes <= 0 || !HASH.test(entry.sha256)) {
            throw new Error(`Invalid ${label.toLowerCase()} inventory entry.`);
        }
        total += entry.bytes;
        if (total > MAX_TOOL_BYTES) throw new Error(`${label} exceeds its size limit.`);
        const file = path.join(directory, entry.fileName);
        const stat = fs.lstatSync(file);
        if (!stat.isFile() || stat.isSymbolicLink()) throw new Error(`${label} file is not regular.`);
        if (stat.size !== entry.bytes) throw new Error(`${label} file size differs from inventory.`);
        const actual = await hashFile(file, entry.bytes, signal);
        if (actual.bytes !== entry.bytes || actual.sha256 !== entry.sha256) {
            throw new Error(`${label} file hash differs from inventory.`);
        }
    }
    return path.join(directory, manifest.entryPoint);
}

module.exports = { verifyManagedToolFiles, hashFile };
