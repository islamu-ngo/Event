using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class StorageScopeTotalsQuery
{
    internal static async Task<StorageScopeTotals> ReadAsync(
        ExploreDbContext context, Guid tenantId, string provider, CancellationToken cancellationToken)
    {
        // Custody includes soft-deleted rows until irreversible byte deletion is recorded.
        var sessions = context.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant]);
        var objects = context.StorageObjects.IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .Where(value => value.TenantId == tenantId && value.Provider == provider
                && value.Provider != StorageProviders.LegacyExternal
                && !context.StorageObjectDeletionTombstones.Any(tombstone => tombstone.Id == value.Id)
                && (value.Purpose != StorageObjectPurposes.EventResource
                    || value.LifecycleState == StorageObjectLifecycleStates.Active
                    || value.LifecycleState == StorageObjectLifecycleStates.Quarantined
                    || sessions
                        .Any(session => session.TenantId == tenantId && session.StorageObjectId == value.Id
                            && session.Status == StorageUploadSessionStates.Finalized)));
        var reservations = sessions
            .Where(session => session.TenantId == tenantId && session.Provider == provider
                && (session.Status == StorageUploadSessionStates.Reserved || session.Status == StorageUploadSessionStates.Uploading));
        var totals = await context.Tenants.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(tenant => tenant.Id == tenantId)
            .Select(_ => new StorageScopeTotals(
                objects.Sum(value => value.LifecycleState == StorageObjectLifecycleStates.Quarantined ? (long?)0 : value.Size) ?? 0,
                reservations.Sum(session => (long?)session.ReservedBytes) ?? 0,
                objects.Sum(value => value.LifecycleState == StorageObjectLifecycleStates.Quarantined ? (long?)value.Size : 0) ?? 0,
                objects.LongCount()))
            .SingleOrDefaultAsync(cancellationToken);
        return totals ?? new(0, 0, 0, 0);
    }

    /// <summary>Produces the complete custody aggregate, using compiled parameter expressions only.</summary>
    internal static string BuildSql(
        ExploreDbContext context, string tenantIdSql, string providerSql)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string Table<T>() => sql.DelimitIdentifier(
            context.Model.FindEntityType(typeof(T))!.GetTableName()!,
            context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
                ?? context.Model.FindEntityType(typeof(T))!.GetSchema());
        string Column<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(store)!);
        }
        string Literal<T>(string property, object value) => context.Model.FindEntityType(typeof(T))!
            .FindProperty(property)!.GetRelationalTypeMapping().GenerateSqlLiteral(value);
        string O(string property) => "o." + Column<StorageObject>(property);
        string S(string property) => "s." + Column<StorageUploadSession>(property);
        string active = Literal<StorageObject>(nameof(StorageObject.LifecycleState), StorageObjectLifecycleStates.Active);
        string quarantined = Literal<StorageObject>(nameof(StorageObject.LifecycleState), StorageObjectLifecycleStates.Quarantined);
        string resource = Literal<StorageObject>(nameof(StorageObject.Purpose), StorageObjectPurposes.EventResource);
        string legacy = Literal<StorageObject>(nameof(StorageObject.Provider), StorageProviders.LegacyExternal);
        string finalized = Literal<StorageUploadSession>(nameof(StorageUploadSession.Status), StorageUploadSessionStates.Finalized);
        string reserved = Literal<StorageUploadSession>(nameof(StorageUploadSession.Status), StorageUploadSessionStates.Reserved);
        string uploading = Literal<StorageUploadSession>(nameof(StorageUploadSession.Status), StorageUploadSessionStates.Uploading);
        string count = context.Database.IsSqlServer() ? "COUNT_BIG(*)" : "COUNT(*)";
        string integer = context.Database.ProviderName == RelationalNamedLock.MySqlProvider
            ? "SIGNED" : context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(long))!.StoreType;
        string usedAlias = sql.DelimitIdentifier(nameof(StorageScopeTotals.Used));
        string reservedAlias = sql.DelimitIdentifier(nameof(StorageScopeTotals.Reserved));
        string quarantinedAlias = sql.DelimitIdentifier(nameof(StorageScopeTotals.Quarantined));
        string countAlias = sql.DelimitIdentifier(nameof(StorageScopeTotals.Count));
        return $"""
            SELECT totals.{usedAlias}, reservations.{reservedAlias}, totals.{quarantinedAlias}, totals.{countAlias}
            FROM (
                SELECT CAST(COALESCE(SUM(CASE WHEN {O(nameof(StorageObject.LifecycleState))} = {quarantined}
                    THEN 0 ELSE {O(nameof(StorageObject.Size))} END), 0) AS {integer}) AS {usedAlias},
                    CAST(COALESCE(SUM(CASE WHEN {O(nameof(StorageObject.LifecycleState))} = {quarantined}
                    THEN {O(nameof(StorageObject.Size))} ELSE 0 END), 0) AS {integer}) AS {quarantinedAlias},
                    {count} AS {countAlias}
                FROM {Table<StorageObject>()} o
                WHERE {O(nameof(StorageObject.TenantId))} = {tenantIdSql}
                  AND {O(nameof(StorageObject.Provider))} = {providerSql}
                  AND {O(nameof(StorageObject.Provider))} <> {legacy}
                  AND NOT EXISTS (
                      SELECT 1 FROM {Table<StorageObjectDeletionTombstone>()} t
                      WHERE t.{Column<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Id))} = {O(nameof(StorageObject.Id))})
                  AND ({O(nameof(StorageObject.Purpose))} <> {resource}
                    OR {O(nameof(StorageObject.LifecycleState))} IN ({active}, {quarantined})
                    OR EXISTS (
                        SELECT 1 FROM {Table<StorageUploadSession>()} s
                        WHERE {S(nameof(StorageUploadSession.TenantId))} = {tenantIdSql}
                          AND {S(nameof(StorageUploadSession.StorageObjectId))} = {O(nameof(StorageObject.Id))}
                          AND {S(nameof(StorageUploadSession.Status))} = {finalized}))
            ) totals
            CROSS JOIN (
                SELECT CAST(COALESCE(SUM({S(nameof(StorageUploadSession.ReservedBytes))}), 0) AS {integer}) AS {reservedAlias}
                FROM {Table<StorageUploadSession>()} s
                WHERE {S(nameof(StorageUploadSession.TenantId))} = {tenantIdSql}
                  AND {S(nameof(StorageUploadSession.Provider))} = {providerSql}
                  AND {S(nameof(StorageUploadSession.Status))} IN ({reserved}, {uploading})
            ) reservations
            """;
    }
}

internal sealed record StorageScopeTotals(
    [property: Column("Used")] long Used,
    [property: Column("Reserved")] long Reserved,
    [property: Column("Quarantined")] long Quarantined,
    [property: Column("Count")] long Count);
