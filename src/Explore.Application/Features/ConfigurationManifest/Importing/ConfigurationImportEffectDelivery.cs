namespace Explore.Application.Features.ConfigurationManifest.Importing;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Documents;

public interface IConfigurationImportEffectDelivery
{
    Task DrainPendingAsync(CancellationToken cancellationToken);
    Task DeliverAsync(Guid messageId, CancellationToken cancellationToken);
    Task DispatchClaimedAsync(
        OutboxMessage message,
        CancellationToken cancellationToken);
}

public sealed class ConfigurationImportEffectDelivery(
    IConfigurationImportEffectOutboxRepository outbox,
    IConfigurationImportOperationRepository operations,
    IHierarchicalSettingsResolver settings,
    ITypedSettingsDocumentResolver documents,
    ITenantRepository tenants,
    IEnumerable<INotificationHandler<SettingChangedNotification>>
        notificationHandlers) : IConfigurationImportEffectDelivery
{
    public async Task DrainPendingAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<OutboxMessage> pending =
            await outbox.GetPendingImportEffectsAsync(
                batchSize: 1_000,
                cancellationToken);
        foreach (OutboxMessage message in pending)
            await DeliverAsync(message, cancellationToken);
    }

    public async Task DeliverAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        OutboxMessage message = await outbox.GetByIdAsync(
                messageId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Configuration import effect message was not found.");
        await DeliverAsync(message, cancellationToken);
    }

    public async Task DispatchClaimedAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Id == Guid.Empty
            || message.AggregateId == Guid.Empty
            || message.Status is not OutboxMessageStatus.Processing and not OutboxMessageStatus.DeadLettered
            || message.NextRetryAt is null
            || !string.Equals(message.AggregateType, ConfigurationImportEffectOutbox.AggregateType, StringComparison.Ordinal)
            || !string.Equals(message.EventType, ConfigurationImportEffectOutbox.EventType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Configuration import effect envelope is invalid.");
        }
        OutboxMessage persisted = await outbox.GetByIdAsync(message.Id, cancellationToken)
            ?? throw new InvalidOperationException("Configuration import effect message was not found.");
        if (persisted.AggregateId != message.AggregateId
            || persisted.AggregateType != message.AggregateType || persisted.EventType != message.EventType
            || persisted.Payload != message.Payload || persisted.CreatedAt != message.CreatedAt
            || persisted.Status != message.Status || persisted.NextRetryAt != message.NextRetryAt)
            throw new InvalidOperationException("Configuration import effect custody was lost.");
        ConfigurationImportOperation operation =
            await operations.GetByIdForEffectAsync(
                message.AggregateId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Configuration import operation was not found.");
        if (operation.Id != message.AggregateId
            || operation.EffectOutboxId != message.Id
            || operation.CompletedAt != message.CreatedAt
            || operation.EffectPayloadDigest != Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(message.Payload ?? string.Empty)))
            || operation.Status is not ConfigurationImportOperationStatus.Applied
                and not ConfigurationImportOperationStatus.RolledBack)
        {
            throw new InvalidOperationException("Configuration import effects require their bound committed operation.");
        }
        var notifications = ConfigurationImportEffectOutbox.ReadNotifications(persisted.Payload);
        foreach (SettingChangedNotification notification in notifications)
        {
            if (notification.TenantId == Guid.Empty
                || notification.TenantId is not null && operation.TargetTenantId is { } tenantId
                    && notification.TenantId != tenantId
                || !operation.SelectedSectionKeys.Contains(
                    notification.TenantId is null ? "instance.settings" : "tenant.settings",
                    StringComparer.Ordinal))
                throw new InvalidOperationException("Configuration import notification scope is invalid.");
        }
        await InvalidateAsync(operation, cancellationToken);
        foreach (SettingChangedNotification notification in notifications)
        {
            await notificationHandlers.HandleAsync(
                notification,
                cancellationToken);
        }
    }

    private async Task DeliverAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Status == OutboxMessageStatus.Completed)
            return;
        DateTime? lease = await outbox.TryClaimForProcessing(
            message.Id,
            DateTime.UtcNow,
            cancellationToken);
        if (!lease.HasValue)
            return;
        try
        {
            OutboxMessage claimed = await outbox.GetByIdAsync(message.Id, cancellationToken)
                ?? throw new InvalidOperationException("Configuration import effect claim was not found.");
            if (claimed.Status != OutboxMessageStatus.Processing || claimed.NextRetryAt != lease.Value)
                throw new InvalidOperationException("Configuration import effect claim was replaced.");
            await DispatchClaimedAsync(claimed, cancellationToken);
            if (!await outbox.MarkAsCompleted(
                    message.Id,
                    lease.Value,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    "Configuration import effect lease was lost.");
            }
        }
        catch (Exception exception)
        {
            CancellationToken persistenceToken =
                cancellationToken.IsCancellationRequested
                    ? CancellationToken.None
                    : cancellationToken;
            await outbox.MarkAsFailed(
                message.Id,
                lease.Value,
                exception.GetType().Name,
                isRetryable: true,
                retryDelaySeconds: 5,
                DateTime.UtcNow,
                persistenceToken);
            throw;
        }
    }

    private async Task InvalidateAsync(
        ConfigurationImportOperation operation,
        CancellationToken cancellationToken)
    {
        bool tenantSettingsChanged = operation.SelectedSectionKeys.Contains(
            "tenant.settings",
            StringComparer.Ordinal);
        bool tenantDocumentsChanged = operation.SelectedSectionKeys.Contains(
            "tenant.documents",
            StringComparer.Ordinal);
        if (operation.SelectedSectionKeys.Contains(
                "instance.settings",
                StringComparer.Ordinal))
        {
            settings.InvalidateCache(SettingScope.Instance);
        }

        if (operation.TargetTenantId is { } tenantId)
        {
            if (tenantSettingsChanged)
                settings.InvalidateCache(SettingScope.Tenant, tenantId);
            if (tenantDocumentsChanged)
            {
                documents.InvalidateTenantDocumentCache(
                    tenantId,
                    SettingsDocumentKeys.Tenant.Branding);
            }
            return;
        }

        if (!tenantSettingsChanged && !tenantDocumentsChanged)
            return;
        IReadOnlyList<Tenant> active =
            await tenants.GetAllActiveForConfigurationManifestExportAsync(
                ConfigurationManifestValidator.MaximumTenantCount,
                cancellationToken);
        foreach (Tenant tenant in active)
        {
            if (tenantSettingsChanged)
                settings.InvalidateCache(SettingScope.Tenant, tenant.Id);
            if (tenantDocumentsChanged)
            {
                documents.InvalidateTenantDocumentCache(
                    tenant.Id,
                    SettingsDocumentKeys.Tenant.Branding);
            }
        }
    }
}
