using Explore.Application.Services;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Exposes only a bounded current tenant directory page with audit ownership.
/// The function owner can read audit tenant keys, never attribution or content.
/// Governance reads and deletion remain ordinary exact-tenant Runtime work.
/// </summary>
public static class PostgresEventResourceAuditRetentionContract
{
    public const string OwnerRole = "event_resource_audit_retention_owner";
    public const string MigratorRole = "event_resource_audit_retention_migrator";
    public const string RuntimeRole = "event_resource_audit_retention_runtime";
    public const string PolicyName = "event_resource_audit_ownership";

    public static string FunctionSql(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(EventResourceAuditEntry))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + "_owners", Schema(context, entity));
    }

    /// <summary>Installed only through migration authority, never through Runtime or Global acquisition.</summary>
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
        var audit = context.Model.FindEntityType(typeof(EventResourceAuditEntry))!;
        var tenant = context.Model.FindEntityType(typeof(Tenant))!;
        var auditMapping = StoreObjectIdentifier.Table(audit.GetTableName()!, audit.GetSchema());
        var tenantMapping = StoreObjectIdentifier.Table(tenant.GetTableName()!, tenant.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context, audit));
        string auditTable = sql.DelimitIdentifier(auditMapping.Name, Schema(context, audit));
        string tenantTable = sql.DelimitIdentifier(tenantMapping.Name, Schema(context, tenant));
        string auditTenant = sql.DelimitIdentifier(audit.FindProperty(nameof(EventResourceAuditEntry.TenantId))!
            .GetColumnName(auditMapping)!);
        string tenantId = sql.DelimitIdentifier(tenant.FindProperty(nameof(Tenant.Id))!
            .GetColumnName(tenantMapping)!);
        string function = FunctionSql(context);
        string signature = $"{function}(uuid, integer)";
        var contract = new PostgresPrivilegeGuard.Contract(Schema(context, audit),
            [OwnerRole, MigratorRole, RuntimeRole],
            [new(auditMapping.Name + "_owners", "uuid,integer", OwnerRole, [RuntimeRole])],
            [
                new(auditMapping.Name, OwnerRole, [],
                    [new(audit.FindProperty(nameof(EventResourceAuditEntry.TenantId))!.GetColumnName(auditMapping)!, ["SELECT"])]),
                new(tenantMapping.Name, OwnerRole, [],
                    [new(tenant.FindProperty(nameof(Tenant.Id))!.GetColumnName(tenantMapping)!, ["SELECT"])])
            ],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.GlobalLogin, true, false, false)
            ]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(contract);
        return $"""
            DO $contract$
            DECLARE
                v_role text;
            BEGIN
                IF session_user::text IS DISTINCT FROM {Text(enrollment.MigratorLogin)}
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = {Text(enrollment.MigratorLogin)} AND rolcanlogin)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = {Text(enrollment.GlobalLogin)}
                        AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                        AND NOT rolcreaterole AND NOT rolreplication) THEN
                    RAISE EXCEPTION 'audit_retention_enrollment_invalid' USING ERRCODE = '55000';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{RuntimeRole}')) THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
                        WHERE namespace.nspname = {Text(Schema(context, audit))}
                            AND routine.proname = {Text(auditMapping.Name + "_owners")})
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                            JOIN pg_catalog.pg_class relation ON relation.oid = policy.polrelid
                            JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                            WHERE namespace.nspname = {Text(Schema(context, audit))} AND policy.polname = '{PolicyName}') THEN
                        RAISE EXCEPTION 'audit_retention_partial_bootstrap' USING ERRCODE = '55000';
                    END IF;
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{RuntimeRole}'] LOOP
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END LOOP;
                    GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                    EXECUTE format('GRANT {RuntimeRole} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {Text(enrollment.GlobalLogin)});
                    EXECUTE format('GRANT {MigratorRole} TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', {Text(enrollment.MigratorLogin)});
                    -- Remove only fresh shortcut edges after issuing the declared grants.
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{RuntimeRole}'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                            JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                            JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                            WHERE parent.rolname = v_role AND member.rolname = {Text(enrollment.MigratorLogin)}) THEN
                            EXECUTE format('REVOKE %I FROM %I', v_role, {Text(enrollment.MigratorLogin)});
                        END IF;
                    END LOOP;
                ELSE
                    {guard}
                END IF;
            END
            $contract$;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {RuntimeRole};
            REVOKE ALL ON TABLE {auditTable}, {tenantTable} FROM {OwnerRole};
            GRANT SELECT ({auditTenant}) ON {auditTable} TO {OwnerRole};
            GRANT SELECT ({tenantId}) ON {tenantTable} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {auditTable};
            CREATE POLICY "{PolicyName}" ON {auditTable}
                FOR SELECT TO {OwnerRole} USING (true);

            DO $bootstrap$
            DECLARE
                v_previous_role text := current_setting('role');
            BEGIN
            PERFORM set_config('role', '{OwnerRole}', true);
            EXECUTE $drop$ DROP FUNCTION IF EXISTS {signature} $drop$;
            EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(
                p_after_tenant uuid, p_take integer)
            RETURNS TABLE (tenant_id uuid)
            LANGUAGE plpgsql
            STABLE
            SECURITY DEFINER
            SET search_path = pg_catalog
            AS $function$
            BEGIN
                {guard}
                IF session_user::text IS DISTINCT FROM {Text(enrollment.GlobalLogin)}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                        WHERE (pg_catalog.pg_has_role(session_user, role.oid, 'USAGE')
                            OR pg_catalog.pg_has_role(session_user, role.oid, 'SET'))
                            AND (role.rolsuper OR role.rolbypassrls OR role.rolcreatedb
                                OR role.rolcreaterole OR role.rolreplication)) THEN
                    RAISE EXCEPTION 'audit_retention_global_login_required' USING ERRCODE = '42501';
                END IF;
                IF p_take IS NULL OR p_take NOT BETWEEN 1 AND {EventResourceAuditRetentionService.TenantPageSize} THEN
                    RAISE EXCEPTION 'invalid audit retention owner bounds' USING ERRCODE = '22023';
                END IF;
                RETURN QUERY
                    SELECT directory.{tenantId}
                    FROM {tenantTable} AS directory
                    WHERE EXISTS (
                        SELECT 1 FROM {auditTable} AS audit
                        WHERE audit.{auditTenant} = directory.{tenantId})
                      AND (p_after_tenant IS NULL OR directory.{tenantId} > p_after_tenant)
                    ORDER BY directory.{tenantId}
                    LIMIT p_take;
            END;
            $function$ $ddl$;
            EXECUTE $acl$
                REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {RuntimeRole};
                GRANT EXECUTE ON FUNCTION {signature} TO {RuntimeRole};
            $acl$;
            PERFORM set_config('role', v_previous_role, true);
            END
            $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $contract$
            BEGIN
                {guard}
            END $contract$;
            """;
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
