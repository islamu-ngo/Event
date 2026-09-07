namespace Explore.Application.Features.ConfigurationManifest.Importing;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Validation;
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
    ITenantRepository tenants) : IConfigurationImportEffectDelivery
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
        ConfigurationImportOperation operation =
            await operations.GetByIdForEffectAsync(
                message.AggregateId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Configuration import operation was not found.");
        await InvalidateAsync(operation, cancellationToken);
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
            await DispatchClaimedAsync(message, cancellationToken);
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
