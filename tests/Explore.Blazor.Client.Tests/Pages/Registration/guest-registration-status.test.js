// ABOUTME: Executes the shipped early guest-status script against browser host APIs.
// ABOUTME: Guards fragment erasure, scope binding, explicit private export, and failed clipboard behavior.

import { expect, test } from 'bun:test';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

const shipped = readFileSync(new URL('../../../../src/Explore.Blazor.Client/wwwroot/js/guest-registration-status.js', import.meta.url), 'utf8');
const eventId = crypto.randomUUID();
const orderId = crypto.randomUUID();
const secret = () => Buffer.from(crypto.getRandomValues(new Uint8Array(32))).toString('base64url');
const route = (prefix = '') => `${prefix}/registration/guest/events/${eventId}/orders/${orderId}/status`;

function browser({ fragment = '', query = '', prefix = '', script = shipped } = {}) {
    const calls = [];
    const location = new URL(`https://event.test${route(prefix)}${query}${fragment}`);
    const blobs = new Map();
    const anchor = { click() { calls.push({ type: 'download', href: this.href, filename: this.download }); }, remove() {} };
    const host = {
        window: { location, history: { replaceState(state, title, url) { calls.push({ type: 'scrub', url }); location.href = new URL(url, location).href; } } },
        document: { baseURI: `https://event.test${prefix}/`, createElement() { return anchor; }, body: { appendChild() {} } },
        navigator: { clipboard: { async writeText(value) { calls.push({ type: 'copy', value }); } } },
        URL: class extends URL {
            static createObjectURL(blob) { const url = 'blob:' + crypto.randomUUID(); blobs.set(url, blob); return url; }
            static revokeObjectURL(url) { calls.push({ type: 'revoke', url }); }
        },
        Blob
    };
    runInNewContext(script, host);
    return { ...host, api: host.window.guestRegistrationStatus, calls, blobs, anchor };
}

test('initial execution scrubs before any consumer and restores only the exact route once', () => {
    const token = secret();
    const b = browser({ fragment: '#capability=' + token, prefix: '/t/community' });
    expect(b.window.location.hash).toBe('');
    expect(b.calls).toEqual([{ type: 'scrub', url: route('/t/community') }]);
    expect(b.api.take(eventId, orderId)).toBe(token);
    expect(b.api.take(eventId, orderId)).toBeNull();
    const foreign = browser({ fragment: '#capability=' + token });
    expect(foreign.api.take(eventId, crypto.randomUUID())).toBe('');
    expect(foreign.api.take(eventId, orderId)).toBeNull();
    const changedTenant = browser({ fragment: '#capability=' + token });
    changedTenant.window.location.pathname = route('/t/other');
    expect(changedTenant.api.take(eventId, orderId)).toBe('');
});

test('malformed, duplicate, encoded, query-only, and mixed inputs are erased and denied', () => {
    const token = secret();
    for (const input of [
        { fragment: '#capability=%ZZ' }, { fragment: '#capability=' + token + '&capability=' + token },
        { fragment: '#capability=' + token + '&extra=value' }, { fragment: '#capability=' + token + '=' },
        { fragment: '#token=' + token }, { fragment: '#capability=%41' + token.slice(1) },
        { query: '?capability=' + token }, { fragment: '#capability=' + token, query: '?capability=' + secret() }
    ]) {
        const b = browser(input);
        expect(b.window.location.hash + b.window.location.search).toBe('');
        expect(b.api.take(eventId, orderId)).toBe('');
        expect(b.calls).toEqual([{ type: 'scrub', url: route() }]);
    }
});

if (process.env.GUEST_STATUS_EVIDENCE) {
    test('native initial HTML executes its first inline scrubber before subsequent network or analytics, including PathBase and malformed input', () => {
        for (const [file, prefix] of [['landing.html', ''], ['landing-pathbase.html', '/nested/community']]) {
            const html = readFileSync(`${process.env.GUEST_STATUS_EVIDENCE}/${file}`, 'utf8');
            const first = /<head(?:\s[^>]*)?>\s*<script\b(?![^>]*\bsrc=)(?=[^>]*\bnonce="[^"]+")[^>]*>([\s\S]*?)<\/script>/.exec(html);
            expect(first).not.toBeNull();
            expect(first[1]).toBe(shipped);
            expect(/<base href="([^"]+)"/.exec(html)[1]).toBe(prefix + '/');
            for (const fragment of ['#capability=' + secret(), '#capability=%ZZ']) {
                const b = browser({ prefix, fragment, script: first[1] });
                // The next parser/network or analytics consumer receives the cleansed document location.
                const observedByNextConsumer = b.window.location.href;
                expect(observedByNextConsumer).toBe(`https://event.test${route(prefix)}`);
                expect(b.calls[0]).toEqual({ type: 'scrub', url: route(prefix) });
            }
        }
    });
}

test('ordinary clean navigation has no fragment authority', () => {
    const b = browser();
    expect(b.api.take(eventId, orderId)).toBeNull();
    expect(b.calls).toEqual([]);
});

test('explicit copy builds PathBase-aware URL transiently without changing location or DOM attributes', async () => {
    const token = secret();
    const b = browser({ prefix: '/t/community' });
    expect(await b.api.save('copy', eventId, orderId, token)).toBe(true);
    expect(b.calls).toEqual([{ type: 'copy', value: `https://event.test${route('/t/community')}#capability=${token}` }]);
    expect(b.window.location.href).not.toContain(token);
    expect(JSON.stringify(b.anchor)).not.toContain(token);
});

test('failed or unavailable clipboard reports failure without fallback or silent success', async () => {
    const b = browser();
    b.navigator.clipboard.writeText = async () => { throw new DOMException('Denied', 'NotAllowedError'); };
    expect(await b.api.save('copy', eventId, orderId, secret())).toBe(false);
    b.navigator.clipboard = undefined;
    expect(await b.api.save('copy', eventId, orderId, secret())).toBe(false);
    expect(b.calls).toEqual([]);
});

test('download contains the private bookmark but only a revocable blob URL enters DOM', async () => {
    const token = secret();
    const b = browser();
    expect(await b.api.save('download', eventId, orderId, token)).toBe(true);
    const download = b.calls.find(call => call.type === 'download');
    expect(download.href).toStartWith('blob:');
    expect(download.filename).toBe('private-registration-status.txt');
    expect(await b.blobs.get(download.href).text()).toBe(`https://event.test${route()}#capability=${token}\n`);
    expect(b.calls.at(-1)).toEqual({ type: 'revoke', url: download.href });
    expect(JSON.stringify(b.anchor)).not.toContain(token);
    expect(b.window.location.href).not.toContain(token);
});
