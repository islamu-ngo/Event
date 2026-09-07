const listeners = new WeakMap();
const navigationKeys = new Set(["ArrowDown", "ArrowUp", "Home", "End"]);

export function bindComboboxNavigation(input) {
    unbindComboboxNavigation(input);

    const listener = event => {
        if (event.isComposing) {
            return;
        }

        const hasSuggestions =
            input.dataset.hasSuggestions === "true";
        const hasActiveOption =
            input.getAttribute("aria-expanded") === "true"
            && input.hasAttribute("aria-activedescendant");

        if ((hasSuggestions && navigationKeys.has(event.key))
            || (hasActiveOption && event.key === "Enter")) {
            event.preventDefault();
        }
    };

    input.addEventListener("keydown", listener);
    listeners.set(input, listener);
}

export function scrollActiveOptionIntoView(input) {
    requestAnimationFrame(() => {
        const activeId = input.getAttribute("aria-activedescendant");
        if (!activeId) {
            return;
        }

        document.getElementById(activeId)?.scrollIntoView({
            block: "nearest",
            inline: "nearest"
        });
    });
}

export function ensureContainingDialogModal(input) {
    input.closest("[role='dialog']")?.setAttribute("aria-modal", "true");
}

export function unbindComboboxNavigation(input) {
    const listener = listeners.get(input);
    if (!listener) {
        return;
    }

    input.removeEventListener("keydown", listener);
    listeners.delete(input);
}
