"use strict";

const { randomUUID } = require("node:crypto");
const { resolvePresentation } = require("./preview-protocol");

function snapshot(value, depth = 0) {
    if (depth > 4) throw new Error("Preview selection exceeds its depth bound.");
    if (Array.isArray(value)) return Object.freeze(value.map(child => snapshot(child, depth + 1)));
    if (value && typeof value === "object")
        return Object.freeze(Object.fromEntries(Object.entries(value).map(([key, child]) => [key, snapshot(child, depth + 1)])));
    if (value !== undefined && !["string", "number", "boolean"].includes(typeof value))
        throw new Error("Preview selection contains unsupported data.");
    return value;
}

// Process adapters resolve only after their owned tree has stopped. A failed
// termination is sticky: replacing the coordinator must not imply it was reaped.
function createPreviewCoordinator({ isTrusted, isSupported, build, verify, discover, render, openLive, release,
    onState = () => {}, log = () => {}, sessionId = randomUUID() }) {
    let epoch = 0n;
    let selection;
    let active;
    let liveOwner;
    let cleanupDiagnostic;
    let tail = Promise.resolve();
    let lastGood;
    let catalog;
    let catalogGeneration;
    let diagnostics = Object.freeze([]);
    let blocked = false;
    let disposed = false;
    let state = Object.freeze({ phase: "idle", generation: "0", stale: false, catalogStale: false, diagnostics });

    function publish(phase, generation, diagnostic) {
        state = Object.freeze({ phase, generation, frame: lastGood,
            interactive: phase === "current" && !!liveOwner && liveOwner.frame === lastGood,
            stale: !!lastGood && phase !== "current", diagnostic, diagnostics,
            catalog, catalogStale: !!catalog && catalogGeneration !== generation,
            selectedScenarioId: selection?.scenarioId,
            effectivePresentation: lastGood?.effectivePresentation });
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

    async function execute(chosen, ticket, initial) {
        const controller = new AbortController();
        active = controller;
        const generation = String(ticket);
        const request = Object.freeze({ sessionId, generation, requestId: randomUUID(), presentationId: randomUUID(), selection: chosen });
        let artifact;
        let accepted;
        let candidateCatalog;
        let catalogVerified = false;
        let catalogFresh = false;
        let failure;
        let live;
        let owner;
        let cleanupFailed = false;
        try {
            if (!current(ticket, controller.signal)) return state;
            publish("building", generation);
            artifact = await build(request, controller.signal);
            if (!current(ticket, controller.signal)) return state;
            if (!await verify(artifact, controller.signal))
                throw new Error("Preview inputs changed during the build. Save or refresh to rebuild.");
            if (!current(ticket, controller.signal)) return state;
            publish("discovering", generation);
            candidateCatalog = snapshot(await discover(artifact, request, controller.signal));
            if (!current(ticket, controller.signal)) return state;
            if (candidateCatalog.sessionId !== sessionId || candidateCatalog.generation !== generation)
                throw new Error("The preview catalog does not belong to this request.");
            if (!await verify(artifact, controller.signal))
                throw new Error("Preview inputs changed during catalog discovery. Save or refresh to rebuild.");
            if (!current(ticket, controller.signal)) return state;
            catalogVerified = true;
            catalogFresh = true;
            const scenario = candidateCatalog.scenarios.find(entry => entry.id === chosen.scenarioId);
            if (!scenario) throw new Error("The selected preview scenario is not registered in the current catalog.");
            const expectedPresentation = resolvePresentation(scenario, chosen.presentation);
            publish("rendering", generation);
            catalogFresh = false;
            if (openLive) {
                live = await openLive(artifact, request, controller.signal);
                owner = { session: live, request, ticket, controller, frame: undefined, displayed: undefined,
                    actions: Promise.resolve(), pendingActions: 0, failure: undefined, ended: false,
                    displayedFrames: new WeakSet() };
                liveOwner = owner;
                // Observe the complete process lifetime even if a frame read fails first.
                const ended = live.completion.then(() => {
                    owner.ended = true;
                    if (!controller.signal.aborted) {
                        owner.failure ??= new Error("The live preview worker exited unexpectedly.");
                        controller.abort();
                        throw owner.failure;
                    }
                    return null;
                }, error => {
                    owner.ended = true;
                    owner.failure ??= error;
                    controller.abort();
                    throw error;
                });
                ended.catch(() => {});
                let sequence = 0;
                while (current(ticket, controller.signal)) {
                    const frame = await Promise.race([live.readFrame(controller.signal), ended]);
                    if (!current(ticket, controller.signal)) break;
                    if (!frame) throw new Error("The live preview worker ended before Stop.");
                    if (sequence === 0 && !await verify(artifact, controller.signal))
                        throw new Error("Preview inputs changed before frame admission. Save or refresh to rebuild.");
                    if (owner.ended) throw owner.failure ?? new Error("The live preview worker ended before frame admission.");
                    if (!current(ticket, controller.signal)) break;
                    validateFrame(frame, request, expectedPresentation);
                    if (!Number.isSafeInteger(frame.frameSequence) || frame.frameSequence <= sequence)
                        throw new Error("The live preview frame sequence did not advance.");
                    sequence = frame.frameSequence;
                    // Preserve the adapter's object capability for ACK/input. Labels stay metadata.
                    lastGood = frame;
                    owner.frame = frame;
                    catalog = candidateCatalog;
                    catalogGeneration = generation;
                    catalogFresh = true;
                    diagnostics = Object.freeze([]);
                    publish("current", generation);
                    cleanupDiagnostic = undefined;
                    initial(state);
                }
                return state;
            }
            const frame = await render(artifact, request, controller.signal);
            if (!current(ticket, controller.signal)) return state;
            if (!await verify(artifact, controller.signal))
                throw new Error("Preview inputs changed before frame admission. Save or refresh to rebuild.");
            if (!current(ticket, controller.signal)) return state;
            catalogFresh = true;
            // Adapters validate protocol, identity, bytes and dimensions before returning.
            // Correlation is checked here too so even a late, valid frame cannot publish.
            validateFrame(frame, request, expectedPresentation);
            accepted = Object.freeze({ ...frame, scenarioTitle: scenario.title });
        } catch (error) {
            // An independently supervised verifier can fail to reap while the
            // live worker is exiting. Unknown ownership must stay sticky.
            failure = error?.code === "termination-failed" ? error : owner?.failure ?? error;
        } finally {
            failure ??= owner?.failure;
            if (live) {
                try { await live.stop(); }
                catch (error) {
                    cleanupFailed = true;
                    if (!failure || failure.code === "cancelled" || error.code === "termination-failed") failure = error;
                }
            }
            if (liveOwner === owner) liveOwner = undefined;
            // No directory release or replacement is safe after uncertain termination.
            if (failure?.code !== "termination-failed" && artifact) {
                try {
                    const retained = await release(artifact, { failed: !!failure && failure.code !== "cancelled" && (!controller.signal.aborted || !!owner?.failure || cleanupFailed),
                        diagnostic: failure?.message });
                    if (failure && retained?.diagnosticDirectory)
                        failure = Object.assign(new Error(retained.diagnostic
                            ?? `${failure.message} Retained diagnostics: ${retained.diagnosticDirectory}`),
                        { code: failure.code, diagnostics: failure.diagnostics });
                } catch (error) { failure = error; }
            }
            if (cleanupFailed && failure?.code !== "termination-failed") {
                cleanupDiagnostic = { epoch, message: failureMessage(failure) };
                log("Preview cleanup failed: " + cleanupDiagnostic.message);
            }
            if (active === controller) active = undefined;
            if (failure?.code === "termination-failed") {
                blocked = true;
                publish("blocked", String(epoch), failureMessage(failure));
            } else if (ticket === epoch && !disposed && !blocked) {
                if (catalogVerified && current(ticket, controller.signal)) {
                    catalog = candidateCatalog;
                    catalogGeneration = catalogFresh ? generation : undefined;
                }
                diagnostics = snapshot(failure?.diagnostics ?? []);
                if (!isTrusted()) publish("untrusted", generation, "Trust this workspace to run a preview.");
                else if (failure && (!controller.signal.aborted || owner?.failure))
                    publish(lastGood ? "stale" : "error", generation, failureMessage(failure));
                else if (accepted && current(ticket, controller.signal)) {
                    lastGood = accepted;
                    publish("current", generation);
                }
            }
        }
        return state;
    }

    function validateFrame(frame, request, expectedPresentation) {
        if (frame.sessionId !== request.sessionId || frame.generation !== request.generation
            || frame.requestId !== request.requestId || frame.scenarioId !== request.selection.scenarioId
            || frame.presentationId !== request.presentationId)
            throw new Error("The preview frame does not belong to this request.");
        if (!frame.effectivePresentation || Object.entries(expectedPresentation)
            .some(([key, value]) => frame.effectivePresentation[key] !== value))
            throw new Error("The preview frame does not match its effective presentation.");
    }

    function action(frame, operation, { cleanup = false, continuation = false, displayed = false } = {}) {
        const owner = liveOwner;
        const belongs = () => owner && liveOwner === owner && !owner.ended && !owner.controller.signal.aborted
            && frame?.sessionId === owner.request.sessionId && frame?.generation === owner.request.generation
            && frame?.requestId === owner.request.requestId;
        const validFrame = () => owner.frame === frame || displayed && owner.displayed === frame
            || continuation && owner.displayedFrames.has(frame);
        if (!belongs() || !cleanup && (!current(owner.ticket, owner.controller.signal) || !validFrame()))
            return Promise.resolve(false);
        const fail = error => {
            owner.failure ??= error;
            owner.controller.abort();
            if (owner.ticket === epoch && !blocked) publish("stale", String(epoch), failureMessage(error));
            throw error;
        };
        if (++owner.pendingActions > 64) {
            --owner.pendingActions;
            try { fail(new Error("Preview input exceeded its pending operation bound.")); }
            catch (error) { return Promise.reject(error); }
        }
        const next = owner.actions.then(async () => {
            if (!belongs() || !cleanup && (!current(owner.ticket, owner.controller.signal) || !validFrame())) return false;
            return operation(owner);
        }).catch(fail).finally(() => { --owner.pendingActions; });
        owner.actions = next.catch(() => {});
        return next;
    }

    function acknowledge(frame) {
        return action(frame, async owner => {
            if (owner.displayed === frame) return false;
            const accepted = await owner.session.acknowledge(frame);
            if (accepted) { owner.displayed = frame; owner.displayedFrames.add(frame); }
            return accepted;
        });
    }

    function input(frame, event) {
        const release = event?.type === "pointer" && ["up", "cancel"].includes(event.action)
            || event?.type === "key" && event.action === "up";
        const displayedPointer = event?.type === "wheel" || event?.type === "pointer" && event.action === "down";
        // The adapter owns exact gesture-down identities. This exception only
        // permits it to complete an already-owned release, never a new hit test.
        // Fresh pointer input may still describe the last displayed frame while
        // another image is pending; native checks its input geometry against Core.
        return action(frame, owner => owner.displayed === frame || release
            ? owner.session.input(frame, event) : false, { continuation: release, displayed: displayedPointer });
    }

    function focus(frame, focused) {
        if (typeof focused !== "boolean") return Promise.resolve(false);
        return action(frame, owner => owner.session.focus(focused), { cleanup: !focused, displayed: focused });
    }

    function start(chosen) {
        if (disposed) throw new Error("The preview coordinator has been disposed.");
        if (blocked) return Promise.resolve(state);
        selection = snapshot(chosen);
        cleanupDiagnostic = undefined;
        const ticket = ++epoch;
        diagnostics = Object.freeze([]);
        active?.abort();
        if (!isTrusted()) publish("untrusted", String(ticket), "Trust this workspace to run a preview.");
        else if (!isSupported(selection)) publish("error", String(ticket), "Preview requires a local Windows desktop workspace.");
        else publish("building", String(ticket));
        // Queued obsolete requests never execute; running requests finish their
        // bounded cleanup before the newest generation acquires process ownership.
        let resolveInitial;
        const initial = new Promise(resolve => { resolveInitial = resolve; });
        const next = tail.then(() => execute(selection, ticket, resolveInitial));
        tail = next.catch(error => { log("Preview coordinator failure", error); });
        void tail.then(() => resolveInitial(state));
        return openLive ? initial : next;
    }

    function refresh() { return selection ? start(selection) : Promise.resolve(state); }

    function invalidate() {
        if (disposed || blocked || !selection) return;
        ++epoch;
        diagnostics = Object.freeze([]);
        active?.abort();
        publish("building", String(epoch));
    }

    async function stop() {
        selection = undefined;
        ++epoch;
        diagnostics = Object.freeze([]);
        active?.abort();
        const ticket = epoch;
        if (!blocked) publish("stopping", String(ticket));
        await tail;
        if (!blocked && epoch === ticket) publish("stopped", String(ticket), cleanupDiagnostic?.epoch === ticket ? cleanupDiagnostic.message : undefined);
        return state;
    }

    async function dispose() {
        disposed = true;
        return stop();
    }

    return { start, refresh, invalidate, stop, dispose, acknowledge, input, focus, get state() { return state; } };
}

module.exports = { createPreviewCoordinator };
