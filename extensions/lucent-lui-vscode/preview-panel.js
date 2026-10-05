"use strict";

const { randomBytes, randomUUID } = require("node:crypto");

const presentationKeys = ["logicalWidth", "logicalHeight", "scale", "colorScheme", "contrast", "density"];
const phases = new Set(["idle", "discovering", "building", "rendering", "current", "stale", "stopping", "stopped", "suspended", "blocked", "untrusted", "error"]);
const own = (value, keys) => value !== null && typeof value === "object" && !Array.isArray(value)
    && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
const text = (value, max) => typeof value === "string" && value.length <= max && !/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/.test(value);
const id = value => text(value, 256) && value.trim().length > 0 && !/[\u0000-\u001f\u007f]/.test(value);
const extent = value => Number.isFinite(value) && value >= 1 && value <= 8192;
const factor = value => Number.isFinite(value) && value >= 0.25 && value <= 4;

function presentation(value, exact = false) {
    if (!value || exact && !own(value, presentationKeys)
        || !extent(value.logicalWidth) || !extent(value.logicalHeight) || !factor(value.scale) || !factor(value.density)
        || !["light", "dark"].includes(value.colorScheme) || !["normal", "high"].includes(value.contrast)) return undefined;
    const width = Math.ceil(Math.fround(Math.fround(value.logicalWidth) * Math.fround(value.scale)));
    const height = Math.ceil(Math.fround(Math.fround(value.logicalHeight) * Math.fround(value.scale)));
    if (width > 8192 || height > 8192 || width * height > 16777216) return undefined;
    return Object.fromEntries(presentationKeys.map(key => [key, value[key]]));
}

// Only display text crosses this boundary. Source locations stay extension-owned.
function displayText(value, max = 2048) {
    if (!text(value, max)) return "";
    return value.replace(/\b[a-z][a-z0-9+.-]*:\/\/\S+|\b[a-z]:[\\/]\S+|\\\\[^\s]+/gi, "[location]");
}

function snapshot(view) {
    const state = view?.state ?? {};
    const scenarios = [];
    if (Array.isArray(state.catalog?.scenarios) && state.catalog.scenarios.length <= 64) {
        const seen = new Set();
        for (const scenario of state.catalog.scenarios) {
            const defaults = presentation(scenario);
            if (!id(scenario?.id) || !text(scenario.title, 256) || !defaults || seen.has(scenario.id)) continue;
            seen.add(scenario.id);
            scenarios.push({ id: scenario.id, title: displayText(scenario.title, 256), ...defaults });
        }
    }
    const scenarioId = id(view?.selection?.scenarioId) ? view.selection.scenarioId : undefined;
    const selected = scenarios.find(scenario => scenario.id === scenarioId);
    const chosen = presentation(view?.selection?.presentation, true) ?? presentation(selected);
    const diagnostics = Array.isArray(state.diagnostics) && state.diagnostics.length <= 128
        ? state.diagnostics.map((diagnostic, index) => ({ index, message: displayText(diagnostic?.message) })) : [];
    const frame = state.frame;
    let image;
    if (Buffer.isBuffer(frame?.png) && frame.png.length > 0 && frame.png.length <= 33554432
        && extent(frame.logicalWidth) && extent(frame.logicalHeight) && factor(frame.scale)
        && Number.isSafeInteger(frame.width) && frame.width >= 1 && frame.width <= 8192
        && Number.isSafeInteger(frame.height) && frame.height >= 1 && frame.height <= 8192
        && frame.width * frame.height <= 16777216
        && Math.ceil(Math.fround(Math.fround(frame.logicalWidth) * Math.fround(frame.scale))) === frame.width
        && Math.ceil(Math.fround(Math.fround(frame.logicalHeight) * Math.fround(frame.scale))) === frame.height) {
        image = { png: frame.png.toString("base64"), logicalWidth: frame.logicalWidth, logicalHeight: frame.logicalHeight,
            scale: frame.scale, width: frame.width, height: frame.height,
            scenarioId: id(frame.scenarioId) ? frame.scenarioId : undefined, scenarioTitle: displayText(frame.scenarioTitle, 256),
            presentation: presentation(frame.effectivePresentation),
            generation: typeof frame.generation === "string" && /^[A-Za-z0-9_-]{1,128}$/.test(frame.generation) ? frame.generation : undefined };
    }
    return { phase: phases.has(state.phase) ? state.phase : "error", generation: typeof state.generation === "string" && /^[A-Za-z0-9_-]{1,128}$/.test(state.generation) ? state.generation : "0",
        stale: state.stale === true, catalogStale: state.catalogStale === true, diagnostic: displayText(state.diagnostic),
        diagnostics, scenarios, selection: { scenarioId, presentation: chosen }, zoom: view?.zoom === "fit" ? "fit" : factor(view?.zoom) ? view.zoom : 1,
        running: view?.running === true, frame: image };
}

