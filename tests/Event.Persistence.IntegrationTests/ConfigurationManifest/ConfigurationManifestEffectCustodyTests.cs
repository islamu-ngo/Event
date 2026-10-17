using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Persistence.Repositories;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

public sealed class ConfigurationManifestEffectCustodyTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("none")]
    [Arguments("id")]
    [Arguments("aggregate")]
    [Arguments("route")]
    [Arguments("payload")]
    [Arguments("lease")]
    [Arguments("timestamp")]
    [Arguments("status")]
    [Arguments("unbound")]
    [Arguments("terminal")]
    public async Task EffectsRequireTheCommittedOperationAndOriginalPersistedMessage(string change)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var observation = new Observation();
        ConfigurationManifestOperation operation = ConfigurationManifestOperation.Create(
            ConfigurationManifestAuditMode.Bootstrap,
            "configuration.islamu.org/v1alpha2", "ConfigurationManifest", "custody",
            new string('a', 64), ConfigurationManifestOperationStatus.Applied,
            0, 0, 0, 0, null, null, Now, Now,
            instanceSectionDigest: new string('b', 64), bootstrapGeneration: 1,
            instanceChangedSettingKeyNames: ["instance.display_name"]);
        OutboxMessage message = ConfigurationManifestEffectOutbox.Create(
            Guid.CreateVersion7(), operation.Id, Now);
        message.Status = change == "terminal" ? OutboxMessageStatus.DeadLettered : OutboxMessageStatus.Processing;
        message.NextRetryAt = Now.AddMinutes(5);
        if (change != "unbound")
            operation.BindEffectOutbox(message.Id);
        fixture.Context.ConfigurationManifestOperations.Add(operation);
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
            CreatedAt = message.CreatedAt,
            Status = message.Status,
            NextRetryAt = message.NextRetryAt
        };
        switch (change)
        {
            case "id": submitted.Id = Guid.CreateVersion7(); break;
            case "aggregate": submitted.AggregateId = Guid.CreateVersion7(); break;
            case "route": submitted.EventType = "Unrelated"; break;
            case "payload": submitted.Payload = "{}"; break;
            case "lease": submitted.NextRetryAt = Now.AddMinutes(6); break;
            case "timestamp": submitted.CreatedAt = Now.AddSeconds(1); break;
            case "status": submitted.Status = OutboxMessageStatus.Pending; break;
        }
        var dispatcher = new ConfigurationManifestEffectDispatcher(
            new ConfigurationManifestOperationRepository(fixture.Context),
            new OutboxRepository(fixture.Context),
            Substitute.For<IHierarchicalSettingsResolver>(),
            Substitute.For<ITypedSettingsDocumentResolver>(), [observation]);
        if (change is "none" or "terminal")
        {
            await dispatcher.DispatchAsync(submitted, default);
            await Assert.That(observation.Notifications.Count).IsEqualTo(1);
            await Assert.That(observation.Notifications[0].ChangedAt).IsEqualTo(operation.StartedAt);
            await Assert.That(observation.Notifications[0].TenantId).IsNull();
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(submitted, default));
            await Assert.That(observation.Notifications).IsEmpty();
        }
    }

    private sealed class Observation : INotificationHandler<SettingChangedNotification>
    {
        public List<SettingChangedNotification> Notifications { get; } = [];
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
