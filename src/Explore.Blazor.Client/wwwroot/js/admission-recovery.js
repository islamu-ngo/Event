export function takeCapability() {
    const fragment = new URLSearchParams(window.location.hash.slice(1));
    const capability = fragment.get("capability");
    window.history.replaceState(null, document.title, window.location.pathname);

    if (typeof capability !== "string" || capability.length === 0 || capability.length > 256) {
        return null;
    }

    return capability;
}
