using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Services.Discovery;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Migration-owned, exact-tenant discovery data operations for the enrolled ordinary Runtime login.
/// This capability does not confer disclosure fanout, identity administration, or private source access.
/// </summary>
public static partial class PostgresDiscoveryTraversalContract
{
    public const string OwnerRole = "event_discovery_traversal_owner";
    public const string MigratorRole = "event_discovery_traversal_migrator";
    public const string RuntimeRole = "event_discovery_traversal_runtime";
    private const string Prefix = "discovery_traversal_";
    private const string Policy = "discovery_traversal_owner";
    private const string Stamp = "discovery-traversal:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];
    private static readonly Type[] Sources =
    [
        typeof(Tenant), typeof(EventDiscoveryRevision), typeof(EventDiscoverySnapshotReservation),
        typeof(EventDiscoverySnapshot), typeof(EventDiscoverySnapshotItem),
        typeof(EventDiscoveryIdentity), typeof(EventDiscoveryAlias)
    ];

    private static readonly (string Name, string Signature, string Arguments, Type? Entity, string Scalar)[] Routines =
    [
        ("ready", "uuid", "p_tenant uuid", null, "void"),
        ("tenant_fence", "uuid", "p_tenant uuid", null, "void"),
        ("reservation_fence", "uuid", "p_tenant uuid", null, "void"),
        ("revision_read", "uuid", "p_tenant uuid", typeof(EventDiscoveryRevision), ""),
        ("revision_acquire", "uuid,uuid", "p_tenant uuid,p_id uuid", typeof(EventDiscoveryRevision), ""),
        ("revision_write", "uuid,bigint,bigint", "p_tenant uuid,p_identity bigint,p_disclosure bigint", null, "integer"),
        ("snapshot_get", "uuid,uuid", "p_tenant uuid,p_id uuid", typeof(EventDiscoverySnapshot), ""),
        ("snapshot_reuse", "uuid,text,bigint,bigint,timestamp with time zone",
            "p_tenant uuid,p_hash text,p_identity bigint,p_disclosure bigint,p_now timestamp with time zone",
            typeof(EventDiscoverySnapshot), ""),
        ("snapshot_items", "uuid,uuid,bigint,integer", "p_tenant uuid,p_id uuid,p_after bigint,p_take integer",
            typeof(EventDiscoverySnapshotItem), ""),
        ("snapshot_capture", "uuid,jsonb,jsonb,timestamp with time zone,integer,integer,integer,integer",
            "p_tenant uuid,p_header jsonb,p_items jsonb,p_now timestamp with time zone,p_identities integer,p_lifetime integer,p_live integer,p_physical integer",
            null, "uuid"),
        ("snapshot_purge", "uuid,timestamp with time zone,integer",
            "p_tenant uuid,p_now timestamp with time zone,p_take integer", null, "integer"),
        ("bindings", "uuid,integer,text[]", "p_tenant uuid,p_kind integer,p_hashes text[]", typeof(EventDiscoveryIdentity), ""),
        ("aliases", "uuid,integer,text[]", "p_tenant uuid,p_kind integer,p_hashes text[]", typeof(EventDiscoveryAlias), "")
    ];

    /// <summary>Use the same deployment enrollment in the migrator and its production-faithful fixture.</summary>
    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("discovery_traversal_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    /// <summary>Read-only source, body, ACL and policy attestation; never installs or repairs authority.</summary>
    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new InvalidOperationException("Discovery readiness requires the original open PostgreSQL connection.");
        context.ValidateTenantLease();
        string tenant = context.TenantFilterTenantId is { } selected ? Text(selected.ToString()) + "::uuid" : "NULL::uuid";
        await context.Database.ExecuteSqlRawAsync($"""
            DO $ready$ BEGIN
                {Guard(context, enrollment)}
                {ExactBodies(context, enrollment)}
                IF session_user <> {Text(enrollment.MigratorLogin)} THEN
                    IF {(context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner).ToString().ToLowerInvariant()}
                    THEN RAISE EXCEPTION 'discovery_traversal_managed_lease' USING ERRCODE = '42501'; END IF;
                    PERFORM {FunctionSql(context, "ready")}({tenant});
                ELSIF current_user <> session_user OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'discovery_traversal_readiness_caller' USING ERRCODE = '42501'; END IF;
            END $ready$;
            """, cancellationToken);
    }

