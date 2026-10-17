using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Notifications;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

public sealed class ConfigurationImportEffectAuthorityTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("message")]
    [Arguments("aggregate")]
    [Arguments("event")]
    [Arguments("failed")]
    [Arguments("payload")]
    [Arguments("persisted_payload")]
    [Arguments("lease")]
    [Arguments("timestamp")]
    [Arguments("tenant")]
    public async Task UnboundEffectCannotPublishCommittedSettingNotifications(string mismatch)
    {
        var observation = new NotificationObservation();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            services.AddSingleton<INotificationHandler<SettingChangedNotification>>(observation));
        ConfigurationImportOperation operation = mismatch == "failed"
            ? ConfigurationImportOperation.CreateFailed(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "instance", null, fixture.UserId, null,
                ConfigurationImportDigest.Compute(["artifact"]), 3, ["instance.settings"],
                "validation_failed", "Rejected import", Now, Now)
            : ConfigurationImportOperationTestData.Applied(Guid.CreateVersion7());
        OutboxMessage message = Message(operation, fixture.UserId);
        message.Status = OutboxMessageStatus.Processing;
        message.NextRetryAt = Now.AddMinutes(5);
        if (mismatch != "failed")
            operation.BindEffectPayload(message.Payload);
        fixture.Context.ConfigurationImportOperations.Add(operation);
        fixture.Context.OutboxMessages.Add(message);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var submitted = new OutboxMessage
        {
            Id = message.Id,
            AggregateId = message.AggregateId,
            AggregateType = message.AggregateType,
            EventType = message.EventType,
            Payload = message.Payload,
            Status = message.Status,
            CreatedAt = message.CreatedAt,
            NextRetryAt = message.NextRetryAt
        };
        switch (mismatch)
        {
            case "message": submitted.Id = Guid.CreateVersion7(); break;
            case "aggregate": submitted.AggregateType = nameof(Tenant); break;
            case "event": submitted.EventType = "UnrelatedEffect"; break;
            case "payload": submitted.Payload = "[]"; break;
            case "lease": submitted.NextRetryAt = Now.AddMinutes(6); break;
            case "timestamp": submitted.CreatedAt = message.CreatedAt.AddSeconds(1); break;
            case "persisted_payload":
            case "tenant":
                OutboxMessage stored = (await fixture.Context.OutboxMessages.FindAsync(message.Id))!;
                stored.Payload = mismatch == "tenant"
                    ? ConfigurationImportEffectOutbox.Create(Guid.CreateVersion7(), operation.Id, Now,
                        [new SettingChangedNotification("tenant.display_name", null, "changed",
                            SettingSource.TenantOverride, Guid.CreateVersion7(), fixture.UserId, Now)]).Payload
                    : "[]";
                submitted.Payload = stored.Payload;
                await fixture.Context.SaveChangesAsync();
                break;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Services.GetRequiredService<IConfigurationImportEffectDelivery>()
                .DispatchClaimedAsync(submitted, default));

        await Assert.That(observation.Notifications).IsEmpty();
        await Assert.That((await fixture.Context.ConfigurationImportOperations.FindAsync(operation.Id))!.Status)
            .IsEqualTo(operation.Status);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BoundCommittedEffectPublishesItsPersistedNotification(bool forwardRollback)
    {
        var observation = new NotificationObservation();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            services.AddSingleton<INotificationHandler<SettingChangedNotification>>(observation));
        ConfigurationImportOperation source = ConfigurationImportOperationTestData.Applied(Guid.CreateVersion7());
        ConfigurationImportOperation operation = forwardRollback
            ? ConfigurationImportOperationTestData.Applied(Guid.CreateVersion7(), sourceOperationId: source.Id)
            : source;
        if (forwardRollback)
            fixture.Context.ConfigurationImportOperations.Add(source);
        OutboxMessage message = Message(operation, fixture.UserId);
        operation.BindEffectPayload(message.Payload);
        message.Status = OutboxMessageStatus.Processing;
        message.NextRetryAt = Now.AddMinutes(5);
        fixture.Context.ConfigurationImportOperations.Add(operation);
        fixture.Context.OutboxMessages.Add(message);
        await fixture.Context.SaveChangesAsync();

        await fixture.Services.GetRequiredService<IConfigurationImportEffectDelivery>()
            .DispatchClaimedAsync(message, default);

        await Assert.That(observation.Notifications.Count).IsEqualTo(1);
        await Assert.That(observation.Notifications[0].NewValue).IsEqualTo("changed");
        await Assert.That(source.Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
    }

    private static OutboxMessage Message(ConfigurationImportOperation operation, Guid actor) =>
        ConfigurationImportEffectOutbox.Create(
            operation.EffectOutboxId ?? Guid.CreateVersion7(), operation.Id, operation.CompletedAt!.Value,
            [new SettingChangedNotification("instance.display_name", "old", "changed",
                SettingSource.SystemDefault, null, actor, Now)]);

    private sealed class NotificationObservation : INotificationHandler<SettingChangedNotification>
    {
        internal List<SettingChangedNotification> Notifications { get; } = [];

        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
