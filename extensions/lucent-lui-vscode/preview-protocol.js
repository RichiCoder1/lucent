"use strict";

const fs = require("node:fs/promises");
const path = require("node:path");
const { createHash } = require("node:crypto");
const { TextDecoder } = require("node:util");

const MAX_MESSAGE = 65536;
const MAX_FRAME = 33554432;
const correlation = ["protocolVersion", "sessionId", "generation", "requestId", "projectTargetDigest",
    "inputDigest", "artifactDigest"];
const presentationKeys = ["logicalWidth", "logicalHeight", "scale", "colorScheme", "contrast", "density"];
const scenarioOwnedKeys = ["culture", "uiCulture", "initialTime"];
const commonKeys = [...correlation, "kind"];
const catalogEntryKeys = ["id", "title", "sourceProject", "sourceDocument", "sourceComponent",
    ...presentationKeys, ...scenarioOwnedKeys];
const captureKeys = [...commonKeys, "scenarioId", "presentationId", "outputDirectory", ...presentationKeys];
const resultKeys = [...commonKeys, "scenarioId", "presentationId", ...presentationKeys, ...scenarioOwnedKeys,
    "frameSequence", "fileName", "byteLength", "sha256", "width", "height"];
const integerKeys = new Set(["protocolVersion", "frameSequence", "byteLength", "width", "height"]);