    internal static string FunctionSql(ExploreDbContext context, string operation)
    {
        if (!Routines.Any(routine => routine.Name == operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        return new HumanPrincipalSourceSql(context).Name(Prefix + operation);
    }

    private static void RequireTenant(ExploreDbContext context, Guid tenant)
    {
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || tenant == Guid.Empty || context.BindTenantLease() != tenant
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
            || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Discovery requires the original ordinary tenant lease.");
        if (context.Database.CurrentTransaction?.GetDbTransaction() is { } transaction
            && !ReferenceEquals(transaction.Connection, context.Database.GetDbConnection()))
            throw new InvalidOperationException("Discovery requires the original physical transaction.");
    }

    internal static Task FenceTenantAsync(ExploreDbContext context, Guid tenant, CancellationToken token) =>
        ExecuteAsync(context, tenant, "tenant_fence", token);

    internal static Task FenceReservationAsync(ExploreDbContext context, Guid tenant, CancellationToken token) =>
        ExecuteAsync(context, tenant, "reservation_fence", token);

    private static Task ExecuteAsync(ExploreDbContext context, Guid tenant, string operation, CancellationToken token)
    {
        RequireTenant(context, tenant);
        return context.Database.ExecuteSqlRawAsync($"SELECT {FunctionSql(context, operation)}({{0}})", [tenant], token);
    }

    internal static Task<EventDiscoveryRevision?> ReadRevisionAsync(
        ExploreDbContext context, Guid tenant, CancellationToken token)
    {
        RequireTenant(context, tenant);
        return context.Set<EventDiscoveryRevision>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "revision_read")}({{0}})", tenant)
            .AsNoTracking().SingleOrDefaultAsync(token);
    }

