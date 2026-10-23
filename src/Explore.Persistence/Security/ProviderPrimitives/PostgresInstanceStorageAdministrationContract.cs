using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only accounting reports and atomic repair. Only counter accounting
/// and fences can change; custody sources remain read-only.
/// </summary>
public static partial class PostgresInstanceStorageAdministrationContract
{
    public const string OwnerRole = "event_instance_storage_admin_owner";
    public const string MigratorRole = "event_instance_storage_admin_migrator";
    public const string GlobalRole = "event_instance_storage_admin_global";
    public const string PolicyName = "instance_storage_admin_read";

    public static string UsageFunctionSql(ExploreDbContext context) => FunctionSql(context, "instance_storage_usage");
    public static string ScopesFunctionSql(ExploreDbContext context) => FunctionSql(context, "instance_storage_scopes");

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildRecalculationSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        IEntityType Entity(Type type) => context.Model.FindEntityType(type)!;
        string Table(Type type) => sql.DelimitIdentifier(Entity(type).GetTableName()!, Schema(context, Entity(type)));
        string Column(Type type, string property)
        {
            var entity = Entity(type);
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }
        string C(string property) => Column(typeof(StorageUsageCounter), property);
        (Type Type, string[] Columns)[] grants =
        [
            (typeof(StorageUsageCounter), [nameof(StorageUsageCounter.TenantId), nameof(StorageUsageCounter.Provider),
                nameof(StorageUsageCounter.UsedBytes), nameof(StorageUsageCounter.ReservedBytes),
                nameof(StorageUsageCounter.QuarantinedBytes), nameof(StorageUsageCounter.ObjectCount),
                nameof(StorageUsageCounter.LastRecalculatedAt)]),
            (typeof(StorageObject), [nameof(StorageObject.TenantId), nameof(StorageObject.Provider)]),
            (typeof(StorageUploadSession), [nameof(StorageUploadSession.TenantId), nameof(StorageUploadSession.Provider)])
        ];
        string tableGrants = string.Join("\n", grants.Select(grant => $"""
            REVOKE ALL ON TABLE {Table(grant.Type)} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({string.Join(", ", grant.Columns.Select(column => Column(grant.Type, column)))})
                ON {Table(grant.Type)} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {Table(grant.Type)};
            CREATE POLICY "{PolicyName}" ON {Table(grant.Type)} FOR SELECT TO {OwnerRole} USING (true);
            """));
        string scopeSelects = string.Join("\nUNION\n", grants.Select(grant => $"""
            SELECT {Column(grant.Type, "TenantId")} AS "TenantId",
                {Column(grant.Type, "Provider")}::text AS "Provider"
            FROM {Table(grant.Type)}
            {(grant.Type == typeof(StorageObject) ? $"WHERE {Column(grant.Type, nameof(StorageObject.Provider))} <> '{StorageProviders.LegacyExternal}'" : "")}
            """));
        string schema = sql.DelimitIdentifier(Schema(context, Entity(typeof(StorageUsageCounter))));
        string usage = UsageFunctionSql(context);
        string scopes = ScopesFunctionSql(context);
        return $"""
            DO $contract$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    ELSE
                        EXECUTE format('ALTER ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                END LOOP;
                IF pg_has_role('{GlobalRole}', '{OwnerRole}', 'MEMBER')
                   OR pg_has_role('{GlobalRole}', '{MigratorRole}', 'MEMBER')
                   OR pg_has_role('{OwnerRole}', '{GlobalRole}', 'MEMBER')
                   OR pg_has_role('{MigratorRole}', '{GlobalRole}', 'MEMBER') THEN
                    RAISE EXCEPTION 'instance storage execution and migration authority must remain separate';
                END IF;
                IF EXISTS (
                    WITH RECURSIVE inherited_roles(roleid) AS (
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members AS edge
                        WHERE edge.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members AS edge
                        INNER JOIN inherited_roles AS inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM inherited_roles
                    WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')) THEN
                    RAISE EXCEPTION 'instance storage execution and migration logins must remain separate';
                END IF;
            END $contract$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            {tableGrants}
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {usage}()
                RETURNS TABLE (
                    "Provider" text, "UsedBytes" bigint, "ReservedBytes" bigint,
                    "QuarantinedBytes" bigint, "ObjectCount" bigint,
                    "LastRecalculatedAt" timestamp with time zone)
                LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                    SELECT {C(nameof(StorageUsageCounter.Provider))}::text,
                        sum({C(nameof(StorageUsageCounter.UsedBytes))})::bigint,
                        sum({C(nameof(StorageUsageCounter.ReservedBytes))})::bigint,
                        sum({C(nameof(StorageUsageCounter.QuarantinedBytes))})::bigint,
                        sum({C(nameof(StorageUsageCounter.ObjectCount))})::bigint,
                        max({C(nameof(StorageUsageCounter.LastRecalculatedAt))})
                    FROM {Table(typeof(StorageUsageCounter))}
                    GROUP BY {C(nameof(StorageUsageCounter.Provider))}
                    ORDER BY {C(nameof(StorageUsageCounter.Provider))};
                $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {scopes}()
                RETURNS TABLE ("TenantId" uuid, "Provider" text)
                LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                    {scopeSelects}
                    ORDER BY "TenantId", "Provider";
                $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {usage}(), {scopes}() FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {usage}(), {scopes}() TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            """;
    }

    private static string FunctionSql(ExploreDbContext context, string name)
    {
        var entity = context.Model.FindEntityType(typeof(StorageUsageCounter))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, Schema(context, entity));
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
