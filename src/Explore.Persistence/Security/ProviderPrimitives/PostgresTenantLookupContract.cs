using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Migration-owned public directory projection. Global callers receive function execution
/// only; its non-login owner has column-limited reads and narrowly bounded RLS policies.
/// </summary>
public static class PostgresTenantLookupContract
{
    public const string OwnerRole = "event_tenant_lookup_owner";
    public const string MigratorRole = "event_tenant_lookup_migrator";
    public const string GlobalRole = "event_tenant_lookup_global";
    private const string PolicyName = "tenant_public_directory";

    public static string FunctionSql(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(Tenant))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + "_public_directory", Schema(context, entity));
    }

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var tenantEntity = context.Model.FindEntityType(typeof(Tenant))!;
        var settingEntity = context.Model.FindEntityType(typeof(TenantSetting))!;
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context, tenantEntity));
        string tenants = sql.DelimitIdentifier(tenantEntity.GetTableName()!, Schema(context, tenantEntity));
        string settings = sql.DelimitIdentifier(settingEntity.GetTableName()!, Schema(context, settingEntity));
        string Column(IEntityType entity, string property) => sql.DelimitIdentifier(
            entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        string id = Column(tenantEntity, nameof(Tenant.Id));
        string slug = Column(tenantEntity, nameof(Tenant.Slug));
        string status = Column(tenantEntity, nameof(Tenant.TenantStatusId));
        string tenantId = Column(settingEntity, nameof(TenantSetting.TenantId));
        string key = Column(settingEntity, nameof(TenantSetting.SettingKey));
        string value = Column(settingEntity, nameof(TenantSetting.Value));
        string subdomain = GovernanceSettingKeys.Domains.TenantSubdomain.Replace("'", "''", StringComparison.Ordinal);
        string customDomain = GovernanceSettingKeys.Domains.TenantCustomDomain.Replace("'", "''", StringComparison.Ordinal);
        string function = FunctionSql(context);
        string[] signatures = [$"{function}()"];
        string priorAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            signatures, OwnerRole, GlobalRole, requireComplete: false);
        string currentAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            signatures, OwnerRole, GlobalRole, requireComplete: true);
        return $"""
            DO $prior_acl$ BEGIN {priorAcl} END; $prior_acl$;
            DO $contract$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = v_role AND (rolcanlogin OR rolsuper OR rolcreatedb
                            OR rolcreaterole OR rolinherit OR rolreplication OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'tenant_lookup_role_invalid' USING ERRCODE = '42501';
                    END IF;
                END LOOP;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_auth_members edge
                    JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                    JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                    WHERE member.rolname IN ('{OwnerRole}', '{GlobalRole}')
                        OR member.rolname = '{MigratorRole}' AND parent.rolname <> '{OwnerRole}'
                        OR parent.rolname = '{OwnerRole}' AND member.rolname <> '{MigratorRole}'
                        OR parent.rolname = '{MigratorRole}' AND member.rolname <> CURRENT_USER
                        OR parent.rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')
                            AND edge.admin_option
                        OR parent.rolname = '{OwnerRole}'
                            AND (edge.inherit_option OR NOT edge.set_option)
                        OR parent.rolname = '{MigratorRole}' AND NOT edge.set_option) THEN
                    RAISE EXCEPTION 'tenant_lookup_membership_invalid' USING ERRCODE = '42501';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_shdepend dependency
                    JOIN pg_catalog.pg_roles role ON role.oid = dependency.refobjid
                    WHERE dependency.refclassid = 'pg_catalog.pg_authid'::regclass
                        AND role.rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')
                        AND dependency.deptype = 'o'
                        AND (role.rolname <> '{OwnerRole}'
                            OR dependency.classid <> 'pg_catalog.pg_proc'::regclass
                            OR dependency.dbid <> (SELECT oid FROM pg_catalog.pg_database
                                WHERE datname = pg_catalog.current_database())
                            OR dependency.objid IS DISTINCT FROM
                                pg_catalog.to_regprocedure('{function.Replace("'", "''", StringComparison.Ordinal)}()'))) THEN
                    RAISE EXCEPTION 'tenant_lookup_ownership_invalid' USING ERRCODE = '42501';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_class relation
                    JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                    WHERE namespace.nspname <> 'information_schema' AND namespace.nspname !~ '^pg_'
                        AND (relation.relkind IN ('r', 'p', 'v', 'm', 'f')
                            AND (has_table_privilege('{GlobalRole}', relation.oid,
                                'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR has_any_column_privilege('{GlobalRole}', relation.oid,
                                    'SELECT,INSERT,UPDATE,REFERENCES'))
                            OR relation.relkind = 'S' AND has_sequence_privilege(
                                '{GlobalRole}', relation.oid, 'USAGE,SELECT,UPDATE'))) THEN
                    RAISE EXCEPTION 'tenant_lookup_execution_group_table_privilege' USING ERRCODE = '42501';
                END IF;
                IF pg_has_role('{GlobalRole}', '{OwnerRole}', 'MEMBER')
                   OR pg_has_role('{GlobalRole}', '{MigratorRole}', 'MEMBER')
                   OR pg_has_role('{OwnerRole}', '{GlobalRole}', 'MEMBER')
                   OR pg_has_role('{MigratorRole}', '{GlobalRole}', 'MEMBER') THEN
                    RAISE EXCEPTION 'tenant lookup global and migration authority must remain separate';
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
                    RAISE EXCEPTION 'tenant lookup global and migration logins must remain separate';
                END IF;
            END
            $contract$;
            GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE;
            GRANT {MigratorRole} TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {tenants}, {settings} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({id}, {slug}, {status}) ON {tenants} TO {OwnerRole};
            GRANT SELECT ({tenantId}, {key}, {value}) ON {settings} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {tenants};
            CREATE POLICY "{PolicyName}" ON {tenants} FOR SELECT TO {OwnerRole}
                USING ({status} = {(int)TenantStatusEnum.Active});
            DROP POLICY IF EXISTS "{PolicyName}" ON {settings};
            CREATE POLICY "{PolicyName}" ON {settings} FOR SELECT TO {OwnerRole}
                USING ({key} IN ('{subdomain}', '{customDomain}')
                    AND EXISTS (SELECT 1 FROM {tenants} AS directory
                        WHERE directory.{id} = {tenantId}
                          AND directory.{status} = {(int)TenantStatusEnum.Active}));

            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}()
                RETURNS TABLE ("TenantId" uuid, "Slug" text, "Subdomain" text, "CustomDomain" text)
                LANGUAGE plpgsql
                STABLE
                SECURITY DEFINER
                SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {currentAcl}
                    RETURN QUERY SELECT directory.{id}, directory.{slug}::text,
                        (SELECT domain.{value}::text FROM {settings} AS domain
                         WHERE domain.{tenantId} = directory.{id} AND domain.{key} = '{subdomain}'),
                        (SELECT domain.{value}::text FROM {settings} AS domain
                         WHERE domain.{tenantId} = directory.{id} AND domain.{key} = '{customDomain}')
                    FROM {tenants} AS directory
                    WHERE directory.{status} = {(int)TenantStatusEnum.Active};
                END;
                $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {function}() FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {function}() TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END
            $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $current_acl$ BEGIN {currentAcl} END; $current_acl$;
            """;
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
