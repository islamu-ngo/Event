const promptHandlers = new WeakMap();

export function attachPromptKeyboardHandler(element, dotNetReference) {
    if (!element) {
        return;
    }

    detachPromptKeyboardHandler(element);

    const handler = event => {
        const hasModifier = event.shiftKey || event.altKey || event.ctrlKey || event.metaKey;
        const canSelectReference = element.dataset.referenceAutocompleteSelectable === 'true';

        if (canSelectReference && !hasModifier && event.key === 'Tab') {
            event.preventDefault();
            dotNetReference.invokeMethodAsync('SelectPromptReferenceFromKeyboardAsync');
            return;
        }

        if (canSelectReference && !hasModifier && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
            event.preventDefault();
            dotNetReference
                .invokeMethodAsync(
                    'MovePromptReferenceSelectionAsync',
                    event.key === 'ArrowDown' ? 1 : -1)
                .then(() => window.requestAnimationFrame(() => scrollActiveReferenceOptionIntoView(element)))
                .catch(() => {});
            return;
        }

        if (!hasModifier
            && (event.key === 'Backspace' || event.key === 'Delete')
            && shouldDeleteReferenceMention(element, event.key)) {
            event.preventDefault();
            dotNetReference
                .invokeMethodAsync(
                    'DeletePromptReferenceFromKeyboard',
                    element.selectionStart ?? 0,
                    element.selectionEnd ?? 0,
                    event.key)
                .then(result => applyReferenceDeletionResult(element, result))
                .catch(() => {});
            return;
        }

        if (event.key !== 'Enter' || hasModifier) {
            return;
        }

        event.preventDefault();
        dotNetReference.invokeMethodAsync('SendPromptFromKeyboardAsync');
    };

    element.addEventListener('keydown', handler);
    promptHandlers.set(element, handler);
}

function shouldDeleteReferenceMention(element, key) {
    const tokens = getReferenceMentionTokens(element);
    if (tokens.length === 0) {
        return false;
    }

    const text = element.value ?? '';
    const textLength = text.length;
    let selectionStart = Math.max(0, Math.min(element.selectionStart ?? 0, textLength));
    let selectionEnd = Math.max(0, Math.min(element.selectionEnd ?? selectionStart, textLength));

    if (selectionStart > selectionEnd) {
        [selectionStart, selectionEnd] = [selectionEnd, selectionStart];
    }

    if (selectionStart === selectionEnd) {
        if (key === 'Backspace') {
            if (selectionStart === 0) {
                return false;
            }

            selectionStart -= 1;
        } else if (key === 'Delete') {
            if (selectionStart >= textLength) {
                return false;
            }

            selectionEnd += 1;
        }
    }

    return tokens.some(token => rangeIntersectsToken(text, selectionStart, selectionEnd, token));
}

function getReferenceMentionTokens(element) {
    try {
        const tokens = JSON.parse(element.dataset.referenceMentionTokens || '[]');
        return Array.isArray(tokens)
            ? tokens.filter(token => typeof token === 'string' && token.length > 0)
            : [];
    } catch {
        return [];
    }
}

function rangeIntersectsToken(text, rangeStart, rangeEnd, token) {
    const haystack = text.toLowerCase();
    const needle = token.toLowerCase();
    let tokenStart = haystack.indexOf(needle);

    while (tokenStart >= 0) {
        const tokenEnd = tokenStart + needle.length;
        if (rangeStart < tokenEnd && rangeEnd > tokenStart) {
            return true;
        }

        tokenStart = haystack.indexOf(needle, tokenEnd);
    }

    return false;
}

function applyReferenceDeletionResult(element, result) {
    const handled = result?.handled ?? result?.Handled ?? false;
    if (!handled) {
        return;
    }

    const text = result?.text ?? result?.Text ?? '';
    const selectionStart = result?.selectionStart ?? result?.SelectionStart ?? text.length;
    element.value = text;
    element.setSelectionRange(selectionStart, selectionStart);
}

function scrollActiveReferenceOptionIntoView(element) {
    const listId = element.getAttribute('aria-controls');
    const list = listId ? document.getElementById(listId) : null;
    if (!list) {
        return;
    }

    const activeOption = list.querySelector(
        '.ai-rail__reference-option--active, [aria-selected="true"], [aria-selected="True"]');
    activeOption?.scrollIntoView({ block: 'nearest' });
}

export function focusPrompt(element) {
    if (!element) {
        return;
    }

    element.focus();
}

export function detachPromptKeyboardHandler(element) {
    const handler = promptHandlers.get(element);
    if (!handler) {
        return;
    }

    element.removeEventListener('keydown', handler);
    promptHandlers.delete(element);
}
