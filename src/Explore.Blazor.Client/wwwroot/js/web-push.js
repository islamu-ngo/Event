const deviceIdentifierKey = 'islamu.web-push.device-id';
const serviceWorkerPath = '/push-service-worker.js';

export async function getWebPushState() {
    if (!isSupported()) {
        return state(false, 'unsupported', false, '');
    }

    const registration = await navigator.serviceWorker.getRegistration();
    const subscription = registration === undefined
        ? null
        : await registration.pushManager.getSubscription();

    return state(true, Notification.permission, subscription !== null, getDeviceIdentifier());
}

export async function subscribeWebPush(applicationServerKey) {
    if (!isSupported() || typeof applicationServerKey !== 'string' || applicationServerKey.length === 0) {
        return null;
    }

    const permission = Notification.permission === 'default'
        ? await Notification.requestPermission()
        : Notification.permission;

    if (permission !== 'granted') {
        return null;
    }

    const registration = await navigator.serviceWorker.register(serviceWorkerPath, {
        scope: '/',
        updateViaCache: 'none'
    });
    await navigator.serviceWorker.ready;

    const existing = await registration.pushManager.getSubscription();
    const subscription = existing ?? await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: fromUrlSafeBase64(applicationServerKey)
    });
    const serialized = subscription.toJSON();

    if (!serialized.endpoint || !serialized.keys?.p256dh || !serialized.keys?.auth) {
        await subscription.unsubscribe();
        return null;
    }

    return {
        deviceIdentifier: getDeviceIdentifier(),
        endpoint: serialized.endpoint,
        p256Dh: serialized.keys.p256dh,
        auth: serialized.keys.auth,
        expirationTime: serialized.expirationTime ?? null
    };
}

export async function unsubscribeWebPush() {
    if (!isSupported()) {
        return true;
    }

    const registration = await navigator.serviceWorker.getRegistration();
    const subscription = registration === undefined
        ? null
        : await registration.pushManager.getSubscription();

    return subscription === null || await subscription.unsubscribe();
}

function isSupported() {
    return window.isSecureContext === true
        && 'Notification' in window
        && 'serviceWorker' in navigator
        && 'PushManager' in window;
}

function state(isSupportedValue, permission, hasSubscription, deviceIdentifier) {
    return {
        isSupported: isSupportedValue,
        permission,
        hasSubscription,
        deviceIdentifier
    };
}

function getDeviceIdentifier() {
    let value = localStorage.getItem(deviceIdentifierKey);
    if (value !== null && value.length >= 16 && value.length <= 100) {
        return value;
    }

    value = crypto.randomUUID();
    localStorage.setItem(deviceIdentifierKey, value);
    return value;
}

function fromUrlSafeBase64(value) {
    const padding = '='.repeat((4 - value.length % 4) % 4);
    const raw = atob((value + padding).replaceAll('-', '+').replaceAll('_', '/'));
    return Uint8Array.from(raw, character => character.charCodeAt(0));
}
