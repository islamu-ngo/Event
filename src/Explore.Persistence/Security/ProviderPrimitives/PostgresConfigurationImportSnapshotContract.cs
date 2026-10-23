using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed manifest-catalog reads on the sealed Global instance-import owner.
/// Each call addresses at most one tenant; the manifest reader bounds the complete tenant set.
/// No read acquires another lease or starts a transaction.
/// </summary>
public static partial class PostgresConfigurationImportSnapshotContract
{
    public const string OwnerRole = "event_configuration_import_snapshot_owner";
    public const string MigratorRole = "event_configuration_import_snapshot_migrator";
    public const string GlobalRole = "event_configuration_import_snapshot_global";
    private const string PolicyName = "configuration_import_snapshot_read";

    private static readonly string[] SettingKeys = ConfigurationManifestCatalog.TenantSettings.Keys
        .Order(StringComparer.Ordinal).ToArray();
    private static readonly string[] DocumentKeys = ConfigurationManifestCatalog.TenantDocuments.Values
        .Where(entry => entry.Storage == ConfigurationManifestDocumentStorage.TenantSettingsDocument)
        .Select(entry => entry.DocumentKey).Order(StringComparer.Ordinal).ToArray();

    public static Task<List<TenantSetting>> ReadTenantSettingsAsync(
        ExploreDbContext context, Guid tenantId, IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default)
    {
        string[] requested = ValidateKeys(tenantId, keys, SettingKeys);
        return ReadAsync(context, "settings", tenantId, requested, reader => new TenantSetting
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetGuid(1),
            Tenant = null!,
            SettingKey = reader.GetString(2),
            Value = reader.GetString(3),
            IsLocked = reader.GetBoolean(4),
            CreatedAt = reader.GetDateTime(5),
            CreatedBy = NullableGuid(reader, 6),
            UpdatedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            UpdatedBy = NullableGuid(reader, 8)
        }, cancellationToken);
    }

    public static async Task<IReadOnlyList<TenantSettingsDocument>> ReadTenantDocumentsAsync(
        ExploreDbContext context, Guid tenantId, IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default)
    {
        string[] requested = ValidateKeys(tenantId, keys, DocumentKeys);
        return await ReadAsync(context, "documents", tenantId, requested, reader =>
        {
            var document = TenantSettingsDocument.Create(
                reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetString(4), reader.GetString(5));
            document.Id = reader.GetGuid(0);
            document.ConcurrencyStamp = reader.GetGuid(6);
            document.CreatedAt = reader.GetDateTime(7);
            document.CreatedBy = NullableGuid(reader, 8);
            document.UpdatedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9);
            document.UpdatedBy = NullableGuid(reader, 10);
            return document;
        }, cancellationToken);
    }

    public static async Task<PaidEventPolicyVersion?> ReadActivePolicyAsync(
        ExploreDbContext context, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId is { } tenant)
            ArgumentOutOfRangeException.ThrowIfEqual(tenant, Guid.Empty);
        List<PaidEventPolicyVersion> policies = await ReadAsync(context, "policy", tenantId, null, reader =>
        {
            PolicySnapshot snapshot = JsonSerializer.Deserialize<PolicySnapshot>(reader.GetString(0))
                ?? throw new InvalidOperationException("An active policy snapshot is required.");
            if (snapshot.TenantId != tenantId || !snapshot.IsActive || snapshot.ActiveUniquenessSlot != 0
                || snapshot.PolicyScopeKey != (tenantId is { } id ? $"tenant:{id:N}" : "instance"))
                throw new InvalidOperationException("The active policy snapshot has an invalid scope.");
            var policy = PaidEventPolicyVersion.RestoreActiveForPolicyEvaluation(
                snapshot.Id, snapshot.TenantId, snapshot.VersionNumber, snapshot.IsPaymentsEnabled,
                snapshot.AllowedOrganizerKindIds.Select(id => (ActorTypeEnum)id),
                snapshot.RequiresLocalVerification, snapshot.AllowedCurrencyCodes, snapshot.DefaultCurrencyCode,
                snapshot.RefundProtectionIds.Select(id => (PaidEventRefundProtection)id),
                snapshot.CurrencyRiskLimits.Select(limit => PaidEventPolicyCurrencyRiskLimit.Create(
                    limit.CurrencyCode, limit.PerEventSalesCeilingMinor, limit.PerEventSalesCountCeiling,
                    limit.RollingOrganizerSalesCeilingMinor, limit.RollingOrganizerSalesCountCeiling,
                    limit.RollingOrganizerWindowDays, limit.HighValueReviewThresholdMinor)),
                snapshot.RequiresFirstPaidEventReview, snapshot.FarFutureReviewThresholdDays);
            policy.CreatedAt = snapshot.CreatedAt;
            policy.CreatedBy = snapshot.CreatedBy;
            policy.UpdatedAt = snapshot.UpdatedAt;
            policy.UpdatedBy = snapshot.UpdatedBy;
            return policy;
        }, cancellationToken);
        return policies.SingleOrDefault();
    }

    private static string[] ValidateKeys(Guid tenantId, IReadOnlyCollection<string> keys, string[] catalog)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(keys);
        string[] requested = keys.ToArray();
        if (requested.Length > catalog.Length
            || requested.Distinct(StringComparer.Ordinal).Count() != requested.Length
            || requested.Any(key => key is null || !catalog.Contains(key, StringComparer.Ordinal)))
            throw new ArgumentException("Snapshot keys must be distinct members of the manifest storage catalog.", nameof(keys));
        return requested.Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task<List<T>> ReadAsync<T>(
        ExploreDbContext context, string operation, Guid? tenantId, string[]? keys,
        Func<DbDataReader, T> restore, CancellationToken cancellationToken,
        string? expectedBody = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || !context.IsConfigurationInstanceImportOwner
            || context.TenantFilterTenantId is not null)
            throw new InvalidOperationException("Configuration snapshots require the sealed Global instance-import owner.");
        bool openHere = context.Database.GetDbConnection().State != ConnectionState.Open;
        if (openHere)
            await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            if (expectedBody is not null)
            {
                command.CommandText = "SELECT procedure.prosrc FROM pg_catalog.pg_proc procedure WHERE procedure.oid = to_regprocedure(@signature)"
                    + (operation is "bootstrap" or "tenant_slugs" ? $" AND ({BootstrapFunctionAuthorityPredicate()})" : "");
                command.Parameters.Add(new NpgsqlParameter("signature", NpgsqlDbType.Text)
                { Value = FunctionSql(context, operation) + (keys is null ? "(uuid)" : "(uuid,text[])") });
                if (context.Database.GetCommandTimeout() is { } readinessTimeout)
                    command.CommandTimeout = readinessTimeout;
                var body = await command.ExecuteScalarAsync(cancellationToken);
                if (body is not string source || !string.Equals(source, expectedBody, StringComparison.Ordinal))
                    throw new InvalidOperationException(operation == "bootstrap"
                        ? "configuration_import_bootstrap_source_not_ready"
                        : "configuration_import_tenant_source_not_ready");
                command.Parameters.Clear();
            }
            command.CommandText = $"SELECT * FROM {FunctionSql(context, operation)}(@tenant{(keys is null ? "" : ", @keys")})";
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid)
            { Value = tenantId is { } id ? id : DBNull.Value });
            if (keys is not null)
                command.Parameters.Add(new NpgsqlParameter("keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = keys });
            if (context.Database.GetCommandTimeout() is { } timeout)
                command.CommandTimeout = timeout;
            var result = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                result.Add(restore(reader));
            return result;
        }
        finally
        {
            if (openHere)
                await context.Database.CloseConnectionAsync();
        }
    }

    private static Guid? NullableGuid(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    private sealed record PolicySnapshot(
        Guid Id, Guid? TenantId, string PolicyScopeKey, int VersionNumber, bool IsActive,
        int ActiveUniquenessSlot, bool IsPaymentsEnabled, int[] AllowedOrganizerKindIds,
        bool RequiresLocalVerification, string[] AllowedCurrencyCodes, string? DefaultCurrencyCode,
        int[] RefundProtectionIds, PolicyRiskLimit[] CurrencyRiskLimits,
        bool RequiresFirstPaidEventReview, int? FarFutureReviewThresholdDays,
        DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy);

    private sealed record PolicyRiskLimit(
        string CurrencyCode, long? PerEventSalesCeilingMinor, int? PerEventSalesCountCeiling,
        long? RollingOrganizerSalesCeilingMinor, int? RollingOrganizerSalesCountCeiling,
        int? RollingOrganizerWindowDays, long? HighValueReviewThresholdMinor);
}
