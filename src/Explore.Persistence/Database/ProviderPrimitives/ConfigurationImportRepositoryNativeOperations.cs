using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal sealed class ConfigurationImportRepositoryNativeOperations(ExploreDbContext context)
{
    internal static ConfigurationImportRepositoryNativeOperations? For(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new(context) : null;

    internal Task<LegalDocument?> ReadLegalForUpdateAsync(
        LegalDocumentScope scope, Guid? tenantId, LegalDocumentKind kind, CancellationToken token) =>
        PostgresConfigurationImportLegalMutationContract.ReadForUpdateAsync(context, scope, tenantId, kind, token);

    internal Task WriteLegalAsync(LegalDocument document, CancellationToken token) =>
        PostgresConfigurationImportLegalMutationContract.WriteAsync(context, document, token);

    internal Task StoreEffectAsync(OutboxMessage message, CancellationToken token) =>
        PostgresConfigurationImportReceiptContract.StoreEffectAsync(context, message, token);

    internal Task<OutboxMessage?> ReadEffectAsync(Guid messageId, CancellationToken token) =>
        PostgresConfigurationImportReceiptContract.ReadEffectAsync(context, messageId, token);

    internal Task<PaidEventPolicyVersion?> ReadActivePolicyAsync(Guid? tenantId, CancellationToken token) =>
        context.Database.CurrentTransaction is null
            ? PostgresConfigurationImportSnapshotContract.ReadActivePolicyAsync(context, tenantId, token)
            : PostgresConfigurationImportPaidPolicyContract.ReadActiveAsync(context, tenantId, token);

    internal Task<PaidEventPolicyVersion[]> ReadActiveTenantPolicyPageAsync(
        int offset, int limit, CancellationToken token) =>
        PostgresConfigurationImportPaidPolicyContract.ReadActiveTenantPageAsync(context, offset, limit, token);

    internal Task<TenantSettingsDocument> WriteBrandingAsync(
        Guid tenantId, int schemaVersion, string defaultsVersion, string payloadJson,
        Guid actorUserId, DateTime occurredAt, CancellationToken token) =>
        PostgresConfigurationImportBrandingContract.WriteAsync(
            context, tenantId, schemaVersion, defaultsVersion, payloadJson, actorUserId, occurredAt, token);

    internal Task<IReadOnlyList<TenantSettingsDocument>> ReadTenantDocumentsAsync(
        Guid tenantId, IReadOnlyCollection<string> keys, CancellationToken token) =>
        PostgresConfigurationImportSnapshotContract.ReadTenantDocumentsAsync(context, tenantId, keys, token);

    internal Task<List<TenantSetting>> ReadTenantSettingsAsync(
        Guid tenantId, IReadOnlyCollection<string> keys, CancellationToken token) =>
        PostgresConfigurationImportSnapshotContract.ReadTenantSettingsAsync(context, tenantId, keys, token);

    internal Task UpsertTenantSettingsAsync(
        Guid tenantId, IReadOnlyCollection<TenantSettingOverrideUpsert> overrides,
        Guid actorId, CancellationToken token) =>
        PostgresConfigurationImportTenantSettingContract.UpsertAsync(context, tenantId, overrides, actorId, token);

    internal async Task<string?> UpsertSystemSettingAsync(SystemSetting setting, CancellationToken token)
    {
        string? previous = await PostgresConfigurationImportScalarSettingContract.UpsertAsync(context, setting, token);
        await ManagedTenantProvisioningPolicyProjection.RecordAsync(context, setting.SettingKey, setting, token);
        return previous;
    }

    internal Task<IReadOnlyList<Tenant>> ReadTenantsBySlugsAsync(string[] slugs, CancellationToken token) =>
        PostgresConfigurationImportSnapshotContract.ReadTenantsBySlugsAsync(context, slugs, token);

    internal Task<IReadOnlyList<Tenant>> ReadActiveTenantsAsync(int maximumCount, CancellationToken token) =>
        PostgresConfigurationImportSnapshotContract.ReadActiveTenantsAsync(context, maximumCount, token);

    internal Task<bool> WriteTenantIdentityAsync(
        Guid tenantId, string displayName, Guid actorUserId, DateTime occurredAt, CancellationToken token) =>
        PostgresConfigurationImportTenantIdentityContract.WriteAsync(
            context, tenantId, displayName, actorUserId, occurredAt, token);
}
