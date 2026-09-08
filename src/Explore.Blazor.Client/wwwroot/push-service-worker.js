const displayedNotificationLimit = 3;
const defaultOpenPath = '/notifications';
const defaultTag = 'islamu-notification';
const summaryTag = 'islamu-notification-summary';
const messageType = 'islamu-notification-refresh';

self.addEventListener('push', event => {
    event.waitUntil(handlePush(event));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    event.waitUntil(openOrFocus(event.notification.data?.openPath));
});

async function handlePush(event) {
    const payload = readPayload(event);
    const openPath = safePath(payload.openPath, defaultOpenPath);
    const tag = safeTag(payload.tag, defaultTag);
    const windows = await clients.matchAll({ type: 'window', includeUncontrolled: true });

    await Promise.all(windows.map(client => client.postMessage({ type: messageType })));
    if (windows.some(client => client.visibilityState === 'visible')) {
        return;
    }

    const displayed = (await self.registration.getNotifications())
        .filter(notification => notification.data?.source === 'islamu-event');

    if (displayed.length >= displayedNotificationLimit) {
        displayed.forEach(notification => notification.close());
        await self.registration.showNotification('New updates', {
            body: 'You have new notifications waiting.',
            tag: summaryTag,
            renotify: false,
            data: { source: 'islamu-event', openPath }
        });
        return;
    }

    await self.registration.showNotification('New notification', {
        body: 'Open ISLAMU Event to view it.',
        tag,
        renotify: false,
        data: { source: 'islamu-event', openPath }
    });
}

async function openOrFocus(candidatePath) {
    const openPath = safePath(candidatePath, defaultOpenPath);
    const targetUrl = new URL(openPath, self.location.origin).href;
    const windows = await clients.matchAll({ type: 'window', includeUncontrolled: true });
    const existing = windows.find(client => new URL(client.url).origin === self.location.origin);

    if (existing !== undefined) {
        await existing.navigate(targetUrl);
        return existing.focus();
    }

    return clients.openWindow(targetUrl);
}

function readPayload(event) {
    if (!event.data) {
        return {};
    }

    try {
        const value = event.data.json();
        return value !== null && typeof value === 'object' ? value : {};
    } catch {
        return {};
    }
}

function safePath(value, fallback) {
    return typeof value === 'string'
        && value.startsWith('/')
        && !value.startsWith('//')
        && !value.includes('://')
        ? value
        : fallback;
}

function safeTag(value, fallback) {
    return typeof value === 'string' && /^[a-z0-9_-]{1,64}$/u.test(value)
        ? value
        : fallback;
}
