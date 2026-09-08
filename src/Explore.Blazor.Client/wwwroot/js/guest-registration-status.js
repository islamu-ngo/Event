// ABOUTME: Runs inline in the first host head slot to erase private bookmark input before browser startup.
// ABOUTME: Keeps one exact-route capability in a closure and exports private links only on explicit request.

(() => {
    'use strict';
    const guid = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';
    const route = new RegExp('/registration/guest/events/(' + guid + ')/orders/(' + guid + ')/status/?$', 'i');
    const tokenPattern = /^[A-Za-z0-9_-]{43}$/;
    const path = window.location.pathname;
    const sensitive = /\/registration\/guest\/events\/.*\/status\/?$/i.test(path);
    let pending = null;
    if (sensitive && (window.location.hash || window.location.search)) {
        const fragment = window.location.hash;
        const query = window.location.search;
        // Erase before parsing even malformed input. Never copy input into history state.
        window.history.replaceState(null, '', path);
        const match = route.exec(path);
        const token = fragment.startsWith('#capability=') ? fragment.slice(12) : '';
        pending = { path, eventId: match?.[1].toLowerCase(), orderId: match?.[2].toLowerCase(),
            token: !query && tokenPattern.test(token) ? token : '' };
    }

    window.guestRegistrationStatus = Object.freeze({
        take(eventId, orderId) {
            const captured = pending;
            pending = null;
            if (captured === null) return null;
            return captured.path === window.location.pathname
                && captured.eventId === eventId.toLowerCase() && captured.orderId === orderId.toLowerCase()
                ? captured.token : '';
        },
        async save(action, eventId, orderId, token) {
            if (!tokenPattern.test(token) || !new RegExp('^' + guid + '$', 'i').test(eventId)
                || !new RegExp('^' + guid + '$', 'i').test(orderId)) return false;
            const url = new URL(`registration/guest/events/${eventId}/orders/${orderId}/status`, document.baseURI);
            url.hash = 'capability=' + token;
            try {
                if (action === 'copy') {
                    if (!navigator.clipboard?.writeText) return false;
                    await navigator.clipboard.writeText(url.href);
                    return true;
                }
                if (action !== 'download') return false;
                const blobUrl = URL.createObjectURL(new Blob([url.href + '\n'], { type: 'text/plain;charset=utf-8' }));
                const anchor = document.createElement('a');
                try {
                    anchor.href = blobUrl;
                    anchor.download = 'private-registration-status.txt';
                    document.body.appendChild(anchor);
                    anchor.click();
                } finally {
                    anchor.remove();
                    URL.revokeObjectURL(blobUrl);
                }
                return true;
            } catch {
                // Browser permission/IO failures are an explicit false result; no bearer error details escape.
                return false;
            }
        }
    });
})();
