export async function share(title, url) {
    if (!navigator.share || typeof url !== 'string' || url.trim().length === 0) {
        return false;
    }

    try {
        await navigator.share({
            title: typeof title === 'string' && title.trim().length > 0 ? title : 'Event',
            url
        });
        return true;
    } catch {
        return false;
    }
}

export async function copyText(text) {
    if (!navigator.clipboard?.writeText || typeof text !== 'string' || text.trim().length === 0) {
        return false;
    }

    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

export function scrollToElementById(elementId) {
    if (typeof elementId !== 'string' || elementId.trim().length === 0) {
        return false;
    }

    const element = document.getElementById(elementId);
    if (!element) {
        return false;
    }

    element.scrollIntoView({ behavior: 'smooth', block: 'start' });
    return true;
}

export function downloadBase64File(base64Content, fileName, contentType) {
    if (typeof base64Content !== 'string' || base64Content.length === 0) {
        return false;
    }

    let objectUrl;

    try {
        const byteCharacters = atob(base64Content);
        const bytes = new Uint8Array(byteCharacters.length);

        for (let index = 0; index < byteCharacters.length; index += 1) {
            bytes[index] = byteCharacters.charCodeAt(index);
        }

        const blob = new Blob([bytes], { type: normalizeContentType(contentType) });
        objectUrl = URL.createObjectURL(blob);

        const link = document.createElement('a');
        link.href = objectUrl;
        link.download = sanitizeFileName(fileName);
        link.rel = 'noopener';
        document.body.appendChild(link);
        link.click();
        link.remove();

        return true;
    } catch {
        return false;
    } finally {
        if (objectUrl) {
            setTimeout(() => URL.revokeObjectURL(objectUrl), 0);
        }
    }
}

export function downloadFileFromUrl(url) {
    if (typeof url !== 'string' || url.trim().length === 0) {
        return false;
    }

    try {
        const downloadUrl = new URL(url, window.location.origin);
        if (downloadUrl.origin !== window.location.origin) {
            return false;
        }

        const link = document.createElement('a');
        link.href = downloadUrl.href;
        link.download = '';
        link.rel = 'noopener';
        document.body.appendChild(link);
        link.click();
        link.remove();
        return true;
    } catch {
        return false;
    }
}

export function openSameOriginNewTab(url) {
    if (typeof url !== 'string' || url.trim().length === 0) {
        return false;
    }

    try {
        const targetUrl = new URL(url, window.location.origin);
        if (targetUrl.origin !== window.location.origin) {
            return false;
        }

        const opened = window.open(targetUrl.href, '_blank', 'noopener,noreferrer');
        if (opened) {
            opened.opener = null;
        }

        return Boolean(opened);
    } catch {
        return false;
    }
}

export function focusOpenerAndClose() {
    try {
        if (!window.opener || window.opener.closed) {
            return false;
        }

        window.opener.focus();
        window.close();
        return true;
    } catch {
        return false;
    }
}


function normalizeContentType(contentType) {
    return typeof contentType === 'string' && contentType.trim().length > 0
        ? contentType
        : 'application/octet-stream';
}

function sanitizeFileName(fileName) {
    if (typeof fileName !== 'string') {
        return 'download';
    }

    const sanitized = fileName
        .replace(/[<>:"/\\|?*\u0000-\u001F]/g, '_')
        .trim();

    return sanitized.length > 0 ? sanitized : 'download';
}
