"use strict";

const fs = require("node:fs/promises");
const path = require("node:path");
const { createHash } = require("node:crypto");
const { TextDecoder } = require("node:util");

const MAX_MESSAGE = 65536;
const MAX_FRAME = 33554432;
const correlation = ["protocolVersion", "sessionId", "generation", "requestId", "projectTargetDigest",
    "inputDigest", "artifactDigest", "scenarioId", "presentationId"];
const requestKeys = [...correlation, "outputDirectory"];
const resultKeys = [...correlation, "frameSequence", "fileName", "byteLength", "sha256", "width", "height",
    "logicalWidth", "logicalHeight", "scale"];
const integerKeys = new Set(["protocolVersion", "frameSequence", "byteLength", "width", "height"]);

// The worker schema is deliberately flat. Tokenize complete string values so
// duplicate escaped property names cannot be hidden by JSON.parse's last-win rule.
function decodeFlat(bytes, keys) {
    bytes = Buffer.isBuffer(bytes) ? bytes : Buffer.from(bytes, "utf8");
    if (bytes.length < 2 || bytes.length > MAX_MESSAGE) throw new Error("Invalid preview message size.");
    const text = new TextDecoder("utf-8", { fatal: true }).decode(bytes);
    const token = /\s*("(?:[^"\\\x00-\x1f]|\\(?:["\\/bfnrt]|u[0-9a-fA-F]{4}))*"|-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?|[{}:,])/y;
    let offset = 0;
    function next() {
        token.lastIndex = offset;
        const match = token.exec(text);
        if (!match) throw new Error("Invalid preview JSON.");
        offset = token.lastIndex;
        return match[1];
    }
    if (next() !== "{") throw new Error("Invalid preview object.");
    const value = Object.create(null);
    let key = next();
    while (key !== "}") {
        if (!key.startsWith('"')) throw new Error("Invalid preview field.");
        key = JSON.parse(key);
        if (!keys.includes(key) || Object.hasOwn(value, key)) throw new Error("Unknown or duplicate preview field.");
        if (next() !== ":") throw new Error("Invalid preview field separator.");
        const raw = next();
        if (!raw.startsWith('"') && !/^-?\d/.test(raw)) throw new Error("Invalid preview value.");
        if (integerKeys.has(key) && !/^-?(?:0|[1-9][0-9]*)$/.test(raw))
            throw new Error("Invalid preview integer.");
        value[key] = JSON.parse(raw);
        const separator = next();
        if (separator === "}") break;
        if (separator !== ",") throw new Error("Invalid preview separator.");
        key = next();
        if (key === "}") throw new Error("Invalid trailing comma.");
    }
    if (text.slice(offset).trim() || Object.keys(value).length !== keys.length)
        throw new Error("Incomplete preview message.");
    return value;
}

function digest(value) { return typeof value === "string" && /^[a-f0-9]{64}$/.test(value); }

function validateCorrelation(value) {
    if (value.protocolVersion !== 1) throw new Error("Unsupported preview protocol.");
    for (const key of ["sessionId", "generation", "requestId", "presentationId"])
        if (typeof value[key] !== "string" || !/^[A-Za-z0-9_.-]{1,128}$/.test(value[key]))
            throw new Error("Invalid preview correlation.");
    for (const key of ["projectTargetDigest", "inputDigest", "artifactDigest"])
        if (!digest(value[key])) throw new Error("Invalid preview digest.");
    if (typeof value.scenarioId !== "string" || value.scenarioId.length > 256
        || !value.scenarioId.trim() || /[\x00-\x1f\x7f-\x9f]/.test(value.scenarioId))
        throw new Error("Invalid preview scenario.");
}

function decodeWorkerRequest(bytes) {
    const value = decodeFlat(bytes, requestKeys);
    validateCorrelation(value);
    if (typeof value.outputDirectory !== "string" || value.outputDirectory.length > 4096
        || !/^[A-Za-z]:[\\/]/.test(value.outputDirectory)
        || value.outputDirectory.slice(2).includes(":") || /[\x00-\x1f\x7f-\x9f]/.test(value.outputDirectory))
        throw new Error("Invalid local preview output directory.");
    return Object.freeze(value);
}

function decodeWorkerResult(bytes) {
    const value = decodeFlat(bytes, resultKeys);
    validateCorrelation(value);
    if (!digest(value.sha256) || value.frameSequence !== 1 || value.fileName !== "frame.png"
        || !Number.isInteger(value.byteLength) || value.byteLength < 33 || value.byteLength > MAX_FRAME
        || !Number.isInteger(value.width) || value.width < 1 || value.width > 8192
        || !Number.isInteger(value.height) || value.height < 1 || value.height > 8192
        || ![value.logicalWidth, value.logicalHeight, value.scale].every(n => Number.isFinite(n) && n > 0)
        || Math.ceil(Math.fround(Math.fround(value.logicalWidth) * Math.fround(value.scale))) !== value.width
        || Math.ceil(Math.fround(Math.fround(value.logicalHeight) * Math.fround(value.scale))) !== value.height)
        throw new Error("Invalid preview frame metadata.");
    return Object.freeze(value);
}

async function readBoundedFile(file, maximum) {
    const handle = await fs.open(file, "r");
    try {
        const before = await handle.stat();
        if (!before.isFile() || before.size > maximum) throw new Error("Preview file exceeds its bound.");
        const bytes = Buffer.alloc(before.size);
        let offset = 0;
        while (offset < bytes.length) {
            const { bytesRead } = await handle.read(bytes, offset, bytes.length - offset, offset);
            if (!bytesRead) throw new Error("Preview file changed while being read.");
            offset += bytesRead;
        }
        const after = await handle.stat();
        if (after.size !== before.size || after.mtimeMs !== before.mtimeMs)
            throw new Error("Preview file changed while being read.");
        return bytes;
    } finally { await handle.close(); }
}

async function requireOwnedFile(directory, name) {
    const root = path.resolve(directory);
    const file = path.join(root, name);
    for (const candidate of [root, file]) {
        if ((await fs.lstat(candidate)).isSymbolicLink()) throw new Error("Preview output contains a reparse link.");
        if ((await fs.realpath(candidate)).toLowerCase() !== candidate.toLowerCase())
            throw new Error("Preview output escaped its owned path.");
    }
    return file;
}

async function readVerifiedFrame(directory, expected) {
    const metadata = decodeWorkerResult(await readBoundedFile(await requireOwnedFile(directory, "result.json"), MAX_MESSAGE));
    for (const key of correlation)
        if (metadata[key] !== expected[key]) throw new Error("Preview result identity differs from its launch.");
    const png = await readBoundedFile(await requireOwnedFile(directory, "frame.png"), MAX_FRAME);
    if (png.length !== metadata.byteLength || createHash("sha256").update(png).digest("hex") !== metadata.sha256)
        throw new Error("Preview frame bytes differ from their metadata.");
    if (!png.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]))
        || png.readUInt32BE(8) !== 13 || png.toString("ascii", 12, 16) !== "IHDR"
        || png.readUInt32BE(16) !== metadata.width || png.readUInt32BE(20) !== metadata.height)
        throw new Error("Preview PNG header differs from its declared dimensions.");
    return Object.freeze({ ...metadata, png });
}

module.exports = { decodeWorkerRequest, decodeWorkerResult, readVerifiedFrame, readBoundedFile };
