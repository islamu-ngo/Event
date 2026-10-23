using System.Data;
using System.Data.Common;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed tenant name/audit writes and disclosure fences on the sealed Global import
/// owner's existing transaction. No entity acquires reusable mutation authority.
/// </summary>
public static partial class PostgresConfigurationImportTenantIdentityContract
{
    public const string OwnerRole = "event_configuration_import_tenant_identity_owner";
    public const string MigratorRole = "event_configuration_import_tenant_identity_migrator";
    public const string GlobalRole = "event_configuration_import_tenant_identity_global";

    public static async Task<bool> WriteAsync(
        ExploreDbContext context, Guid tenantId, string displayName, Guid actorUserId,
        DateTime occurredAt, CancellationToken ct = default)
    {
        ValidateOwner(context);
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(actorUserId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (occurredAt == default || occurredAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A nondefault UTC timestamp is required.", nameof(occurredAt));
        if (context.ChangeTracker.Entries<Tenant>().Any(entry => entry.Entity.Id == tenantId
                && entry.State != EntityState.Unchanged))
            throw new InvalidOperationException("Tenant import cannot replace a pending tracked mutation.");

        context.DisclosureMutations.Enlist([tenantId]);
        await context.DisclosureMutations.CaptureAsync(ct);
        bool written = (bool)(await ExecuteAsync(context, "write", ct,
            Parameter("tenant", NpgsqlDbType.Uuid, tenantId),
            Parameter("name", NpgsqlDbType.Text, displayName.Trim()),
            Parameter("actor", NpgsqlDbType.Uuid, actorUserId),
            Parameter("at", NpgsqlDbType.TimestampTz, occurredAt)))!;
        if (written)
        {
            // Keep only this held source coherent. Fidelity uses AsNoTracking on the
            // same connection, so it sees the native uncommitted name immediately.
            foreach (var entry in context.ChangeTracker.Entries<Tenant>()
                         .Where(entry => entry.Entity.Id == tenantId))
            {
                entry.Entity.FullName = displayName.Trim();
                entry.Entity.UpdatedAt = occurredAt;
                entry.Entity.UpdatedBy = actorUserId;
                entry.State = EntityState.Unchanged;
            }
        }
        return written;
    }

    internal static async Task FenceTenantsAsync(
        ExploreDbContext context, Guid[] tenants, CancellationToken ct) =>
        _ = await ExecuteAsync(context, "fence", ct,
            Parameter("tenants", NpgsqlDbType.Array | NpgsqlDbType.Uuid, tenants));

    internal static async Task FinalizeAsync(
        ExploreDbContext context, Guid[] tenants, IReadOnlyCollection<Guid> disclosureTenants,
        IReadOnlyDictionary<Guid, (long? Expected, long Advancement)> identities, CancellationToken ct)
    {
        await FenceTenantsAsync(context, tenants, ct);
        context.DisclosureMutations.Seal();
        foreach (Guid tenantId in tenants)
            await RelationalNamedLock.AcquireTransactionAsync(
                context, $"discovery-epoch:{tenantId:N}", ct);
        await ExecuteAsync(context, "finalize", ct,
            Parameter("tenants", NpgsqlDbType.Array | NpgsqlDbType.Uuid, tenants),
            Parameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                tenants.Select(_ => Guid.CreateVersion7()).ToArray()),
            Parameter("disclosure", NpgsqlDbType.Array | NpgsqlDbType.Boolean,
                tenants.Select(disclosureTenants.Contains).ToArray()),
            Parameter("expected", NpgsqlDbType.Array | NpgsqlDbType.Bigint,
                tenants.Select(id => identities.GetValueOrDefault(id).Expected).ToArray()),
            Parameter("advances", NpgsqlDbType.Array | NpgsqlDbType.Bigint,
                tenants.Select(id => identities.GetValueOrDefault(id).Advancement).ToArray()));
        foreach (var entry in context.ChangeTracker.Entries<EventDiscoveryRevision>()
                     .Where(entry => tenants.Contains(entry.Entity.TenantId)).ToArray())
            entry.State = EntityState.Detached;
    }

    private static DbTransaction ValidateOwner(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || !context.IsConfigurationInstanceImportOwner
            || context.TenantFilterTenantId is not null || context.Database.CurrentTransaction is null
            || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new InvalidOperationException(
                "Tenant identity import requires the sealed tenant-null Global owner and its active transaction.");
        DbTransaction transaction = context.Database.CurrentTransaction.GetDbTransaction();
        if (!ReferenceEquals(transaction.Connection, context.Database.GetDbConnection())
            || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Tenant identity import requires its serializable owner connection.");
        return transaction;
    }

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object value) =>
        new(name, type) { Value = value };

    private static async Task<object?> ExecuteAsync(
        ExploreDbContext context, string operation, CancellationToken ct, params NpgsqlParameter[] parameters)
    {
        DbTransaction transaction = ValidateOwner(context);
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {FunctionSql(context, operation)}(" +
            string.Join(", ", parameters.Select(parameter => "@" + parameter.ParameterName)) + ")";
        foreach (NpgsqlParameter parameter in parameters)
            command.Parameters.Add(parameter);
        if (context.Database.GetCommandTimeout() is { } timeout)
            command.CommandTimeout = timeout;
        return await command.ExecuteScalarAsync(ct);
    }
}
