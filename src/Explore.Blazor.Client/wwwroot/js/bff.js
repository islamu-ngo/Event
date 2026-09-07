/**
 * Read a cookie value by name from document.cookie.
 * Used by BffClient to read the XSRF-TOKEN cookie for CSRF protection on mutations.
 * @param {string} name - The cookie name to read
 * @returns {string|null} The cookie value, or null if not found
 */
export function getCookie(name) {
    const match = document.cookie.match(new RegExp('(^|;\\s*)' + name + '=([^;]*)'));
    return match ? decodeURIComponent(match[2]) : null;
}

/**
 * Check authentication status by calling the server's /auth/status endpoint.
 * @returns {Promise<{isAuthenticated: boolean, name: string|null}>}
 */
export async function checkAuthStatus() {
    try {
        const response = await fetch('/auth/status', {
            method: 'GET',
            credentials: 'same-origin'
        });

        if (response.ok && response.status === 200) {
            const authInfo = await response.json();
            return {
                isAuthenticated: authInfo.isAuthenticated || false,
                name: authInfo.name || null
            };
        }

        return { isAuthenticated: false, name: null };

    } catch (error) {
        console.log('Auth status check failed:', error);
        return { isAuthenticated: false, name: null };
    }
}

// ── Setup secret BFF helpers ─────────────────────────────────────────
// These use browser fetch so cookies (setup-secret, XSRF-TOKEN) flow correctly
// regardless of Blazor render mode (InteractiveServer, Auto, or WebAssembly).

/**
 * Persist setup secret via the BFF. The server sets an HttpOnly cookie on the response.
 * @param {string} secret - The setup secret to persist
 * @returns {Promise<{ok: boolean, status: number, error: string|null}>}
 */
export async function persistSetupSecret(secret) {
    return await _bffPost('/bff/setup-secret', { secret });
}

/**
 * Sync setup secret for an authenticated session via the BFF.
 * @param {string|null} secret - Optional secret; server falls back to cookie/session if omitted
 * @returns {Promise<{ok: boolean, status: number, error: string|null}>}
 */
export async function syncSetupSecret(secret) {
    return await _bffPost('/bff/setup-secret/sync', { secret: secret || '' });
}

/**
 * Delete the persisted setup secret via the BFF.
 * @returns {Promise<{ok: boolean, status: number, error: string|null}>}
 */
export async function deleteSetupSecret() {
    return await _bffMutate('DELETE', '/bff/setup-secret');
}

const checkoutIssueControllers = new Map();

export async function issueRegistrationPaymentCheckoutTicket(url, guestCapability, operationId) {
    const headers = { 'Accept': 'application/json' };
    const xsrf = getCookie('XSRF-TOKEN');
    if (xsrf) {
        headers['X-CSRF-TOKEN'] = xsrf;
    }
    if (guestCapability) {
        headers['X-Registration-Order-Capability'] = guestCapability;
    }

    const controller = new AbortController();
    checkoutIssueControllers.set(operationId, controller);
    try {
        const response = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            mode: 'same-origin',
            headers,
            signal: controller.signal
        });
        return response.ok ? await response.json() : null;
    } catch (error) {
        if (error.name === 'AbortError') {
            return null;
        }
        throw error;
    } finally {
        if (checkoutIssueControllers.get(operationId) === controller) {
            checkoutIssueControllers.delete(operationId);
        }
    }
}

export function abortRegistrationPaymentCheckoutTicket(operationId) {
    const controller = checkoutIssueControllers.get(operationId);
    if (controller) {
        controller.abort();
        checkoutIssueControllers.delete(operationId);
    }
}

/**
 * Get the current setup secret status (persisted? valid?).
 * @returns {Promise<{hasPersistedSecret: boolean, isValid: boolean, error: string|null}>}
 */
export async function getSetupSecretStatus() {
    try {
        const response = await fetch('/bff/setup-secret', {
            method: 'GET',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        });

        if (response.ok) {
            return await response.json();
        }

        return { hasPersistedSecret: false, isValid: false, error: 'Status check failed.' };
    } catch (error) {
        console.log('Setup secret status check failed:', error);
        return { hasPersistedSecret: false, isValid: false, error: error.message };
    }
}

/** @private Shared POST helper for BFF setup-secret mutations. */
async function _bffPost(url, body) {
    return await _bffMutate('POST', url, body);
}

/**
 * Simple GET + JSON parse helper for browser-side fetch.
 * @param {string} url - The URL to fetch
 * @returns {Promise<any>} The parsed JSON response
 */
export async function fetchJson(url) {
    const response = await fetch(url, {
        method: 'GET',
        credentials: 'same-origin',
        headers: { 'Accept': 'application/json' }
    });
    if (!response.ok) {
        throw new Error('Fetch failed: ' + response.status);
    }
    return await response.json();
}

/**
 * Submit Local Identity credentials from the browser so the BFF Set-Cookie
 * response is applied to the browser cookie jar rather than a server self-call.
 * @param {string} url - Local login or registration endpoint.
 * @param {object} body - Typed Local Identity request body.
 * @returns {Promise<object|null>} The bounded success response, or null.
 */
export async function authenticateLocal(url, body) {
    const result = await _bffMutate('POST', url, body);
    return result.ok ? result.data : null;
}

/** @private Shared mutation helper. Reads XSRF token from cookie if present. */
async function _bffMutate(method, url, body) {
    try {
        const headers = { 'Accept': 'application/json' };
        if (body) {
            headers['Content-Type'] = 'application/json';
        }

        const xsrf = getCookie('XSRF-TOKEN');
        if (xsrf) {
            headers['X-CSRF-TOKEN'] = xsrf;
        }

        const response = await fetch(url, {
            method,
            credentials: 'same-origin',
            headers,
            body: body ? JSON.stringify(body) : undefined
        });

        const text = await response.text();
        const data = tryParseJson(text);
        let error = null;
        if (!response.ok) {
            error = data?.detail
                || data?.title
                || 'Request failed with status ' + response.status;
        }

        return { ok: response.ok, status: response.status, error, data };
    } catch (error) {
        console.log('BFF mutation failed:', error);
        return { ok: false, status: 0, error: error.message };
    }
}

function tryParseJson(text) {
    try {
        return JSON.parse(text);
    } catch {
        return null;
    }
}

function flattenProblemDetailsErrors(errors) {
    if (!errors || typeof errors !== 'object') {
        return [];
    }

    return Object.values(errors)
        .flatMap(value => Array.isArray(value) ? value : [])
        .filter(value => typeof value === 'string');
}
