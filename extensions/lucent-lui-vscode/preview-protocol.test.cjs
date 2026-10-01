"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const os = require("node:os");
const { createHash } = require("node:crypto");
const { test } = require("node:test");
const { decodeWorkerRequest, decodeWorkerResult, readVerifiedFrame } = require("./preview-protocol");
const fixtures = path.resolve(__dirname, "../../src/Lucent.Preview.Protocol/Fixtures");

test("worker and coordinator share the same valid and invalid wire fixtures", async () => {
    const names = (await fs.readdir(fixtures)).filter(name => /^(request|result)-.*\.json$/.test(name));
    assert.ok(names.includes("request-valid.json") && names.includes("result-valid.json"));
    assert.ok(names.includes("request-invalid-duplicate.json"));
    for (const name of names) {
        const bytes = await fs.readFile(path.join(fixtures, name));
        const decode = name.startsWith("request") ? decodeWorkerRequest : decodeWorkerResult;
        if (name.includes("-invalid-")) assert.throws(() => decode(bytes), undefined, name);
        else assert.doesNotThrow(() => decode(bytes), name);
    }
});

test("bounded flat parsing rejects disguised duplicates, trailing data and missing fields", async () => {
    const text = await fs.readFile(path.join(fixtures, "request-valid.json"), "utf8");
    assert.throws(() => decodeWorkerRequest(text.replace('"protocolVersion": 1,', '"protocolVersion": 1, "protocol\\u0056ersion": 1,')), /duplicate/);
    assert.throws(() => decodeWorkerRequest(text + "{}"));
    assert.throws(() => decodeWorkerRequest(text.replace('"protocolVersion": 1', '"protocolVersion": 1.0')));
    assert.throws(() => decodeWorkerRequest(" ".repeat(65537)), /size/);
    assert.throws(() => decodeWorkerRequest(Buffer.from([0xff, 0xff])));
});

// An independent one-pixel PNG, not an output assembled by the production codec.
const pixel = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1ZkAAAAASUVORK5CYII=", "base64");

async function frameFixture(t) {
    const directory = await fs.mkdtemp(path.join(os.tmpdir(), "lucent-preview-frame-"));
    t.after(async () => {
        // Only the exact owned mkdtemp directory is removed.
        assert.equal(path.dirname(directory), os.tmpdir());
        assert.ok(path.basename(directory).startsWith("lucent-preview-frame-"));
        await fs.rm(directory, { recursive: true, force: true });
    });
    const expected = JSON.parse(await fs.readFile(path.join(fixtures, "request-valid.json"), "utf8"));
    const metadata = { ...JSON.parse(await fs.readFile(path.join(fixtures, "result-valid.json"), "utf8")),
        byteLength: pixel.length, sha256: createHash("sha256").update(pixel).digest("hex"),
        width: 1, height: 1, logicalWidth: 1, logicalHeight: 1, scale: 1 };
    await fs.writeFile(path.join(directory, "frame.png"), pixel);
    await fs.writeFile(path.join(directory, "result.json"), JSON.stringify(metadata));
    return { directory, expected, metadata };
}

test("frame admission independently checks bytes, header dimensions and correlation", async t => {
    const f = await frameFixture(t);
    const frame = await readVerifiedFrame(f.directory, f.expected);
    assert.deepEqual(frame.png, pixel);
    await assert.rejects(readVerifiedFrame(f.directory, { ...f.expected, requestId: "different" }), /identity/);
    await fs.writeFile(path.join(f.directory, "frame.png"), Buffer.alloc(pixel.length));
    await assert.rejects(readVerifiedFrame(f.directory, f.expected), /bytes/);
    await fs.writeFile(path.join(f.directory, "frame.png"), pixel);
    await fs.writeFile(path.join(f.directory, "result.json"), JSON.stringify({ ...f.metadata, width: 2, logicalWidth: 2 }));
    await assert.rejects(readVerifiedFrame(f.directory, f.expected), /header/);
});

test("frame admission rejects linked output directories", async t => {
    const f = await frameFixture(t);
    const link = path.join(f.directory, "linked");
    await fs.symlink(f.directory, link, process.platform === "win32" ? "junction" : "dir");
    try { await assert.rejects(readVerifiedFrame(link, f.expected), /reparse|owned path/); }
    finally { await fs.unlink(link); }
});
