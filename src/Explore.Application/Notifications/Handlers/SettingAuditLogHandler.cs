using Explore.Domain.Settings;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Notifications.Handlers;

public class SettingAuditLogHandler : INotificationHandler<SettingChangedNotification>
{
    private readonly ILogger<SettingAuditLogHandler> _logger;

    public SettingAuditLogHandler(ILogger<SettingAuditLogHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SettingChangedNotification notification, CancellationToken cancellationToken)
    {
        bool isSensitive = notification.IsSensitive
            || SettingRegistry.Get(notification.Key)?.IsSensitive == true;
        string? oldValue = isSensitive && notification.OldValue is not null
            ? SettingChangedNotification.RedactedValue
            : notification.OldValue;
        string? newValue = isSensitive && notification.NewValue is not null
            ? SettingChangedNotification.RedactedValue
            : notification.NewValue;

        _logger.LogInformation(
            "Setting changed: {SettingKey} Scope={Scope} TenantId={TenantId} Actor={ActorUserId} OldValue={OldValue} NewValue={NewValue}",
            notification.Key,
            notification.Scope,
            notification.TenantId,
            notification.ActorUserId,
            oldValue,
            newValue);

        return Task.CompletedTask;
    }
}
