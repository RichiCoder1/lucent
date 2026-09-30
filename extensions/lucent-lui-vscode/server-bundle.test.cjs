"use strict";

const assert = require("node:assert/strict");
const crypto = require("node:crypto");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const { verifyBundledServer } = require("./server-bundle");

const hash = bytes => crypto.createHash("sha256").update(bytes).digest("hex");
const sourceCommit = "a".repeat(40);
function canonical(value) {
    if (Array.isArray(value)) return value.map(canonical);
    if (value && typeof value === "object") {
        return Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])]));
    }
    return value;
}

function fixture() {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "lucent-bundle-test-"));
    const server = Buffer.from("server bytes");
    const compiler = Buffer.from("compiler bytes");
    const identity = {
        schemaVersion: 1,
        sourceCommit,
        server: { sha256: hash(server) },
        compiler: { sha256: hash(compiler) }
    };
    const files = new Map([
        ["Lucent.Lui.LanguageServer.dll", server],
        ["Lucent.Lui.Compiler.dll", compiler],
        ["lucent-server.json", Buffer.from(JSON.stringify(identity))],
        ["notices/LICENSE.txt", Buffer.from("notice")]
    ]);
    for (const [name, bytes] of files) {
        const target = path.join(root, name);
        fs.mkdirSync(path.dirname(target), { recursive: true });
        fs.writeFileSync(target, bytes);
    }
    const manifest = {
        files: [...files].map(([fileName, bytes]) => ({ fileName, bytes: bytes.length, sha256: hash(bytes) })),
        schemaVersion: 1
    };
    const inventory = Buffer.from(JSON.stringify(manifest));
    fs.writeFileSync(path.join(root, "lucent-server-files.json"), inventory);
    const expected = { identity, filesSha256: hash(Buffer.from(JSON.stringify(canonical(manifest)))) };
    return { root, expected };
}

function withFixture(action) {
    const value = fixture();
    try { action(value); }
    finally {
        const prefix = path.join(os.tmpdir(), "lucent-bundle-test-");
        const resolved = path.resolve(value.root);
        assert.ok(resolved.startsWith(prefix), "Refusing to remove a test fixture outside the temporary test prefix.");
        fs.rmSync(resolved, { recursive: true, force: true });
    }
}

test("verified bundled server returns the expected DLL and exact identity", () => withFixture(({ root, expected }) => {
    const result = verifyBundledServer(root, expected, sourceCommit);
    assert.equal(result.serverPath, path.join(root, "Lucent.Lui.LanguageServer.dll"));
    assert.deepEqual(result.identity, expected.identity);
}));

test("changed and missing bundled bytes fail before execution", () => {
    withFixture(({ root, expected }) => {
        fs.writeFileSync(path.join(root, "Lucent.Lui.LanguageServer.dll"), "changed bytes");
        assert.throws(() => verifyBundledServer(root, expected, sourceCommit), /differs from inventory/);
    });
    withFixture(({ root, expected }) => {
        fs.rmSync(path.join(root, "notices/LICENSE.txt"));
        assert.throws(() => verifyBundledServer(root, expected, sourceCommit), /ENOENT/);
    });
});

test("unexpected files and mismatched release identity fail before execution", () => {
    withFixture(({ root, expected }) => {
        fs.writeFileSync(path.join(root, "extra.dll"), "unlisted");
        assert.throws(() => verifyBundledServer(root, expected, sourceCommit), /undeclared files/);
    });
    withFixture(({ root, expected }) => {
        assert.throws(() => verifyBundledServer(root, expected, "b".repeat(40)), /matching release identity/);
    });
});
