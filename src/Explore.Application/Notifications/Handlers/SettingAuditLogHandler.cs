using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Notifications.Handlers;

public class SettingAuditLogHandler : INotificationHandler<SettingChangedNotification>
{
    private readonly ILogger<SettingAuditLogHandler> _logger;

    public SettingAuditLogHandler(ILogger<SettingAuditLogHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Setting changed at {Scope} scope",
            Enum.IsDefined(notification.Scope) ? notification.Scope.ToString() : "unknown");

        return Task.CompletedTask;
    }
}
