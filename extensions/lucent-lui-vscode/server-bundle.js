"use strict";

const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");

const MAX_FILE_BYTES = 512 * 1024 * 1024;
const MAX_TOTAL_BYTES = 1024 * 1024 * 1024;

function canonical(value) {
    if (Array.isArray(value)) return value.map(canonical);
    if (value && typeof value === "object") {
        return Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])]));
    }
    return value;
}

function same(left, right) {
    return JSON.stringify(canonical(left)) === JSON.stringify(canonical(right));
}

function sha256(bytes) {
    return crypto.createHash("sha256").update(bytes).digest("hex");
}

function safeName(name) {
    return typeof name === "string" && name.length > 0 && !name.startsWith("/")
        && !name.includes("\\") && !name.includes(":") && !/[\x00-\x1f]/.test(name)
        && name.split("/").every(part => part && part !== "." && part !== "..");
}

function regularFile(root, name) {
    if (!safeName(name)) throw new Error(`Unsafe bundled server path: ${name}`);
    let current = root;
    if (!fs.lstatSync(current).isDirectory()) throw new Error("Bundled server root is not a directory.");
    const parts = name.split("/");
    for (let index = 0; index < parts.length; index++) {
        current = path.join(current, parts[index]);
        const entry = fs.lstatSync(current);
        if (entry.isSymbolicLink() || (index === parts.length - 1 ? !entry.isFile() : !entry.isDirectory())) {
            throw new Error(`Bundled server path is not a regular file: ${name}`);
        }
    }
    return current;
}

function actualFiles(root) {
    const result = [];
    function visit(directory, prefix) {
        for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
            if (entry.isSymbolicLink()) throw new Error(`Bundled server contains a link: ${entry.name}`);
            const name = prefix ? `${prefix}/${entry.name}` : entry.name;
            if (!safeName(name)) throw new Error(`Unsafe bundled server path: ${name}`);
            if (entry.isDirectory()) visit(path.join(directory, entry.name), name);
            else if (entry.isFile()) result.push(name);
            else throw new Error(`Unsupported bundled server entry: ${name}`);
        }
    }
    visit(root, "");
    return result.sort();
}

function verifyBundledServer(root, expected, sourceCommit) {
    if (!expected || !expected.identity || !/^[0-9a-f]{64}$/.test(expected.filesSha256)
        || expected.identity.sourceCommit !== sourceCommit) {
        throw new Error("The bundled server has no matching release identity.");
    }
    const manifestPath = regularFile(root, "lucent-server-files.json");
    if (fs.statSync(manifestPath).size > 2 * 1024 * 1024) throw new Error("Bundled server inventory is too large.");
    const manifestBytes = fs.readFileSync(manifestPath);
    const manifest = JSON.parse(manifestBytes.toString("utf8"));
    if (manifest.schemaVersion !== 1 || !Array.isArray(manifest.files) || manifest.files.length > 10000
        || sha256(Buffer.from(JSON.stringify(canonical(manifest)))) !== expected.filesSha256) {
        throw new Error("Bundled server inventory differs from the release identity.");
    }
    const names = new Set();
    let total = manifestBytes.length;
    const hashes = new Map();
    for (const file of manifest.files) {
        const name = file.fileName;
        if (!safeName(name) || name === "lucent-server-files.json" || names.has(name.toLowerCase())
            || !Number.isInteger(file.bytes) || file.bytes < 1 || file.bytes > MAX_FILE_BYTES
            || !/^[0-9a-f]{64}$/.test(file.sha256)) {
            throw new Error(`Invalid bundled server inventory entry: ${name}`);
        }
        names.add(name.toLowerCase());
        const filePath = regularFile(root, name);
        if (fs.statSync(filePath).size !== file.bytes) throw new Error(`Bundled server file differs from inventory: ${name}`);
        const bytes = fs.readFileSync(filePath);
        total += bytes.length;
        if (total > MAX_TOTAL_BYTES || bytes.length !== file.bytes || sha256(bytes) !== file.sha256) {
            throw new Error(`Bundled server file differs from inventory: ${name}`);
        }
        hashes.set(name, file.sha256);
    }
    const expectedFiles = [...manifest.files.map(file => file.fileName), "lucent-server-files.json"].sort();
    if (!same(actualFiles(root), expectedFiles)) throw new Error("Bundled server contains missing or undeclared files.");
    const identity = JSON.parse(fs.readFileSync(regularFile(root, "lucent-server.json"), "utf8"));
    if (!same(identity, expected.identity) || hashes.get("Lucent.Lui.LanguageServer.dll") !== identity.server.sha256
        || hashes.get("Lucent.Lui.Compiler.dll") !== identity.compiler.sha256) {
        throw new Error("Bundled server assembly identity differs from the release identity.");
    }
    return { serverPath: regularFile(root, "Lucent.Lui.LanguageServer.dll"), identity };
}

module.exports = { verifyBundledServer };
