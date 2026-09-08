// ABOUTME: Executes the shipped Local lifecycle fragment and antiforgery module without source scraping.
// ABOUTME: Models browser host APIs to assert history erasure ordering, exact encoding, and one-shot failed transport.

import { afterEach, expect, test } from 'bun:test';
import { takeLocalAccountCapability, submitLocalAccountRequest } from '../../../../src/Explore.Blazor.Client/wwwroot/js/local-account-recovery.js';

const original = { window: globalThis.window, document: globalThis.document, fetch: globalThis.fetch };
afterEach(() => Object.assign(globalThis, original));

function browser(fragment = '') {
    const calls = [];
    const location = { hash: fragment, pathname: '/auth/local-account-recovery', search: '' };
    globalThis.window = {
        location,
        history: { state: null, replaceState(state, title, url) {
            calls.push({ type: 'replace', url });
            location.hash = '';
        } }
    };
    globalThis.document = { cookie: '' };
    return calls;
}

function pointer() {
    return {
        operationId: crypto.randomUUID(), localSubjectId: crypto.randomUUID(), personalActorId: crypto.randomUUID(),
        externalLoginId: crypto.randomUUID(), generation: crypto.randomUUID(), purpose: '3',
        token: crypto.randomUUID() + '+/%=' + crypto.randomUUID()
    };
}

test('removes the complete fragment before antiforgery or mutation and decodes once', async () => {
    const fields = pointer();
    const calls = browser('#' + new URLSearchParams(fields));
    globalThis.fetch = async (url, options) => {
        calls.push({ type: 'fetch', url, options });
        expect(window.location.hash).toBe('');
        expect(url).not.toContain(fields.token);
        if (url === '/auth/status') {
            document.cookie = 'XSRF-TOKEN=' + encodeURIComponent(crypto.randomUUID());
            return new Response(null, { status: 200 });
        }
        expect(options.headers['X-CSRF-TOKEN']).toBeTruthy();
        expect(options.credentials).toBe('same-origin');
        expect(JSON.parse(options.body)).toEqual({ ...fields, purpose: 3 });
        return new Response(null, { status: 204 });
    };
    const extracted = takeLocalAccountCapability();
    expect(extracted).toEqual({ ...fields, purpose: 3 });
    expect(await submitLocalAccountRequest('/bff/auth/local/password-recoveries/consume', extracted)).toBe(204);
    expect(calls.map(call => call.type)).toEqual(['replace', 'fetch', 'fetch']);
    expect(calls[0].url).toBe('/auth/local-account-recovery');
    expect(takeLocalAccountCapability()).toBeNull();
});

test('malformed and duplicate pointers are erased but cannot become a capability', () => {
    for (const fragment of ['#token=' + crypto.randomUUID(), '#' + new URLSearchParams(pointer()) + '&purpose=1']) {
        const calls = browser(fragment);
        expect(takeLocalAccountCapability()).toEqual({});
        expect(window.location.hash).toBe('');
        expect(calls.length).toBe(1);
    }
});

test('query-only capabilities are rejected and removed from history', () => {
    const calls = browser();
    window.location.search = '?' + new URLSearchParams(pointer());
    expect(takeLocalAccountCapability()).toEqual({});
    expect(calls).toEqual([{ type: 'replace', url: '/auth/local-account-recovery' }]);
});

test('never posts while a fragment remains and never trusts a foreign endpoint', async () => {
    browser('#' + new URLSearchParams(pointer()));
    let count = 0;
    globalThis.fetch = async () => { count++; throw new Error('Unexpected request'); };
    expect(await submitLocalAccountRequest('/bff/auth/local/email-verifications/consume', {})).toBe(400);
    window.location.hash = '';
    expect(await submitLocalAccountRequest('https://external.example.test/recovery', {})).toBe(400);
    expect(count).toBe(0);
});

test('failed mutations are not retried and response details are not parsed', async () => {
    browser();
    document.cookie = 'XSRF-TOKEN=' + crypto.randomUUID();
    let posts = 0;
    globalThis.fetch = async () => { posts++; return new Response(crypto.randomUUID(), { status: 409 }); };
    expect(await submitLocalAccountRequest('/bff/auth/local/email-verifications/consume', pointer())).toBe(409);
    expect(posts).toBe(1);
});