// Catalogs add one bounded array of objects. Tokenize every object rather than
// accepting JSON.parse's last-win handling of escaped duplicate property names.
function decodeJson(bytes) {
    bytes = Buffer.isBuffer(bytes) ? bytes : Buffer.from(bytes, "utf8");
    if (bytes.length < 2 || bytes.length > MAX_MESSAGE) throw new Error("Invalid preview message size.");
    const text = new TextDecoder("utf-8", { fatal: true }).decode(bytes);
    const token = /\s*("(?:[^"\\\x00-\x1f]|\\(?:["\\/bfnrt]|u[0-9a-fA-F]{4}))*"|-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?|true|false|[{}\[\]:,])/y;
    let offset = 0;
    function next() {
        token.lastIndex = offset;
        const match = token.exec(text);
        if (!match) throw new Error("Invalid preview JSON.");
        offset = token.lastIndex;
        return match[1];
    }
    function parse(raw, depth, field) {
        if (depth > 4) throw new Error("Preview JSON exceeds its depth bound.");
        if (raw === "{") {
            const value = Object.create(null);
            let key = next();
            if (key === "}") return value;
            while (true) {
                if (!key.startsWith('"')) throw new Error("Invalid preview field.");
                key = JSON.parse(key);
                if (Object.hasOwn(value, key)) throw new Error("Duplicate preview field.");
                if (next() !== ":") throw new Error("Invalid preview field separator.");
                value[key] = parse(next(), depth + 1, key);
                const separator = next();
                if (separator === "}") return value;
                if (separator !== ",") throw new Error("Invalid preview separator.");
                key = next();
            }
        }
        if (raw === "[") {
            const value = [];
            let item = next();
            if (item === "]") return value;
            while (true) {
                if (value.length === 64) throw new Error("Preview catalog exceeds its count bound.");
                value.push(parse(item, depth + 1));
                const separator = next();
                if (separator === "]") return value;
                if (separator !== ",") throw new Error("Invalid preview separator.");
                item = next();
            }
        }
        if (!raw.startsWith('"') && !/^-?\d/.test(raw) && raw !== "true" && raw !== "false") throw new Error("Invalid preview value.");
        if (integerKeys.has(field) && !/^-?(?:0|[1-9][0-9]*)$/.test(raw))
            throw new Error("Invalid preview integer.");
        return JSON.parse(raw);
    }
    const first = next();
    if (first !== "{") throw new Error("Invalid preview object.");
    const value = parse(first, 0);
    if (text.slice(offset).trim()) throw new Error("Trailing preview data.");
    return value;
}

function exact(value, keys) {
    if (!value || Array.isArray(value) || typeof value !== "object"
        || Object.keys(value).length !== keys.length || keys.some(key => !Object.hasOwn(value, key)))
        throw new Error("Unknown or missing preview field.");
}

function boundedText(value, maximum, allowEmpty = false) {
    return typeof value === "string" && value.length <= maximum
        && (allowEmpty || !!value.trim()) && !/[\x00-\x1f\x7f-\x9f]/.test(value);
}

function freezeTree(value) {
    if (value && typeof value === "object") {
        for (const child of Object.values(value)) freezeTree(child);
        Object.freeze(value);
    }
    return value;
}

function digest(value) { return typeof value === "string" && /^[a-f0-9]{64}$/.test(value); }

function validateCorrelation(value) {
    if (value.protocolVersion !== 2) throw new Error("Unsupported preview protocol.");
    for (const key of ["sessionId", "generation", "requestId"])
        if (typeof value[key] !== "string" || !/^[A-Za-z0-9_.-]{1,128}$/.test(value[key]))
            throw new Error("Invalid preview correlation.");
    for (const key of ["projectTargetDigest", "inputDigest", "artifactDigest"])
        if (!digest(value[key])) throw new Error("Invalid preview digest.");
}

function validateScenario(value) {
    if (!boundedText(value.scenarioId, 256))
        throw new Error("Invalid preview scenario.");
    if (typeof value.presentationId !== "string" || !/^[A-Za-z0-9_.-]{1,128}$/.test(value.presentationId))
        throw new Error("Invalid preview presentation identity.");
}

function validateOutput(value) {
    if (typeof value.outputDirectory !== "string" || value.outputDirectory.length > 4096
        || !/^[A-Za-z]:[\\/]/.test(value.outputDirectory)
        || value.outputDirectory.slice(2).includes(":") || /[\x00-\x1f\x7f-\x9f]/.test(value.outputDirectory))
        throw new Error("Invalid local preview output directory.");
}

function validatePresentation(value) {
    for (const [key, minimum, maximum] of [["logicalWidth", 1, 8192], ["logicalHeight", 1, 8192],
        ["scale", 0.25, 4], ["density", 0.25, 4]]) {
        const number = value[key];
        const rounded = Math.fround(number);
        if (typeof number !== "number" || !Number.isFinite(number) || number < minimum || number > maximum
            || !Number.isFinite(rounded) || rounded < minimum || rounded > maximum)
            throw new Error("Invalid preview presentation number.");
        // JSON's shortest float32 spelling may parse as a different double.
        // All presentation comparisons use the worker's float32 semantics.
        value[key] = rounded;
    }
    if (!["light", "dark"].includes(value.colorScheme) || !["normal", "high"].includes(value.contrast))
        throw new Error("Invalid preview presentation appearance.");
    const width = Math.ceil(Math.fround(Math.fround(value.logicalWidth) * Math.fround(value.scale)));
    const height = Math.ceil(Math.fround(Math.fround(value.logicalHeight) * Math.fround(value.scale)));
    if (width < 1 || height < 1 || width > 8192 || height > 8192 || width * height > 16777216)
        throw new Error("Preview presentation exceeds its pixel bound.");
    return { width, height };
}

function validateScenarioOwned(value) {
    if (!boundedText(value.culture, 64, true) || !boundedText(value.uiCulture, 64, true))
        throw new Error("Invalid preview culture.");
    const time = typeof value.initialTime === "string"
        && /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})\.\d{7}\+00:00$/.exec(value.initialTime);
    if (!time) throw new Error("Invalid preview initial time.");
    const [year, month, day, hour, minute, second] = time.slice(1).map(Number);
    const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
    const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    if (year < 1 || month < 1 || month > 12 || day < 1 || day > days[month - 1]
        || hour > 23 || minute > 59 || second > 59) throw new Error("Invalid preview initial time.");
}

function resolvePresentation(scenario, overrides = {}) {
    if (!overrides || typeof overrides !== "object" || Array.isArray(overrides)
        || Object.keys(overrides).some(key => !presentationKeys.includes(key)))
        throw new Error("Unknown preview presentation override.");
    const value = Object.fromEntries([...presentationKeys, ...scenarioOwnedKeys].map(key => [key,
        Object.hasOwn(overrides, key) ? overrides[key] : scenario[key]]));
    validatePresentation(value);
    validateScenarioOwned(value);
    return Object.freeze(value);
}

