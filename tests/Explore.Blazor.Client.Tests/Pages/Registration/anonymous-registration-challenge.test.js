// ABOUTME: Executes the shipped dedicated worker and interop with real native Web Crypto.
// ABOUTME: Verifies wire binding, bounded input, progress, cancellation and duplicate-work exclusion without polling.

import { expect, test } from "bun:test";
import { createSolver } from "../../../../src/Explore.Blazor.Client/wwwroot/js/anonymous-registration-challenge-interop.js";

function challenge(difficulty = 16) {
    return { protectedChallenge: "public-worker-contract-vector", difficulty, version: 1,
        expiresAt: new Date(Date.now() + 119000).toISOString() };
}

function signal() {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
}

async function leadingBits(envelope, nonce) {
    const bytes = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(
        `islamu-event:anonymous-registration:v1\n${envelope}\n${nonce}`)));
    let bits = 0;
    for (const byte of bytes) {
        if (byte === 0) bits += 8;
        else return bits + Math.clz32(byte) - 24;
    }
    return bits;
}

for (const difficulty of [16, 17]) {
    test(`real worker solves exact v1 nonce/envelope with ${difficulty} MSB-first zero bits`, async () => {
        const solver = createSolver();
        const input = challenge(difficulty);
        const messages = [];
        try {
            const nonce = await solver.solve(input, { invokeMethodAsync(method, attempts) {
                messages.push({ method, attempts });
                return Promise.resolve();
            } });
            expect(/^[0-9a-f]{16}$/.test(nonce)).toBe(true);
            expect(await leadingBits(input.protectedChallenge, nonce)).toBeGreaterThanOrEqual(difficulty);
            expect(await leadingBits(input.protectedChallenge + "-altered", nonce)).toBeLessThan(difficulty);
            expect(messages[0]).toEqual({ method: "ReportProgress", attempts: 0 });
            expect(messages.every(message => Number.isInteger(message.attempts))).toBe(true);
        } finally { solver.cancel(); }
    }, 30000);
}

test("subscribes to progress before cancel and rejects duplicate work without replacing original", async () => {
    const solver = createSolver();
    const entered = signal();
    const result = solver.solve(challenge(22), { invokeMethodAsync() {
        solver.cancel();
        entered.resolve();
        return Promise.resolve();
    } }).then(() => "solved", error => error.message);
    await expect(solver.solve(challenge(), {})).rejects.toThrow("challenge-busy");
    await entered.promise;
    expect(await result).toBe("challenge-cancelled");
    // Cancellation clears worker authority; a new explicit attempt can fail independently.
    await expect(solver.solve({ ...challenge(), version: 2 }, {})).rejects.toThrow("challenge-unavailable");
}, 5000);

for (const invalid of [
    { version: 2 }, { difficulty: 15 }, { difficulty: 23 }, { difficulty: 16.5 },
    { protectedChallenge: "a".repeat(4097) }, { protectedChallenge: "bad\nenvelope" },
    { expiresAt: new Date(0).toISOString() }, { expiresAt: "invalid" },
    { expiresAt: new Date(Date.now() + 240000).toISOString() }
]) {
    test("invalid worker input cannot produce a proof", async () => {
        const solver = createSolver();
        try { await expect(solver.solve({ ...challenge(), ...invalid }, {})).rejects.toThrow("challenge-unavailable"); }
        finally { solver.cancel(); }
    }, 5000);
}
