"use strict";

const { randomUUID } = require("node:crypto");

// Process adapters resolve only after their owned tree has stopped. A failed
// termination is sticky: replacing the coordinator must not imply it was reaped.
function createPreviewCoordinator({ isTrusted, isSupported, build, verify, render, release,
    onState = () => {}, log = () => {}, sessionId = randomUUID() }) {
    let epoch = 0n;
    let selection;
    let active;
    let tail = Promise.resolve();
    let lastGood;
    let blocked = false;
    let disposed = false;
    let state = Object.freeze({ phase: "idle", generation: "0", stale: false });

    function publish(phase, generation, diagnostic) {
        state = Object.freeze({ phase, generation, frame: lastGood,
            stale: !!lastGood && phase !== "current", diagnostic });
        try { onState(state); } catch (error) { log("Preview state observer failed", error); }
        return state;
    }

    function current(ticket, signal) {
        return !disposed && !blocked && ticket === epoch && !signal.aborted
            && !!selection && isTrusted() && isSupported(selection);
    }

    function failureMessage(error) {
        return String(error?.message ?? "The preview operation failed.").slice(0, 2048);
    }

    async function execute(chosen, ticket) {
        const controller = new AbortController();
        active = controller;
        const generation = String(ticket);
        const request = Object.freeze({ sessionId, generation, requestId: randomUUID(), selection: chosen });
        let artifact;
        let accepted;
        let failure;
        try {
            if (!current(ticket, controller.signal)) return state;
            publish("building", generation);
            artifact = await build(request, controller.signal);
            if (!current(ticket, controller.signal)) return state;
            if (!await verify(artifact, controller.signal))
                throw new Error("Preview inputs changed during the build. Save or refresh to rebuild.");
            if (!current(ticket, controller.signal)) return state;
            publish("rendering", generation);
            const frame = await render(artifact, request, controller.signal);
            if (!current(ticket, controller.signal)) return state;
            if (!await verify(artifact, controller.signal))
                throw new Error("Preview inputs changed before frame admission. Save or refresh to rebuild.");
            if (!current(ticket, controller.signal)) return state;
            // Adapters validate protocol, identity, bytes and dimensions before returning.
            // Correlation is checked here too so even a late, valid frame cannot publish.
            if (frame.sessionId !== sessionId || frame.generation !== generation
                || frame.requestId !== request.requestId || frame.scenarioId !== chosen.scenarioId)
                throw new Error("The preview frame does not belong to this request.");
            accepted = frame;
        } catch (error) {
            failure = error;
        } finally {
            // No directory release or replacement is safe after uncertain termination.
            if (failure?.code !== "termination-failed" && artifact) {
                try {
                    const retained = await release(artifact, { failed: !!failure && failure.code !== "cancelled" && !controller.signal.aborted,
                        diagnostic: failure?.message });
                    if (failure && retained?.diagnosticDirectory)
                        failure = Object.assign(new Error(retained.diagnostic
                            ?? `${failure.message} Retained diagnostics: ${retained.diagnosticDirectory}`), { code: failure.code });
                } catch (error) { failure = error; }
            }
            if (active === controller) active = undefined;
            if (failure?.code === "termination-failed") {
                blocked = true;
                publish("blocked", String(epoch), failureMessage(failure));
            } else if (ticket === epoch && !disposed && !blocked) {
                if (!isTrusted()) publish("untrusted", generation, "Trust this workspace to run a preview.");
                else if (failure && !controller.signal.aborted)
                    publish(lastGood ? "stale" : "error", generation, failureMessage(failure));
                else if (accepted && current(ticket, controller.signal)) {
                    lastGood = accepted;
                    publish("current", generation);
                }
            }
        }
        return state;
    }

    function start(chosen) {
        if (disposed) throw new Error("The preview coordinator has been disposed.");
        if (blocked) return Promise.resolve(state);
        selection = Object.freeze({ ...chosen });
        const ticket = ++epoch;
        active?.abort();
        if (!isTrusted()) publish("untrusted", String(ticket), "Trust this workspace to run a preview.");
        else if (!isSupported(selection)) publish("error", String(ticket), "Preview requires a local Windows desktop workspace.");
        else publish("building", String(ticket));
        // Queued obsolete requests never execute; running requests finish their
        // bounded cleanup before the newest generation acquires process ownership.
        const next = tail.then(() => execute(selection, ticket));
        tail = next.catch(error => { log("Preview coordinator failure", error); });
        return next;
    }

    function refresh() { return selection ? start(selection) : Promise.resolve(state); }

    function invalidate() {
        if (disposed || blocked || !selection) return;
        ++epoch;
        active?.abort();
        publish("building", String(epoch));
    }

    async function stop() {
        selection = undefined;
        ++epoch;
        active?.abort();
        if (!blocked) publish("stopped", String(epoch));
        await tail;
        return state;
    }

    async function dispose() {
        disposed = true;
        return stop();
    }

    return { start, refresh, invalidate, stop, dispose, get state() { return state; } };
}

module.exports = { createPreviewCoordinator };