function createPreviewPanel({ panel, onAction = () => {}, onVisibility = () => {}, onClose = () => {},
    schedule = setTimeout, cancelScheduled = clearTimeout }) {
    let disposed = false;
    let visible = panel.visible === true;
    let ready = false;
    let panelId;
    let revision = 0;
    let latest;
    let pending;
    let flight;
    let timer;
    const listeners = [];
    function callback(fn, value) {
        try { Promise.resolve(fn(value)).catch(() => {}); } catch { /* The controller owns diagnostics. */ }
    }
    function clearFlight() {
        if (timer !== undefined) cancelScheduled(timer);
        timer = undefined;
        flight = undefined;
    }
    function suspend() { clearFlight(); pending = undefined; ready = false; }
    function handshake() {
        suspend();
        panelId = randomUUID();
        if (latest) latest = { ...latest, deliveryId: String(++revision) };
        panel.webview.html = html(panelId, randomBytes(24).toString("hex"));
    }
    function flush() {
        if (disposed || !visible || !ready || flight || !pending) return;
        const entry = pending;
        pending = undefined;
        flight = entry;
        timer = schedule(() => { if (flight === entry) suspend(); }, 5000);
        const message = { version: 1, panelId, kind: "view", deliveryId: entry.deliveryId, view: entry.view };
        try {
            Promise.resolve(panel.webview.postMessage(message)).then(posted => {
                if (!posted && flight === entry) suspend();
            }, () => { if (flight === entry) suspend(); });
        } catch { if (flight === entry) suspend(); }
    }
    function receive(message) {
        if (disposed || !visible || message?.version !== 1 || message.panelId !== panelId) return;
        if (message.kind === "ready" && own(message, ["version", "panelId", "kind"])) {
            // A new explicit readiness signal can recover a timed-out page.
            if (ready) return;
            clearFlight();
            ready = true;
            if (latest) latest = { ...latest, deliveryId: String(++revision) };
            pending = latest;
            flush();
            return;
        }
        if (message.kind === "ack" && own(message, ["version", "panelId", "kind", "deliveryId"])) {
            if (flight?.deliveryId !== message.deliveryId) return;
            clearFlight();
            flush();
            return;
        }
        const base = ["version", "panelId", "deliveryId", "kind"];
        // Stop addresses this panel's owner, not a frame or indexed capability.
        // It must remain usable while a newer display update is awaiting delivery.
        if (message.kind === "stop" && own(message, base) && typeof message.deliveryId === "string") {
            callback(onAction, { kind: "stop" });
            return;
        }
        if (!ready || !latest || message.deliveryId !== latest.deliveryId) return;
        let action;
        switch (message.kind) {
            case "select":
                if (own(message, [...base, "scenarioId"])
                    && latest.view.scenarios.some(scenario => scenario.id === message.scenarioId))
                    action = { kind: "select", scenarioId: message.scenarioId };
                break;
            case "presentation": {
                const chosen = presentation(message.presentation, true);
                if (own(message, [...base, "presentation"]) && chosen) action = { kind: "presentation", presentation: chosen };
                break;
            }
            case "zoom":
                if (own(message, [...base, "zoom"]) && (message.zoom === "fit" || factor(message.zoom))) action = { kind: "zoom", zoom: message.zoom };
                break;
            case "diagnostic":
                if (own(message, [...base, "index"]) && Number.isSafeInteger(message.index)
                    && latest.view.diagnostics.some(item => item.index === message.index)) action = { kind: "diagnostic", index: message.index };
                break;
            case "start": case "pickScenario": case "output": case "reset": case "refresh": case "stop":
                if (own(message, base)) action = { kind: message.kind };
                break;
        }
        if (action) callback(onAction, action);
    }
    function dispose() {
        if (disposed) return;
        disposed = true;
        suspend();
        latest = undefined;
        while (listeners.length) listeners.pop().dispose();
    }
    listeners.push(panel.webview.onDidReceiveMessage(receive));
    listeners.push(panel.onDidChangeViewState(event => {
        if (disposed || visible === (event.webviewPanel.visible === true)) return;
        visible = event.webviewPanel.visible === true;
        if (visible) handshake(); else suspend();
        callback(onVisibility, visible);
    }));
    listeners.push(panel.onDidDispose(() => { if (!disposed) { dispose(); callback(onClose); } }));
    handshake();
    return { update(view) {
        if (disposed) return;
        latest = { deliveryId: String(++revision), view: snapshot(view) };
        if (visible && ready) { pending = latest; flush(); }
    }, dispose };
}

