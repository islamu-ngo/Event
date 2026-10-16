using Explore.Persistence.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only, bounded hold-expiry identities. This owner cannot expire holds or read orders'
/// purchaser, line, payment or participant content. Runtime owns every subsequent business effect.
/// </summary>
public static class PostgresInventoryHoldExpiryDiscoveryContract
{
    public const string OwnerRole = "hold_expiry_discovery_owner";
    public const string MigratorRole = "hold_expiry_discovery_migrator";
    public const string ExecutorRole = "hold_expiry_discovery_executor";
    public const string PolicyName = "hold_expiry_identity_discovery";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("inventory_hold_expiry_targets", Schema(context));

    internal static async Task<IReadOnlyList<InventoryHoldExpiryTarget>> DiscoverAsync(
        ExploreDbContext context, DateTime utcNow, int batchSize, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<InventoryHoldExpiryTarget>(
            $$"""
            SELECT "TenantId" AS tenant_id, "OrderId" AS order_id, "HoldId" AS hold_id
            FROM {{FunctionSql(context)}}({0}, {1})
            """, utcNow, batchSize).ToListAsync(cancellationToken);

    /// <summary>Migration owner installs after forced RLS using the deployment's existing finite enrollment.</summary>
    public static async Task ApplyAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        enrollment.Validate();
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = context.GetService<ISqlGenerationHelper>();
        string schemaName = Schema(context);
        string schema = sql.DelimitIdentifier(schemaName);
        string function = FunctionSql(context);
        string signature = function + "(timestamp with time zone, integer)";
        (Type Type, string[] Properties)[] sources =
        [
            (typeof(RegistrationInventoryHold), ["Id", "TenantId", "RegistrationOrderId",
                "RegistrationInventoryHoldStatusId", "ExpiresAt", "IsDeleted"]),
            (typeof(RegistrationOrder), ["Id", "TenantId", "RegistrationOrderStatusId", "UpdatedAt", "IsDeleted"])
        ];
        var relations = sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string mappedSchema = context.GetService<IDbContextOptions>()
                .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
            if (mappedSchema != schemaName)
                throw new InvalidOperationException("hold_expiry_discovery_sources_require_one_schema");
            return new PostgresPrivilegeGuard.Relation(entity.GetTableName()!, OwnerRole, [],
                source.Properties.Select(property => new PostgresPrivilegeGuard.Column(
                    ColumnName(context, source.Type, property), ["SELECT"])).ToArray());
        }).ToArray();
        var contract = new PostgresPrivilegeGuard.Contract(schemaName,
            [OwnerRole, MigratorRole, ExecutorRole],
            [new("inventory_hold_expiry_targets", "timestamp with time zone, integer", OwnerRole, [ExecutorRole])],
            relations,
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(ExecutorRole, enrollment.GlobalLogin, true, false, false)
            ]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(contract);
        string tables = string.Join(", ", sources.Select(source => Text(Table(context, source.Type)) + "::regclass"));
        string sourceGuard = $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                WHERE role.rolname = {Text(enrollment.GlobalLogin)} AND role.rolcanlogin
                    AND NOT role.rolsuper AND NOT role.rolbypassrls AND NOT role.rolcreatedb
                    AND NOT role.rolcreaterole AND NOT role.rolreplication)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                    WHERE (pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)}, role.oid, 'USAGE')
                        OR pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)}, role.oid, 'SET'))
                        AND (role.rolsuper OR role.rolbypassrls OR role.rolcreatedb
                            OR role.rolcreaterole OR role.rolreplication))
                OR pg_catalog.has_schema_privilege({Text(enrollment.GlobalLogin)}, {Text(schemaName)}, 'CREATE')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                    LEFT JOIN pg_catalog.pg_policy policy
                        ON policy.polrelid = relation.oid AND policy.polname = '{PolicyName}'
                    WHERE relation.oid IN ({tables}) AND (
                        NOT relation.relrowsecurity OR NOT relation.relforcerowsecurity
                        OR policy.oid IS NULL OR policy.polcmd <> 'r' OR NOT policy.polpermissive
                        OR policy.polroles IS DISTINCT FROM ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        OR pg_catalog.pg_get_expr(policy.polqual, policy.polrelid) IS DISTINCT FROM 'true'
                        OR policy.polwithcheck IS NOT NULL
                        OR pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)}, relation.oid,
                            'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)}, relation.oid,
                            'SELECT,INSERT,UPDATE,REFERENCES')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy other
                            WHERE other.polrelid = relation.oid AND other.polname <> '{PolicyName}'
                                AND other.polcmd IN ('r', '*')
                                AND EXISTS (SELECT 1 FROM unnest(other.polroles) role_id
                                    WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}', role_id, 'USAGE'))
                                AND (NOT other.polpermissive OR other.polname <> 'tenant_isolation'))))
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                    WHERE routine.oid = pg_catalog.to_regprocedure({Text(signature)})
                        AND routine.provolatile = 's') THEN
                RAISE EXCEPTION 'hold_expiry_discovery_not_ready' USING ERRCODE = '55000';
            END IF;
            """;
        string grants = string.Join("\n", relations.Select(relation => $"""
            GRANT SELECT ({string.Join(", ", relation.Columns.Select(column => sql.DelimitIdentifier(column.Name)))})
                ON {sql.DelimitIdentifier(relation.Name, schemaName)} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {sql.DelimitIdentifier(relation.Name, schemaName)};
            CREATE POLICY "{PolicyName}" ON {sql.DelimitIdentifier(relation.Name, schemaName)}
                FOR SELECT TO {OwnerRole} USING (true);
            """));
        string H(string property) => sql.DelimitIdentifier(ColumnName(context, typeof(RegistrationInventoryHold), property));
        string O(string property) => sql.DelimitIdentifier(ColumnName(context, typeof(RegistrationOrder), property));
        string holds = Table(context, typeof(RegistrationInventoryHold));
        string orders = Table(context, typeof(RegistrationOrder));
        return $"""
            DO $enrollment$
            DECLARE v_role text;
            BEGIN
                IF session_user::text IS DISTINCT FROM {Text(enrollment.MigratorLogin)}
                    OR current_setting('role') NOT IN ('none', {Text(enrollment.MigratorLogin)})
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = {Text(enrollment.MigratorLogin)} AND rolcanlogin)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = {Text(enrollment.GlobalLogin)} AND rolcanlogin
                            AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                            AND NOT rolcreaterole AND NOT rolreplication) THEN
                    RAISE EXCEPTION 'hold_expiry_discovery_enrollment_invalid' USING ERRCODE = '55000';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{ExecutorRole}')) THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
                        WHERE namespace.nspname = {Text(schemaName)} AND routine.proname = 'inventory_hold_expiry_targets')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                            JOIN pg_catalog.pg_class relation ON relation.oid = policy.polrelid
                            JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                            WHERE namespace.nspname = {Text(schemaName)} AND policy.polname = '{PolicyName}') THEN
                        RAISE EXCEPTION 'hold_expiry_discovery_partial_bootstrap' USING ERRCODE = '55000';
                    END IF;
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{ExecutorRole}'] LOOP
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END LOOP;
                    GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                    EXECUTE format('GRANT {ExecutorRole} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {Text(enrollment.GlobalLogin)});
                    EXECUTE format('GRANT {MigratorRole} TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', {Text(enrollment.MigratorLogin)});
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{ExecutorRole}'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members membership
                            JOIN pg_catalog.pg_roles parent ON parent.oid = membership.roleid
                            JOIN pg_catalog.pg_roles member ON member.oid = membership.member
                            WHERE parent.rolname = v_role AND member.rolname = {Text(enrollment.MigratorLogin)}) THEN
                            EXECUTE format('REVOKE %I FROM %I', v_role, {Text(enrollment.MigratorLogin)});
                        END IF;
                    END LOOP;
                    IF ({PostgresPrivilegeGuard.BuildRoleGraphSql(contract)}) IS NOT TRUE THEN
                        RAISE EXCEPTION 'native_privilege_guard_not_ready' USING ERRCODE = '55000';
                    END IF;
                ELSE
                    {guard}
                    {sourceGuard}
                END IF;
            END $enrollment$;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {ExecutorRole};
            {grants}
            DO $install$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(p_now timestamp with time zone, p_take integer)
                RETURNS TABLE ("TenantId" uuid, "OrderId" uuid, "HoldId" uuid)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {guard}
                    {PostgresPrivilegeGuard.RestrictedCallerSql}
                    {sourceGuard}
                    IF session_user::text IS DISTINCT FROM {Text(enrollment.GlobalLogin)}
                        OR NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'hold_expiry_discovery_unbound_global_required' USING ERRCODE = '42501';
                    END IF;
                    IF p_now IS NULL OR NOT isfinite(p_now) OR p_take IS NULL
                        OR p_take < 1 OR p_take > {InventoryHoldExpiryDiscovery.MaximumBatchSize} THEN
                        RAISE EXCEPTION 'invalid hold expiry discovery bounds' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY
                        (SELECT h.{H("TenantId")}, h.{H("RegistrationOrderId")}, h.{H("Id")}
                         FROM {holds} h
                         WHERE h.{H("RegistrationInventoryHoldStatusId")} = {(int)RegistrationInventoryHoldStatusEnum.Active}
                             AND h.{H("ExpiresAt")} <= p_now AND NOT h.{H("IsDeleted")}
                         ORDER BY h.{H("ExpiresAt")}, h.{H("Id")} LIMIT p_take)
                        UNION ALL
                        (SELECT o.{O("TenantId")}, o.{O("Id")}, NULL::uuid FROM {orders} o
                         WHERE o.{O("RegistrationOrderStatusId")} = {(int)RegistrationOrderStatusEnum.NeedsReconciliation}
                             AND NOT o.{O("IsDeleted")}
                         ORDER BY o.{O("UpdatedAt")}, o.{O("Id")} LIMIT p_take);
                END $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {ExecutorRole};
                    GRANT EXECUTE ON FUNCTION {signature} TO {ExecutorRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $verify$ BEGIN
                {guard}
                {sourceGuard}
            END $verify$;
            """;
    }

    private static string ColumnName(ExploreDbContext context, Type type, string property)
    {
        var entity = context.Model.FindEntityType(type)!;
        return entity.FindProperty(property)!.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }

    private static string Table(ExploreDbContext context, Type type) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Model.FindEntityType(type)!.GetTableName()!, Schema(context));

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(RegistrationInventoryHold))!.GetSchema() ?? "public";

    private static string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
