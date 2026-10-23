namespace Explore.Application.Features.ConfigurationManifest.Application;

using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Documents;
using Explore.Application.Contracts.Operations;

public static class ConfigurationManifestEffectOutbox
{
    public const string AggregateType = "ConfigurationManifestOperation";
    public const string EventType = "ConfigurationManifestEffectsRequested";
    public const int MaxRetries = 5;

    public static OutboxMessage Create(
        ConfigurationManifestOperation operation,
        IReadOnlyCollection<ConfigurationManifestTenantResult> results)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Guid messageId = operation.EffectOutboxId ?? Guid.Empty;
        Guid operationId = operation.Id;
        if (messageId == Guid.Empty || messageId.Version != 7
            || operationId == Guid.Empty || operationId.Version != 7)
        {
            throw new ArgumentException("Manifest effect identities must be UUIDv7.");
        }

        if (operation.CompletedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Manifest effect timestamp must use UTC kind.", nameof(operation));
        }

        return new OutboxMessage
        {
            Id = messageId,
            AggregateType = AggregateType,
            AggregateId = operationId,
            EventType = EventType,
            Payload = ConfigurationManifestEffectPayload.Create(operation, results),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = operation.CompletedAt,
            MaxRetries = MaxRetries
        };
    }
}

public interface IConfigurationManifestEffectDispatcher
{
    Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public sealed class ConfigurationManifestEffectDispatcher(
    IConfigurationManifestEffectOutboxRepository outboxRepository,
    IHierarchicalSettingsReader settingsResolver,
    ITypedSettingsDocumentResolver typedSettingsDocumentResolver,
    IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : IConfigurationManifestEffectDispatcher
{
    public async Task DispatchAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Id == Guid.Empty || message.AggregateId == Guid.Empty
            || message.AggregateType != ConfigurationManifestEffectOutbox.AggregateType
            || message.EventType != ConfigurationManifestEffectOutbox.EventType
            || message.Status is not OutboxMessageStatus.Processing and not OutboxMessageStatus.DeadLettered
            || message.NextRetryAt is null)
            throw new InvalidOperationException("Manifest effect envelope is invalid.");
        OutboxMessage persisted = await outboxRepository.GetByIdAsync(message.Id, cancellationToken)
            ?? throw new InvalidOperationException("Manifest effect message was not found.");
        if (persisted.AggregateId != message.AggregateId
            || persisted.AggregateType != message.AggregateType
            || persisted.EventType != message.EventType
            || !string.Equals(persisted.Payload, message.Payload, StringComparison.Ordinal)
            || persisted.Status != message.Status || persisted.NextRetryAt != message.NextRetryAt
            || persisted.CreatedAt != message.CreatedAt)
            throw new InvalidOperationException("Manifest effect custody was lost.");
        var payload = ConfigurationManifestEffectPayload.Read(persisted.Payload, persisted.CreatedAt);
        var failures = new List<Exception>();
        if (!payload.InstanceSettingKeys.IsEmpty)
        {
            TryEffect(
                () => settingsResolver.InvalidateCache(SettingScope.Instance),
                failures);
        }

        foreach (var tenant in payload.Tenants)
        {
            TryEffect(
                () => settingsResolver.InvalidateCache(SettingScope.Tenant, tenant.TenantId),
                failures);
            TryEffect(
                () => typedSettingsDocumentResolver.InvalidateTenantDocumentCache(
                    tenant.TenantId,
                    SettingsDocumentKeys.Tenant.Branding),
                failures);
        }

        ImmutableArray<SettingChangedNotification> notifications =
        [
            .. payload.InstanceSettingKeys.Select(key =>
                new SettingChangedNotification(
                    key,
                    oldValue: null,
                    newValue: null,
                    SettingSource.SystemDefault,
                    tenantId: null,
                    actorUserId: null,
                    payload.NotificationTimeUtc)),
            .. payload.Tenants.SelectMany(tenant => tenant.SettingKeys.Select(key =>
                new SettingChangedNotification(
                    key,
                    oldValue: null,
                    newValue: null,
                    SettingSource.TenantOverride,
                    tenant.TenantId,
                    actorUserId: null,
                    payload.NotificationTimeUtc)))
        ];
        foreach (SettingChangedNotification notification in notifications)
        {
            try
            {
                await notificationHandlers.HandleAsync(notification, cancellationToken);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count == 0)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested
            && failures.Any(exception => exception is OperationCanceledException))
        {
            throw new OperationCanceledException(cancellationToken);
        }

        throw new AggregateException("One or more configuration-manifest post-commit effects failed.", failures);
    }

    private static void TryEffect(Action effect, ICollection<Exception> failures)
    {
        try
        {
            effect();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }
}
