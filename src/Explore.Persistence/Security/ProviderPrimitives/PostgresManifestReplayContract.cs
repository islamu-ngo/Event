using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Execute-only, exact-operation replay of bounded safe bootstrap tenant-result metadata.</summary>
public static class PostgresManifestReplayContract
{
    public const string OwnerRole = "event_manifest_replay_owner";
    public const string MigratorRole = "event_manifest_replay_migrator";
    public const string RuntimeRole = "event_manifest_replay_runtime";
    public const string GlobalRole = "event_manifest_replay_global";

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(ConfigurationManifestTenantResult))!.GetSchema() ?? "public";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("configuration_manifest_replay_results", Schema(context));

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
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context));
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context));
        string Column<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }
        (string Name, string Type)[] columns =
        [
            (nameof(ConfigurationManifestTenantResult.Id), "uuid"),
            (nameof(ConfigurationManifestTenantResult.OperationId), "uuid"),
            (nameof(ConfigurationManifestTenantResult.TenantId), "uuid"),
            (nameof(ConfigurationManifestTenantResult.Status), "text"),
            (nameof(ConfigurationManifestTenantResult.ReasonCode), "text"),
            (nameof(ConfigurationManifestTenantResult.CompletedAt), "timestamp with time zone"),
            ("_changedSettingKeyNames", "text"), ("_changedDocumentKeyNames", "text")
        ];
        string results = Table<ConfigurationManifestTenantResult>();
        string operations = Table<ConfigurationManifestOperation>();
        string resultOperation = Column<ConfigurationManifestTenantResult>(nameof(ConfigurationManifestTenantResult.OperationId));
        string operationId = Column<ConfigurationManifestOperation>(nameof(ConfigurationManifestOperation.Id));
        string operationStatus = Column<ConfigurationManifestOperation>(nameof(ConfigurationManifestOperation.Status));
        string operationMode = Column<ConfigurationManifestOperation>(nameof(ConfigurationManifestOperation.Mode));
        string projection = string.Join(", ", columns.Select(column =>
            $"result.{Column<ConfigurationManifestTenantResult>(column.Name)}::{column.Type}"));
        string grants = string.Join(", ", columns.Select(column => Column<ConfigurationManifestTenantResult>(column.Name)));
        string returns = string.Join(", ", columns.Select(column =>
            $"{Column<ConfigurationManifestTenantResult>(column.Name)} {column.Type}"));
        string function = FunctionSql(context);
        return $"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                FOREACH role_name IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{RuntimeRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name
                        AND (rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolinherit OR rolreplication OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'manifest_replay_role_invalid';
                    END IF;
                END LOOP;
                IF pg_has_role('{RuntimeRole}', '{OwnerRole}', 'MEMBER')
                    OR pg_has_role('{RuntimeRole}', '{MigratorRole}', 'MEMBER')
                    OR pg_has_role('{GlobalRole}', '{OwnerRole}', 'MEMBER')
                    OR pg_has_role('{GlobalRole}', '{MigratorRole}', 'MEMBER')
                    OR pg_has_role('{OwnerRole}', '{RuntimeRole}', 'MEMBER')
                    OR pg_has_role('{OwnerRole}', '{GlobalRole}', 'MEMBER')
                    OR pg_has_role('{MigratorRole}', '{RuntimeRole}', 'MEMBER')
                    OR pg_has_role('{MigratorRole}', '{GlobalRole}', 'MEMBER')
                    OR EXISTS (
                        WITH RECURSIVE memberships AS (
                            SELECT roleid FROM pg_catalog.pg_auth_members
                            WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                            UNION SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                            JOIN memberships inherited ON edge.member = inherited.roleid)
                        SELECT 1 FROM memberships WHERE roleid IN (
                            SELECT oid FROM pg_catalog.pg_roles
                            WHERE rolname IN ('{RuntimeRole}', '{GlobalRole}'))) THEN
                    RAISE EXCEPTION 'manifest_replay_migration_authority_invalid';
                END IF;
            END $roles$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {RuntimeRole}, {GlobalRole};
            REVOKE ALL ON {results}, {operations} FROM {OwnerRole}, {RuntimeRole}, {GlobalRole};
            GRANT SELECT ({grants}) ON {results} TO {OwnerRole};
            GRANT SELECT ({operationId}, {operationStatus}, {operationMode}) ON {operations} TO {OwnerRole};
            DROP POLICY IF EXISTS "manifest_replay_results" ON {results};
            CREATE POLICY "manifest_replay_results" ON {results} FOR SELECT TO {OwnerRole} USING (true);
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {function}(p_operation uuid)
                RETURNS TABLE ({returns})
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    IF p_operation IS NULL OR p_operation = '00000000-0000-0000-0000-000000000000'::uuid THEN
                        RAISE EXCEPTION 'manifest_replay_operation_invalid' USING ERRCODE = '22023';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM {operations} operation
                        WHERE operation.{operationId} = p_operation
                            AND operation.{operationStatus} = '{nameof(ConfigurationManifestOperationStatus.Applied)}'
                            AND operation.{operationMode} = '{nameof(ConfigurationManifestAuditMode.Bootstrap)}') THEN
                        RETURN;
                    END IF;
                    IF (SELECT count(*) FROM {results} result WHERE result.{resultOperation} = p_operation)
                        > {ConfigurationManifestValidator.MaximumTenantCount} THEN
                        RAISE EXCEPTION 'manifest_replay_result_bound_exceeded' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY SELECT {projection} FROM {results} result
                        WHERE result.{resultOperation} = p_operation
                        ORDER BY result.{Column<ConfigurationManifestTenantResult>(nameof(ConfigurationManifestTenantResult.TenantId))};
                END;
                $body$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {function}(uuid) FROM PUBLIC, {RuntimeRole}, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {function}(uuid) TO {RuntimeRole}, {GlobalRole};
                $acl$;
                PERFORM set_config('role', previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            """;
    }
}
