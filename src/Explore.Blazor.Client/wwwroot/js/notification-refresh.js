let notificationRefreshSource = null;
let serviceWorkerMessageHandler = null;
let notificationRefreshSession = null;

export function startNotificationRefresh(url, dotNetRef) {
    stopNotificationRefresh();

    const session = { stopped: false, busy: false, timer: null };
    notificationRefreshSession = session;

    async function invoke(method, ...args) {
        if (session.stopped || session.busy) {
            return;
        }

        session.busy = true;
        try {
            await dotNetRef.invokeMethodAsync(method, ...args);
        } catch (error) {
            console.warn('Notification refresh callback failed', error);
        } finally {
            session.busy = false;
        }
    }

    function schedulePoll() {
        session.timer = setTimeout(async () => {
            try {
                await invoke('HandleNotificationPoll');
            } finally {
                if (!session.stopped) {
                    schedulePoll();
                }
            }
        }, 60_000);
    }

    // Polling enters the current circuit through JS interop, even when SSE cannot connect.
    schedulePoll();

    try {
        notificationRefreshSource = new EventSource(url, { withCredentials: true });

        notificationRefreshSource.addEventListener('notification-refresh', event => {
            if (!event.data) {
                return;
            }

            const hint = JSON.parse(event.data);
            void invoke(
                'HandleNotificationRefresh',
                hint.unreadCount ?? hint.UnreadCount ?? 0,
                hint.hasUnread ?? hint.HasUnread ?? false,
                hint.reason ?? hint.Reason ?? 'refresh',
                hint.generatedAt ?? hint.GeneratedAt ?? null);
        });

        notificationRefreshSource.onerror = () => {
            void invoke('HandleNotificationRefreshError');
        };
    } catch (error) {
        console.warn('Notification refresh SSE startup failed', error);
        void invoke('HandleNotificationRefreshError');
    }

    if ('serviceWorker' in navigator) {
        serviceWorkerMessageHandler = event => {
            if (event.data?.type === 'islamu-notification-refresh') {
                void invoke('HandleWebPushRefresh');
            }
        };
        navigator.serviceWorker.addEventListener('message', serviceWorkerMessageHandler);
    }
}

export function stopNotificationRefresh() {
    if (notificationRefreshSession !== null) {
        notificationRefreshSession.stopped = true;
        clearTimeout(notificationRefreshSession.timer);
        notificationRefreshSession = null;
    }

    if (notificationRefreshSource !== null) {
        notificationRefreshSource.close();
        notificationRefreshSource = null;
    }

    if (serviceWorkerMessageHandler !== null && 'serviceWorker' in navigator) {
        navigator.serviceWorker.removeEventListener('message', serviceWorkerMessageHandler);
        serviceWorkerMessageHandler = null;
    }
}
