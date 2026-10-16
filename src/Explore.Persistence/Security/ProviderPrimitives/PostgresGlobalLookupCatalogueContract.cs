using System.Data;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only startup reference data. The closed selector admits only the cache's
/// seventeen lookup types; EventType contributes only its global, tenant-null rows.
/// </summary>
public static partial class PostgresGlobalLookupCatalogueContract
{
    public const string OwnerRole = "event_global_lookup_owner";
    public const string MigratorRole = "event_global_lookup_migrator";
    public const string RuntimeRole = "event_global_lookup_runtime";
    private const string Routine = "global_lookup_catalogue";
    private const string Policy = "global_lookup_catalogue_read";
    private const string Stamp = "global-lookup-catalogue:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];
    private static readonly (string Name, string StoreType, Type ClrType)[] ResultColumns =
    [
        ("Id", "integer", typeof(int)), ("MasterCode", "text", typeof(string)),
        ("FullName", "text", typeof(string)), ("Description", "text", typeof(string)),
        ("MinAge", "integer", typeof(int?)), ("MaxAge", "integer", typeof(int?)),
        ("RoleScopeId", "integer", typeof(int)), ("IsSystem", "boolean", typeof(bool)),
        ("TenantId", "uuid", typeof(Guid?))
    ];

    internal sealed record Lookup(int Selector, Type Type, string[] Properties);

    // Properties are source declarations, never inferred from live columns. A new
    // property or entity requires an explicit contract change, not wider privileges.
    private static readonly Lookup[] Lookups =
    [
        new(1, typeof(EventType), ["Id", "MasterCode", "FullName", "Description", "TenantId"]),
        new(2, typeof(EventFormat), ["Id", "MasterCode", "FullName", "Description"]),
        new(3, typeof(EventStatus), ["Id", "MasterCode", "FullName", "Description"]),
        new(4, typeof(AudienceAge), ["Id", "MasterCode", "FullName", "Description", "MinAge", "MaxAge"]),
        new(5, typeof(AudienceGender), ["Id", "MasterCode", "FullName", "Description"]),
        new(6, typeof(Madhab), ["Id", "MasterCode", "FullName", "Description"]),
        new(7, typeof(Language), ["Id", "MasterCode", "FullName", "Description"]),
        new(8, typeof(VisibilityType), ["Id", "MasterCode", "FullName", "Description"]),
        new(9, typeof(ApprovalStatus), ["Id", "MasterCode", "FullName", "Description"]),
        new(10, typeof(RegistrationMode), ["Id", "MasterCode", "FullName", "Description"]),
        new(11, typeof(Role), ["Id", "MasterCode", "FullName", "Description", "RoleScopeId", "IsSystem"]),
        new(12, typeof(OrganizationPosition), ["Id", "MasterCode", "FullName", "Description"]),
        new(13, typeof(ActorType), ["Id", "MasterCode", "FullName", "Description"]),
        new(14, typeof(DidCustodyType), ["Id", "MasterCode", "FullName", "Description"]),
        new(15, typeof(FileType), ["Id", "MasterCode", "FullName", "Description"]),
        new(16, typeof(TagType), ["Id", "MasterCode", "FullName", "Description"]),
        new(17, typeof(OwnerType), ["Id", "MasterCode", "FullName", "Description"])
    ];

    public static string FunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Routine);

    internal static Lookup Resolve(ExploreDbContext context, Type type)
    {
        var lookup = Lookups.SingleOrDefault(value => value.Type == type)
            ?? throw new InvalidOperationException("global_lookup_type_not_declared");
        var entity = context.Model.FindEntityType(type)
            ?? throw new InvalidOperationException("global_lookup_model_missing");
        if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(lookup.Properties.Order(StringComparer.Ordinal))
            || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id"
            || entity.BaseType is not null || entity.GetDerivedTypes().Any()
            || entity.GetComplexProperties().Any()
            || entity.GetNavigations().Any(navigation => navigation.IsEagerLoaded)
            || entity.GetSkipNavigations().Any()
            || lookup.Properties.Any(name => entity.FindProperty(name)!.ClrType
                != ResultColumns.Single(column => column.Name == name).ClrType))
            throw new InvalidOperationException("global_lookup_model_shape_drift");
        var tenantTable = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .SingleOrDefault(table => table.TableName == entity.GetTableName());
        if (type == typeof(EventType)
            ? tenantTable is not { AllowsNullTenant: true, ParentJoin: null, TenantIdColumn: not null }
            : tenantTable is not null)
            throw new InvalidOperationException("global_lookup_non_global_source");
        return lookup;
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("global_lookup_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    /// <summary>Read-only attestation on the caller's original open connection.</summary>
    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("global_lookup_original_open_connection_required");
        context.ValidateTenantLease();
        var definition = Define(context, enrollment);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"""
            DO $ready$ BEGIN
                {definition.Guard}
                {ExactBody(context, definition.Body)}
                {PostgresPrivilegeGuard.RestrictedCallerSql}
                IF session_user <> {Text(enrollment.RuntimeLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                    OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
                    OR pg_catalog.current_setting('session_replication_role') <> 'origin'
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NOT NULL
                THEN RAISE EXCEPTION 'global_lookup_caller_mismatch' USING ERRCODE = '42501'; END IF;
            END $ready$;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static string EntitySql(ExploreDbContext context, Lookup lookup)
    {
        var names = new HumanPrincipalSourceSql(context);
        string columns = string.Join(",", lookup.Properties.Select(property =>
            $"value.{names.Quote(property)} AS {names.Column(lookup.Type, property)}"));
        return $"SELECT {columns} FROM {FunctionSql(context)}(@lookup_type) AS value";
    }

    private static string ExactBody(ExploreDbContext context, string body) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
            WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context) + "(integer)")})
                AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                AND f.prorettype = 'record'::regtype AND f.proretset
                AND f.proargdefaults IS NULL AND f.provariadic = 0
                AND f.proallargtypes = ARRAY['integer'::regtype::oid,{string.Join(",", ResultColumns.Select(column => Text(column.StoreType) + "::regtype::oid"))}]
                AND f.proargmodes = ARRAY['i',{string.Join(",", ResultColumns.Select(_ => "'t'"))}]::"char"[]
                AND f.proargnames = ARRAY['p_type',{string.Join(",", ResultColumns.Select(column => Text(column.Name)))}]::text[]
                AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(body))})
        THEN RAISE EXCEPTION 'global_lookup_body_drift' USING ERRCODE = '55000'; END IF;
        """;

    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
