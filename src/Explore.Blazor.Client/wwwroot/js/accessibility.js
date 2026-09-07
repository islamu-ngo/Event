let _savedFocusElement = null;

/**
 * Sets focus on the first element matching the CSS selector.
 * Uses requestAnimationFrame to ensure the DOM has settled after Blazor render.
 * @param {string} selector - CSS selector for the target element.
 * @param {boolean} preventScroll - If true, prevents scrolling to the focused element.
 */
export function setFocus(selector, preventScroll) {
    requestAnimationFrame(() => {
        const el = document.querySelector(selector);
        if (el) {
            if (el.tabIndex < 0 && !el.hasAttribute('tabindex')) {
                el.setAttribute('tabindex', '-1');
            }
            el.focus({ preventScroll: !!preventScroll });
        }
    });
}

/**
 * Sets focus on a specific element by its ID.
 * @param {string} elementId - The DOM element ID.
 * @param {boolean} preventScroll - If true, prevents scrolling to the focused element.
 */
export function setFocusById(elementId, preventScroll) {
    requestAnimationFrame(() => {
        const el = document.getElementById(elementId);
        if (el) {
            if (el.tabIndex < 0 && !el.hasAttribute('tabindex')) {
                el.setAttribute('tabindex', '-1');
            }
            el.focus({ preventScroll: !!preventScroll });
        }
    });
}

/**
 * Announces a message to screen readers via an ARIA live region.
 * Creates or reuses a live region container. Clears previous text first
 * so the same message can be announced consecutively.
 * @param {string} message - The text to announce.
 * @param {"polite"|"assertive"} politeness - ARIA live politeness level.
 */
export function announce(message, politeness) {
    const regionId = politeness === 'assertive'
        ? 'aria-live-assertive'
        : 'aria-live-polite';

    let region = document.getElementById(regionId);
    if (!region) {
        return;
    }

    // Clear then set — ensures repeated identical messages are re-announced.
    region.textContent = '';
    requestAnimationFrame(() => {
        region.textContent = message;
    });
}

/**
 * Saves a reference to the currently focused element for later restoration.
 * Used before opening modals/dialogs to return focus when they close.
 */
export function saveActiveElement() {
    _savedFocusElement = document.activeElement;
}

/**
 * Restores focus to the previously saved element, or falls back to a selector chain.
 * Fallback chain: saved element → selector → main content → body.
 * @param {string|null} fallbackSelector - Optional CSS selector as first fallback.
 */
export function restoreFocus(fallbackSelector) {
    requestAnimationFrame(() => {
        if (_savedFocusElement && document.contains(_savedFocusElement)) {
            _savedFocusElement.focus();
            _savedFocusElement = null;
            return;
        }

        _savedFocusElement = null;

        if (fallbackSelector) {
            const fallback = document.querySelector(fallbackSelector);
            if (fallback) {
                fallback.focus();
                return;
            }
        }

        const main = document.getElementById('main-content');
        if (main) {
            main.focus();
            return;
        }

        document.body.focus();
    });
}

/**
 * Focuses the first h1 on the page after a navigation event.
 * Uses a two-frame delay to ensure Blazor has finished rendering the new page.
 * If no h1 is found, falls back to the main content landmark.
 */
export function focusOnNavigate() {
    // Double-rAF: first frame Blazor commits DOM, second frame we focus.
    requestAnimationFrame(() => {
        requestAnimationFrame(() => {
            const h1 = document.querySelector('h1');
            if (h1) {
                if (h1.tabIndex < 0 && !h1.hasAttribute('tabindex')) {
                    h1.setAttribute('tabindex', '-1');
                }
                h1.focus({ preventScroll: true });
                return;
            }

            const main = document.getElementById('main-content');
            if (main) {
                main.focus({ preventScroll: true });
            }
        });
    });
}

/**
 * Returns the user's motion preference.
 * @returns {"reduce"|"no-preference"} The prefers-reduced-motion value.
 */
export function getPreferredMotion() {
    if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        return 'reduce';
    }
    return 'no-preference';
}
