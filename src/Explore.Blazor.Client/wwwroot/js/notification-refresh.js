let notificationRefreshSource = null;
let serviceWorkerMessageHandler = null;

export function startNotificationRefresh(url, dotNetRef) {
    stopNotificationRefresh();

    notificationRefreshSource = new EventSource(url, { withCredentials: true });

    notificationRefreshSource.addEventListener('notification-refresh', event => {
        if (!event.data) {
            return;
        }

        const hint = JSON.parse(event.data);
        dotNetRef.invokeMethodAsync(
            'HandleNotificationRefresh',
            hint.unreadCount ?? hint.UnreadCount ?? 0,
            hint.hasUnread ?? hint.HasUnread ?? false,
            hint.reason ?? hint.Reason ?? 'refresh',
            hint.generatedAt ?? hint.GeneratedAt ?? null);
    });

    notificationRefreshSource.onerror = () => {
        dotNetRef.invokeMethodAsync('HandleNotificationRefreshError');
    };

    if ('serviceWorker' in navigator) {
        serviceWorkerMessageHandler = event => {
            if (event.data?.type === 'islamu-notification-refresh') {
                dotNetRef.invokeMethodAsync('HandleWebPushRefresh');
            }
        };
        navigator.serviceWorker.addEventListener('message', serviceWorkerMessageHandler);
    }
}

export function stopNotificationRefresh() {
    if (notificationRefreshSource !== null) {
        notificationRefreshSource.close();
        notificationRefreshSource = null;
    }

    if (serviceWorkerMessageHandler !== null && 'serviceWorker' in navigator) {
        navigator.serviceWorker.removeEventListener('message', serviceWorkerMessageHandler);
        serviceWorkerMessageHandler = null;
    }
}
