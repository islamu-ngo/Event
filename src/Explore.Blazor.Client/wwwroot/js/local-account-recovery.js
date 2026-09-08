// ABOUTME: Removes Local lifecycle capabilities from history before returning their memory-only pointer.
// ABOUTME: Posts once through same-origin antiforgery without exposing response bodies or logging credentials.

export function takeLocalAccountCapability() {
    const hash = window.location.hash;
    const query = new URLSearchParams(window.location.search);
    const keys = ['operationId', 'localSubjectId', 'personalActorId', 'externalLoginId', 'generation'];
    const privateQuery = [...keys, 'purpose', 'token'].some(key => query.has(key));
    if (!hash && !privateQuery) return null;
    if (privateQuery) for (const key of [...keys, 'purpose', 'token']) query.delete(key);
    const search = query.toString();
    window.history.replaceState(window.history.state, '', window.location.pathname + (search ? '?' + search : ''));
    if (privateQuery) return {};
    const fields = new URLSearchParams(hash.slice(1));
    const pointer = {};
    for (const key of keys) {
        const value = fields.get(key);
        if (fields.getAll(key).length !== 1 || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value || '')) return {};
        pointer[key] = value;
    }
    const purpose = fields.get('purpose');
    const token = fields.get('token');
    if (fields.getAll('purpose').length !== 1 || !['1', '2', '3'].includes(purpose)
        || fields.getAll('token').length !== 1 || !token || token.length > 8192) return {};
    pointer.purpose = Number(purpose);
    pointer.token = token;
    return pointer;
}

export async function submitLocalAccountRequest(path, body) {
    const allowed = ['/email-verifications', '/email-verifications/consume', '/password-recoveries', '/password-recoveries/consume', '/password'];
    if (!allowed.some(suffix => path === '/bff/auth/local' + suffix) || window.location.hash) return 400;
    try {
        const { getCookie } = await import('./bff.js');
        let xsrf = getCookie('XSRF-TOKEN');
        if (!xsrf) {
            await fetch('/auth/status', { credentials: 'same-origin', cache: 'no-store' });
            xsrf = getCookie('XSRF-TOKEN');
        }
        if (!xsrf) return 400;
        const response = await fetch(path, {
            method: 'POST', credentials: 'same-origin', mode: 'same-origin', cache: 'no-store', redirect: 'error',
            headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': xsrf },
            body: JSON.stringify(body)
        });
        return response.status;
    } catch {
        return 0;
    }
}
