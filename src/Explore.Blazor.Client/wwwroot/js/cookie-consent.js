export function readConsent(cookieKey) {
    if (!cookieKey) return null;
    const match = document.cookie.match(new RegExp("(?:^|;\\s*)" + escapeRegex(cookieKey) + "=([^;]*)"));
    return match ? decodeURIComponent(match[1]) : null;
}

export function writeConsent(cookieKey, value, lifetimeDays) {
    if (!cookieKey || !value) return;
    const maxAge = (lifetimeDays || 180) * 24 * 60 * 60;
    document.cookie = `${cookieKey}=${encodeURIComponent(value)};path=/;max-age=${maxAge};SameSite=Lax;Secure`;
}

export function clearConsent(cookieKey) {
    if (!cookieKey) return;
    document.cookie = `${cookieKey}=;path=/;max-age=0;SameSite=Lax;Secure`;
}

function escapeRegex(str) {
    return str.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
