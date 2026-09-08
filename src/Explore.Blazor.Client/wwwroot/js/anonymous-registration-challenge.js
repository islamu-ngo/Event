// ABOUTME: Dedicated bounded anonymous-registration proof worker using native Web Crypto only.
// ABOUTME: Emits value-free failures and numeric progress; never allocates or persists registration authority.

let running = false;
self.onmessage = async ({ data }) => {
    if (running) return;
    running = true;
    const { protectedChallenge, expiresAt, difficulty, version } = data ?? {};
    const expiry = Date.parse(expiresAt);
    const now = Date.now();
    if (version !== 1 || !Number.isInteger(difficulty) || difficulty < 16 || difficulty > 22 ||
        typeof protectedChallenge !== "string" || !/^[A-Za-z0-9_-]{1,4096}$/.test(protectedChallenge) ||
        !Number.isFinite(expiry) || expiry <= now || expiry > now + 120000) {
        self.postMessage({ type: "error", code: "invalid-or-expired" });
        return;
    }
    if (!self.crypto?.subtle) {
        self.postMessage({ type: "error", code: "unsupported" });
        return;
    }
    const deadline = Math.min(expiry, now + 90000);
    const encoder = new TextEncoder();
    const prefix = `islamu-event:anonymous-registration:v1\n${protectedChallenge}\n`;
    const wholeBytes = Math.floor(difficulty / 8);
    const remainingBits = difficulty % 8;
    let nextProgress = now + 1000;
    self.postMessage({ type: "progress", attempts: 0 });
    try {
        for (let attempt = 0; attempt < 16777216; attempt++) {
            if (Date.now() >= deadline) {
                self.postMessage({ type: "error", code: "expired-or-time-limit" });
                return;
            }
            // This bounded search is within Number's exact integer range and the unsigned-64 wire range.
            const nonce = attempt.toString(16).padStart(16, "0");
            const hash = new Uint8Array(await self.crypto.subtle.digest("SHA-256", encoder.encode(prefix + nonce)));
            let valid = true;
            for (let index = 0; index < wholeBytes; index++) valid &&= hash[index] === 0;
            if (remainingBits) valid &&= (hash[wholeBytes] >>> (8 - remainingBits)) === 0;
            if (valid) {
                if (Date.now() >= deadline) {
                    self.postMessage({ type: "error", code: "expired-or-time-limit" });
                } else {
                    self.postMessage({ type: "solved", nonce });
                }
                return;
            }
            if (Date.now() >= nextProgress) {
                self.postMessage({ type: "progress", attempts: attempt + 1 });
                nextProgress = Date.now() + 1000;
            }
        }
        self.postMessage({ type: "error", code: "work-limit" });
    } catch {
        self.postMessage({ type: "error", code: "crypto-unavailable" });
    }
};
