"use strict";

const fs = require("node:fs/promises");
const path = require("node:path");
const { createHash } = require("node:crypto");
const { readBoundedFile } = require("./preview-protocol");

const MAX_LOG = 1024 * 1024;
const MAX_DIAGNOSTICS = 100;

function localPath(file) {
    if (typeof file !== "string" || file.length > 2048 || /[\x00-\x1f\x7f-\x9f]/.test(file)) return undefined;
    const windows = /^[A-Za-z]:[\\/]/.test(file);
    const paths = windows ? path.win32 : path;
    if ((!windows && !path.isAbsolute(file)) || file.startsWith("\\") || file.startsWith("//")
        || (process.platform === "win32" && !windows)
        || (windows && file.slice(2).includes(":"))) return undefined;
    if (!/\.(?:lui|cs)$/i.test(file) || file.split(/[\\/]/).some(part => ["..", "obj", "bin"].includes(part.toLowerCase())))
        return undefined;
    return { paths, file: paths.resolve(file), windows };
}

// Compiler #line directives already map generated errors to authored .lui
// locations. Logs are data; neither an SDK label nor a worker catalog label
// grants permission to navigate outside the extension's selected source roots.
function parseCompilerDiagnostics(logs, identity = "") {
    const entries = [];
    const seen = new Set();
    for (const log of logs) {
        const text = Buffer.from(log).subarray(0, MAX_LOG).toString("utf8");
        for (const line of text.split(/\r?\n/)) {
            if (line.length > 4096) continue;
            const match = /^(.+)\((\d+),(\d+)(?:,\d+,\d+)?\):\s*(error|warning)\s+([A-Za-z][A-Za-z0-9]{1,31}):\s*(.*?)(?:\s+\[[^\]\r\n]+\])?$/.exec(line);
            if (!match) continue;
            const location = localPath(match[1]);
            const row = Number(match[2]);
            const column = Number(match[3]);
            if (!location || !Number.isSafeInteger(row) || !Number.isSafeInteger(column)
                || row < 1 || row > 1000000 || column < 1 || column > 1000000) continue;
            const message = match[6].replace(/[\x00-\x1f\x7f-\x9f]/g, " ").slice(0, 2048);
            const key = `${location.file}\n${row}\n${column}\n${match[4]}\n${match[5]}\n${message}`;
            if (seen.has(key)) continue;
            seen.add(key);
            entries.push(Object.freeze({ id: createHash("sha256").update(identity + "\n" + key).digest("hex").slice(0, 32),
                file: location.file, line: row, column, severity: match[4], code: match[5], message }));
            if (entries.length === MAX_DIAGNOSTICS) return Object.freeze(entries);
        }
    }
    return Object.freeze(entries);
}

function isDiagnosticLocationAllowed(diagnostic, roots) {
    const location = localPath(diagnostic?.file);
    if (!location || !/^[a-f0-9]{32}$/.test(diagnostic.id ?? "")
        || !Number.isSafeInteger(diagnostic.line) || diagnostic.line < 1 || diagnostic.line > 1000000
        || !Number.isSafeInteger(diagnostic.column) || diagnostic.column < 1 || diagnostic.column > 1000000
        || !Array.isArray(roots) || roots.length > 64) return false;
    return roots.some(root => {
        if (typeof root !== "string" || !location.paths.isAbsolute(root)) return false;
        const relative = location.paths.relative(location.paths.resolve(root), location.file);
        return !!relative && relative !== ".." && !relative.startsWith(".." + location.paths.sep)
            && !location.paths.isAbsolute(relative);
    });
}

async function verifyDiagnosticLocation(diagnostic, roots) {
    if (!isDiagnosticLocationAllowed(diagnostic, roots)) throw new Error("Preview diagnostic is outside authored source roots.");
    const candidate = path.resolve(diagnostic.file);
    const actual = await fs.realpath(candidate);
    // Short Windows path aliases are legitimate; actual link ancestors are not.
    for (let ancestor = candidate; ; ancestor = path.dirname(ancestor)) {
        if ((await fs.lstat(ancestor)).isSymbolicLink())
            throw new Error("Preview diagnostic source contains a reparse link.");
        if (path.dirname(ancestor) === ancestor) break;
    }
    const physicalRoots = await Promise.all(roots.map(root => fs.realpath(root)));
    if (!isDiagnosticLocationAllowed({ ...diagnostic, file: actual }, physicalRoots))
        throw new Error("Preview diagnostic escaped its physical source roots.");
    const bytes = await readBoundedFile(actual, 4 * MAX_LOG);
    const lines = bytes.toString("utf8").split(/\r?\n/);
    if (diagnostic.line > lines.length || diagnostic.column > lines[diagnostic.line - 1].length + 1)
        throw new Error("Preview diagnostic location is outside its authored document.");
    return Object.freeze({ file: actual, line: diagnostic.line, column: diagnostic.column });
}

module.exports = { parseCompilerDiagnostics, isDiagnosticLocationAllowed, verifyDiagnosticLocation };
