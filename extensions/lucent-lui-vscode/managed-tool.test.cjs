"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const crypto = require("node:crypto");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { verifyManagedToolFiles, verifyNuGetDoctorFiles } = require("./managed-tool");

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

test("optional NuGet payload binds dependencies and notices without widening the static doctor inventory", async t => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "lucent-nuget-inventory-"));
    t.after(() => fs.rmSync(root, { recursive: true, force: true }));
    const sourceCommit = "1".repeat(40);
    const files = ["Lucent.Tools.NuGet.dll", "Lucent.Tools.NuGet.deps.json", "Lucent.Tools.NuGet.runtimeconfig.json", "NuGet.Protocol.dll", "notices/NuGet-LICENSE.txt"].map(fileName => {
        const bytes = Buffer.from(fileName);
        fs.mkdirSync(path.dirname(path.join(root, fileName)), { recursive: true });
        fs.writeFileSync(path.join(root, fileName), bytes);
        return { fileName, bytes: bytes.length, sha256: crypto.createHash("sha256").update(bytes).digest("hex") };
    });
    const manifest = { schemaVersion: 1, sourceCommit, entryPoint: "Lucent.Tools.NuGet.dll", files };
    assert.equal(await verifyNuGetDoctorFiles(root, manifest, sourceCommit), path.join(root, manifest.entryPoint));
    await assert.rejects(verifyNuGetDoctorFiles(root, manifest, "2".repeat(40)), /manifest/);
    await assert.rejects(verifyManagedToolFiles(root, manifest, sourceCommit, "Lucent.Tools"), /manifest/);
    for (const fileName of ["../secret", "C:/secret", "notices/../secret", "notices\\secret"]) {
        await assert.rejects(verifyNuGetDoctorFiles(root, { ...manifest, files: [...files, { ...files[0], fileName }] }, sourceCommit), /inventory/);
    }
    await assert.rejects(verifyNuGetDoctorFiles(root, { ...manifest, files: [...files, { ...files[0], fileName: files[0].fileName.toUpperCase() }] }, sourceCommit), /inventory/);
    const dependency = path.join(root, "NuGet.Protocol.dll");
    fs.writeFileSync(dependency, Buffer.alloc(files[3].bytes, 0));
    await assert.rejects(verifyNuGetDoctorFiles(root, manifest, sourceCommit), /hash/);
    fs.writeFileSync(dependency, Buffer.from(files[3].fileName));
    fs.writeFileSync(path.join(root, "unexpected.dll"), "extra");
    await assert.rejects(verifyNuGetDoctorFiles(root, manifest, sourceCommit), /undeclared/);
    fs.unlinkSync(path.join(root, "unexpected.dll"));
    fs.unlinkSync(dependency);
    await assert.rejects(verifyNuGetDoctorFiles(root, manifest, sourceCommit), /missing/);
});
