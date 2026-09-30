"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const crypto = require("node:crypto");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { verifyManagedToolFiles } = require("./managed-tool");

test("doctor inventory binds its exact tool and source before execution", async t => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "lucent-doctor-inventory-"));
    t.after(() => fs.rmSync(root, { recursive: true, force: true }));
    const sourceCommit = "1".repeat(40);
    const files = ["Lucent.Tools.dll", "Lucent.Tools.deps.json", "Lucent.Tools.runtimeconfig.json"].map(fileName => {
        const bytes = Buffer.from(fileName);
        fs.writeFileSync(path.join(root, fileName), bytes);
        return { fileName, bytes: bytes.length, sha256: crypto.createHash("sha256").update(bytes).digest("hex") };
    });
    const manifest = { schemaVersion: 1, sourceCommit, entryPoint: "Lucent.Tools.dll", files };
    assert.equal(await verifyManagedToolFiles(root, manifest, sourceCommit, "Lucent.Tools"), path.join(root, manifest.entryPoint));
    await assert.rejects(verifyManagedToolFiles(root, manifest, "2".repeat(40), "Lucent.Tools"), /manifest/);
    await assert.rejects(verifyManagedToolFiles(root, manifest, sourceCommit, "Lucent.Tooling.Cache"), /manifest/);
    await assert.rejects(verifyManagedToolFiles(root, { ...manifest, entryPoint: "../Lucent.Tools.dll" }, sourceCommit, "Lucent.Tools"), /manifest/);
    fs.mkdirSync(path.join(root, "nested"));
    await assert.rejects(verifyManagedToolFiles(root, manifest, sourceCommit, "Lucent.Tools"), /undeclared/);
});