function html(panelId, nonce) {
    return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}'; base-uri 'none'; form-action 'none';">
<title>Lucent Preview</title><style nonce="${nonce}">
body{font:var(--vscode-font-size,13px) var(--vscode-font-family);color:var(--vscode-foreground);background:var(--vscode-editor-background);margin:0;height:100vh;display:flex;flex-direction:column;overflow:hidden}button,input,select{font:inherit;color:var(--vscode-input-foreground);background:var(--vscode-input-background);border:1px solid var(--vscode-input-border,var(--vscode-panel-border));padding:4px 8px;border-radius:2px;min-height:30px;box-sizing:border-box}button{cursor:pointer;background:var(--vscode-button-secondaryBackground);color:var(--vscode-button-secondaryForeground)}button:hover{background:var(--vscode-button-secondaryHoverBackground)}button:focus-visible,input:focus-visible,select:focus-visible{outline:1px solid var(--vscode-focusBorder);outline-offset:2px}button:disabled,input:disabled,select:disabled{opacity:.55;cursor:default}.toolbar{display:flex;gap:6px;align-items:center;padding:8px;border-bottom:1px solid var(--vscode-panel-border);flex-shrink:0;flex-wrap:wrap}#pickScenario{max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.toolbar label{display:flex;align-items:center;gap:4px}.toolbar select{max-width:92px}#scenario{display:none}#status{margin:8px 12px;font-weight:600}#diagnostic,#draftError{white-space:pre-wrap;overflow-wrap:anywhere;color:var(--vscode-errorForeground);margin:0 12px}#diagnostics{display:flex;flex-direction:column;align-items:start;gap:6px;margin:8px 12px;max-height:20vh;overflow:auto}#diagnostics button{max-width:100%;text-align:left;overflow-wrap:anywhere}#canvas{overflow:auto;min-height:64px;flex:1;display:flex;align-items:safe center;justify-content:safe center}#image{display:block;pointer-events:none;user-select:none;flex-shrink:0}#image[hidden],[hidden]{display:none!important}#caption{color:var(--vscode-descriptionForeground);margin:8px 12px;font-size:11px;overflow-wrap:anywhere}#empty{padding:16px;color:var(--vscode-descriptionForeground)}#presentation{border-bottom:1px solid var(--vscode-panel-border);padding:12px;margin:0;max-height:55vh;overflow:auto}fieldset{border:0;padding:0;margin:0;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px}fieldset label{display:flex;flex-direction:column;gap:4px}fieldset input,fieldset select{width:100%;min-width:0}.sheetActions{display:flex;gap:8px;margin-top:12px;flex-wrap:wrap}.help{margin:8px 0;color:var(--vscode-descriptionForeground)}#apply{background:var(--vscode-button-background);color:var(--vscode-button-foreground)}::selection{background:var(--vscode-editor-selectionBackground)}input{caret-color:var(--vscode-foreground)}@media(max-width:700px){#pickScenario{flex-basis:100%;text-align:left}}@media(max-width:480px){fieldset{grid-template-columns:repeat(2,minmax(0,1fr))}.toolbar{gap:4px}#settings{max-width:130px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}}</style></head>
<body data-panel-id="${panelId}"><div class="toolbar"><button id="pickScenario" type="button">Choose scenario</button><select id="scenario" aria-label="Preview scenario" hidden></select><button id="settings" type="button" aria-expanded="false" aria-controls="presentation">Presentation</button><label>Zoom<select id="zoom" aria-label="Display zoom"><option value="fit">Fit</option><option value="0.25">25%</option><option value="0.5">50%</option><option value="1">100%</option><option value="1.5">150%</option><option value="2">200%</option><option value="3">300%</option><option value="4">400%</option></select></label><button id="appearance" type="button" title="Change requested appearance; restarts with fresh state while running">Light</button><button id="reset" type="button" title="Same scenario and presentation; rebuilds with fresh state">Reset scenario</button><button id="start" type="button">Start preview</button><button id="stop" type="button">Stop</button><button id="output" type="button">Show preview output</button></div>
<form id="presentation" hidden aria-label="Presentation settings"><fieldset id="controls" disabled><label>Logical width<input id="logicalWidth" type="number" min="1" max="8192" step="any" required></label><label>Logical height<input id="logicalHeight" type="number" min="1" max="8192" step="any" required></label><label>Device scale<input id="scale" type="number" min="0.25" max="4" step="any" required></label><label>Appearance<select id="colorScheme"><option value="light">Light</option><option value="dark">Dark</option></select></label><label>Contrast<select id="contrast"><option value="normal">Normal</option><option value="high">High</option></select></label><label>Fixture density<input id="density" type="number" min="0.25" max="4" step="any" required></label></fieldset><p class="help">Apply restarts with fresh state while running. While stopped, settings wait for Start. Choosing another scenario discards this draft.</p><div id="draftError" role="alert"></div><div class="sheetActions"><button id="apply" type="submit">Apply &amp; restart</button><button id="defaults" type="button">Scenario defaults</button><button id="cancel" type="button">Cancel</button></div></form>
<div id="status" role="status" aria-live="polite">Waiting for preview</div><div id="diagnostic" role="alert"></div><div id="diagnostics" aria-label="Source diagnostics"></div><div id="canvas"><div id="empty">No accepted image yet</div><img id="image" alt="Compiled component preview" hidden draggable="false"></div><p id="caption">Images are noninteractive. Display zoom does not change renderer scale.</p><script nonce="${nonce}">(${webviewMain.toString()})();</script></body></html>`;
}

function webviewMain() {
    const api = acquireVsCodeApi();
    const panelId = document.body.dataset.panelId;
    const element = id => document.getElementById(id);
    let deliveryId;
    let presentationIdentity;
    let requested;
    let currentView;
    let displayZoom = "fit";
    let draftDirty = false;
    let draftScenario;
    const keys = ["logicalWidth", "logicalHeight", "scale", "colorScheme", "contrast", "density"];
    const fillDraft = chosen => { for (const key of keys) element(key).value = chosen?.[key] ?? ""; draftDirty = false; element("draftError").textContent = ""; };
    const closeSheet = () => { element("presentation").hidden = true; element("appearance").disabled = !requested; element("settings").setAttribute?.("aria-expanded", "false"); fillDraft(requested); element("settings").focus?.(); };
    const sizeImage = () => {
        if (!currentView?.frame) return;
        const frame = currentView.frame;
        const canvas = element("canvas");
        const zoom = displayZoom === "fit" ? Math.min(1, (canvas.clientWidth || frame.logicalWidth) / frame.logicalWidth,
            (canvas.clientHeight || frame.logicalHeight) / frame.logicalHeight) : Number(displayZoom);
        element("image").style.width = `${frame.logicalWidth * zoom}px`;
        element("image").style.height = `${frame.logicalHeight * zoom}px`;
        const shown = frame.presentation;
        element("caption").textContent = `${frame.scenarioTitle || frame.scenarioId || "Accepted frame"} · ${frame.logicalWidth} × ${frame.logicalHeight} · render scale ${frame.scale}${shown ? " · " + shown.colorScheme + " · " + shown.contrast + " contrast · density " + shown.density : ""}${frame.generation ? " · generation " + frame.generation : ""} · display ${Math.round(zoom * 100)}%${currentView.stale ? " · previous image, out of date" : ""}. Image is noninteractive.`;
    };
    const send = (action, identity = deliveryId) => { if (identity) api.postMessage({ version: 1, panelId, deliveryId: identity, ...action }); };
    const ready = () => api.postMessage({ version: 1, panelId, kind: "ready" });
    const labels = { idle: "Ready", discovering: "Loading scenarios", building: "Building preview", rendering: "Rendering preview", current: "Ready · saved source", stale: "Previous image · out of date", stopping: "Stopping preview · waiting for cleanup", stopped: "Stopped · changes apply on Start", suspended: "Preview paused while hidden", blocked: "Cleanup needs attention", untrusted: "Workspace trust required", error: "Preview unavailable" };
    element("scenario").addEventListener("change", () => send({ kind: "select", scenarioId: element("scenario").value }));
    element("zoom").addEventListener("change", () => send({ kind: "zoom", zoom: element("zoom").value === "fit" ? "fit" : Number(element("zoom").value) }));
    for (const kind of ["reset", "start", "stop", "output", "pickScenario"]) element(kind).addEventListener("click", () => send({ kind }));
    element("settings").addEventListener("click", () => {
        if (element("presentation").hidden === false) { closeSheet(); return; }
        fillDraft(requested); element("presentation").hidden = false; element("appearance").disabled = true;
        element("settings").setAttribute?.("aria-expanded", "true"); element("logicalWidth").focus?.();
    });
    element("cancel").addEventListener("click", closeSheet);
    element("appearance").addEventListener("click", () => { if (requested && element("presentation").hidden !== false) send({ kind: "presentation", presentation: { ...requested, colorScheme: requested.colorScheme === "light" ? "dark" : "light" } }); });
    for (const key of keys) element(key).addEventListener("input", () => { draftDirty = true; });
    element("defaults").addEventListener("click", () => fillDraft(currentView?.scenarios.find(item => item.id === currentView.selection.scenarioId)));
    document.addEventListener("keydown", event => { if (event.key === "Escape" && element("presentation").hidden === false) { event.preventDefault(); closeSheet(); } });
    window.addEventListener("resize", sizeImage);
    if (typeof ResizeObserver !== "undefined") new ResizeObserver(sizeImage).observe(element("canvas"));
    element("presentation").addEventListener("submit", event => {
        event.preventDefault();
        const presentation = {};
        for (const key of ["logicalWidth", "logicalHeight", "scale", "density"]) presentation[key] = Number(element(key).value);
        for (const key of ["colorScheme", "contrast"]) presentation[key] = element(key).value;
        const extent = value => Number.isFinite(value) && value >= 1 && value <= 8192;
        const factor = value => Number.isFinite(value) && value >= 0.25 && value <= 4;
        const width = Math.ceil(Math.fround(Math.fround(presentation.logicalWidth) * Math.fround(presentation.scale)));
        const height = Math.ceil(Math.fround(Math.fround(presentation.logicalHeight) * Math.fround(presentation.scale)));
        if (!extent(presentation.logicalWidth) || !extent(presentation.logicalHeight) || !factor(presentation.scale) || !factor(presentation.density)
            || !["light", "dark"].includes(presentation.colorScheme) || !["normal", "high"].includes(presentation.contrast)
            || width > 8192 || height > 8192 || width * height > 16777216) {
            element("draftError").textContent = "Use logical sizes 1–8192 and scale/density 0.25–4. Rendered dimensions must stay within 8192 pixels and 16,777,216 total pixels."; return;
        }
        send({ kind: "presentation", presentation });
        closeSheet();
    });
    window.addEventListener("message", event => {
        const message = event.data;
        if (message?.version !== 1 || message.panelId !== panelId || message.kind !== "view" || typeof message.deliveryId !== "string") return;
        deliveryId = message.deliveryId;
        const view = message.view;
        currentView = view;
        displayZoom = view.zoom;
        element("status").textContent = (labels[view.phase] || "Preview") + (view.stale && view.phase !== "stale" ? " · previous image is out of date" : "") + (view.catalogStale ? " · catalog out of date" : "");
        element("diagnostic").textContent = view.diagnostic;
        const scenario = element("scenario");
        scenario.replaceChildren();
        for (const item of view.scenarios) {
            const option = document.createElement("option"); option.value = item.id; option.textContent = item.title; scenario.appendChild(option);
        }
        scenario.value = view.selection.scenarioId || "";
        scenario.disabled = !view.scenarios.length;
        element("pickScenario").textContent = view.scenarios.find(item => item.id === view.selection.scenarioId)?.title || "Choose scenario";
        element("pickScenario").disabled = !view.scenarios.length || view.phase === "stopping" || view.phase === "blocked";
        element("start").hidden = view.running || view.phase === "stopping" || view.phase === "blocked"; element("stop").hidden = !view.running && view.phase !== "stopping";
        element("start").disabled = view.phase === "stopping" || view.phase === "blocked";
        element("stop").disabled = view.phase === "stopping"; element("stop").textContent = view.phase === "stopping" ? "Stopping…" : "Stop";
        element("reset").disabled = !view.running || view.phase === "stopping";
        const chosen = view.selection.presentation;
        requested = chosen;
        element("appearance").textContent = chosen?.colorScheme === "dark" ? "Dark" : "Light";
        element("appearance").disabled = !chosen || element("presentation").hidden === false || view.phase === "stopping" || view.phase === "blocked";
        element("settings").disabled = !chosen || view.phase === "stopping" || view.phase === "blocked";
        element("apply").textContent = view.running ? "Apply & restart" : "Apply for next start";
        element("apply").disabled = !chosen || view.phase === "stopping" || view.phase === "blocked";
        element("defaults").disabled = !chosen || view.phase === "stopping" || view.phase === "blocked";
        element("settings").textContent = view.frame ? `${view.frame.logicalWidth} × ${view.frame.logicalHeight}` : "Presentation";
        element("controls").disabled = !chosen || view.phase === "stopping" || view.phase === "blocked";
        const identity = JSON.stringify([view.selection.scenarioId, chosen]);
        if (identity !== presentationIdentity) {
            presentationIdentity = identity;
            if (!draftDirty || element("presentation").hidden !== false || draftScenario !== view.selection.scenarioId) fillDraft(chosen);
        }
        draftScenario = view.selection.scenarioId;
        element("zoom").value = view.zoom;
        const diagnostics = element("diagnostics"); diagnostics.replaceChildren();
        for (const item of view.diagnostics) {
            const button = document.createElement("button"); button.type = "button"; button.textContent = item.message || "Open source diagnostic";
            button.addEventListener("click", () => send({ kind: "diagnostic", index: item.index }, message.deliveryId)); diagnostics.appendChild(button);
        }
        const image = element("image");
        const ack = () => api.postMessage({ version: 1, panelId, kind: "ack", deliveryId: message.deliveryId });
        if (view.frame) {
            image.onload = ack;
            image.onerror = () => { element("diagnostic").textContent = "The accepted image could not be displayed."; };
            image.hidden = false; element("empty").hidden = true;
            sizeImage();
            image.style.opacity = view.stale ? "0.6" : "1";
            image.alt = view.stale ? "Previous compiled component preview, out of date" : "Compiled component preview";
            const source = "data:image/png;base64," + view.frame.png;
            if (image.src === source && image.complete && image.naturalWidth > 0) {
                image.onload = null;
                ack();
            } else image.src = source;
        } else { image.onload = null; image.onerror = null; image.removeAttribute("src"); image.hidden = true; element("empty").hidden = false;
            element("caption").textContent = "Images are noninteractive. Display zoom does not change renderer scale."; ack(); }
    });
    document.addEventListener("visibilitychange", () => { if (!document.hidden) ready(); });
    ready();
}

module.exports = { createPreviewPanel };
