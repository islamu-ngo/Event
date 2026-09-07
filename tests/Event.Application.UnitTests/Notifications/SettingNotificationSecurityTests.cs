using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Notifications;
using Explore.Application.Notifications.Handlers;
using Explore.Domain.Constants;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Application.UnitTests.Notifications;

public sealed class SettingNotificationSecurityTests
{
    [Test]
    public async Task SensitiveSettingNotification_RedactsValuesAndCarriesSensitivityMetadata()
    {
        string canary = $"canary-{Guid.NewGuid():N}";
        Guid actorUserId = Guid.NewGuid();

        var notification = new SettingChangedNotification(
            InfrastructureSecretSettingKeys.Reporting.OspreyApiKey,
            canary,
            canary,
            SettingSource.SystemDefault,
            null,
            actorUserId,
            DateTime.UtcNow);

        await Assert.That(notification.IsSensitive).IsTrue();
        await Assert.That(notification.OldValue).DoesNotContain(canary);
        await Assert.That(notification.NewValue).DoesNotContain(canary);
    }

    [Test]
    public async Task AuditHandler_DefenseInDepthNeverRendersSensitiveCanary()
    {
        string canary = $"canary-{Guid.NewGuid():N}";
        Guid actorUserId = Guid.NewGuid();
        var logger = Substitute.For<ILogger<SettingAuditLogHandler>>();
        object? capturedState = null;
        logger.When(candidate => candidate.Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()))
            .Do(call => capturedState = call.ArgAt<object>(2));
        var handler = new SettingAuditLogHandler(logger);
        var notification = new SettingChangedNotification(
            InfrastructureSecretSettingKeys.Reporting.OspreyApiKey,
            canary,
            canary,
            SettingSource.SystemDefault,
            actorUserId,
            null,
            DateTime.UtcNow);

        await handler.Handle(notification, CancellationToken.None);

        await Assert.That(capturedState?.ToString() ?? string.Empty).DoesNotContain(canary);
        await Assert.That(capturedState?.ToString() ?? string.Empty).Contains(actorUserId.ToString());
    }
}