    internal static async Task<EventDiscoveryRevision> AcquireRevisionAsync(
        ExploreDbContext context, Guid tenant, CancellationToken token)
    {
        RequireTenant(context, tenant);
        var rows = await context.Set<EventDiscoveryRevision>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "revision_acquire")}({{0}},{{1}})", tenant, Guid.CreateVersion7())
            .AsNoTracking().ToListAsync(token);
        return rows.Single();
    }

    internal static async Task<int> UpdateRevisionAsync(
        ExploreDbContext context, Guid tenant, EventDiscoveryRevision revision, CancellationToken token)
    {
        RequireTenant(context, tenant);
        return await context.Database.SqlQueryRaw<int>(
                $"SELECT {FunctionSql(context, "revision_write")}({{0}},{{1}},{{2}}) AS \"Value\"",
                tenant, revision.IdentityEpoch, revision.DisclosureEpoch).SingleAsync(token);
    }

    internal static Task<EventDiscoverySnapshot?> GetSnapshotAsync(
        ExploreDbContext context, Guid tenant, Guid id, CancellationToken token)
    {
        RequireTenant(context, tenant);
        return context.Set<EventDiscoverySnapshot>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "snapshot_get")}({{0}},{{1}})", tenant, id)
            .AsNoTracking().SingleOrDefaultAsync(token);
    }

    internal static Task<EventDiscoverySnapshot?> FindReusableAsync(ExploreDbContext context, Guid tenant,
        string hash, long identity, long disclosure, DateTime now, CancellationToken token)
    {
        RequireTenant(context, tenant);
        return context.Set<EventDiscoverySnapshot>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "snapshot_reuse")}({{0}},{{1}},{{2}},{{3}},{{4}})",
                tenant, hash, identity, disclosure, now).AsNoTracking().SingleOrDefaultAsync(token);
    }

    internal static async Task<IReadOnlyList<EventDiscoverySnapshotItem>> GetItemsAsync(
        ExploreDbContext context, Guid tenant, Guid snapshot, long after, int take, CancellationToken token)
    {
        RequireTenant(context, tenant);
        return await context.Set<EventDiscoverySnapshotItem>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "snapshot_items")}({{0}},{{1}},{{2}},{{3}})",
                tenant, snapshot, after, take).AsNoTracking().OrderBy(item => item.Ordinal).ToArrayAsync(token);
    }

    internal static async Task<EventDiscoverySnapshot?> CaptureAsync(ExploreDbContext context,
        EventDiscoverySnapshot snapshot, EventDiscoveryTraversalLimits limits, DateTime now, CancellationToken token)
    {
        RequireTenant(context, snapshot.TenantId);
        string header = JsonSerializer.Serialize(new
        {
            snapshot.Id,
            snapshot.TenantId,
            snapshot.CriteriaHash,
            snapshot.IdentityEpoch,
            snapshot.DisclosureEpoch,
            snapshot.CreatedAtUtc,
            snapshot.ExpiresAtUtc,
            snapshot.ItemCount,
            snapshot.Truncated,
            snapshot.LocalSourceComplete,
            snapshot.RemoteSourceComplete
        });
        string items = JsonSerializer.Serialize(snapshot.Items.Select(item => new
        {
            item.SourceKind,
            item.SourceId,
            item.CanonicalKind,
            item.CanonicalId,
            item.MatchingSessionId
        }));
        var id = await context.Database.SqlQueryRaw<Guid?>(
                $"SELECT {FunctionSql(context, "snapshot_capture")}(@tenant,@header,@items,@now,@identities,@lifetime,@live,@physical) AS \"Value\"",
                new NpgsqlParameter("tenant", snapshot.TenantId),
                new NpgsqlParameter("header", NpgsqlDbType.Jsonb) { Value = header },
                new NpgsqlParameter("items", NpgsqlDbType.Jsonb) { Value = items },
                new NpgsqlParameter("now", now),
                new NpgsqlParameter("identities", limits.MaxIdentities),
                new NpgsqlParameter("lifetime", (int)limits.Lifetime.TotalMinutes),
                new NpgsqlParameter("live", limits.MaxLiveSnapshots),
                new NpgsqlParameter("physical", limits.MaxPhysicalItems)).SingleAsync(token);
        return id is null ? null : id == snapshot.Id ? snapshot
            : await GetSnapshotAsync(context, snapshot.TenantId, id.Value, token);
    }

    internal static async Task<int> PurgeAsync(
        ExploreDbContext context, Guid tenant, DateTime now, int take, CancellationToken token)
    {
        RequireTenant(context, tenant);
        return await context.Database.SqlQueryRaw<int>(
            $"SELECT {FunctionSql(context, "snapshot_purge")}({{0}},{{1}},{{2}}) AS \"Value\"",
            tenant, now, take).SingleAsync(token);
    }

    internal static async Task<IReadOnlyList<EventDiscoveryIdentity>> GetBindingsAsync(ExploreDbContext context,
        Guid tenant, EventDiscoverySourceKind kind, IReadOnlyCollection<string> sourceKeys, CancellationToken token)
    {
        RequireTenant(context, tenant);
        if (sourceKeys.Count > EventDiscoveryTraversalLimits.IdentityCeiling)
            throw new ArgumentException("Discovery binding reads are bounded to 1000 source keys.", nameof(sourceKeys));
        var exactKeys = sourceKeys.ToHashSet(StringComparer.Ordinal);
        string[] hashes = exactKeys.Select(EventDiscoveryIdentity.HashSourceKey).ToArray();
        // Two fixed bounded functions materialize entities, not EF Include joins against private tables.
        // The caller's source transaction remains intact; the terminal revision fence validates its image.
        var identities = await context.Set<EventDiscoveryIdentity>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "bindings")}({{0}},{{1}},{{2}})", tenant, (int)kind, hashes)
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking().ToArrayAsync(token);
        var aliases = await context.Set<EventDiscoveryAlias>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "aliases")}({{0}},{{1}},{{2}})", tenant, (int)kind, hashes)
            .AsNoTracking().ToArrayAsync(token);
        var byId = identities.ToDictionary(identity => identity.Id);
        foreach (var alias in aliases)
        {
            if (byId.TryGetValue(alias.MemberIdentityId, out var member))
            {
                member.Alias = alias;
                alias.Member = member;
            }
            if (byId.TryGetValue(alias.PrimaryIdentityId, out var primary)) alias.Primary = primary;
        }
        return identities.Where(identity => identity.SourceKind == kind && exactKeys.Contains(identity.SourceKey)).ToArray();
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