function decodeWorkerRequest(bytes) {
    const value = decodeJson(bytes);
    validateCorrelation(value);
    if (value.kind === "preview-catalog-request") exact(value, [...commonKeys, "outputDirectory"]);
    else if (value.kind === "preview-capture-request") {
        exact(value, captureKeys);
        validateScenario(value);
        validatePresentation(value);
    } else throw new Error("Unsupported preview request kind.");
    validateOutput(value);
    return freezeTree(value);
}

function decodeCatalogResult(bytes) {
    const value = decodeJson(bytes);
    validateCorrelation(value);
    exact(value, [...commonKeys, "scenarios"]);
    if (value.kind !== "preview-catalog-result" || !Array.isArray(value.scenarios) || value.scenarios.length > 64)
        throw new Error("Invalid preview catalog.");
    const ids = new Set();
    for (const scenario of value.scenarios) {
        exact(scenario, catalogEntryKeys);
        if (!boundedText(scenario.id, 256) || ids.has(scenario.id) || !boundedText(scenario.title, 256)
            || !boundedText(scenario.sourceProject, 2048) || !boundedText(scenario.sourceDocument, 2048)
            || !boundedText(scenario.sourceComponent, 512)) throw new Error("Invalid preview catalog entry.");
        ids.add(scenario.id);
        validatePresentation(scenario);
        validateScenarioOwned(scenario);
    }
    return freezeTree(value);
}

function decodeWorkerResult(bytes) {
    const value = decodeJson(bytes);
    validateCorrelation(value);
    exact(value, resultKeys);
    validateScenario(value);
    const dimensions = validatePresentation(value);
    validateScenarioOwned(value);
    if (value.kind !== "preview-frame-result" || !digest(value.sha256) || value.frameSequence !== 1 || value.fileName !== "frame.png"
        || !Number.isInteger(value.byteLength) || value.byteLength < 33 || value.byteLength > MAX_FRAME
        || !Number.isInteger(value.width) || value.width < 1 || value.width > 8192
        || !Number.isInteger(value.height) || value.height < 1 || value.height > 8192
        || dimensions.width !== value.width || dimensions.height !== value.height)
        throw new Error("Invalid preview frame metadata.");
    return freezeTree(value);
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
    // Canonical spelling can expand a legitimate Windows 8.3 alias. Reject
    // actual links along the path, then compare canonical file containment.
    for (let candidate = file; ; candidate = path.dirname(candidate)) {
        if ((await fs.lstat(candidate)).isSymbolicLink()) throw new Error("Preview output contains a reparse link.");
        if (candidate === path.dirname(candidate)) break;
    }
    const canonicalRoot = await fs.realpath(root);
    const canonicalFile = await fs.realpath(file);
    const samePath = (left, right) => process.platform === "win32" ? left.toLowerCase() === right.toLowerCase() : left === right;
    if (!samePath(canonicalFile, path.join(canonicalRoot, name)))
        throw new Error("Preview output escaped its owned path.");
    return file;
}

async function readVerifiedFrame(directory, expected) {
    const metadata = decodeWorkerResult(await readBoundedFile(await requireOwnedFile(directory, "result.json"), MAX_MESSAGE));
    for (const key of [...correlation, "scenarioId", "presentationId", ...presentationKeys, ...scenarioOwnedKeys])
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

async function readVerifiedCatalog(directory, expected) {
    const catalog = decodeCatalogResult(await readBoundedFile(await requireOwnedFile(directory, "catalog.json"), MAX_MESSAGE));
    for (const key of correlation)
        if (catalog[key] !== expected[key]) throw new Error("Preview catalog identity differs from its launch.");
    return catalog;
}

module.exports = { decodeWorkerRequest, decodeWorkerResult, decodeCatalogResult, readVerifiedFrame,
    readVerifiedCatalog, readBoundedFile, resolvePresentation, decodeJson, exact,
    validateCorrelation, validateScenario, validatePresentation, MAX_MESSAGE, MAX_FRAME };
