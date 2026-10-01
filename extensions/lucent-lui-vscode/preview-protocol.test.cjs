"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const os = require("node:os");
const { createHash } = require("node:crypto");
const { test } = require("node:test");
const { decodeWorkerRequest, decodeWorkerResult, decodeCatalogResult, readVerifiedFrame,
    readVerifiedCatalog, resolvePresentation } = require("./preview-protocol");
const fixtures = path.resolve(__dirname, "../../src/Lucent.Preview.Protocol/Fixtures");

test("worker and coordinator share the same valid and invalid wire fixtures", async () => {
    const names = (await fs.readdir(fixtures)).filter(name => /^(?:catalog-)?(?:request|result)-.*\.json$/.test(name));
    assert.ok(names.includes("request-valid.json") && names.includes("result-valid.json"));
    assert.ok(names.includes("request-invalid-duplicate.json"));
    for (const name of names) {
        const bytes = await fs.readFile(path.join(fixtures, name));
        const decode = name.startsWith("catalog-result") ? decodeCatalogResult
            : name.includes("request-") ? decodeWorkerRequest : decodeWorkerResult;
        if (name.includes("-invalid-")) assert.throws(() => decode(bytes), undefined, name);
        else assert.doesNotThrow(() => decode(bytes), name);
    }
});

test("bounded flat parsing rejects disguised duplicates, trailing data and missing fields", async () => {
    const text = await fs.readFile(path.join(fixtures, "request-valid.json"), "utf8");
    assert.throws(() => decodeWorkerRequest(text.replace('"protocolVersion": 2,', '"protocolVersion": 2, "protocol\\u0056ersion": 2,')), /duplicate/i);
    assert.throws(() => decodeWorkerRequest(text + "{}"));
    assert.throws(() => decodeWorkerRequest(text.replace('"protocolVersion": 2', '"protocolVersion": 2.0')));
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
    Object.assign(expected, { logicalWidth: 1, logicalHeight: 1, scale: 1,
        culture: metadata.culture, uiCulture: metadata.uiCulture, initialTime: metadata.initialTime });
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
    await assert.rejects(readVerifiedFrame(f.directory, { ...f.expected, logicalWidth: 2 }), /header/);
});

test("catalog admission binds identity and deeply freezes bounded scenario metadata", async t => {
    const f = await frameFixture(t);
    const catalog = JSON.parse(await fs.readFile(path.join(fixtures, "catalog-result-valid.json"), "utf8"));
    await fs.writeFile(path.join(f.directory, "catalog.json"), JSON.stringify(catalog));
    const accepted = await readVerifiedCatalog(f.directory, f.expected);
    assert.equal(accepted.scenarios[0].sourceDocument, "ScenarioCard.lui");
    assert.ok(Object.isFrozen(accepted) && Object.isFrozen(accepted.scenarios) && Object.isFrozen(accepted.scenarios[0]));
    await assert.rejects(readVerifiedCatalog(f.directory, { ...f.expected, artifactDigest: "d".repeat(64) }), /identity/);
    const scenario = catalog.scenarios[0];
    assert.throws(() => decodeCatalogResult(JSON.stringify({ ...catalog, scenarios: Array(65).fill(scenario) })), /count/);
    assert.throws(() => decodeCatalogResult(JSON.stringify({ ...catalog, scenarios: [{ ...scenario, title: "x".repeat(257) }] })));
    assert.throws(() => decodeCatalogResult(JSON.stringify({ ...catalog, scenarios: [{ ...scenario,
        initialTime: "2025-02-29T00:00:00.1234567+00:00" }] })), /time/);
    assert.throws(() => decodeCatalogResult(JSON.stringify({ ...catalog, scenarios: [{ ...scenario, extra: "unrecognized" }] })), /field/);
    const nested = JSON.stringify(catalog).replace('"density":1', '"density":1,"dens\\u0069ty":2');
    assert.throws(() => decodeCatalogResult(nested), /Duplicate/);
});

test("effective presentation uses float32 bounds and preserves scenario culture and clock ticks", async () => {
    const catalog = decodeCatalogResult(await fs.readFile(path.join(fixtures, "catalog-result-valid.json")));
    const scenario = { ...catalog.scenarios[0], initialTime: "2024-02-29T23:59:59.1234567+00:00", culture: "en-US" };
    const effective = resolvePresentation(scenario, { scale: 1.1, density: 0.3, colorScheme: "dark", contrast: "high" });
    assert.equal(effective.scale, 1.100000023841858);
    assert.equal(effective.density, 0.30000001192092896);
    assert.equal(effective.initialTime, "2024-02-29T23:59:59.1234567+00:00");
    assert.equal(effective.culture, "en-US");
    assert.equal(scenario.colorScheme, "light");
    assert.ok(Object.isFrozen(effective));
    for (const overrides of [{ culture: "fr-FR" }, { scale: Infinity }, { density: 0.249999999 },
        { logicalWidth: 8192, logicalHeight: 8192, scale: 1 }, { logicalWidth: 8192, scale: 4 },
        { colorScheme: "system" }, { contrast: "auto" }, { logicalWidth: "160" }])
        assert.throws(() => resolvePresentation(scenario, overrides));
});

test("frame admission rejects every differing effective presentation field", async t => {
    const f = await frameFixture(t);
    for (const [key, value] of Object.entries({ colorScheme: "dark", contrast: "high", density: 2,
        culture: "fr-FR", uiCulture: "fr-FR", initialTime: "1970-01-01T00:00:00.0000001+00:00" }))
        await assert.rejects(readVerifiedFrame(f.directory, { ...f.expected, [key]: value }), /identity/, key);
});

test("frame admission rejects linked output directories", async t => {
    const f = await frameFixture(t);
    const link = path.join(f.directory, "linked");
    await fs.symlink(f.directory, link, process.platform === "win32" ? "junction" : "dir");
    try { await assert.rejects(readVerifiedFrame(link, f.expected), /reparse|owned path/); }
    finally { await fs.unlink(link); }
});

test("frame admission accepts canonical aliases without treating them as escaped output", async t => {
    const f = await frameFixture(t);
    const realpath = fs.realpath.bind(fs);
    const canonicalRoot = await realpath(f.directory);
    // Model an 8.3 alias expanding to the same canonical directory. Reads still
    // use the real owned fixture; only its canonical spelling differs.
    t.mock.method(fs, "realpath", async candidate => {
        const actual = await realpath(candidate);
        return actual.startsWith(canonicalRoot)
            ? path.join(path.dirname(canonicalRoot), "expanded-profile-name", path.relative(canonicalRoot, actual)) : actual;
    });
    const frame = await readVerifiedFrame(f.directory, f.expected);
    assert.deepEqual(frame.png, pixel);
});

test("frame admission rejects a link above the otherwise ordinary output directory", async t => {
    const f = await frameFixture(t);
    const output = path.join(f.directory, "output");
    await fs.mkdir(output);
    await fs.copyFile(path.join(f.directory, "frame.png"), path.join(output, "frame.png"));
    await fs.copyFile(path.join(f.directory, "result.json"), path.join(output, "result.json"));
    const link = path.join(f.directory, "linked-parent");
    await fs.symlink(f.directory, link, process.platform === "win32" ? "junction" : "dir");
    try { await assert.rejects(readVerifiedFrame(path.join(link, "output"), f.expected), /reparse|owned path/); }
    finally { await fs.unlink(link); }
});
