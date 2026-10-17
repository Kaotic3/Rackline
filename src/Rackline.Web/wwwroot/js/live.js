// Live updates for the public pages, using the SignalR JavaScript client.
//
// A page opts in with one attribute:
//   data-live-tournament="<tournament id>"  - watches every match in the tournament
//   data-live-match="<match id>"            - watches a single match
// Match elements carry data-match, data-state, data-e1 and data-e2, and contain
// .js-score1 / .js-score2 (and optionally .js-bnr1 / .js-bnr2) to fill in.
//
// Scores change in place. Anything structural (a match starting or finishing, a player
// moving on, a status change) reloads the page, so tables and brackets always come
// from the server's own rules.
(() => {
    "use strict";

    let connection = null;
    let watching = null;
    let reloadTimer = null;

    const setStatus = (text) =>
        document.querySelectorAll("[data-live-status]").forEach((el) => (el.textContent = text));

    const reloadSoon = () => {
        clearTimeout(reloadTimer);
        reloadTimer = setTimeout(() => location.reload(), 300);
    };

    const target = () => {
        const tournament = document.querySelector("[data-live-tournament]");
        if (tournament) {
            return { kind: "Tournament", id: tournament.dataset.liveTournament };
        }
        const match = document.querySelector("[data-live-match]");
        return match ? { kind: "Match", id: match.dataset.liveMatch } : null;
    };

    const fill = (root, selector, value) =>
        root.querySelectorAll(selector).forEach((el) => (el.textContent = value));

    const applyMatch = (match) => {
        const el = document.querySelector(`[data-match="${match.id}"]`);
        if (!el) {
            return;
        }
        const structural =
            el.dataset.state !== match.state ||
            (el.dataset.e1 || "") !== (match.slot1.entryId || "") ||
            (el.dataset.e2 || "") !== (match.slot2.entryId || "");
        if (structural) {
            reloadSoon();
            return;
        }
        fill(el, ".js-score1", match.score1);
        fill(el, ".js-score2", match.score2);
        fill(el, ".js-bnr1", match.breakAndRuns1);
        fill(el, ".js-bnr2", match.breakAndRuns2);
        el.classList.remove("flash");
        void el.offsetWidth; // restart the highlight animation
        el.classList.add("flash");
    };

    const connect = async () => {
        if (connection) {
            return connection;
        }
        connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/live")
            .withAutomaticReconnect()
            .build();
        connection.on("MatchUpdated", applyMatch);
        connection.on("MatchesUpdated", (_tournamentId, matches) => matches.forEach(applyMatch));
        connection.on("TournamentUpdated", reloadSoon);
        connection.onreconnecting(() => setStatus("Reconnecting…"));
        connection.onreconnected(async () => {
            // Group membership is lost with the old connection, and updates may have been missed.
            if (watching) {
                await connection.invoke(`Watch${watching.kind}`, watching.id);
            }
            reloadSoon();
        });
        connection.onclose(() => setStatus("Live updates are off. Refresh the page to try again."));
        await connection.start();
        return connection;
    };

    const sync = async () => {
        const next = target();
        if (watching && (!next || next.kind !== watching.kind || next.id !== watching.id)) {
            await connection.invoke(`Unwatch${watching.kind}`, watching.id).catch(() => {});
            watching = null;
        }
        if (next && !watching) {
            try {
                const hub = await connect();
                await hub.invoke(`Watch${next.kind}`, next.id);
                watching = next;
                setStatus("Live updates on");
            } catch {
                setStatus("Live updates are unavailable at the moment.");
            }
        }
    };

    // Blazor's enhanced navigation swaps page content without running page scripts,
    // so re-check what to watch after every navigation as well as on first load.
    if (window.Blazor?.addEventListener) {
        window.Blazor.addEventListener("enhancedload", sync);
    }
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", sync);
    } else {
        sync();
    }
})();
