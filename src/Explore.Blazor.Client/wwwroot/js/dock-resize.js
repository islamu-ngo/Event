export function setPointerCapture(element, pointerId) {
    if (!element || typeof element.setPointerCapture !== 'function') {
        return;
    }

    element.setPointerCapture(pointerId);
}

export function releasePointerCapture(element, pointerId) {
    if (!element || typeof element.releasePointerCapture !== 'function') {
        return;
    }

    if (typeof element.hasPointerCapture === 'function' && !element.hasPointerCapture(pointerId)) {
        return;
    }

    element.releasePointerCapture(pointerId);
}
