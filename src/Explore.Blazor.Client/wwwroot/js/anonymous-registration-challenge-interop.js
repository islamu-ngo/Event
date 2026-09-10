// ABOUTME: Owns one cancellable dedicated proof worker per browser solver instance.
// ABOUTME: Bounds worker lifetime and sends only numeric progress to the rendered Blazor flow.

export function createSolver() {
    let active = null;
    return {
        solve(challenge, progress) {
            if (active) return Promise.reject(new Error("challenge-busy"));
            return new Promise((resolve, reject) => {
                let worker;
                try {
                    worker = new Worker(new URL("./anonymous-registration-challenge.js", import.meta.url), { type: "module" });
                } catch {
                    reject(new Error("challenge-unsupported"));
                    return;
                }
                const finish = (nonce, code) => {
                    if (active?.worker !== worker) return;
                    clearTimeout(active.timer);
                    worker.terminate();
                    active = null;
                    if (code) reject(new Error(code));
                    else resolve(nonce);
                };
                active = { worker, finish, timer: setTimeout(() => finish(null, "challenge-time-limit"), 90000) };
                worker.onmessage = ({ data }) => {
                    if (data?.type === "progress" && Number.isInteger(data.attempts) && data.attempts >= 0) {
                        progress.invokeMethodAsync("ReportProgress", data.attempts)
                            .catch(() => finish(null, "challenge-disconnected"));
                    } else if (data?.type === "solved" && /^[0-9a-f]{16}$/.test(data.nonce)) {
                        finish(data.nonce, null);
                    } else {
                        finish(null, "challenge-unavailable");
                    }
                };
                worker.onerror = (event) => {
                    event.preventDefault();
                    finish(null, "challenge-unavailable");
                };
                worker.postMessage(challenge);
            });
        },
        cancel() {
            active?.finish(null, "challenge-cancelled");
        }
    };
}
