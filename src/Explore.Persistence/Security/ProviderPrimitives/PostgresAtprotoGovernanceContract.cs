using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Exposes only the three federation-governance override keys through execute-only Global authority.</summary>
public static class PostgresAtprotoGovernanceContract
{
    public const string OwnerRole = "event_atproto_governance_owner";
    public const string MigratorRole = "event_atproto_governance_migrator";
    public const string RuntimeRole = "event_atproto_governance_runtime";
    private const string PolicyName = "atproto_governance_overrides";

    public static string FunctionSql(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(TenantSetting))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + "_atproto_governance", Schema(context, entity));
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
        var entity = context.Model.FindEntityType(typeof(TenantSetting))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context, entity));
        string table = sql.DelimitIdentifier(mapping.Name, Schema(context, entity));
        string columns = string.Join(", ", entity.GetProperties()
            .Select(property => sql.DelimitIdentifier(property.GetColumnName(mapping)!)));
        string key = sql.DelimitIdentifier(entity.FindProperty(nameof(TenantSetting.SettingKey))!.GetColumnName(mapping)!);
        string tenant = sql.DelimitIdentifier(entity.FindProperty(nameof(TenantSetting.TenantId))!.GetColumnName(mapping)!);
        string keys = $"'{GovernanceSettingKeys.Federation.AtprotoEventsEnabled}', " +
            $"'{GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled}', " +
            $"'{GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode}'";
        string function = FunctionSql(context);
        string priorAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [$"{function}(text)"], OwnerRole, RuntimeRole, requireComplete: false);
        string currentAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [$"{function}(text)"], OwnerRole, RuntimeRole, requireComplete: true);
        return $"""
            DO $prior_acl$ BEGIN {priorAcl} END; $prior_acl$;
            DO $roles$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{RuntimeRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role
                        AND (rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication)) THEN
                        RAISE EXCEPTION 'atproto_governance_authority_not_ready';
                    END IF;
                END LOOP;
                IF pg_has_role('{RuntimeRole}', '{OwnerRole}', 'MEMBER')
                   OR pg_has_role('{RuntimeRole}', '{MigratorRole}', 'MEMBER') THEN
                    RAISE EXCEPTION 'atproto_governance_authority_not_ready';
                END IF;
                IF EXISTS (
                    WITH RECURSIVE inherited_roles(roleid) AS (
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        WHERE edge.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        JOIN inherited_roles inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM inherited_roles
                    WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{RuntimeRole}')) THEN
                    RAISE EXCEPTION 'atproto_governance_authority_not_ready';
                END IF;
            END; $roles$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {RuntimeRole};
            REVOKE ALL ON TABLE {table} FROM {OwnerRole};
            GRANT SELECT ({columns}) ON {table} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {table};
            CREATE POLICY "{PolicyName}" ON {table}
                FOR SELECT TO {OwnerRole} USING ({key} IN ({keys}));
            DO $install$
            DECLARE v_previous text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {function}(p_key text)
                RETURNS SETOF {table}
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    {currentAcl}
                    IF p_key IS NULL OR p_key NOT IN ({keys}) THEN
                        RAISE EXCEPTION 'atproto_governance_key_invalid' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY SELECT overrides.* FROM {table} overrides
                        WHERE overrides.{key} = p_key ORDER BY overrides.{tenant};
                END; $body$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {function}(text) FROM PUBLIC, {RuntimeRole};
                    GRANT EXECUTE ON FUNCTION {function}(text) TO {RuntimeRole};
                $acl$;
                PERFORM set_config('role', v_previous, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $current_acl$ BEGIN {currentAcl} END; $current_acl$;
            """;
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
