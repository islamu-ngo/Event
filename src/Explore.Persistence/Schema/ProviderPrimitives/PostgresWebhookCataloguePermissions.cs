using System.Data;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>Fixed catalogue storage rights; C# retains descriptor synchronization authority.</summary>
public static class PostgresWebhookCataloguePermissions
{
    private const string Drift = "webhook_catalogue_permissions_drift";
    private static readonly string[] Properties =
    [
        "Id", "Name", "GroupName", "Description", "SchemaJson", "SchemaVersion", "IsPublic", "IsEnabled",
        "PayloadRetentionDays", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"
    ];
    private static readonly string[] UpdateProperties =
        ["GroupName", "Description", "SchemaJson", "SchemaVersion", "IsPublic", "IsEnabled",
            "PayloadRetentionDays", "UpdatedAt", "UpdatedBy"];

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        if (System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("webhook_catalogue_installer_ambient_transaction");
        var source = Mapping(context);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var physical = transaction.GetDbTransaction();
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.ContextId != contextId || interrupted || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || !connection.Owns(physical) || System.Transactions.Transaction.Current is not null)
                throw new UnauthorizedAccessException("webhook_catalogue_installer_owner_changed");
        }
        connection.StateChange += Changed;
        string savepoint = "webhook_catalogue_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                RequireOriginal();
                if (!await IsTrueAsync(context, Identity(enrollment.MigratorLogin), cancellationToken))
                    throw new UnauthorizedAccessException("webhook_catalogue_installer_wrong_identity");
                RequireOriginal();
                string complete = Manifest(source, enrollment, complete: true);
                string fresh = Fresh(source, enrollment);
                if (!await IsTrueAsync(context,
                    $"({Manifest(source, enrollment, complete: false)}) AND (({fresh}) OR ({complete}))",
                    cancellationToken))
                    throw new InvalidOperationException(Drift);
                RequireOriginal();
                bool install = await IsTrueAsync(context, fresh, cancellationToken);
                RequireOriginal();
                if (install)
                {
                    var names = context.GetService<ISqlGenerationHelper>();
                    string role = names.DelimitIdentifier(enrollment.GlobalLogin);
                    string columns = string.Join(',', source.Columns.Values.Select(column => names.DelimitIdentifier(column.Name)));
                    string updates = string.Join(',', UpdateProperties.Select(property => names.DelimitIdentifier(source.Columns[property].Name)));
                    await using var command = context.Database.GetDbConnection().CreateCommand();
                    command.Transaction = physical;
                    command.CommandText = $"""
                        GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {role};
                        GRANT SELECT ({columns}), INSERT ({columns}), UPDATE ({updates}) ON {source.Table} TO {role};
                        """;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                    RequireOriginal();
                }
                if (!await IsTrueAsync(context, $"({Identity(enrollment.MigratorLogin)}) AND ({complete})", cancellationToken))
                    throw new InvalidOperationException(Drift);
                RequireOriginal();
                await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            }
            catch
            {
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
                throw;
            }
            if (owned is not null) await owned.CommitAsync(CancellationToken.None);
        }
        finally { connection.StateChange -= Changed; }
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("webhook_catalogue_enrollment_required");
        enrollment.Validate();
        context.RequireBoundTenantLease();
        var contextId = context.ContextId;
        var tenantContext = context.TenantContext;
        var connection = RelationalConnectionIdentity.Capture(context);
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || !ReferenceEquals(context.TenantContext, tenantContext)
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null
                || System.Transactions.Transaction.Current is not null || interrupted || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend)
                throw new UnauthorizedAccessException("webhook_catalogue_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            string identity = $"""
                ({Identity(enrollment.GlobalLogin)})
                AND pg_catalog.current_setting('session_replication_role')='origin'
                AND COALESCE(pg_catalog.current_setting('app.current_tenant_id',true),'')=''
                """;
            if (!await IsTrueAsync(context, identity, cancellationToken))
                throw new UnauthorizedAccessException("webhook_catalogue_identity_not_ready");
            RequireOriginal();
            bool ready = await IsTrueAsync(context,
                $"({identity}) AND ({CompleteGlobalCataloguePredicate(context, enrollment)})", cancellationToken);
            RequireOriginal();
            if (!ready) throw new UnauthorizedAccessException("webhook_catalogue_permissions_not_ready");
        }
        finally { connection.StateChange -= Changed; }
    }

    internal static IReadOnlyList<string> GlobalCatalogueRelations(ExploreDbContext context) => [Mapping(context).Table];

    internal static string CompleteGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        Manifest(Mapping(context), enrollment, complete: true);

    private static string Manifest(Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string owner = RoleOid(enrollment.MigratorLogin);
        string global = RoleOid(enrollment.GlobalLogin);
        string runtime = RoleOid(enrollment.RuntimeLogin);
        string expected = "VALUES " + string.Join(',', source.Columns.SelectMany(pair =>
            (UpdateProperties.Contains(pair.Key, StringComparer.Ordinal)
                ? new[] { "SELECT", "INSERT", "UPDATE" } : new[] { "SELECT", "INSERT" })
            .Select(privilege => $"({Text(pair.Value.Name)},{Text(privilege)})")));
        string required = complete ? $"""
            AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (
                SELECT 1 FROM actual a WHERE a.column_name=e.column_name AND a.privilege_type=e.privilege_type
                    AND a.grantee={global} AND a.grantor={owner} AND NOT a.is_grantable))
            AND pg_catalog.has_schema_privilege({global},{Text(source.Schema)},'USAGE')
            """ : "";
        return $"""
            (WITH expected(column_name,privilege_type) AS ({expected}), actual AS ({Actual(source, enrollment)})
            SELECT (SELECT count(*)=3 FROM pg_catalog.pg_roles
                    WHERE rolname IN ({Text(enrollment.MigratorLogin)},{Text(enrollment.RuntimeLogin)},{Text(enrollment.GlobalLogin)}) AND rolcanlogin)
                AND ({SourcePredicate(source, enrollment)})
                AND NOT EXISTS (SELECT 1 FROM actual a WHERE a.grantee<>{global} OR a.grantor<>{owner}
                    OR a.is_grantable OR NOT EXISTS (SELECT 1 FROM expected e
                        WHERE e.column_name=a.column_name AND e.privilege_type=a.privilege_type))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid IN ({global},{runtime}))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE (pg_catalog.pg_has_role({global},r.oid,'MEMBER') OR pg_catalog.pg_has_role({runtime},r.oid,'MEMBER'))
                        AND (r.oid={owner} OR r.rolsuper OR r.rolbypassrls OR r.rolcreatedb
                            OR r.rolcreaterole OR r.rolreplication
                            OR pg_catalog.has_schema_privilege(r.oid,{Text(source.Schema)},'CREATE')
                            OR pg_catalog.has_schema_privilege(r.oid,{Text(source.Schema)},'USAGE WITH GRANT OPTION')
                            OR pg_catalog.has_table_privilege(r.oid,pg_catalog.to_regclass({Text(source.Table)}),'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", $"pg_catalog.to_regclass({Text(source.Table)})", false)}))
                {required})
            """;
    }

    // Include every non-owner ACL, not just the enrolled logins, so inherited and PUBLIC rights cannot hide.
    private static string Actual(Source source, PostgresNativeQueueEnrollment enrollment) => $"""
        SELECT NULL::text AS column_name, acl.* FROM pg_catalog.pg_class c
        CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(c.relacl,pg_catalog.acldefault('r',c.relowner))) acl
        WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)})
            AND (acl.grantee<>{RoleOid(enrollment.MigratorLogin)} OR acl.grantor<>{RoleOid(enrollment.MigratorLogin)})
        UNION ALL
        SELECT a.attname::text, acl.* FROM pg_catalog.pg_attribute a
        CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
        WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attnum<>0 AND NOT a.attisdropped
            AND (acl.grantee<>{RoleOid(enrollment.MigratorLogin)} OR acl.grantor<>{RoleOid(enrollment.MigratorLogin)})
        """;

    private static string Fresh(Source source, PostgresNativeQueueEnrollment enrollment) =>
        $"NOT EXISTS ({Actual(source, enrollment)})";

    private static string SourcePredicate(Source source, PostgresNativeQueueEnrollment enrollment) => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
            WHERE t.oid=pg_catalog.to_regclass({Text(source.Table)}) AND t.relkind='r' AND t.relpersistence='p'
                AND NOT t.relispartition AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                AND t.relowner={RoleOid(enrollment.MigratorLogin)} AND n.nspowner={RoleOid(enrollment.MigratorLogin)}
                AND n.nspname={Text(source.Schema)})
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits
            WHERE inhrelid=pg_catalog.to_regclass({Text(source.Table)}) OR inhparent=pg_catalog.to_regclass({Text(source.Table)}))
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=pg_catalog.to_regclass({Text(source.Table)}))
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger WHERE tgrelid=pg_catalog.to_regclass({Text(source.Table)}) AND NOT tgisinternal)
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass({Text(source.Table)}))
        AND (SELECT count(*)=13 FROM pg_catalog.pg_attribute
            WHERE attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND attnum>0 AND NOT attisdropped)
        AND ({string.Join(" AND ", source.Columns.Values.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND a.attname={Text(column.Name)} AND a.attnum>0 AND NOT a.attisdropped AND a.attidentity='' AND a.attgenerated=''
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)}
                AND a.attnotnull={(column.Required ? "true" : "false")})
            """))})
        """;

    private static Source Mapping(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(WebhookEventType))
            ?? throw new InvalidOperationException("webhook_catalogue_model_missing");
        if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(Properties.Order(StringComparer.Ordinal))
            || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id"
            || entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
            || entity.GetSkipNavigations().Any() || entity.GetNavigations().Any()
            || entity.GetProperties().Any(property => property.IsShadowProperty() || property.ClrType != (property.Name switch
            {
                "Id" => typeof(Guid),
                "CreatedBy" or "UpdatedBy" => typeof(Guid?),
                "CreatedAt" => typeof(DateTime),
                "UpdatedAt" => typeof(DateTime?),
                "SchemaVersion" or "PayloadRetentionDays" => typeof(int),
                "IsPublic" or "IsEnabled" => typeof(bool),
                _ => typeof(string)
            })))
            throw new InvalidOperationException("webhook_catalogue_model_shape_drift");
        string table = entity.GetTableName() ?? throw new InvalidOperationException("webhook_catalogue_table_missing");
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        if (PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .Any(source => source.TableName == table && (source.Schema ?? schema) == schema))
            throw new InvalidOperationException("webhook_catalogue_model_tenancy_drift");
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        var columns = Properties.ToDictionary(name => name, name =>
        {
            var property = entity.FindProperty(name)!;
            return new Column(property.GetColumnName(store)!, property.GetRelationalTypeMapping().StoreType, !property.IsNullable);
        });
        if (columns.Values.Select(column => column.Name).Distinct(StringComparer.Ordinal).Count() != 13)
            throw new InvalidOperationException("webhook_catalogue_model_shape_drift");
        return new Source(schema, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(table, schema), columns);
    }

    private static string Identity(string login) =>
        $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)";
    private static string RoleOid(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static async Task<bool> IsTrueAsync(ExploreDbContext context, string predicate, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT ({predicate})";
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
    private sealed record Column(string Name, string StoreType, bool Required);
    private sealed record Source(string Schema, string Table, Dictionary<string, Column> Columns);
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
