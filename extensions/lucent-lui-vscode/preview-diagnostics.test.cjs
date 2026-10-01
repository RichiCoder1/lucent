"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const os = require("node:os");
const { test } = require("node:test");
const { parseCompilerDiagnostics, isDiagnosticLocationAllowed, verifyDiagnosticLocation } = require("./preview-diagnostics");

test("mapped compiler logs become deduplicated immutable authored-location data", () => {
    const text = "C:\\project\\Card.lui(12,4): error CS0103: The name 'MissingValue' does not exist [C:\\project\\Fixture.csproj]\n"
        + "C:\\project\\Helper.cs(7,2,7,9): warning CS0168: Unused local\n";
    const entries = parseCompilerDiagnostics([text, text], "request-1");
    assert.equal(entries.length, 2);
    assert.deepEqual({ ...entries[0], id: "opaque" }, { id: "opaque", file: "C:\\project\\Card.lui", line: 12,
        column: 4, severity: "error", code: "CS0103", message: "The name 'MissingValue' does not exist" });
    assert.match(entries[0].id, /^[a-f0-9]{32}$/);
    assert.notEqual(parseCompilerDiagnostics([text], "request-2")[0].id, entries[0].id);
    assert.ok(Object.isFrozen(entries) && Object.isFrozen(entries[0]));
});

test("diagnostic parser bounds output and never treats arbitrary labels as source locations", () => {
    const rejected = ["relative.lui", "https://host/Card.lui", "file:///C:/project/Card.lui", "\\\\host\\Card.lui",
        "//host/Card.lui", "C:\\project\\obj\\Card.cs", "C:\\project\\bin\\Card.cs", "C:\\project\\..\\Card.cs",
        "C:\\project\\Card.cs:stream", "C:\\project\\Card.png"];
    assert.equal(parseCompilerDiagnostics(rejected.map(file => `${file}(1,1): error CS0103: text`)).length, 0);
    const lines = Array.from({ length: 200 }, (_, index) => `C:\\project\\Card.lui(${index + 1},1): error CS0103: text`);
    assert.equal(parseCompilerDiagnostics([lines.join("\n")]).length, 100);
    assert.equal(parseCompilerDiagnostics(["x".repeat(1024 * 1024) + "\nC:\\project\\Card.lui(1,1): error CS0103: late"]).length, 0);
    assert.equal(parseCompilerDiagnostics(["C:\\project\\Card.lui(0,1): error CS0103: bad row"]).length, 0);
});

test("navigation roots and position bounds are independent from diagnostic text and IDs", () => {
    const [entry] = parseCompilerDiagnostics(["C:\\project\\Card.lui(12,4): error CS0103: text"]);
    assert.equal(isDiagnosticLocationAllowed(entry, ["C:\\project"]), true);
    assert.equal(isDiagnosticLocationAllowed(entry, ["C:\\other"]), false);
    assert.equal(isDiagnosticLocationAllowed({ ...entry, file: "C:\\project-extra\\Card.lui" }, ["C:\\project"]), false);
    assert.equal(isDiagnosticLocationAllowed({ ...entry, file: "C:\\project\\obj\\Card.cs" }, ["C:\\project"]), false);
    assert.equal(isDiagnosticLocationAllowed({ ...entry, id: "open-this-uri" }, ["C:\\project"]), false);
    assert.equal(isDiagnosticLocationAllowed({ ...entry, column: Infinity }, ["C:\\project"]), false);
});

test("physical diagnostic navigation rejects links and out-of-document positions", async t => {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), "lucent-preview-diagnostics-"));
    t.after(async () => {
        assert.equal(path.dirname(root), os.tmpdir());
        assert.ok(path.basename(root).startsWith("lucent-preview-diagnostics-"));
        await fs.rm(root, { recursive: true, force: true });
    });
    const source = path.join(root, "Card.lui");
    await fs.writeFile(source, "first line\nsecond line\n");
    const [entry] = parseCompilerDiagnostics([`${source}(2,4): error CS0103: text`]);
    const location = await verifyDiagnosticLocation(entry, [root]);
    assert.equal(location.file.toLowerCase(), (await fs.realpath(source)).toLowerCase());
    assert.equal(location.line, 2);
    assert.equal(location.column, 4);
    await assert.rejects(verifyDiagnosticLocation({ ...entry, line: 10 }, [root]), /document/);
    await assert.rejects(verifyDiagnosticLocation({ ...entry, column: 100 }, [root]), /document/);
    const linked = path.join(root, "linked");
    await fs.symlink(root, linked, process.platform === "win32" ? "junction" : "dir");
    try { await assert.rejects(verifyDiagnosticLocation({ ...entry, file: path.join(linked, "Card.lui") }, [root]), /reparse/); }
    finally { await fs.unlink(linked); }
});
